using CMS.ContentEngine;
using CMS.Websites;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Looks up local item IDs for inventory GUIDs, which admin editor URLs need. IDs aren't part of
/// the inventory because the inventory is also the target's wire contract, where a local ID means
/// nothing. Called for one listing page of rows at a time.
/// </summary>
internal interface IContentSyncItemIdResolver
{
    /// <summary>Maps <c>WebPageItemGUID</c> to <c>WebPageItemID</c>.</summary>
    public Task<IReadOnlyDictionary<Guid, int>> GetWebPageItemIdsAsync(
        string websiteChannelName, string languageName, IReadOnlyCollection<Guid> webPageItemGuids, CancellationToken cancellationToken);

    /// <summary>Maps <c>ContentItemGUID</c> to <c>ContentItemID</c>.</summary>
    public Task<IReadOnlyDictionary<Guid, int>> GetContentItemIdsAsync(
        string workspaceName, string languageName, IReadOnlyCollection<string> contentTypeNames,
        IReadOnlyCollection<Guid> contentItemGuids, CancellationToken cancellationToken);
}

internal sealed class ContentSyncItemIdResolver(IContentQueryExecutor contentQueryExecutor) : IContentSyncItemIdResolver
{
    public async Task<IReadOnlyDictionary<Guid, int>> GetWebPageItemIdsAsync(
        string websiteChannelName, string languageName, IReadOnlyCollection<Guid> webPageItemGuids, CancellationToken cancellationToken)
    {
        if (webPageItemGuids.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var builder = new ContentItemQueryBuilder()
            .ForContentTypes(types => types.ForWebsite(websiteChannelName, PathMatch.Children("/")))
            .InLanguage(languageName)
            .Parameters(p => p.Where(where => where.WhereIn(nameof(IWebPageContentQueryDataContainer.WebPageItemGUID), webPageItemGuids)));

        var ids = await contentQueryExecutor.GetWebPageResult(
            builder,
            container => (container.WebPageItemGUID, container.WebPageItemID),
            cancellationToken: cancellationToken);

        return ids.ToDictionary(id => id.WebPageItemGUID, id => id.WebPageItemID);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetContentItemIdsAsync(
        string workspaceName, string languageName, IReadOnlyCollection<string> contentTypeNames,
        IReadOnlyCollection<Guid> contentItemGuids, CancellationToken cancellationToken)
    {
        // A content item query must name its content types (see LocalContentInventoryService).
        if (contentItemGuids.Count == 0 || contentTypeNames.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var builder = new ContentItemQueryBuilder()
            .ForContentTypes(types => types.OfContentType([.. contentTypeNames]))
            .InWorkspaces(workspaceName)
            .InLanguage(languageName)
            .Parameters(p => p.Where(where => where.WhereIn(nameof(IContentQueryDataContainer.ContentItemGUID), contentItemGuids)));

        var ids = await contentQueryExecutor.GetResult(
            builder,
            container => (container.ContentItemGUID, container.ContentItemID),
            cancellationToken: cancellationToken);

        return ids.ToDictionary(id => id.ContentItemGUID, id => id.ContentItemID);
    }
}
