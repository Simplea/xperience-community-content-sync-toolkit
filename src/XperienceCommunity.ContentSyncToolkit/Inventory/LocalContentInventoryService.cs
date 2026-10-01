using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Websites;

namespace XperienceCommunity.ContentSyncToolkit.Inventory;

/// <summary>
/// Lists the items Content Sync can act on: every item with a published version (compared by that
/// version), plus unpublished items, whose unpublish Content Sync can also push. Secured items are
/// included; Content Sync syncs them like any other. See docs/specs/content-inventory-foundation.md,
/// Publication-state scope.
/// </summary>
internal sealed class LocalContentInventoryService(
    IContentQueryExecutor contentQueryExecutor,
    IContentScopeLookup scopeLookup) : ILocalContentInventoryService
{
    // Offset-based paging requires OrderBy for consistent results; TopN cannot be combined with
    // Offset in the same query scope, so this is the only paging mechanism used here.
    private const int PageSize = 500;

    // The published versions. The default ForPreview = false returns each item's published version
    // even when a newer draft exists, which is what Content Sync would push.
    private static readonly ContentQueryExecutionOptions publishedOptions = new() { IncludeSecuredItems = true };

    // Unpublished items have no published version, so they need the latest versions, restricted to
    // the Unpublished status. A single ForPreview query for everything would instead return a pending
    // draft in place of a published version, changing what published items are compared by.
    private static readonly ContentQueryExecutionOptions unpublishedOptions = new() { ForPreview = true, IncludeSecuredItems = true };

    // An unknown channel or language is an empty inventory, not an error: the content query would
    // throw, and the target endpoint must not confirm or deny that a scope exists.
    public async Task<IReadOnlyList<ContentInventoryItem>> GetWebPagesAsync(
        string websiteChannelName, string languageName, CancellationToken cancellationToken)
    {
        if (!await scopeLookup.WebsiteChannelExistsAsync(websiteChannelName, cancellationToken)
            || !await scopeLookup.ContentLanguageExistsAsync(languageName, cancellationToken))
        {
            return [];
        }

        var published = await PageThroughAsync(
            offset => QueryWebPagesAsync(websiteChannelName, languageName, offset, unpublishedOnly: false, cancellationToken));
        var unpublished = await PageThroughAsync(
            offset => QueryWebPagesAsync(websiteChannelName, languageName, offset, unpublishedOnly: true, cancellationToken));

        return Merge(published, unpublished);
    }

    public async Task<IReadOnlyList<ContentInventoryItem>> GetContentHubItemsAsync(
        string workspaceName, string languageName, CancellationToken cancellationToken)
    {
        // An unknown workspace already matches nothing; an unknown language would throw.
        if (!await scopeLookup.ContentLanguageExistsAsync(languageName, cancellationToken))
        {
            return [];
        }

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

        var published = await PageThroughAsync(
            offset => QueryContentHubItemsAsync(workspaceName, languageName, reusableContentTypeNames, offset, unpublishedOnly: false, cancellationToken));
        var unpublished = await PageThroughAsync(
            offset => QueryContentHubItemsAsync(workspaceName, languageName, reusableContentTypeNames, offset, unpublishedOnly: true, cancellationToken));

        return Merge(published, unpublished);
    }

    // An item has either a published version or an unpublished one, never both, but the published
    // row wins should both ever appear, so a GUID is never listed twice.
    internal static IReadOnlyList<ContentInventoryItem> Merge(
        IReadOnlyList<ContentInventoryItem> published, IReadOnlyList<ContentInventoryItem> unpublished)
    {
        if (unpublished.Count == 0)
        {
            return published;
        }

        var publishedGuids = published.Select(item => item.Guid).ToHashSet();

        return [.. published, .. unpublished.Where(item => !publishedGuids.Contains(item.Guid))];
    }

    private async Task<IReadOnlyList<ContentInventoryItem>> QueryWebPagesAsync(
        string websiteChannelName, string languageName, int offset, bool unpublishedOnly, CancellationToken cancellationToken)
    {
        var builder = new ContentItemQueryBuilder()
            .ForContentTypes(types => types.ForWebsite(websiteChannelName, PathMatch.Children("/")))
            .InLanguage(languageName)
            .Parameters(p => Page(p, offset, unpublishedOnly));

        return [.. await contentQueryExecutor.GetWebPageResult(
            builder,
            container => MapWebPage(container, websiteChannelName, languageName, unpublishedOnly),
            unpublishedOnly ? unpublishedOptions : publishedOptions,
            cancellationToken)];
    }

    private async Task<IReadOnlyList<ContentInventoryItem>> QueryContentHubItemsAsync(
        string workspaceName, string languageName, string[] contentTypeNames, int offset, bool unpublishedOnly, CancellationToken cancellationToken)
    {
        var builder = new ContentItemQueryBuilder()
            .ForContentTypes(types => types.OfContentType(contentTypeNames))
            .InWorkspaces(workspaceName)
            .InLanguage(languageName)
            .Parameters(p => Page(p, offset, unpublishedOnly));

        return [.. await contentQueryExecutor.GetResult(
            builder,
            container => MapContentHubItem(container, workspaceName, languageName, unpublishedOnly),
            unpublishedOnly ? unpublishedOptions : publishedOptions,
            cancellationToken)];
    }

    private static void Page(ContentQueryParameters parameters, int offset, bool unpublishedOnly)
    {
        if (unpublishedOnly)
        {
            parameters.Where(where => where.WhereEquals(
                nameof(IContentQueryDataContainer.ContentItemCommonDataVersionStatus), (int)VersionStatus.Unpublished));
        }

        parameters
            .OrderBy(nameof(IContentQueryDataContainer.ContentItemID))
            .Offset(offset, PageSize);
    }

    private static async Task<IReadOnlyList<ContentInventoryItem>> PageThroughAsync(
        Func<int, Task<IReadOnlyList<ContentInventoryItem>>> queryPage)
    {
        var items = new List<ContentInventoryItem>();
        int offset = 0;

        while (true)
        {
            var page = await queryPage(offset);
            items.AddRange(page);

            if (page.Count < PageSize)
            {
                return items;
            }

            offset += PageSize;
        }
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

    // Unpublished rows get the status by constant rather than VersionStatus.ToString(): 30.8.0 also
    // declares Archived with the same value, and .NET doesn't guarantee which name ToString() picks.
    private static string VersionStatusOf(IContentQueryDataContainer container, bool unpublished) =>
        unpublished ? ContentInventoryVersionStatus.Unpublished : container.ContentItemCommonDataVersionStatus.ToString();

    private static ContentInventoryItem MapWebPage(
        IWebPageContentQueryDataContainer container, string websiteChannelName, string languageName, bool unpublished) =>
        new(
            container.WebPageItemGUID,
            ContentInventoryItemKind.WebPage,
            container.ContentTypeName,
            websiteChannelName,
            languageName,
            container.WebPageItemTreePath,
            container.ContentItemCommonDataLastPublishedWhen,
            VersionStatusOf(container, unpublished))
        {
            Name = container.WebPageItemName,
        };

    private static ContentInventoryItem MapContentHubItem(
        IContentQueryDataContainer container, string workspaceName, string languageName, bool unpublished) =>
        new(
            container.ContentItemGUID,
            ContentInventoryItemKind.ContentHubItem,
            container.ContentTypeName,
            workspaceName,
            languageName,
            null,
            container.ContentItemCommonDataLastPublishedWhen,
            VersionStatusOf(container, unpublished))
        {
            Name = container.ContentItemName,
        };
}
