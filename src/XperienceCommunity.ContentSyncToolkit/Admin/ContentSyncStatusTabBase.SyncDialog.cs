using System.Text.Json;
using System.Text.Json.Nodes;

using CMS.ContentEngine;
using CMS.Core;
using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages.Internal;

using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.Sync;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

using RowAction = Kentico.Xperience.Admin.Base.Action;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>What the tabs need to answer Xperience's Sync dialog.</summary>
internal sealed class ContentSyncDialogBackend(
    ContentSyncDialogSelectionStore selections,
    IContentSyncDialogData data,
    IContentSyncEngine engine,
    IInfoProvider<ContentLanguageInfo> languageProvider)
{
    public ContentSyncDialogSelectionStore Selections => selections;

    public IContentSyncDialogData Data => data;

    public IContentSyncEngine Engine => engine;

    public async Task<ContentLanguageInfo?> GetLanguageAsync(string languageName, CancellationToken cancellationToken) =>
        (await languageProvider.Get().WhereEquals(nameof(ContentLanguageInfo.ContentLanguageName), languageName)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)).FirstOrDefault();
}

/// <summary>
/// Xperience's own Sync dialog on the status list: the dialog is Xperience's client component, and all
/// its work is page commands on the page that hosts it, answered here with the JSON Xperience's own pages
/// answer with. Rows keep their "{guid}_{language}" identifiers, and each editor's selection is remembered
/// for the commands that don't carry it. See docs/specs/sync-actions.md, Using Xperience's own Sync dialog.
/// </summary>
internal abstract partial class ContentSyncStatusTabBase
{
    private const string DialogComponentStatusName = "@kentico/xperience-admin-base/ContentVersionStatus";
    private const string ContentItemObjectType = "ContentItem";

    // Xperience's dialog type enum exists from 31.x; its protocol is the one the commands below answer.
    private static readonly System.Reflection.PropertyInfo? dialogContentTypeProperty =
        typeof(ContentSynchronizationProperties).GetProperty("ContentType");

    /// <summary>Whether this Xperience version has the Sync dialog the commands below answer.</summary>
    private static bool UsesSyncDialog => dialogContentTypeProperty is not null;

    private ContentSynchronizationComponent SyncDialogComponent()
    {
        var properties = new ContentSynchronizationProperties
        {
            TableConfiguration = new ContentSynchronizationTableClientProperties
            {
                // The review list's columns, as Content hub's own dialog has them.
                Columns =
                [
                    DialogColumn("Name", "Name"),
                    DialogColumn("ContentType", "Content type"),
                    DialogColumn("Status", "Status"),
                ],
                HeaderActions = [],
                // As Xperience's own dialog. Its Sync sends only the ticked rows of review pages the
                // editor opened; ContentSyncDialogSelection adds the rows never shown.
                PageSizes = [10, 25],
                PageSize = 25,
                PageSizesLabel = "Items per page",
                MaxVisibleRowActions = 3,
            },
        };

        dialogContentTypeProperty!.SetValue(
            properties,
            Enum.Parse(dialogContentTypeProperty.PropertyType, Kind == ContentInventoryItemKind.WebPage ? "WebPages" : "ContentItems"));

        return new ContentSynchronizationComponent { Properties = properties };
    }

    private static Column DialogColumn(string name, string? caption, bool visible = true, ColumnContentType contentType = ColumnContentType.Text, int minWidth = 8, int maxWidth = 100) =>
        new() { Name = name, Caption = caption, Visible = visible, MinWidth = minWidth, MaxWidth = maxWidth, ContentType = contentType };

    // ---- The restore options --------------------------------------------------------------------

    [PageCommand(Permission = SystemPermissions.VIEW)]
    public Task<ICommandResponse<object>> GetPublishForm() =>
        Task.FromResult(ResponseFrom<object>(new { items = PublishFormItems(restoreNow: true, scheduledTime: null) }));

