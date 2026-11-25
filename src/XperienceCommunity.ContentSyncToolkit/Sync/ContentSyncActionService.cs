using System.Text;

using CMS.ContentEngine;
using CMS.Core;
using CMS.DataEngine;

using Kentico.Xperience.Admin.Base.Authentication;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Sync;

/// <summary>What a Sync action asks for: listing rows of one tab, and when the target restores them.</summary>
/// <param name="RowIds">Row identifiers, "{guid}_{language}": <c>WebPageItemGUID</c>s on Pages, <c>ContentItemGUID</c>s on Content hub.</param>
/// <param name="Kind">Which tab the rows are from.</param>
/// <param name="ScheduledRestore">When the target restores them; <see langword="null"/> for as soon as it can.</param>
internal sealed record ContentSyncActionRequest(IReadOnlyList<string> RowIds, ContentInventoryItemKind Kind, DateTime? ScheduledRestore);

/// <summary>What happened, for the message the editor sees.</summary>
/// <param name="Succeeded">Whether anything was sent and nothing failed straight away.</param>
/// <param name="Message">What to tell the editor.</param>
internal sealed record ContentSyncActionResult(bool Succeeded, string Message)
{
    /// <summary>The syncs started, one per language and size limit.</summary>
    public IReadOnlyList<Guid> SyncGuids { get; init; } = [];
}

/// <summary>One row of a plan: a requested row, or a page added to keep its level's order.</summary>
/// <param name="Guid">The row GUID: <c>WebPageItemGUID</c> or <c>ContentItemGUID</c>.</param>
/// <param name="LanguageName">The language version.</param>
/// <param name="ContentItemId">This instance's content item, when it has the row.</param>
/// <param name="WebPageItemId">This instance's page, for pages.</param>
/// <param name="Item">The row's comparison, when this instance has published it.</param>
/// <param name="Availability">Whether it's sent, and if not, why.</param>
/// <param name="AddedForOrder">A sibling added because a Reordered page's level goes whole.</param>
internal sealed record ContentSyncPlannedRow(
    Guid Guid,
    string LanguageName,
    int? ContentItemId,
    int? WebPageItemId,
    ContentSyncStatusItem? Item,
    ContentSyncAvailability Availability,
    bool AddedForOrder = false)
{
    public string RowId => ContentSyncRules.RowId(Guid, LanguageName);
}

/// <summary>What a sync of some rows would send, row by row, before anything is sent.</summary>
/// <param name="Rows">Every requested row, then the siblings added for page order.</param>
/// <param name="Notes">Page order notes for the editor.</param>
/// <param name="Failure">Why nothing can be planned (the target couldn't be reached), if so.</param>
internal sealed record ContentSyncPlan(IReadOnlyList<ContentSyncPlannedRow> Rows, IReadOnlyList<string> Notes, string? Failure = null)
{
    public IReadOnlyList<ContentSyncPlannedRow> Sendable => [.. Rows.Where(row => row.Availability.CanSync)];

    /// <summary>Rows that won't be sent, counted by reason: "in sync" → 3.</summary>
    public IReadOnlyDictionary<string, int> Skipped =>
        Rows.Where(row => !row.Availability.CanSync)
            .GroupBy(row => row.Availability.Summary ?? row.Availability.Reason ?? string.Empty)
            .ToDictionary(group => group.Key, group => group.Count());
}

/// <summary>Runs the status pages' Sync actions. See docs/specs/sync-actions.md.</summary>
internal interface IContentSyncActionService
{
    /// <summary>Plans and starts a sync of the rows, with the toolkit's own messages.</summary>
    public Task<ContentSyncActionResult> SyncAsync(ContentSyncActionRequest request, CancellationToken cancellationToken);

    /// <summary>What a sync of the rows would send, row by row, without sending anything.</summary>
    public Task<ContentSyncPlan> PlanAsync(ContentSyncActionRequest request, CancellationToken cancellationToken);

