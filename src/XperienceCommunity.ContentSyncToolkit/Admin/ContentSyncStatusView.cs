using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal enum ContentSyncStatusViewKind
{
    NotConfigured,
    NoScopes,
    ScopeNotFound,
    LanguageNotFound,
    TargetUnavailable,
    Empty,
    AllInSync,
    Items,
}

/// <param name="Kind">Which state the tab should render.</param>
/// <param name="Items">The current page of items; empty unless <paramref name="Kind"/> is <see cref="ContentSyncStatusViewKind.Items"/>.</param>
/// <param name="TotalCount">Items matching the filters and search, before paging.</param>
internal sealed record ContentSyncStatusView(
    ContentSyncStatusViewKind Kind,
    IReadOnlyList<ContentSyncStatusItem> Items,
    int TotalCount)
{
    /// <summary>The channel/workspace the items belong to; set once the scope is resolved.</summary>
    public ContentSyncScope? Scope { get; init; }

    /// <summary>The compared language; set once the language is resolved, and null when every language is compared.</summary>
    public string? LanguageName { get; init; }

    /// <summary>This instance's languages, for the Language column; set once they're loaded.</summary>
    public IReadOnlyList<ContentSyncLanguage> Languages { get; init; } = [];

    public static ContentSyncStatusView Of(ContentSyncStatusViewKind kind) => new(kind, [], 0);
}

/// <summary>The filter panel's fields other than the channel/workspace and language. Null means not filtered.</summary>
/// <param name="Status">A <see cref="ContentSyncStatusListingSupport.ParseStatusFilter"/> value.</param>
/// <param name="ContentTypeName">A content type code name.</param>
/// <param name="PublishedFrom">First day to include, from a date input (midnight).</param>
/// <param name="PublishedTo">Last day to include, from a date input (midnight).</param>
/// <param name="ShowInSync">Whether to list items in sync; see <see cref="ContentSyncStatusListingSupport.HidesInSync"/>.</param>
internal sealed record ContentSyncStatusFilter(
    string? Status = null,
    string? ContentTypeName = null,
    DateTime? PublishedFrom = null,
    DateTime? PublishedTo = null,
    bool ShowInSync = false);

// RequestedScopeName/RequestedLanguageName come from the filter, or are null when not applied.
// PageIndex is zero-based, as LoadDataSettings.SelectedPage provides it.
internal sealed record ContentSyncStatusViewRequest(
    bool IsSourceConfigured,
    string? RequestedScopeName,
    string? RequestedLanguageName,
    bool ForceRefresh,
    ContentSyncStatusFilter Filter,
    string? SearchTerm,
    string? SortBy,
    bool SortDescending,
    int PageSize,
    int PageIndex);

/// <summary>
/// Every decision a sync status tab makes about what to show, kept free of Xperience's page
/// types so it can be unit tested. States are checked in a fixed order: configuration first
/// (no fetch is attempted without a target), then available scopes, then the requested scope and
/// language, then target availability, then whether the scope has content.
/// </summary>
internal static class ContentSyncStatusViewBuilder
{
    public static async Task<ContentSyncStatusView> BuildAsync(
        ContentSyncStatusViewRequest request,
        Func<CancellationToken, Task<IReadOnlyList<ContentSyncScope>>> getScopes,
        Func<CancellationToken, Task<IReadOnlyList<ContentSyncLanguage>>> getLanguages,
        Func<string, string, bool, CancellationToken, Task<ContentSyncStatusResult>> getStatus,
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

        var scope = ResolveScope(scopes, request.RequestedScopeName);
        if (scope is null)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.ScopeNotFound);
        }

        var languages = await getLanguages(cancellationToken);
        if (languages.Count == 0)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.Empty);
        }

        bool allLanguages = ContentSyncStatusListingSupport.IsAllLanguages(request.RequestedLanguageName);
        string? languageName = allLanguages ? null : ResolveLanguageName(languages, request.RequestedLanguageName);
        if (!allLanguages && languageName is null)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.LanguageNotFound);
        }

        // Each language is its own comparison, cached on its own, as when it's selected alone. They run
        // one after another, so a site with many languages doesn't send the target as many requests at once.
        List<ContentSyncStatusItem> allItems = [];
        foreach (string name in languageName is null ? languages.Select(language => language.Name) : [languageName])
        {
            var result = await getStatus(scope.Name, name, request.ForceRefresh, cancellationToken);
            if (!result.TargetAvailable)
            {
                return ContentSyncStatusView.Of(ContentSyncStatusViewKind.TargetUnavailable);
            }

            allItems.AddRange(result.Items);
        }

        if (allItems.Count == 0)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.Empty);
        }

        var filter = request.Filter;
        bool hideInSync = ContentSyncStatusListingSupport.HidesInSync(filter, request.SearchTerm);
        var items = ContentSyncStatusListingSupport.ApplyHideInSync(allItems, hideInSync);
        if (hideInSync && items.Count == 0)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.AllInSync);
        }

        items = ContentSyncStatusListingSupport.ApplyStatusFilter(items, ContentSyncStatusListingSupport.ParseStatusFilter(filter.Status));
        items = ContentSyncStatusListingSupport.ApplyContentTypeFilter(items, filter.ContentTypeName);
        items = ContentSyncStatusListingSupport.ApplyPublishedFilter(items, filter.PublishedFrom, filter.PublishedTo);
        items = ContentSyncStatusListingSupport.ApplySearch(items, request.SearchTerm);
        items = ContentSyncStatusListingSupport.ApplySort(items, request.SortBy, request.SortDescending, LanguageDisplayNames(languages));
        int totalCount = items.Count;
        items = ContentSyncStatusListingSupport.ApplyPaging(items, request.PageSize, request.PageIndex);

        return new ContentSyncStatusView(ContentSyncStatusViewKind.Items, items, totalCount)
        {
            Scope = scope,
            LanguageName = languageName,
            Languages = languages,
        };
    }

    public static IReadOnlyDictionary<string, string> LanguageDisplayNames(IReadOnlyList<ContentSyncLanguage> languages) =>
        languages
            .DistinctBy(language => language.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(language => language.Name, language => language.DisplayName, StringComparer.OrdinalIgnoreCase);

    // No filter applied yet defaults to the first scope; a requested scope that no longer exists
    // (deleted after the dropdown loaded) returns null rather than silently showing another one.
    private static ContentSyncScope? ResolveScope(IReadOnlyList<ContentSyncScope> scopes, string? requestedScopeName)
    {
        if (string.IsNullOrEmpty(requestedScopeName))
        {
            return scopes[0];
        }

        return scopes.FirstOrDefault(scope => string.Equals(scope.Name, requestedScopeName, StringComparison.OrdinalIgnoreCase));
    }

    // As for scopes, a requested language that no longer exists returns null rather than another one.
    private static string? ResolveLanguageName(IReadOnlyList<ContentSyncLanguage> languages, string? requestedLanguageName) =>
        languages.FirstOrDefault(language => string.Equals(language.Name, requestedLanguageName, StringComparison.OrdinalIgnoreCase))?.Name;
}
