using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <param name="Name">The language code name the toolkit's services take (e.g. <c>en</c>).</param>
/// <param name="DisplayName">The name shown in the Language filter.</param>
/// <param name="IsDefault">Whether this is the instance's default content language.</param>
internal sealed record ContentSyncLanguage(string Name, string DisplayName, bool IsDefault);

/// <param name="Name">The content type's code name, as inventory items carry it.</param>
/// <param name="DisplayName">The name shown in the Content type filter.</param>
internal sealed record ContentSyncContentType(string Name, string DisplayName);

/// <summary>
/// Supplies the sync status admin page's Language and Content type filter options, and the
/// languages a tab resolves its comparison language from.
/// </summary>
internal interface IContentSyncFilterOptionsProvider
{
    public Task<IReadOnlyList<ContentSyncLanguage>> GetContentLanguagesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Website content types for <see cref="ContentInventoryItemKind.WebPage"/>, reusable content
    /// types for <see cref="ContentInventoryItemKind.ContentHubItem"/>.
    /// </summary>
    public Task<IReadOnlyList<ContentSyncContentType>> GetContentTypesAsync(ContentInventoryItemKind kind, CancellationToken cancellationToken);
}
