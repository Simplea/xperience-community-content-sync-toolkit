
using Kentico.Xperience.Admin.Base;

using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Pure filter/search/sort/paging and status-presentation logic for the sync status admin page's
/// listing pages, kept free of any live-Xperience dependency so it can be unit tested directly —
/// mirrors how <c>ContentSyncStatusComparer</c> is kept pure in the foundation.
/// </summary>
internal static class ContentSyncStatusListingSupport
{
    // Column names double as the LoadDataSettings.SortBy values the listing sends back.
    public const string NameColumn = "name";
    public const string ContentTypeColumn = "contentType";
    public const string StatusColumn = "status";
    public const string LastPublishedColumn = "lastPublished";

    // Status filter values: Incompatible, or a ContentSyncStatus member name.
    public const string IncompatibleStatusFilter = "incompatible";

    // The Status filter sends its selected values as one comma-separated string; see
    // ContentSyncStatusMultiValueConditionBuilder.
    public const char StatusFilterSeparator = ',';

    // The order statuses are listed and sorted in, after Incompatible: content still live on the
    // target that was taken down here first, then what a sync would create, update or move, then what
    // only the target has, then what's done.
    private static readonly ContentSyncStatus[] statusOrder =
    [
        ContentSyncStatus.Unpublished,
        ContentSyncStatus.New,
        ContentSyncStatus.Changed,
        ContentSyncStatus.Moved,
        ContentSyncStatus.Reordered,
        ContentSyncStatus.NotPublished,
        ContentSyncStatus.OnlyOnTarget,
        ContentSyncStatus.InSync,
    ];

    /// <summary>Every status, in listing order.</summary>
    internal static IReadOnlyList<ContentSyncStatus> StatusOrder => statusOrder;

    /// <summary>The Status filter's options: every tag, in sort order (Incompatible, then each status).</summary>
    public static IReadOnlyList<(string Value, string Text)> StatusFilterOptions { get; } =
    [
        (IncompatibleStatusFilter, IncompatibleLabel),
        .. statusOrder.Select(status => (status.ToString(), StatusLabel(status))),
    ];

    // "Hide items in sync" leaves everything that differs, Incompatible and Only on target included,
    // so an item never disappears just because it can't be synced yet.
    public static IReadOnlyList<ContentSyncStatusItem> ApplyHideInSync(IReadOnlyList<ContentSyncStatusItem> items, bool hideInSync) =>
        hideInSync ? [.. items.Where(item => item.HasCompatibilityIssues || item.Status != ContentSyncStatus.InSync)] : items;

    // The filter shows items matching any selected option. Each option matches what the tag shows:
    // an incompatible item shows Incompatible, so it matches Incompatible and not its status. Null
    // means no filter, including when no value is known.
    public static Func<ContentSyncStatusItem, bool>? ParseStatusFilter(string? value)
    {
        var matchers = (value ?? string.Empty)
            .Split(StatusFilterSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseStatusFilterOption)
            .OfType<Func<ContentSyncStatusItem, bool>>()
            .ToList();

        return matchers.Count == 0 ? null : item => matchers.Exists(matches => matches(item));
    }

    private static Func<ContentSyncStatusItem, bool>? ParseStatusFilterOption(string value)
    {
        if (string.Equals(value, IncompatibleStatusFilter, StringComparison.OrdinalIgnoreCase))
        {
            return item => item.HasCompatibilityIssues;
        }

        return Enum.TryParse(value, ignoreCase: true, out ContentSyncStatus status) && Enum.IsDefined(status)
            ? item => !item.HasCompatibilityIssues && item.Status == status
            : null;
    }

    public static IReadOnlyList<ContentSyncStatusItem> ApplyStatusFilter(
        IReadOnlyList<ContentSyncStatusItem> items, Func<ContentSyncStatusItem, bool>? matches) =>
        matches is null ? items : [.. items.Where(matches)];

    public static IReadOnlyList<ContentSyncStatusItem> ApplyContentTypeFilter(
        IReadOnlyList<ContentSyncStatusItem> items, string? contentTypeName) =>
        string.IsNullOrEmpty(contentTypeName)
            ? items
            : [.. items.Where(item => string.Equals(ContentTypeName(item), contentTypeName, StringComparison.OrdinalIgnoreCase))];