    /// <summary>Starts the plan's sendable rows.</summary>
    public Task<ContentSyncActionResult> StartAsync(ContentSyncPlan plan, ContentSyncActionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a sync of the plan's sendable rows with only the selected dependencies (Xperience's Detailed
    /// selection), by content item ID.
    /// </summary>
    public Task<ContentSyncActionResult> StartWithSelectedDependenciesAsync(
        ContentSyncPlan plan, ContentSyncActionRequest request, IReadOnlyCollection<int> selectedContentItemIds, CancellationToken cancellationToken);
}

/// <summary>
/// Syncs listing rows: finds each row's channel or workspace, compares it again (so a stale page or a
/// ticked row that's In sync can't send anything), checks the Synchronize permission, and starts one
/// sync per language and size limit. Rows that can't go are kept in the plan with the reason, never sent.
/// </summary>
internal sealed class ContentSyncActionService(
    IContentSyncEngine engine,
    IContentSyncStatusService statusService,
    IContentSyncScopeProvider scopeProvider,
    IContentSyncScopeAccess scopeAccess,
    IContentSyncToolkitSettings settings,
    IAuthenticatedUserAccessor userAccessor,
    IInfoProvider<ContentLanguageInfo> languageProvider,
    IEventLogService eventLogService,
    IOptions<ContentSyncToolkitOptions> options,
    ContentSyncTracker tracker,
    TimeProvider timeProvider) : IContentSyncActionService
{
    internal const string EventSource = "ContentSyncToolkit";

    // How long a command waits for the first result: enough to catch validation failures (an
    // incompatible content type fails in under a second), short enough not to hold the editor up.
    private static readonly TimeSpan firstResultWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan pollInterval = TimeSpan.FromMilliseconds(500);

    private const string WebPageObjectType = "cms.webpageitem";
    private const string ContentItemObjectType = "cms.contentitem";
    private const string CommonDataObjectType = "cms.contentitemcommondata";

    /// <summary>A row this instance has, with its scope and IDs.</summary>
    private sealed record LocalRow(Guid Guid, string LanguageName, int ContentItemId, int ScopeId, int? WebPageItemId);

    public async Task<ContentSyncActionResult> SyncAsync(ContentSyncActionRequest request, CancellationToken cancellationToken)
    {
        if (request.ScheduledRestore is { } scheduled && IsPast(scheduled))
        {
            return new ContentSyncActionResult(false, "Choose a restore time in the future.");
        }

        var plan = await PlanAsync(request, cancellationToken);
        return await StartAsync(plan, request, selectedContentItemIds: null, waitForFailures: true, cancellationToken);
    }

    public async Task<ContentSyncPlan> PlanAsync(ContentSyncActionRequest request, CancellationToken cancellationToken)
    {
        bool pages = request.Kind == ContentInventoryItemKind.WebPage;
        string scopeNoun = pages ? "page" : "workspace";
        var requested = request.RowIds.Select(ContentSyncRules.ParseRowId).OfType<(Guid Guid, string LanguageName)>().Distinct().ToList();
        var planned = new List<ContentSyncPlannedRow>();

        var localRows = await GetLocalRowsAsync(requested, pages, cancellationToken);

        // The scopes the user can see; a row elsewhere is treated as if it weren't here.
        var scopes = (pages
            ? await scopeProvider.GetWebsiteChannelsAsync(cancellationToken)
            : await scopeProvider.GetWorkspacesAsync(cancellationToken)).ToDictionary(scope => scope.Id);

        // Rows only on the target: their comparison, for the name and status the list showed.
        var onlyOnTarget = requested.Where(row => !localRows.ContainsKey(row)).ToList();
        var targetItems = await FindInComparisonsAsync(onlyOnTarget, scopes.Values, pages, cancellationToken);
        foreach (var (guid, language) in onlyOnTarget)
        {
            planned.Add(new ContentSyncPlannedRow(
                guid, language, null, null, targetItems.GetValueOrDefault((guid, language.ToLowerInvariant())),
                Unavailable(ContentSyncRules.NotOnThisInstance, ContentSyncRules.OnlyOnTargetSummary)));
        }

        int siblingCount = 0;
        var reorderedLevels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in localRows.Values.GroupBy(row => (row.ScopeId, Language: row.LanguageName.ToLowerInvariant())))
        {
            if (!scopes.TryGetValue(group.Key.ScopeId, out var scope))
            {
                planned.AddRange(group.Select(row => Planned(row, null, Unavailable(ContentSyncRules.NotOnThisInstance, ContentSyncRules.OnlyOnTargetSummary))));
                continue;
            }

            string languageName = group.First().LanguageName;
            var status = pages
                ? await statusService.GetWebPageSyncStatusAsync(scope.Name, languageName, forceRefresh: false, cancellationToken)
                : await statusService.GetContentHubSyncStatusAsync(scope.Name, languageName, forceRefresh: false, cancellationToken);
            if (!status.TargetAvailable)
            {
                return new ContentSyncPlan([], [], "The target couldn't be reached, so nothing was synced. Try again later.");
            }

            var items = status.Items.ToDictionary(item => item.Guid);
            var allowedPages = pages
                ? await scopeAccess.GetSynchronizablePagesAsync(scope, [.. group.Select(row => row.WebPageItemId!.Value)], cancellationToken)
                : null;
            bool workspaceAllowed = !pages && await scopeAccess.CanSynchronizeWorkspaceAsync(scope.Id, cancellationToken);

            var groupRows = new List<ContentSyncPlannedRow>();
            foreach (var row in group)
            {
                if (!items.TryGetValue(row.Guid, out var item))
                {
                    // Not in the comparison: never published here, so there's nothing Content Sync could send.
                    groupRows.Add(Planned(row, null, Unavailable(ContentSyncRules.NoPublishedVersion, ContentSyncRules.NotPublishedSummary)));
                    continue;
                }

                // Already in a sync started from the list: sending it again would only queue a duplicate.
                if (tracker.RunningSyncOf(request.Kind, row.Guid, row.LanguageName) is not null)
                {
                    groupRows.Add(Planned(row, item, Unavailable("A sync of it is in progress.", ContentSyncRules.AlreadySyncingSummary)));
                    continue;
                }

                bool allowed = pages ? allowedPages!.Contains(row.WebPageItemId!.Value) : workspaceAllowed;
                groupRows.Add(Planned(row, item, ContentSyncRules.Evaluate(item, allowed, movedTo: null, scopeNoun)));
            }

            planned.AddRange(groupRows);

            if (pages)
            {
                var sending = groupRows.Where(row => row.Availability.CanSync).Select(row => row.Item!).ToList();
                var siblings = await PlanSiblingsAsync(scope, languageName, sending, status.Items, cancellationToken);
                planned.AddRange(siblings);
                siblingCount += siblings.Count(row => row.Availability.CanSync);

                if (options.Value.ComparePageOrder)
                {
                    // After the siblings: a level that goes across whole isn't left out of order.
                    reorderedLevels.UnionWith(ContentSyncRules.LevelsReorderedByNewPages(
                        [.. sending, .. siblings.Where(row => row.Availability.CanSync).Select(row => row.Item!)], status.Items));
                }
            }
        }

        var notes = new List<string>();
        if (siblingCount > 0)
        {
            notes.Add($"Includes {siblingCount} sibling page{(siblingCount == 1 ? string.Empty : "s")}, to keep their order.");
        }

        if (reorderedLevels.Count > 0)
        {
            notes.Add("Adding these pages changes the order of their siblings on the target: "
                + string.Join(", ", reorderedLevels.Select(level => level.Length == 0 ? "the top level" : level))
                + ". Their siblings will show Reordered; sync them to match.");
        }

        return new ContentSyncPlan(planned, notes);
    }

