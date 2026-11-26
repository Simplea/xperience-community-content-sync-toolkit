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

    /// <summary>The compared language's code name; set once the language is resolved.</summary>
    public string? LanguageName { get; init; }

    /// <summary>Who last modified each listed item's language version here, keyed by row GUID. Only on target items have none.</summary>
    public IReadOnlyDictionary<Guid, ContentSyncModifier> Modifiers { get; init; } = new Dictionary<Guid, ContentSyncModifier>();

    public ContentSyncModifier? ModifierOf(ContentSyncStatusItem item) => Modifiers.GetValueOrDefault(item.Guid);

    public static ContentSyncStatusView Of(ContentSyncStatusViewKind kind) => new(kind, [], 0);
}

/// <summary>The filter panel's fields. Null means not filtered.</summary>
/// <param name="Status">A <see cref="ContentSyncStatusListingSupport.ParseStatusFilter"/> value.</param>
/// <param name="ContentTypeName">A content type code name.</param>
/// <param name="PublishedFrom">First day to include, from a date input (midnight).</param>
/// <param name="PublishedTo">Last day to include, from a date input (midnight).</param>
/// <param name="HideInSync">Whether to leave out items in sync; see <see cref="ContentSyncStatusListingSupport.HidesInSync"/>.</param>
/// <param name="OnlyMyChanges">Whether to list only the items the signed-in user last modified on this instance.</param>
internal sealed record ContentSyncStatusFilter(
    string? Status = null,
    string? ContentTypeName = null,
    DateTime? PublishedFrom = null,
    DateTime? PublishedTo = null,
    bool HideInSync = false,
    bool OnlyMyChanges = false);

// ScopeId (the channel's or workspace's ID) and LanguageName (a code name) come from the address, chosen
// in Kentico's dropdowns in the top bar. PageIndex is zero-based, as LoadDataSettings.SelectedPage provides it.
internal sealed record ContentSyncStatusViewRequest(
    bool IsSourceConfigured,
    int ScopeId,
    string LanguageName,
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
/// language, then target availability, then whether there's anything to list.
/// </summary>
internal static class ContentSyncStatusViewBuilder
{
    public static async Task<ContentSyncStatusView> BuildAsync(
        ContentSyncStatusViewRequest request,
        Func<CancellationToken, Task<IReadOnlyList<ContentSyncScope>>> getScopes,
        Func<CancellationToken, Task<IReadOnlyList<ContentSyncLanguage>>> getLanguages,
        Func<string, string, bool, CancellationToken, Task<ContentSyncStatusResult>> getStatus,
        Func<ContentSyncScope, string, CancellationToken, Task<IReadOnlyDictionary<Guid, ContentSyncModifier>>> getModifiers,
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

        // Only the editor's own scopes: an address with another ID (deleted, or one they can't open)
        // isn't replaced with a different scope.
        var scope = scopes.FirstOrDefault(candidate => candidate.Id == request.ScopeId);
        if (scope is null)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.ScopeNotFound);
        }

        string? languageName = ResolveLanguageName(await getLanguages(cancellationToken), request.LanguageName);
        if (languageName is null)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.LanguageNotFound);
        }

        var result = await getStatus(scope.Name, languageName, request.ForceRefresh, cancellationToken);
        if (!result.TargetAvailable)
        {
            return ContentSyncStatusView.Of(ContentSyncStatusViewKind.TargetUnavailable);
        }

        // Only items with a copy here have a modifier here, and only they can be the user's.
        var languageModifiers = await getModifiers(scope, languageName, cancellationToken);
        var modifiers = new Dictionary<Guid, ContentSyncModifier>();
        List<ContentSyncStatusItem> allItems = [];
        foreach (var item in result.Items)
        {
            var modifier = item.Local is null ? null : languageModifiers.GetValueOrDefault(item.Guid);
            if (modifier is not null)
            {
                modifiers[item.Guid] = modifier;
            }

            if (!request.Filter.OnlyMyChanges || modifier?.IsCurrentUser == true)
            {
                allItems.Add(item);
            }
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
        items = ContentSyncStatusListingSupport.ApplySort(
            items,
            request.SortBy,
            request.SortDescending,
            item => modifiers.GetValueOrDefault(item.Guid)?.Name ?? string.Empty);
        int totalCount = items.Count;
        items = ContentSyncStatusListingSupport.ApplyPaging(items, request.PageSize, request.PageIndex);

        return new ContentSyncStatusView(ContentSyncStatusViewKind.Items, items, totalCount)
        {
            Scope = scope,
            LanguageName = languageName,
            Modifiers = modifiers,
        };
    }

    /// <summary>
    /// Where a tab opens without a remembered choice: the first scope with content here, so the list
    /// isn't empty, since Xperience hides the toolbar under an empty list with no filter applied
    /// (issue #33). Only when every scope is empty, and there's nothing to switch to, the first one.
    /// Null when the editor has no scopes.
    /// </summary>
    public static async Task<ContentSyncScope?> DefaultScopeAsync(
        IReadOnlyList<ContentSyncScope> scopes, Func<ContentSyncScope, CancellationToken, Task<bool>> hasContent, CancellationToken cancellationToken)
    {
        foreach (var candidate in scopes)
        {
            if (await hasContent(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return scopes.Count > 0 ? scopes[0] : null;
    }

    // A language that no longer exists returns null rather than another one.
    private static string? ResolveLanguageName(IReadOnlyList<ContentSyncLanguage> languages, string requestedLanguageName) =>
        languages.FirstOrDefault(language => string.Equals(language.Name, requestedLanguageName, StringComparison.OrdinalIgnoreCase))?.Name;
}
