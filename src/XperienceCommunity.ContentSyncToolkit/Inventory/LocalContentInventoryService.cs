using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Websites;

namespace XperienceCommunity.ContentSyncToolkit.Inventory;

internal sealed class LocalContentInventoryService(IContentQueryExecutor contentQueryExecutor) : ILocalContentInventoryService
{
    // Offset-based paging requires OrderBy for consistent results; TopN cannot be combined with
    // Offset in the same query scope, so this is the only paging mechanism used here.
    private const int PageSize = 500;

    public async Task<IReadOnlyList<ContentInventoryItem>> GetWebPagesAsync(
        string websiteChannelName, string languageName, CancellationToken cancellationToken)
    {
        var items = new List<ContentInventoryItem>();
        int offset = 0;

        while (true)
        {
            var builder = new ContentItemQueryBuilder()
                .ForContentTypes(types => types.ForWebsite(websiteChannelName, PathMatch.Children("/")))
                .InLanguage(languageName)
                .Parameters(p => p
                    .OrderBy(nameof(IContentQueryDataContainer.ContentItemID))
                    .Offset(offset, PageSize));

            var page = (await contentQueryExecutor.GetWebPageResult(
                builder,
                container => MapWebPage(container, websiteChannelName, languageName),
                cancellationToken: cancellationToken)).ToList();

            items.AddRange(page);

            if (page.Count < PageSize)
            {
                break;
            }

            offset += PageSize;
        }

        return items;
    }

    public async Task<IReadOnlyList<ContentInventoryItem>> GetContentHubItemsAsync(
        string workspaceName, string languageName, CancellationToken cancellationToken)
    {
        // ContentItemQueryBuilder requires an explicit content-type list — it does not default to
        // "all types" for an unfiltered multi-type query (confirmed against a live instance: an
        // empty ForContentTypes configuration throws "Cannot generate query without limiting
        // content types"). Reusable content-hub items can be of any reusable content type, so every
        // reusable type name is fetched dynamically rather than requiring the caller to know them.
        string[] reusableContentTypeNames = await GetReusableContentTypeNamesAsync(cancellationToken);

        if (reusableContentTypeNames.Length == 0)
        {
            return [];
        }

        var items = new List<ContentInventoryItem>();
        int offset = 0;

        while (true)
        {
            var builder = new ContentItemQueryBuilder()
                .ForContentTypes(types => types.OfContentType(reusableContentTypeNames))
                .InWorkspaces(workspaceName)
                .InLanguage(languageName)
                .Parameters(p => p
                    .OrderBy(nameof(IContentQueryDataContainer.ContentItemID))
                    .Offset(offset, PageSize));

            var page = (await contentQueryExecutor.GetResult(
                builder,
                container => MapContentHubItem(container, workspaceName, languageName),
                cancellationToken: cancellationToken)).ToList();

            items.AddRange(page);

            if (page.Count < PageSize)
            {
                break;
            }

            offset += PageSize;
        }

        return items;
    }

    private static async Task<string[]> GetReusableContentTypeNamesAsync(CancellationToken cancellationToken)
    {
        // DataClassInfoProvider implements CMS.DataEngine.Internal.INotManagedByContainer, so
        // Kentico deliberately excludes it from DI — it has a public parameterless constructor and
        // is meant to be instantiated directly, following the classic (pre-DI) Kentico API pattern.
        var classes = await new DataClassInfoProvider().Get()
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return [.. classes
            .Where(dataClass => dataClass.ClassContentTypeType == ClassContentTypeType.REUSABLE)
            .Select(dataClass => dataClass.ClassName)];
    }

    private static ContentInventoryItem MapWebPage(
        IWebPageContentQueryDataContainer container, string websiteChannelName, string languageName) =>
        new(
            container.WebPageItemGUID,
            ContentInventoryItemKind.WebPage,
            container.ContentTypeName,
            websiteChannelName,
            languageName,
            container.WebPageItemTreePath,
            container.ContentItemCommonDataLastPublishedWhen,
            container.ContentItemCommonDataVersionStatus.ToString());

    private static ContentInventoryItem MapContentHubItem(
        IContentQueryDataContainer container, string workspaceName, string languageName) =>
        new(
            container.ContentItemGUID,
            ContentInventoryItemKind.ContentHubItem,
            container.ContentTypeName,
            workspaceName,
            languageName,
            null,
            container.ContentItemCommonDataLastPublishedWhen,
            container.ContentItemCommonDataVersionStatus.ToString());
}
