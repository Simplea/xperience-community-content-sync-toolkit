using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal enum ContentSyncStatusViewKind
{
    NotConfigured,
    NoScopes,
    ScopeNotFound,
    TargetUnavailable,
    Empty,
    Items,
}

/// <param name="Kind">Which state the tab should render.</param>
/// <param name="Items">The current page of items; empty unless <paramref name="Kind"/> is <see cref="ContentSyncStatusViewKind.Items"/>.</param>
/// <param name="TotalCount">Items matching the search, before paging.</param>
internal sealed record ContentSyncStatusView(
    ContentSyncStatusViewKind Kind,
    IReadOnlyList<ContentSyncStatusItem> Items,
    int TotalCount)
{
    public static ContentSyncStatusView Of(ContentSyncStatusViewKind kind) => new(kind, [], 0);
}

// RequestedScopeName is the channel/workspace from the filter, or null when none is applied yet.
// PageIndex is zero-based, as LoadDataSettings.SelectedPage provides it.
internal sealed record ContentSyncStatusViewRequest(
    bool IsSourceConfigured,
    string? RequestedScopeName,
    bool ForceRefresh,
    string? SearchTerm,
    string? SortBy,
    bool SortDescending,
    int PageSize,
    int PageIndex);

/// <summary>
/// Every decision a sync status tab makes about what to show, kept free of Xperience's page
/// types so it can be unit tested. States are checked in a fixed order: configuration first
/// (no fetch is attempted without a target), then available scopes, then the requested scope,
/// then target availability, then whether the scope has content.
/// </summary>
internal static class ContentSyncStatusViewBuilder
{
    public static async Task<ContentSyncStatusView> BuildAsync(
        ContentSyncStatusViewRequest request,
        Func<CancellationToken, Task<IReadOnlyList<ContentSyncScope>>> getScopes,
        Func<string, bool, CancellationToken, Task<ContentSyncStatusResult>> getStatus,
        CancellationToken cancellationToken)
    {
        if (!request.IsSourceConfigured)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.NotConfigured);
        }

        var scopes = await getScopes(cancellationToken);
        if (scopes.Count == 0)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.NoScopes);
        }

        string? scopeName = ResolveScopeName(scopes, request.RequestedScopeName);
        if (scopeName is null)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.ScopeNotFound);
        }

        var result = await getStatus(scopeName, request.ForceRefresh, cancellationToken);
        if (!result.TargetAvailable)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.TargetUnavailable);
        }

        if (result.Items.Count == 0)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.Empty);
        }

        var items = ContentSyncStatusListingSupport.ApplySearch(result.Items, request.SearchTerm);
        items = ContentSyncStatusListingSupport.ApplySort(items, request.SortBy, request.SortDescending);
        int totalCount = items.Count;
        items = ContentSyncStatusListingSupport.ApplyPaging(items, request.PageSize, request.PageIndex);

        return new ContentSyncStatusView(ContentSyncStatusViewKind.Items, items, totalCount);
    }

    // No filter applied yet defaults to the first scope; a requested scope that no longer exists
    // (deleted after the dropdown loaded) returns null rather than silently showing another one.
    private static string? ResolveScopeName(IReadOnlyList<ContentSyncScope> scopes, string? requestedScopeName)
    {
        if (string.IsNullOrEmpty(requestedScopeName))
        {
            return scopes[0].Name;
        }

        return scopes.FirstOrDefault(scope => string.Equals(scope.Name, requestedScopeName, StringComparison.OrdinalIgnoreCase))?.Name;
    }
}