    // The scopes' comparisons are cached by the list the rows were selected from.
    private async Task<Dictionary<(Guid, string), ContentSyncStatusItem>> FindInComparisonsAsync(
        IReadOnlyCollection<(Guid Guid, string LanguageName)> rows, IEnumerable<ContentSyncScope> scopes, bool pages, CancellationToken cancellationToken)
    {
        var found = new Dictionary<(Guid, string), ContentSyncStatusItem>();
        foreach (var language in rows.GroupBy(row => row.LanguageName.ToLowerInvariant()))
        {
            var wanted = language.Select(row => row.Guid).ToHashSet();
            foreach (string scopeName in scopes.Select(scope => scope.Name))
            {
                var status = pages
                    ? await statusService.GetWebPageSyncStatusAsync(scopeName, language.First().LanguageName, forceRefresh: false, cancellationToken)
                    : await statusService.GetContentHubSyncStatusAsync(scopeName, language.First().LanguageName, forceRefresh: false, cancellationToken);
                foreach (var item in status.Items.Where(item => wanted.Remove(item.Guid)))
                {
                    found[(item.Guid, language.Key)] = item;
                }

                if (wanted.Count == 0)
                {
                    break;
                }
            }
        }

        return found;
    }

    private static ContentSyncPlannedRow Planned(LocalRow row, ContentSyncStatusItem? item, ContentSyncAvailability availability, bool addedForOrder = false) =>
        new(row.Guid, row.LanguageName, row.ContentItemId, row.WebPageItemId, item, availability, addedForOrder);

