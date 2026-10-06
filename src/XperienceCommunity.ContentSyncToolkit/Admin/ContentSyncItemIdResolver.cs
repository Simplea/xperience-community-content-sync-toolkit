using CMS.ContentEngine;
using CMS.Websites;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>A local item's ID and whether its latest version is a draft newer than what's published.</summary>
/// <param name="Id">The item's ID, which admin editor URLs need.</param>
/// <param name="HasNewerDraft">
/// Whether the latest version is a Draft (New version) of a published item. Content Sync sends the
/// published version, so the draft isn't compared; the admin page only mentions it.
/// </param>
internal sealed record ContentSyncLocalItem(int Id, bool HasNewerDraft);

/// <summary>
/// Looks up local item IDs for inventory GUIDs, which admin editor URLs need, and whether each item
/// has a newer draft. Neither is part of the inventory because the inventory is also the target's
/// wire contract, where a local ID means nothing and drafts aren't synced. Called for one listing
/// page of rows at a time, and in batches for page permissions.
/// </summary>
internal interface IContentSyncItemIdResolver
{
    /// <summary>Maps <c>WebPageItemGUID</c> to the page's <c>WebPageItemID</c> and draft state.</summary>
    public Task<IReadOnlyDictionary<Guid, ContentSyncLocalItem>> GetWebPageItemsAsync(
        string websiteChannelName, string languageName, IReadOnlyCollection<Guid> webPageItemGuids, CancellationToken cancellationToken);

    /// <summary>Maps <c>ContentItemGUID</c> to the item's <c>ContentItemID</c> and draft state.</summary>
    public Task<IReadOnlyDictionary<Guid, ContentSyncLocalItem>> GetContentItemsAsync(
        string workspaceName, string languageName, IReadOnlyCollection<string> contentTypeNames,
        IReadOnlyCollection<Guid> contentItemGuids, CancellationToken cancellationToken);
}

internal sealed class ContentSyncItemIdResolver(IContentQueryExecutor contentQueryExecutor) : IContentSyncItemIdResolver
{
    // Latest versions and secured items, so every item the inventory lists (including unpublished and
    // secured ones) resolves; an item's ID is the same in every version. The latest version is also
    // what tells a pending draft apart: for a published item with a newer draft, it's the draft.
    private static readonly ContentQueryExecutionOptions options = new() { ForPreview = true, IncludeSecuredItems = true };

    public async Task<IReadOnlyDictionary<Guid, ContentSyncLocalItem>> GetWebPageItemsAsync(
        string websiteChannelName, string languageName, IReadOnlyCollection<Guid> webPageItemGuids, CancellationToken cancellationToken)
    {
        if (webPageItemGuids.Count == 0)
        {
            return new Dictionary<Guid, ContentSyncLocalItem>();
        }

        var builder = new ContentItemQueryBuilder()
            .ForContentTypes(types => types.ForWebsite(websiteChannelName, PathMatch.Children("/")))
            .InLanguage(languageName)
            .Parameters(p => p.Where(where => where.WhereIn(nameof(IWebPageContentQueryDataContainer.WebPageItemGUID), webPageItemGuids)));

        var items = await contentQueryExecutor.GetWebPageResult(
            builder,
            container => (container.WebPageItemGUID, Item: new ContentSyncLocalItem(container.WebPageItemID, IsNewerDraft(container))),
            options,
            cancellationToken);

        return items.ToDictionary(item => item.WebPageItemGUID, item => item.Item);
    }

    public async Task<IReadOnlyDictionary<Guid, ContentSyncLocalItem>> GetContentItemsAsync(
        string workspaceName, string languageName, IReadOnlyCollection<string> contentTypeNames,
        IReadOnlyCollection<Guid> contentItemGuids, CancellationToken cancellationToken)
    {
        // A content item query must name its content types (see LocalContentInventoryService).
        if (contentItemGuids.Count == 0 || contentTypeNames.Count == 0)
        {
            return new Dictionary<Guid, ContentSyncLocalItem>();
        }

        var builder = new ContentItemQueryBuilder()
            .ForContentTypes(types => types.OfContentType([.. contentTypeNames]))
            .InWorkspaces(workspaceName)
            .InLanguage(languageName)
            .Parameters(p => p.Where(where => where.WhereIn(nameof(IContentQueryDataContainer.ContentItemGUID), contentItemGuids)));

        var items = await contentQueryExecutor.GetResult(
            builder,
            container => (container.ContentItemGUID, Item: new ContentSyncLocalItem(container.ContentItemID, IsNewerDraft(container))),
            options,
            cancellationToken);

        return items.ToDictionary(item => item.ContentItemGUID, item => item.Item);
    }

    // Draft is Draft (New version): a draft of an item published before. A never-published item is
    // InitialDraft, and the inventory doesn't list it.
    private static bool IsNewerDraft(IContentQueryDataContainer container) =>
        container.ContentItemCommonDataVersionStatus == VersionStatus.Draft;
}
