using Kentico.Xperience.Admin.Base;

using XperienceCommunity.ContentSyncToolkit.Inventory;
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

    // Both bounds are inclusive whole days, compared with the date the Last published column shows.
    // Items without a publish date can't satisfy a bound, so they drop out while either is set.
    public static IReadOnlyList<ContentSyncStatusItem> ApplyPublishedFilter(
        IReadOnlyList<ContentSyncStatusItem> items, DateTime? publishedFrom, DateTime? publishedTo)
    {
        if (publishedFrom is null && publishedTo is null)
        {
            return items;
        }

        return [.. items.Where(item =>
        {
            var published = LastPublishedWhen(item);
            return published is not null
                && (publishedFrom is null || published.Value >= publishedFrom.Value.Date)
                && (publishedTo is null || published.Value < publishedTo.Value.Date.AddDays(1));
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
                Order(items, ContentTypeName, StringComparer.OrdinalIgnoreCase, descending),
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

    public static string DisplayName(ContentSyncStatusItem item)
    {
        var source = item.Local ?? item.Remote;
        return source?.TreePath ?? source?.Name ?? string.Empty;
    }

    public static string ContentTypeName(ContentSyncStatusItem item) => (item.Local ?? item.Remote)?.ContentTypeName ?? string.Empty;

    public static DateTime? LastPublishedWhen(ContentSyncStatusItem item) => (item.Local ?? item.Remote)?.LastPublishedWhen;

    // Explains a status that involves an unpublished item, so "Out of date on target" isn't
    // ambiguous between newer edits and a different publish state. Null for the ordinary cases.
    public static string? StatusTooltip(ContentSyncStatusItem item)
    {
        bool localUnpublished = item.Local is not null && ContentInventoryVersionStatus.IsUnpublished(item.Local.VersionStatus);
        bool remoteUnpublished = item.Remote is not null && ContentInventoryVersionStatus.IsUnpublished(item.Remote.VersionStatus);

        return (item.Local, item.Remote) switch
        {
            (not null, not null) when localUnpublished && !remoteUnpublished => "Unpublished here, still published on the target.",
            (not null, not null) when !localUnpublished && remoteUnpublished => "Published here, unpublished on the target.",
            (not null, not null) when localUnpublished => "Unpublished on both instances.",
            (not null, null) when localUnpublished => "Unpublished here, and not on the target yet.",
            (null, not null) when remoteUnpublished => "Unpublished on the target, and not on this instance.",
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
