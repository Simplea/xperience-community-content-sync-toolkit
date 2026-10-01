using Kentico.Xperience.Admin.Base;

using System.Net;

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

    // Status filter values: this one, or a ContentSyncStatus member name.
    public const string NeedsActionStatusFilter = "needs-action";

    // The Status dropdown's options, in DropDownComponent's "value;text" per-line format.
    public const string StatusFilterOptions =
        NeedsActionStatusFilter + ";Needs action\r\n"
        + nameof(ContentSyncStatus.MissingOnTarget) + ";Missing on target\r\n"
        + nameof(ContentSyncStatus.OutOfDateOnTarget) + ";Out of date on target\r\n"
        + nameof(ContentSyncStatus.ExtraOnTarget) + ";Extra on target\r\n"
        + nameof(ContentSyncStatus.InSync) + ";In sync";

    // "Needs action" is what Content Sync still has to push; Extra is left out because Content
    // Sync only pushes from source to target. Null means no filter, including for an unknown value.
    public static IReadOnlyCollection<ContentSyncStatus>? ParseStatusFilter(string? value)
    {
        if (string.Equals(value, NeedsActionStatusFilter, StringComparison.OrdinalIgnoreCase))
        {
            return [ContentSyncStatus.MissingOnTarget, ContentSyncStatus.OutOfDateOnTarget];
        }

        return Enum.TryParse(value, ignoreCase: true, out ContentSyncStatus status) && Enum.IsDefined(status)
            ? [status]
            : null;
    }

    public static IReadOnlyList<ContentSyncStatusItem> ApplyStatusFilter(
        IReadOnlyList<ContentSyncStatusItem> items, IReadOnlyCollection<ContentSyncStatus>? statuses) =>
        statuses is null ? items : [.. items.Where(item => statuses.Contains(item.Status))];

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
    // also the fallback when no column is sent), so items needing action come first. Within a status,
    // the most recently published come first, since those are what editors are working on; every
    // other sort breaks ties by display name, ascending. Tie-breaks don't follow the sort direction.
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
            _ => Order(items, item => StatusSortRank(item.Status), Comparer<int>.Default, descending)
                .ThenByDescending(PublishedOrEarliest),
        };

        return [.. ordered.ThenBy(DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    // Never-published items sort as the earliest possible date.
    private static DateTime PublishedOrEarliest(ContentSyncStatusItem item) => LastPublishedWhen(item) ?? DateTime.MinValue;

    // Most urgent first: what Content Sync would add, then update, then what only the target has.
    public static int StatusSortRank(ContentSyncStatus status) => status switch
    {
        ContentSyncStatus.MissingOnTarget => 0,
        ContentSyncStatus.OutOfDateOnTarget => 1,
        ContentSyncStatus.ExtraOnTarget => 2,
        ContentSyncStatus.InSync => 3,
        _ => 4,
    };

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

    // Says why an item has its status and, where Content Sync needs something unusual, what to do,
    // so "Out of date on target" isn't ambiguous between newer edits, a different publish state, and
    // a moved or reordered page. Null for the ordinary cases. A sync that would fail comes first.
    public static string? StatusTooltip(ContentSyncStatusItem item)
    {
        string? reason = StatusReasonTooltip(item);

        if (item.RequiredObjectIssues.Count == 0)
        {
            return reason;
        }

        string blocked = "Can't sync yet: the target "
            + string.Join("; ", item.RequiredObjectIssues.Select(IssuePhrase))
            + ". A developer needs to deploy it to the target first.";

        return reason is null ? blocked : blocked + " " + reason;
    }

    // "has no content type Event", for the row tooltip, which isn't HTML.
    public static string IssuePhrase(RequiredObjectIssue issue) =>
        IssuePhrase(issue, ObjectNoun(issue.Object.Kind) + " " + issue.Object.DisplayName);

    // The same, as an HTML list item for the banner, with the name encoded and emphasized.
    public static string IssueHtml(RequiredObjectIssue issue) =>
        "<li>" + IssuePhrase(issue, ObjectNoun(issue.Object.Kind) + " <strong>" + WebUtility.HtmlEncode(issue.Object.DisplayName) + "</strong>") + "</li>";

    private static string IssuePhrase(RequiredObjectIssue issue, string obj) => issue.Problem switch
    {
        RequiredObjectProblem.DifferentGuidOnTarget => "has a different " + obj + " with the same code name (recreated rather than deployed)",
        RequiredObjectProblem.DefinitionDiffers => "has different fields for " + obj,
        _ => "has no " + obj,
    };

    private static string ObjectNoun(RequiredObjectKind kind) => kind switch
    {
        RequiredObjectKind.ContentType => "content type",
        RequiredObjectKind.Language => "language",
        RequiredObjectKind.WebsiteChannel => "website channel",
        RequiredObjectKind.Workspace => "workspace",
        _ => "object",
    };

    private static string? StatusReasonTooltip(ContentSyncStatusItem item)
    {
        bool localUnpublished = item.Local is not null && ContentInventoryVersionStatus.IsUnpublished(item.Local.VersionStatus);
        bool remoteUnpublished = item.Remote is not null && ContentInventoryVersionStatus.IsUnpublished(item.Remote.VersionStatus);

        return (item.Local, item.Remote) switch
        {
            (null, _) when remoteUnpublished => "Unpublished on the target, and not on this instance." + CannotDeleteHint,
            (null, _) => "Only on the target." + CannotDeleteHint,
            (not null, null) when localUnpublished => "Unpublished here, and not on the target yet.",
            (not null, null) => null,
            _ when localUnpublished && !remoteUnpublished => "Unpublished here, still published on the target.",
            _ when !localUnpublished && remoteUnpublished => "Published here, unpublished on the target.",
            _ when item.Reason == ContentSyncStatusReason.Moved =>
                "Moved here. To move it on the target, sync all pages on its old and new level.",
            _ when item.Reason == ContentSyncStatusReason.Reordered =>
                "Page order on this level changed here. To reorder the target, sync all pages on this level.",
            _ when item.Reason == ContentSyncStatusReason.PublishedMoreRecently => "Published here after the target's copy.",
            _ when localUnpublished => "Unpublished on both instances.",
            _ => null,
        };
    }

    public static string StatusLabel(ContentSyncStatus status) => status switch
    {
        ContentSyncStatus.InSync => "In sync",
        ContentSyncStatus.MissingOnTarget => "Missing on target",
        ContentSyncStatus.OutOfDateOnTarget => "Out of date on target",
        ContentSyncStatus.ExtraOnTarget => "Extra on target",
        _ => status.ToString(),
    };

    // Resolved by name at runtime, not as compiled Color.X constants: Color's numeric values shift
    // between Xperience versions (31.x inserted a member, so the 30.8.0 value of
    // SuccessBackgroundHighEmphasis is SkeletonContent on 31.x), and enum constants are baked into
    // this assembly as the integers of the version it was compiled against.
    public static Color StatusColor(ContentSyncStatus status) => Enum.Parse<Color>(status switch
    {
        ContentSyncStatus.InSync => nameof(Color.SuccessBackgroundHighEmphasis),
        ContentSyncStatus.MissingOnTarget => nameof(Color.AlertBackgroundHighEmphasis),
        ContentSyncStatus.OutOfDateOnTarget => nameof(Color.WarningBackgroundHighEmphasis),
        ContentSyncStatus.ExtraOnTarget => nameof(Color.BackgroundTagGrey),
        _ => nameof(Color.BackgroundTagGrey),
    });
}