    private static ContentSyncAvailability Unavailable(string reason, string summary) => ContentSyncAvailability.Unavailable(reason, summary);

    // A Reordered page's whole level goes with it (Kentico: sync all pages on the same level), as far as
    // the user may sync those pages.
    private async Task<IReadOnlyList<ContentSyncPlannedRow>> PlanSiblingsAsync(
        ContentSyncScope channel, string languageName, IReadOnlyCollection<ContentSyncStatusItem> sending, IEnumerable<ContentSyncStatusItem> comparison,
        CancellationToken cancellationToken)
    {
        var siblings = ContentSyncRules.SiblingsToInclude(sending, comparison);
        if (siblings.Count == 0)
        {
            return [];
        }

        var rows = await GetLocalRowsAsync([.. siblings.Select(sibling => (sibling.Guid, languageName))], pages: true, cancellationToken);
        var allowed = await scopeAccess.GetSynchronizablePagesAsync(channel, [.. rows.Values.Select(row => row.WebPageItemId!.Value)], cancellationToken);

        return [.. siblings
            .Where(sibling => rows.ContainsKey((sibling.Guid, languageName)))
            .Select(sibling =>
            {
                var row = rows[(sibling.Guid, languageName)];
                return Planned(
                    row,
                    sibling,
                    allowed.Contains(row.WebPageItemId!.Value)
                        ? ContentSyncAvailability.Available
                        : Unavailable("You don't have the Synchronize permission for this page.", ContentSyncRules.NoPermissionSummary),
                    addedForOrder: true);
            })];
    }

    // Xperience's Sync dialog follows each sync's progress itself, so starting from it doesn't wait.
    public Task<ContentSyncActionResult> StartAsync(ContentSyncPlan plan, ContentSyncActionRequest request, CancellationToken cancellationToken) =>
        StartAsync(plan, request, selectedContentItemIds: null, waitForFailures: false, cancellationToken);

    public Task<ContentSyncActionResult> StartWithSelectedDependenciesAsync(
        ContentSyncPlan plan, ContentSyncActionRequest request, IReadOnlyCollection<int> selectedContentItemIds, CancellationToken cancellationToken) =>
        StartAsync(plan, request, selectedContentItemIds, waitForFailures: false, cancellationToken);