    [PageCommand(Permission = SystemPermissions.VIEW)]
    public Task<ICommandResponse<object>> ChangePublishForm(SyncDialogFormChange change)
    {
        bool restoreNow = !string.Equals(ReadString(change.Data, "RestoreNow"), "False", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(ResponseFrom<object>(new
        {
            items = PublishFormItems(restoreNow, restoreNow ? null : ReadString(change.Data, "ScheduledTime")),
        }));
    }

    // Include forms, as in Xperience's dialog: offered when the user may sync forms on this version. The
    // forms aren't listed, in Xperience's dialog either. See docs/specs/include-forms.md.
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse<object>> LoadContentSynchronizationInitialData(CancellationToken cancellationToken) =>
        ResponseFrom<object>(new { hasFormSyncPermission = SyncActionsEnabled && await actionService.CanIncludeFormsAsync(cancellationToken) });

    // ---- What goes -------------------------------------------------------------------------------

    // The review list, from the rows the editor selected. Opened from a listing, the dialog lists rows
    // on both tabs (Xperience's page tree opens the tree version, with other commands).
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse<object>> LoadSynchronizationContentItems(SyncDialogListRequest args, CancellationToken cancellationToken)
    {
        var selection = await RememberSelectionAsync(ToStrings(args.Identifiers));
        var plan = await PlanSelectionAsync(selection.RowIds, cancellationToken);
        if (plan?.Failure is { } failure)
        {
            return ResponseFrom<object>(new LoadDataResult { Rows = [], TotalCount = 0 }).AddErrorMessage(failure);
        }

        bool severalLanguages = plan!.Rows.Select(row => row.LanguageName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        var rows = ReviewRows(plan, severalLanguages, await DeletedHereAsync(plan, cancellationToken));
        var page = Page(rows, args.CurrentPage, args.PageSize).ToList();
        selection.MarkShown(page.Select(row => (string)row.Identifier));

        var response = ResponseFrom<object>(new LoadDataResult
        {
            Rows = page,
            TotalCount = rows.Count,
        });

        return args.CurrentPage <= 1 ? AddPlanMessages(response, plan) : response;
    }

    // ---- Detailed selection ----------------------------------------------------------------------

    [PageCommand(Permission = SystemPermissions.VIEW)]
    public Task<ICommandResponse<object>> GetContentSynchronizationTable() =>
        Task.FromResult(ResponseFrom<object>(new
        {
            tableConfiguration = new ContentSynchronizationTableClientProperties
            {
                Columns =
                [
                    DialogColumn("Name", "Name"),
                    DialogColumn("ContentType", "Content type"),
                    DialogColumn("Status", "Status"),
                    DialogColumn("Channel", "Channel"),
                    DialogColumn("Workspace", "Workspace"),
                    DialogColumn("ObjectType", null, visible: false),
                    DialogColumn("Actions", "Actions", contentType: ColumnContentType.Action, minWidth: 6, maxWidth: 6),
                ],
                HeaderActions = [],
                PageSizes = [10, 25],
                PageSize = 25,
                PageSizesLabel = "Items per page",
                MaxVisibleRowActions = 3,
            },
        }));

    // The selected rows and everything Content Sync would take along with them, as Xperience works it
    // out, for the editor to choose from. One language at a time, as in Xperience.
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse<object>> LoadDetailedSynchronizationItems(SyncDialogListRequest args, CancellationToken cancellationToken)
    {
        var empty = new LoadDataResult { Rows = [], TotalCount = 0 };
        var (plan, language, error) = await DetailedSelectionPlanAsync(cancellationToken);
        if (error is not null)
        {
            return ResponseFrom<object>(empty).AddErrorMessage(error);
        }

        var roots = plan!.Sendable.Select(row => row.ContentItemId!.Value).Distinct().ToList();
        var dependencies = await dialog.Data.GetDependenciesAsync(roots, language!.ContentLanguageID, args.IncludeSubPages, cancellationToken);
        var items = await dialog.Data.GetItemsAsync([.. roots, .. dependencies], language, cancellationToken);

        var rows = items
            .Where(item => string.IsNullOrEmpty(args.SearchTerm) || item.Name.Contains(args.SearchTerm, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => roots.Contains(item.ContentItemId) ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(DetailedRow)
            .ToList();

        return ResponseFrom<object>(new LoadDataResult { Rows = Page(rows, args.CurrentPage, args.PageSize), TotalCount = rows.Count });
    }

    // ---- Starting and following the sync ---------------------------------------------------------

    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse<object>> CreateContentSynchronization(SyncDialogCreate args, CancellationToken cancellationToken)
    {
        if (await GetSelectionAsync() is not { } selection)
        {
            return CreateFailed(ReopenMessage);
        }

        if (!TryReadRestore(args, DateTime.UtcNow, out var scheduledRestore, out string? restoreError))
        {
            return CreateFailed(restoreError!);
        }

        var rowIds = selection.Ticked(args.Identifiers is null ? null : ToStrings(args.Identifiers));
        if (rowIds.Count == 0)
        {
            return CreateFailed("Select what to sync.");
        }

        return await StartFromDialogAsync(
            new ContentSyncActionRequest(rowIds, Kind, scheduledRestore, args.IncludeLinkedForms),
            (plan, request) => actionService.StartAsync(plan, request, cancellationToken),
            cancellationToken);
    }

    // Detailed selection: only the chosen items go, with what they can't go without. A selected row
    // left unticked stays here.
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse<object>> CreateContentSynchronizationWithSelectedDependencies(SyncDialogCreate args, CancellationToken cancellationToken)
    {
        var (plan, _, error) = await DetailedSelectionPlanAsync(cancellationToken);
        if (error is not null)
        {
            return CreateFailed(error);
        }

        if (!TryReadRestore(args, DateTime.UtcNow, out var scheduledRestore, out string? restoreError))
        {
            return CreateFailed(restoreError!);
        }

        var (chosenPlan, dependencies) = ChooseDetailed(plan!, SelectedContentItemIds(args));

        return await StartFromDialogAsync(
            new ContentSyncActionRequest([.. chosenPlan.Rows.Select(row => row.RowId)], Kind, scheduledRestore, args.IncludeLinkedForms),
            (_, request) => actionService.StartWithSelectedDependenciesAsync(chosenPlan, request, dependencies, cancellationToken),
            cancellationToken,
            chosenPlan);
    }

    internal static IReadOnlySet<int> SelectedContentItemIds(SyncDialogCreate args) =>
        (args.SelectedIdentifiers?.GetValueOrDefault(ContentItemObjectType) ?? []).Select(ToInt).OfType<int>().ToHashSet();

    /// <summary>
    /// What Detailed selection sends: the rows whose items are still ticked (the others are left out as
    /// "Not selected."), and the ticked dependencies, which go with them.
    /// </summary>
    internal static (ContentSyncPlan Plan, IReadOnlyList<int> Dependencies) ChooseDetailed(ContentSyncPlan plan, IReadOnlySet<int> selected)
    {
        var chosen = plan with
        {
            Rows = [.. plan.Rows.Select(row => row.Availability.CanSync && !selected.Contains(row.ContentItemId!.Value)
                ? row with { Availability = ContentSyncAvailability.Unavailable("Not selected.", "not selected") }
                : row)],
        };
        var roots = chosen.Sendable.Select(row => row.ContentItemId!.Value).ToHashSet();

        return (chosen, [.. selected.Where(id => !roots.Contains(id)).Order()]);
    }

    // Several syncs (languages, size limits) are followed as one: the first failure, or else the one
    // furthest behind, until all complete.
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse<object>> GetContentSynchronizationProgress(SyncDialogProgressRequest args, CancellationToken cancellationToken)
    {
        var progresses = new List<ContentSyncProgress>();
        foreach (var syncGuid in tracker.SyncsOf(args.SynchronizationGuid))
        {
            progresses.Add(await dialog.Engine.GetProgressAsync(syncGuid, cancellationToken));
        }

        var shown = ShownProgress(progresses);

        return ResponseFrom<object>(new
        {
            status = KenticoStatus(shown.State),
            title = shown.Title,
            description = shown.Description,
            errors = shown.Errors.Count == 0 ? null : shown.Errors.Select(error => new { title = (string?)null, message = error }).ToList(),
        });
    }

    /// <summary>Of several syncs followed as one: the first failure, or else the one furthest behind, or else the first.</summary>
    internal static ContentSyncProgress ShownProgress(IReadOnlyList<ContentSyncProgress> progresses) =>
        progresses.FirstOrDefault(progress => progress.Failed)
            ?? progresses.Where(progress => !progress.IsFinished).OrderBy(progress => progress.State).FirstOrDefault()
            ?? progresses[0];

    private async Task<ICommandResponse<object>> StartFromDialogAsync(
        ContentSyncActionRequest request,
        Func<ContentSyncPlan, ContentSyncActionRequest, Task<ContentSyncActionResult>> start,
        CancellationToken cancellationToken,
        ContentSyncPlan? plan = null)
    {
        if (!SyncActionsEnabled)
        {
            return CreateFailed("Sync actions are turned off.");
        }

        try
        {
            plan ??= await actionService.PlanAsync(request, cancellationToken);
            var result = await start(plan, request);
            if (result.SyncGuids.Count == 0)
            {
                return CreateFailed(result.Message);
            }

            var response = ResponseFrom<object>(new { success = true, synchronizationGuid = tracker.AddGroup(result.SyncGuids), formItems = (object?)null });
            return result.SyncGuids.Count > 1
                ? response.AddInfoMessage($"Sent in {result.SyncGuids.Count} syncs, one per language and size limit.")
                : response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EventLogService.LogException(nameof(ContentSyncStatusTabBase), "SYNCDIALOG", ex);
            return CreateFailed("The sync couldn't start. Details are in the event log.");
        }
    }

    private ICommandResponse<object> CreateFailed(string message) =>
        ResponseFrom<object>(new { success = false, synchronizationGuid = (Guid?)null, formItems = (object?)null }).AddErrorMessage(message);

    private const string ReopenMessage = "Close this dialog and open it again from the list.";

    // ---- Selection and plan ----------------------------------------------------------------------

    // The review sends its rows with each page; should one ever come without them, the dialog's rows stay.
    private async Task<ContentSyncDialogSelection> RememberSelectionAsync(IReadOnlyList<string> rowIds)
    {
        int userId = (await userAccessor.Get()).UserID;
        return rowIds.Count == 0 && dialog.Selections.Get(userId, TabKey) is { } current
            ? current
            : dialog.Selections.Remember(userId, TabKey, rowIds);
    }

    private async Task<ContentSyncDialogSelection?> GetSelectionAsync() =>
        dialog.Selections.Get((await userAccessor.Get()).UserID, TabKey);

    private async Task<ContentSyncPlan?> PlanSelectionAsync(IReadOnlyList<string> rowIds, CancellationToken cancellationToken)
    {
        try
        {
            return await actionService.PlanAsync(new ContentSyncActionRequest(rowIds, Kind, null), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EventLogService.LogException(nameof(ContentSyncStatusTabBase), "SYNCDIALOG", ex);
            return new ContentSyncPlan([], [], "The rows couldn't be checked. Details are in the event log.");
        }
    }

    private async Task<(ContentSyncPlan? Plan, ContentLanguageInfo? Language, string? Error)> DetailedSelectionPlanAsync(CancellationToken cancellationToken)
    {
        if (!dialog.Data.SupportsDetailedSelection)
        {
            return (null, null, "This Xperience version doesn't offer Detailed selection.");
        }

        if (await GetSelectionAsync() is not { } selection)
        {
            return (null, null, ReopenMessage);
        }

        var plan = await PlanSelectionAsync(selection.RowIds, cancellationToken);
        if (plan!.Failure is { } failure)
        {
            return (null, null, failure);
        }

        var languages = plan.Sendable.Select(row => row.LanguageName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (languages.Count != 1)
        {
            return (null, null, languages.Count == 0
                ? "Nothing selected needs a sync. " + ContentSyncActionService.DescribeSkipped(plan.Skipped)
                : "Detailed selection works on one language at a time. Select rows in one language, or sync without it.");
        }

        return (plan, await dialog.GetLanguageAsync(languages[0], cancellationToken), null);
    }

    private static ICommandResponse<object> AddPlanMessages(ICommandResponse<object> response, ContentSyncPlan plan)
    {
        foreach (string note in plan.Notes)
        {
            response = response.AddInfoMessage(note);
        }

        if (plan.Skipped.Count > 0)
        {
            response = response.AddInfoMessage(ContentSyncActionService.DescribeSkipped(plan.Skipped).Replace("Skipped", "Won't sync", StringComparison.Ordinal));
        }

        return response;
    }

    // ---- Rows ------------------------------------------------------------------------------------

    internal static string RowName(ContentSyncPlannedRow row, bool withLanguage)
    {
        string name = row.Item is { } item ? ContentSyncStatusListingSupport.DisplayName(item) : row.Guid.ToString();
        if (row.AddedForOrder)
        {
            name += " (sibling, keeps the order)";
        }

        return withLanguage ? $"{name} ({row.LanguageName})" : name;
    }

    // Rows only on the target that are in this instance's recycle bin, labelled Deleted here as in the list.
    private async Task<IReadOnlySet<string>> DeletedHereAsync(ContentSyncPlan plan, CancellationToken cancellationToken)
    {
        var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in plan.Rows.Where(row => row.ContentItemId is null).GroupBy(row => row.LanguageName, StringComparer.OrdinalIgnoreCase))
        {
            var found = await recycleBinLookup.GetDeletedItemsAsync(language.Key, [.. language.Select(row => row.Guid)], cancellationToken);
            deleted.UnionWith(language.Where(row => found.ContainsKey(row.Guid)).Select(row => row.RowId));
        }

        return deleted;
    }

    /// <summary>The review list: the rows that go first, then those that don't, each in the order selected.</summary>
    /// <param name="plan">The selected rows, checked.</param>
    /// <param name="withLanguage">Whether names say the language (a selection in several).</param>
    /// <param name="deletedHere">Rows only on the target that this instance has in its recycle bin.</param>
    internal static IReadOnlyList<Row> ReviewRows(ContentSyncPlan plan, bool withLanguage, IReadOnlySet<string>? deletedHere = null) =>
        [.. plan.Rows.OrderBy(row => row.Availability.CanSync ? 0 : 1)
            .Select(row => ReviewRow(row, withLanguage, deletedHere?.Contains(row.RowId) == true))];

    internal static Row ReviewRow(ContentSyncPlannedRow row, bool withLanguage, bool deletedHere = false) =>
        new()
        {
            Identifier = row.RowId,
            Disabled = !row.Availability.CanSync,
            InactiveMessage = row.Availability.CanSync ? null : row.Availability.Reason,
            Cells =
            [
                new StringCell { Value = RowName(row, withLanguage) },
                new StringCell { Value = row.Item is { } item ? ContentSyncStatusListingSupport.ContentTypeDisplayName(item) : string.Empty },
                new NamedComponentCell { Name = DialogComponentStatusName, ComponentProps = new ContentVersionStatusNamedComponentCellProps { Status = ReviewStatus(row, deletedHere) } },
            ],
        };

    // The row's status here, in the dialog's status component: what a sync does, or why it's left out.
    internal static ContentItemStatus ReviewStatus(ContentSyncPlannedRow row, bool deletedHere = false)
    {
        if (!row.Availability.CanSync)
        {
            // No tooltip of its own: the greyed-out row shows the reason (its inactive message).
            return Status(Icons.MinusCircle, deletedHere ? ContentSyncStatusListingSupport.DeletedHereLabel : SkippedLabel(row), nameof(Color.IconLowEmphasis), nameof(Color.TextLowEmphasis), null);
        }

        string label = row.Item is { } item ? ContentSyncStatusListingSupport.StatusLabel(item) : "Sync";
        string? tooltip = row.AddedForOrder ? "Included to keep the order of the pages on its level." : null;
        return Status(Icons.ArrowSend, label, nameof(Color.InfoIcon), nameof(Color.InfoText), tooltip);
    }

    // The comparison's own label where there is one ("In sync"), otherwise the reason in a few words.
    internal static string SkippedLabel(ContentSyncPlannedRow row)
    {
        if (row.Item is { } item && !item.Status.NeedsSync())
        {
            return ContentSyncStatusListingSupport.StatusLabel(item);
        }

        string summary = row.Availability.Summary ?? "won't sync";
        return char.ToUpperInvariant(summary[0]) + summary[1..];
    }

    private static ContentItemStatus Status(string icon, string label, string iconColor, string labelColor, string? tooltip) =>
        new()
        {
            IconName = icon,
            Label = label,
            IconColor = Enum.Parse<Color>(iconColor),
            LabelColor = Enum.Parse<Color>(labelColor),
            ParametrizedTooltip = new ParametrizedTooltip { TooltipTemplate = tooltip },
        };

    internal static Row DetailedRow(ContentSyncDialogItem item) =>
        new()
        {
            Identifier = item.ContentItemId,
            Disabled = item.SentStatus is null,
            InactiveMessage = item.SentStatus is null ? "It has no published version, so it isn't sent." : null,
            Cells =
            [
                new StringCell { Value = item.Name },
                new StringCell { Value = item.ContentType },
                new NamedComponentCell { Name = DialogComponentStatusName, ComponentProps = new ContentVersionStatusNamedComponentCellProps { Status = DetailedStatus(item) } },
                new StringCell { Value = item.Channel },
                new StringCell { Value = item.Workspace },
                new StringCell { Value = ContentItemObjectType },
                new ActionCell
                {
                    Actions = item.Link is null
                        ? []
                        :
                        [
                            new RowAction(ActionType.Link)
                            {
                                Icon = Icons.ArrowRightTopSquare,
                                Parameter = item.Link,
                                LinkParameters = new LinkParameters { UseWindowOpen = true, Target = "_blank" },
                            },
                        ],
                },
            ],
        };

    internal static ContentItemStatus DetailedStatus(ContentSyncDialogItem item)
    {
        // Only these two versions are ever sent; anything else has none to send.
        string notSentLabel = item.SentStatus == VersionStatus.Unpublished ? "Unpublished" : "Not published";
        var status = item.SentStatus == VersionStatus.Published
            ? Status(Icons.CheckCircleInverted, "Published", nameof(Color.SuccessIcon), nameof(Color.SuccessText), null)
            : Status(Icons.MinusCircle, notSentLabel, nameof(Color.IconLowEmphasis), nameof(Color.TextLowEmphasis), null);

        if (item.HasNewerDraft)
        {
            // Get-only in Xperience's types, so filled in place.
            var draftIcon = new SecondaryIcon
            {
                IconName = Icons.ICircle,
                IconColor = Enum.Parse<Color>(nameof(Color.IconDefault)),
                IconPosition = SecondaryIconPosition.Right,
            };
            if (draftIcon.ParametrizedTooltip is { } tooltip)
            {
                tooltip.TooltipTemplate = "The latest published version will be synchronized.";
            }

            status.SecondaryIcons?.Add(draftIcon);
        }

        return status;
    }

    private static IEnumerable<Row> Page(IReadOnlyList<Row> rows, int currentPage, int pageSize) =>
        pageSize <= 0 ? rows : rows.Skip((Math.Max(currentPage, 1) - 1) * pageSize).Take(pageSize);

    // ---- The restore form, as Xperience's own dialog sends it -------------------------------------

    internal static JsonArray PublishFormItems(bool restoreNow, string? scheduledTime)
    {
        var items = new JsonArray
        {
            JsonNode.Parse("""
                {"options":[
                  {"value":"True","label":"Restore on the target immediately","caption":null,"disabled":false,"readOnly":false,"alert":null},
                  {"value":"False","label":"Postpone restoration on the target","caption":null,"disabled":false,"readOnly":false,"alert":null}],
                 "inline":false,"ariaLabel":"Restore options","componentName":"@kentico/xperience-admin-base/RadioGroup",
                 "guid":"00000000-0000-0000-0000-000000000000","hasDependencies":true,"name":"RestoreNow","statusText":null,
                 "explanationText":null,"label":null,"required":false,"tooltip":null,"disabled":false,"editMode":"default",
                 "inactiveMessage":null,"validationRules":[],"additionalActions":[],"value":"True",
                 "explanationTextAsHtml":false,"tooltipAsHtml":false}
                """),
        };
        items[0]!["value"] = restoreNow ? "True" : "False";

        if (!restoreNow)
        {
            var scheduled = JsonNode.Parse("""
                {"invalidFormatDateTime":"9999-10-10T10:10:10.01-05:00","startYear":1946,"endYear":2046,"months":null,
                 "componentName":"@kentico/xperience-admin-base/DateTimeInput","guid":"00000000-0000-0000-0000-000000000000",
                 "hasDependencies":false,"name":"ScheduledTime","statusText":null,"explanationText":null,"label":"Date and time",
                 "required":true,"tooltip":null,"disabled":false,"editMode":"default","inactiveMessage":null,
                 "validationRules":[
                   {"invalidFormatDateTime":"9999-10-10T10:10:10.01-05:00","ruleName":"@kentico/xperience-admin-base/DateTimeFormatValidationRule","isValid":true,"errorMessage":"The entered date is not in a valid format."},
                   {"offset":600000,"ruleName":"@kentico/xperience-admin-base/DateInFutureValidationRule","isValid":true,"errorMessage":"The entered date/time must be in the future."},
                   {"ruleName":"@kentico/xperience-admin-base/RequiredValueValidationRule","isValid":true,"errorMessage":"The 'Date and time' field is required."}],
                 "additionalActions":[],"value":null,"explanationTextAsHtml":false,"tooltipAsHtml":false}
                """)!;
            scheduled["value"] = scheduledTime;
            scheduled["validationRules"]![2]!["isValid"] = !string.IsNullOrEmpty(scheduledTime);
            items.Add(scheduled);
        }

        return items;
    }

    // A postponed restore comes as "scheduledRestore", in UTC; none means restore immediately.
    internal static bool TryReadRestore(SyncDialogCreate args, DateTime utcNow, out DateTime? scheduledRestore, out string? error)
    {
        scheduledRestore = args.ScheduledRestore?.UtcDateTime;
        error = scheduledRestore is { } when && when <= utcNow ? "Choose a restore time in the future." : null;
        return error is null;
    }

    private static string? ReadString(IDictionary<string, JsonElement>? data, string name) =>
        data is not null && data.TryGetValue(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> ToStrings(IEnumerable<JsonElement>? values) =>
        [.. (values ?? []).Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText())];

    private static int? ToInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : null;
    }

    // Kentico's status names, which the dialog's progress panel reads.
    internal static string KenticoStatus(ContentSyncProgressState state) => state switch
    {
        ContentSyncProgressState.Starting => "Created",
        ContentSyncProgressState.WaitingForTarget => "Ready",
        ContentSyncProgressState.Transferring => "ContentDataUploading",
        ContentSyncProgressState.Restoring => "Restoring",
        ContentSyncProgressState.Completed => "Completed",
        ContentSyncProgressState.Failed => "Failed",
        ContentSyncProgressState.TimedOut => "PollingTimeout",
        ContentSyncProgressState.Unknown => "Created",
        _ => "Created",
    };
}

/// <summary>A listing request from the dialog, with the rows it's for.</summary>
internal sealed class SyncDialogListRequest
{
    public int CurrentPage { get; set; } = 1;

    public int PageSize { get; set; } = 25;

    public string? SearchTerm { get; set; }

    public List<JsonElement>? Identifiers { get; set; }

    public string? LanguageName { get; set; }

    public bool IncludeSubPages { get; set; }
}

internal sealed class SyncDialogFormChange
{
    public Dictionary<string, JsonElement>? Data { get; set; }

    public List<string>? ChangedFieldNames { get; set; }
}

/// <summary>The dialog's Create request: the ticked rows (or, from Detailed selection, the chosen items) and when to restore.</summary>
internal sealed class SyncDialogCreate
{
    public List<JsonElement>? Identifiers { get; set; }

    public Dictionary<string, List<JsonElement>>? SelectedIdentifiers { get; set; }

    public DateTimeOffset? ScheduledRestore { get; set; }

    /// <summary>Include forms, when the dialog offered it.</summary>
    public bool IncludeLinkedForms { get; set; }
}

internal sealed class SyncDialogProgressRequest
{
    public Guid SynchronizationGuid { get; set; }
}