    // Both bounds are inclusive whole days. Publish dates are UTC; the date inputs carry no time
    // zone, so their days are taken in the server's time zone (the server can't see the editor's,
    // which the Last published column uses). Items without a publish date can't satisfy a bound, so
    // they drop out while either is set.
    public static IReadOnlyList<ContentSyncStatusItem> ApplyPublishedFilter(
        IReadOnlyList<ContentSyncStatusItem> items, DateTime? publishedFrom, DateTime? publishedTo)
    {
        if (publishedFrom is null && publishedTo is null)
        {
            return items;
        }

        var fromUtc = ContentInventoryTime.ToUtc(publishedFrom?.Date);
        var toUtc = ContentInventoryTime.ToUtc(publishedTo?.Date.AddDays(1));

        return [.. items.Where(item =>
        {
            var published = ContentInventoryTime.ToUtc(LastPublishedWhen(item));
            return published is not null
                && (fromUtc is null || published.Value >= fromUtc.Value)
                && (toUtc is null || published.Value < toUtc.Value);
        })];
    }

    public static IReadOnlyList<ContentSyncStatusItem> ApplySearch(
        IReadOnlyList<ContentSyncStatusItem> items, string? searchTerm) =>
        string.IsNullOrWhiteSpace(searchTerm)
            ? items
            : [.. items.Where(item => DisplayName(item).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))];

    // Status is the default sort (the tabs set it as the Status column's default direction, and it's
    // also the fallback when no column is sent): Incompatible first, since those need a developer before
    // anyone can sync them, then by status (see statusOrder). Within a status, the most recently
    // published come first, since those are what editors are working on; every other sort breaks ties
    // by display name, ascending. Tie-breaks don't follow the sort direction.
    public static IReadOnlyList<ContentSyncStatusItem> ApplySort(
        IReadOnlyList<ContentSyncStatusItem> items, string? sortBy, bool descending)
    {
        var ordered = sortBy switch
        {
            _ when IsColumn(sortBy, NameColumn) =>
                Order(items, DisplayName, StringComparer.OrdinalIgnoreCase, descending),
            _ when IsColumn(sortBy, ContentTypeColumn) =>
                Order(items, ContentTypeDisplayName, StringComparer.OrdinalIgnoreCase, descending),
            _ when IsColumn(sortBy, LastPublishedColumn) =>
                Order(items, PublishedOrEarliest, Comparer<DateTime>.Default, descending),
            _ => Order(items, item => (item.HasCompatibilityIssues ? 0 : 1, StatusSortRank(item.Status)), Comparer<(int, int)>.Default, descending)
                .ThenByDescending(PublishedOrEarliest),
        };

        return [.. ordered.ThenBy(DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    // Never-published items sort as the earliest possible date.
    private static DateTime PublishedOrEarliest(ContentSyncStatusItem item) => LastPublishedWhen(item) ?? DateTime.MinValue;

    /// <summary>The status's position in the listing order (see statusOrder); unknown values last.</summary>
    public static int StatusSortRank(ContentSyncStatus status)
    {
        int index = Array.IndexOf(statusOrder, status);
        return index < 0 ? statusOrder.Length : index;
    }

    private static bool IsColumn(string? sortBy, string column) =>
        string.Equals(sortBy, column, StringComparison.OrdinalIgnoreCase);

    private static IOrderedEnumerable<ContentSyncStatusItem> Order<TKey>(
        IEnumerable<ContentSyncStatusItem> items,
        Func<ContentSyncStatusItem, TKey> keySelector,
        IComparer<TKey> comparer,
        bool descending) =>
        descending ? items.OrderByDescending(keySelector, comparer) : items.OrderBy(keySelector, comparer);

    // selectedPage is zero-based: ListingPageBase builds LoadDataSettings.SelectedPage as the
    // client's one-based CurrentPage minus one.
    public static IReadOnlyList<ContentSyncStatusItem> ApplyPaging(
        IReadOnlyList<ContentSyncStatusItem> items, int pageSize, int selectedPage)
    {
        if (pageSize <= 0)
        {
            return items;
        }

        int pageIndex = Math.Max(selectedPage, 0);
        return [.. items.Skip(pageSize * pageIndex).Take(pageSize)];
    }

    // Pages show their tree path, which also says where they are; content hub items show the display
    // name editors see in the Content hub, falling back to the code name from an older target.
    public static string DisplayName(ContentSyncStatusItem item)
    {
        var source = item.Local ?? item.Remote;
        return source?.TreePath ?? NonEmpty(source?.DisplayName) ?? source?.Name ?? string.Empty;
    }

    /// <summary>The content type code name, which the Content type filter matches.</summary>
    public static string ContentTypeName(ContentSyncStatusItem item) => (item.Local ?? item.Remote)?.ContentTypeName ?? string.Empty;

    public static string ContentTypeDisplayName(ContentSyncStatusItem item)
    {
        var source = item.Local ?? item.Remote;
        return NonEmpty(source?.ContentTypeDisplayName) ?? source?.ContentTypeName ?? string.Empty;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    public static DateTime? LastPublishedWhen(ContentSyncStatusItem item) => (item.Local ?? item.Remote)?.LastPublishedWhen;

    private const string CannotDeleteHint =
        " Content Sync can't delete content: if it was deleted here, delete it on the target.";

    public const string IncompatibleLabel = "Incompatible";

    // An incompatible item's tooltip: each compatibility error in the words of Kentico's own Content
    // Sync dialog ("Content type Image has different field definitions on the source and target
    // instance."), so editors see the same message in both places, then who fixes it and what the sync
    // will do once it can run.
    private static string IncompatibleTooltip(ContentSyncStatusItem item) =>
        string.Join(" ", item.RequiredObjectIssues.Select(IssueSentence))
            + (item.RequiredObjectIssues.Count == 1
                ? " A developer needs to deploy it to the target first. "
                : " A developer needs to deploy them to the target first. ")
            + item.Status switch
            {
                ContentSyncStatus.New => "Then a sync creates it.",
                ContentSyncStatus.Unpublished => "Then a sync unpublishes it there.",
                ContentSyncStatus.Moved => "Then sync all pages on its old and new level to move it.",
                ContentSyncStatus.Reordered => "Then sync all pages on its level to fix the order.",
                // Only statuses a sync would change can be incompatible; the rest update.
                ContentSyncStatus.Changed or ContentSyncStatus.OnlyOnTarget or ContentSyncStatus.InSync or ContentSyncStatus.NotPublished => "Then a sync updates it.",
                _ => "Then a sync updates it.",
            };

    // One compatibility error as a sentence, worded like Kentico's Content Sync dialog; the tooltip
    // isn't HTML.
    public static string IssueSentence(RequiredObjectIssue issue)
    {
        string obj = ObjectNoun(issue.Object.Kind) + " " + issue.Object.DisplayName;

        return issue.Problem switch
        {
            RequiredObjectProblem.DefinitionDiffers => obj + " has different field definitions on the source and target instance.",
            RequiredObjectProblem.DifferentGuidOnTarget => obj + " has a different identity on the target instance: it was recreated there instead of deployed.",
            RequiredObjectProblem.MissingOnTarget => obj + " doesn't exist on the target instance.",
            _ => obj + " doesn't exist on the target instance.",
        };
    }

    private static string ObjectNoun(RequiredObjectKind kind) => kind switch
    {
        RequiredObjectKind.ContentType => "Content type",
        RequiredObjectKind.Language => "Language",
        RequiredObjectKind.WebsiteChannel => "Website channel",
        RequiredObjectKind.Workspace => "Workspace",
        _ => "Object",
    };

    private static string TargetState(bool onTarget, bool unpublishedThere)
    {
        if (!onTarget)
        {
            return "The target doesn't have it.";
        }

        return unpublishedThere ? "The target has it unpublished." : "The target still has it published.";
    }

    private const string UnpublishedDraftTooltip =
        "Unpublished here, then edited again, so it has no published version. Content Sync can't sync it until it's published again.";

    private const string NeverPublishedTooltip =
        "Never published here: only a draft exists on this instance. Content Sync can't sync it until it's published.";

    private const string NewerDraftHint = "A newer draft here isn't published yet, and a sync only sends the published version.";

    // The status says what a sync would do; the tooltip adds what it's based on and, where Content
    // Sync needs something unusual, how to do it. Null where the label says it all.
    //
    // hasNewerDraft (a published item whose latest version is a draft) adds a note: Content Sync sends
    // the published version, so the status is right, but an editor who just saved changes could
    // otherwise read In sync as "my changes are on the target".
    public static string? StatusTooltip(ContentSyncStatusItem item, bool hasNewerDraft = false)
    {
        bool showDraft = hasNewerDraft && item.Local is not null && !ContentInventoryVersionStatus.IsUnpublished(item.Local.VersionStatus);
        string? tooltip = StatusOnlyTooltip(item);

        if (!showDraft)
        {
            return tooltip;
        }

        return tooltip is null
            ? "The target has the published version. " + NewerDraftHint
            : tooltip + " " + NewerDraftHint;
    }

    private static string? StatusOnlyTooltip(ContentSyncStatusItem item)
    {
        if (item.HasCompatibilityIssues)
        {
            return IncompatibleTooltip(item);
        }

        bool localUnpublished = item.Local is not null && ContentInventoryVersionStatus.IsUnpublished(item.Local.VersionStatus);
        bool remoteUnpublished = item.Remote is not null && ContentInventoryVersionStatus.HasNoPublishedVersion(item.Remote.VersionStatus);

        return item.Status switch
        {
            ContentSyncStatus.New when localUnpublished => "Not on the target yet. It's unpublished here, so a sync creates it unpublished.",
            ContentSyncStatus.New => "Not on the target yet. A sync creates it.",
            ContentSyncStatus.Changed when remoteUnpublished => "Published here, unpublished on the target. A sync publishes it there.",
            ContentSyncStatus.Changed => "Published here after the target's copy. A sync updates it.",
            ContentSyncStatus.Unpublished => "Unpublished here, still published on the target. A sync unpublishes it there.",
            ContentSyncStatus.Moved => "At a different place in the tree on the target. To move it, sync all pages on its old and new level.",
            ContentSyncStatus.Reordered => ReorderTooltip(item.Reorder),
            ContentSyncStatus.NotPublished =>
                (item.Local is not null && ContentInventoryVersionStatus.IsNeverPublished(item.Local.VersionStatus) ? NeverPublishedTooltip : UnpublishedDraftTooltip)
                + " " + TargetState(item.Remote is not null, remoteUnpublished),
            ContentSyncStatus.OnlyOnTarget when remoteUnpublished => "Unpublished on the target, and not on this instance." + CannotDeleteHint,
            ContentSyncStatus.OnlyOnTarget => "Only on the target." + CannotDeleteHint,
            ContentSyncStatus.InSync when localUnpublished => "Unpublished on both instances.",
            _ => null,
        };
    }

    // How many out-of-place pages a tooltip names before "and N more".
    private const int MaxNamedPages = 3;

    // Names the pages out of place, says the usual cause, and how to fix it. Positions aren't given
    // as numbers: the page tree also shows drafts, which aren't compared, so "3rd" could disagree
    // with what the editor sees.
    private static string ReorderTooltip(ContentSyncReorder? reorder)
    {
        const string cause = " This usually happens when only some pages of a level are synced.";

        if (reorder is null || reorder.MisplacedPages.Count + reorder.HiddenMisplacedCount == 0)
        {
            return "Page order on this level differs on the target." + cause + " " + ReorderFix(reorder);
        }

        // Pages the user can't see are counted, never named.
        var names = reorder.MisplacedPages.Take(MaxNamedPages).Select(PageName).ToList();
        int more = reorder.MisplacedPages.Count - names.Count;
        if (reorder.HiddenMisplacedCount > 0)
        {
            names.Add(reorder.HiddenMisplacedCount == 1 ? "a page you can't see" : $"{reorder.HiddenMisplacedCount} pages you can't see");
        }

        string list = JoinNames(names, more);
        string verb = reorder.MisplacedPages.Count + reorder.HiddenMisplacedCount == 1 ? "is" : "are";

        return $"Page order on this level differs on the target: {list} {verb} in a different position there."
            + cause + " " + ReorderFix(reorder);
    }

    // "A", "A and B", "A, B and C", or "A, B, C and 2 more".
    private static string JoinNames(IReadOnlyList<string> names, int more)
    {
        if (more > 0)
        {
            return string.Join(", ", names) + $" and {more} more";
        }

        return names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }

    // Kentico's "Sync with all subpages" on the parent syncs the whole level. The top level has no
    // parent page to sync from.
    private static string ReorderFix(ContentSyncReorder? reorder)
    {
        if (reorder is null || reorder.ParentPath.Length == 0)
        {
            return "To fix it, sync all pages on this level.";
        }

        string parent = reorder.Parent is { } parentPage
            ? PageName(parentPage)
            : reorder.ParentPath[(reorder.ParentPath.LastIndexOf('/') + 1)..];

        return $"To fix it, use Sync with all subpages on {parent}.";
    }

    private static string PageName(ContentInventoryItem page) =>
        NonEmpty(page.DisplayName)
        ?? (page.TreePath is { } path ? path[(path.LastIndexOf('/') + 1)..] : null)
        ?? page.Name;

    // Content Sync's restoration task applies a sync on the target every 30 seconds (Kentico's
    // documentation), so a status checked right after a sync can still show the old state.
    private const string SyncDelayHint = " A sync can take about 30 seconds to reach the target.";

    /// <summary>
    /// The Refresh button's tooltip: what it does, how long the target's list is otherwise reused
    /// (<see cref="ContentSyncToolkitOptions.InventoryCacheDuration"/>), and when to use it.
    /// </summary>
    public static string RefreshTooltip(TimeSpan cacheDuration)
    {
        if (cacheDuration <= TimeSpan.Zero)
        {
            return "Reloads the target's status." + SyncDelayHint;
        }

        return $"Reloads the target's status, which can be up to {Describe(cacheDuration)} old." + SyncDelayHint;
    }

    // "90 seconds", "1 second", "5 minutes", "1 hour".
    private static string Describe(TimeSpan duration)
    {
        if (duration.TotalSeconds < 120)
        {
            return Plural((int)Math.Round(duration.TotalSeconds), "second");
        }

        return duration.TotalMinutes < 120
            ? Plural((int)Math.Round(duration.TotalMinutes), "minute")
            : Plural((int)Math.Round(duration.TotalHours), "hour");
    }

    private static string Plural(int count, string unit) => count == 1 ? $"1 {unit}" : $"{count} {unit}s";

    /// <summary>The item's tag: Incompatible for an incompatible item, its status otherwise.</summary>
    public static string StatusLabel(ContentSyncStatusItem item) => item.HasCompatibilityIssues ? IncompatibleLabel : StatusLabel(item.Status);

    public static string StatusLabel(ContentSyncStatus status) => status switch
    {
        ContentSyncStatus.Unpublished => "Unpublished",
        ContentSyncStatus.New => "New",
        ContentSyncStatus.Changed => "Changed",
        ContentSyncStatus.Moved => "Moved",
        ContentSyncStatus.Reordered => "Reordered",
        ContentSyncStatus.NotPublished => "Not published",
        ContentSyncStatus.OnlyOnTarget => "Only on target",
        ContentSyncStatus.InSync => "In sync",
        _ => status.ToString(),
    };

    // Resolved by name at runtime, not as compiled Color.X constants: Color's numeric values shift
    // between Xperience versions (31.x inserted a member, so the 30.8.0 value of
    // SuccessBackgroundHighEmphasis is SkeletonContent on 31.x), and enum constants are baked into
    // this assembly as the integers of the version it was compiled against.
    //
    // Four colors, one per thing to do: red for Incompatible (a developer first), one color for
    // everything a sync would change (the label says what), grey for what a sync can't change (only
    // on the target, or not published here), green for done.
    public static Color StatusColor(ContentSyncStatusItem item) =>
        item.HasCompatibilityIssues ? Enum.Parse<Color>(nameof(Color.AlertBackgroundHighEmphasis)) : StatusColor(item.Status);

    public static Color StatusColor(ContentSyncStatus status) => Enum.Parse<Color>(status switch
    {
        ContentSyncStatus.InSync => nameof(Color.SuccessBackgroundHighEmphasis),
        ContentSyncStatus.OnlyOnTarget or ContentSyncStatus.NotPublished => nameof(Color.BackgroundTagGrey),
        ContentSyncStatus.Unpublished or ContentSyncStatus.New or ContentSyncStatus.Changed
            or ContentSyncStatus.Moved or ContentSyncStatus.Reordered
            => nameof(Color.BackgroundTagKenticoOrange),
        _ => nameof(Color.BackgroundTagKenticoOrange),
    });
}