    private async Task<ContentSyncActionResult> StartAsync(
        ContentSyncPlan plan, ContentSyncActionRequest request, IReadOnlyCollection<int>? selectedContentItemIds, bool waitForFailures,
        CancellationToken cancellationToken)
    {
        if (request.ScheduledRestore is { } scheduled && IsPast(scheduled))
        {
            return new ContentSyncActionResult(false, "Choose a restore time in the future.");
        }

        if (plan.Failure is { } failure)
        {
            return new ContentSyncActionResult(false, failure);
        }

        var skipped = plan.Skipped.ToDictionary(pair => pair.Key, pair => pair.Value);
        var sendable = plan.Sendable;
        if (sendable.Count == 0)
        {
            return new ContentSyncActionResult(false, "Nothing was synced. " + DescribeSkipped(skipped));
        }

        var user = await userAccessor.Get();
        var languages = (await languageProvider.Get().GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
            .ToDictionary(language => language.ContentLanguageName, StringComparer.OrdinalIgnoreCase);
        var started = new List<(Guid SyncGuid, IReadOnlyList<ContentSyncPlannedRow> Rows, string Language)>();

        foreach (var batch in ContentSyncRules.Batches(sendable, row => row.LanguageName, request.Kind))
        {
            var language = languages[batch[0].LanguageName];
            var versions = await GetVersionsAsync(
                [.. batch.Select(row => row.ContentItemId!.Value), .. selectedContentItemIds ?? []], language.ContentLanguageID, cancellationToken);
            if (versions.Count == 0)
            {
                skipped[ContentSyncRules.NotPublishedSummary] = skipped.GetValueOrDefault(ContentSyncRules.NotPublishedSummary) + batch.Count;
                continue;
            }

            var syncGuid = await engine.StartAsync(
                new ContentSyncEngineRequest(
                    [.. versions.Values],
                    [.. versions.Keys],
                    language.ContentLanguageID,
                    user.UserName,
                    user.UserID,
                    request.ScheduledRestore)
                {
                    WithSelectedDependencies = selectedContentItemIds is not null,
                },
                cancellationToken);

            started.Add((syncGuid, batch, language.ContentLanguageDisplayName));
            tracker.Add(new ContentSyncTrackedSync(
                syncGuid, request.Kind, [.. batch.Select(row => (row.Guid, row.LanguageName))], user.UserName, timeProvider.GetUtcNow(), request.ScheduledRestore));
            eventLogService.LogInformation(EventSource, "SYNCSTARTED", DescribeStarted(syncGuid, user.UserName, language.ContentLanguageDisplayName, request, batch));
        }

        if (started.Count == 0)
        {
            return new ContentSyncActionResult(false, "Nothing was synced. " + DescribeSkipped(skipped));
        }

        var syncGuids = started.Select(sync => sync.SyncGuid).ToList();
        var failures = waitForFailures
            ? await WaitForFailuresAsync(syncGuids, request.ScheduledRestore is not null, cancellationToken)
            : [];
        if (failures.Count > 0)
        {
            return new ContentSyncActionResult(false, "The sync failed: " + string.Join(" ", failures)) { SyncGuids = syncGuids };
        }

        return new ContentSyncActionResult(true, DescribeResult(started, request, skipped, plan.Notes)) { SyncGuids = syncGuids };
    }

    // Waits briefly for an early failure (validation, an incompatible content type) on any of the syncs.
    // A scheduled sync waits for its time, so there's nothing to wait for.
    private async Task<IReadOnlyList<string>> WaitForFailuresAsync(IReadOnlyList<Guid> syncGuids, bool scheduled, CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        if (scheduled)
        {
            return failures;
        }

        var pending = syncGuids.ToHashSet();
        var deadline = timeProvider.GetUtcNow() + firstResultWait;
        while (pending.Count > 0 && timeProvider.GetUtcNow() < deadline)
        {
            foreach (var syncGuid in pending.ToList())
            {
                var progress = await engine.GetProgressAsync(syncGuid, cancellationToken);
                if (progress.Failed)
                {
                    failures.Add(progress.Errors.Count > 0 ? string.Join(" ", progress.Errors) : progress.Title ?? progress.State.ToString());
                    pending.Remove(syncGuid);
                }
                else if (progress.IsFinished || progress.State is ContentSyncProgressState.WaitingForTarget or ContentSyncProgressState.Restoring)
                {
                    // Past validation: whatever happens next, the editor follows it in the list.
                    pending.Remove(syncGuid);
                }
            }

            if (pending.Count > 0)
            {
                await Task.Delay(pollInterval, timeProvider, cancellationToken);
            }
        }

        return failures;
    }

    // Each requested row that this instance has: its content item, scope (channel or workspace) and page ID.
    private static async Task<Dictionary<(Guid, string), LocalRow>> GetLocalRowsAsync(
        IReadOnlyList<(Guid Guid, string LanguageName)> requested, bool pages, CancellationToken cancellationToken)
    {
        var rows = new Dictionary<(Guid, string), LocalRow>();
        var guids = requested.Select(row => row.Guid).Distinct().ToList();
        var found = new Dictionary<Guid, (int ContentItemId, int ScopeId, int? WebPageItemId)>();

        foreach (var batch in guids.Chunk(1000))
        {
            var query = pages
                ? new ObjectQuery(WebPageObjectType)
                    .Columns("WebPageItemGUID", "WebPageItemID", "WebPageItemContentItemID", "WebPageItemWebsiteChannelID")
                    .WhereIn("WebPageItemGUID", batch.ToList())
                : new ObjectQuery(ContentItemObjectType)
                    .Columns("ContentItemGUID", "ContentItemID", "ContentItemWorkspaceID")
                    .WhereIn("ContentItemGUID", batch.ToList());

            foreach (var info in await query.GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
            {
                found[(Guid)info.GetValue(pages ? "WebPageItemGUID" : "ContentItemGUID")] = pages
                    ? (Convert.ToInt32(info.GetValue("WebPageItemContentItemID")), Convert.ToInt32(info.GetValue("WebPageItemWebsiteChannelID")), Convert.ToInt32(info.GetValue("WebPageItemID")))
                    : (Convert.ToInt32(info.GetValue("ContentItemID")), Convert.ToInt32(info.GetValue("ContentItemWorkspaceID") ?? 0), null);
            }
        }

        foreach (var (guid, language) in requested)
        {
            if (found.TryGetValue(guid, out var local))
            {
                rows[(guid, language)] = new LocalRow(guid, language, local.ContentItemId, local.ScopeId, local.WebPageItemId);
            }
        }

        return rows;
    }

    // The language version Content Sync sends: published or unpublished, never a draft. Keyed by its ID.
    private static async Task<IReadOnlyDictionary<int, BaseInfo>> GetVersionsAsync(
        IReadOnlyCollection<int> contentItemIds, int languageId, CancellationToken cancellationToken)
    {
        var versions = await new ObjectQuery(CommonDataObjectType)
            .WhereIn("ContentItemCommonDataContentItemID", contentItemIds.Distinct().ToList())
            .WhereEquals("ContentItemCommonDataContentLanguageID", languageId)
            .WhereIn("ContentItemCommonDataVersionStatus", new[] { (int)VersionStatus.Published, (int)VersionStatus.Unpublished })
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return versions.ToDictionary(version => Convert.ToInt32(version.GetValue("ContentItemCommonDataID")));
    }

    // The form's date may come as UTC or as the server's local time.
    private bool IsPast(DateTime when) =>
        when.Kind == DateTimeKind.Utc ? when <= timeProvider.GetUtcNow().UtcDateTime : when <= timeProvider.GetLocalNow().DateTime;

    private string DescribeResult(
        IReadOnlyList<(Guid SyncGuid, IReadOnlyList<ContentSyncPlannedRow> Rows, string Language)> started, ContentSyncActionRequest request,
        Dictionary<string, int> skipped, IReadOnlyList<string> notes)
    {
        int count = started.Sum(sync => sync.Rows.Count);
        string noun = Noun(request.Kind, count);
        string languages = string.Join(", ", started.Select(sync => sync.Language).Distinct());
        string target = ContentSyncRules.InstanceName(ContentSyncRules.TargetName(settings.Source.TargetUrl), options.Value.TargetLabel);
        var message = new StringBuilder(request.ScheduledRestore is { } when
            ? $"Sync scheduled: {count} {noun} ({languages}) to {target}, restored on {ContentSyncRules.RestoreTimeText(when, timeProvider.LocalTimeZone)}."
            : $"Sync started: {count} {noun} ({languages}) to {target}. The target usually restores it within 30 seconds; then reload the list.");

        if (started.Count > 1)
        {
            message.Append($" Sent in {started.Count} syncs.");
        }

        foreach (string note in notes)
        {
            message.Append(' ').Append(note);
        }

        if (skipped.Count > 0)
        {
            message.Append(' ').Append(DescribeSkipped(skipped));
        }

        return message.ToString();
    }

    // "Skipped 4: 3 in sync, 1 incompatible."
    internal static string DescribeSkipped(IReadOnlyDictionary<string, int> skipped) =>
        skipped.Count == 0
            ? string.Empty
            : $"Skipped {skipped.Values.Sum()}: " + string.Join(", ", skipped.Select(pair => $"{pair.Value} {pair.Key}")) + ".";

    private static string Noun(ContentInventoryItemKind kind, int count) =>
        (kind == ContentInventoryItemKind.WebPage ? "page" : "item") + (count == 1 ? string.Empty : "s");

    // The Event log entry for a started sync: who, what and when, one item per line. Kentico's own list
    // of content synchronizations records the sync but not which items it included.
    private static string DescribeStarted(
        Guid syncGuid, string userName, string language, ContentSyncActionRequest request, IReadOnlyList<ContentSyncPlannedRow> rows)
    {
        var text = new StringBuilder()
            .AppendLine($"Sync {syncGuid} started from the Content Sync Toolkit's status list by {userName}.")
            .AppendLine($"Language: {language}. {(request.Kind == ContentInventoryItemKind.WebPage ? "Pages" : "Items")}: {rows.Count}.")
            .AppendLine(request.ScheduledRestore is { } when ? $"Restore on the target: {when:u}." : "Restore on the target: immediately.");

        foreach (var row in rows)
        {
            var local = row.Item!.Local!;
            text.AppendLine($"- {local.TreePath ?? local.DisplayName ?? local.Name} ({row.Item.Status}, {row.Guid})");
        }

        return text.ToString();
    }
}
