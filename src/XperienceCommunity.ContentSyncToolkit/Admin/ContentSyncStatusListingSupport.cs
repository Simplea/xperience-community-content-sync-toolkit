using Kentico.Xperience.Admin.Base;

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

    public static IReadOnlyList<ContentSyncStatusItem> ApplyStatusFilter(
        IReadOnlyList<ContentSyncStatusItem> items, ContentSyncStatus? statusFilter) =>
        statusFilter is null ? items : [.. items.Where(item => item.Status == statusFilter)];

    public static IReadOnlyList<ContentSyncStatusItem> ApplySearch(
        IReadOnlyList<ContentSyncStatusItem> items, string? searchTerm) =>
        string.IsNullOrWhiteSpace(searchTerm)
            ? items
            : [.. items.Where(item => DisplayName(item).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))];

    // No sort column (the listing's initial state) sorts by status, so items needing action come
    // first. Every sort breaks ties by display name, ascending.
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
                Order(items, item => LastPublishedWhen(item) ?? DateTime.MinValue, Comparer<DateTime>.Default, descending),
            _ => Order(items, item => StatusSortRank(item.Status), Comparer<int>.Default, descending),
        };

        return [.. ordered.ThenBy(DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

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
