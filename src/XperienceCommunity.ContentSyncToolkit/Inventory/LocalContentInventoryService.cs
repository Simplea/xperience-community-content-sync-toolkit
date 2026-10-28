using CMS.ContentEngine;
using CMS.Core;
using CMS.DataEngine;
using CMS.Websites;

namespace XperienceCommunity.ContentSyncToolkit.Inventory;

/// <summary>
/// Lists the items Content Sync can act on: every item with a published version (compared by that
/// version), plus unpublished items, whose unpublish Content Sync can also push. Items without a
/// published version for another reason are listed too: unpublished and then edited again
/// (<see cref="ContentInventoryVersionStatus.UnpublishedDraft"/>) and never published
/// (<see cref="ContentInventoryVersionStatus.NeverPublished"/>). Content Sync can't sync them, but
/// leaving them out would make an item the other instance has look deleted here. Secured items are
/// included; Content Sync syncs them like any other. See docs/specs/content-inventory-foundation.md,
/// Publication-state scope.
/// </summary>
internal sealed class LocalContentInventoryService(
    IContentQueryExecutor contentQueryExecutor,
    IContentScopeLookup scopeLookup,
    IEventLogService eventLogService) : ILocalContentInventoryService
{
    // Items are read in batches by content item ID (keyset paging): each batch starts after the last
    // ID read, so its cost doesn't grow with how far into the list it is, as Offset's does. Each batch
    // also has a fixed cost across every content type it covers, so batches are large. Measured on a
    // 20,000-item rig: Offset with 500-item batches took 1.8-4 s, this takes 0.2-0.4 s. See
    // docs/specs/content-inventory-foundation.md, Large inventories.
    private const int PageSize = 5000;

    // The published versions. The default ForPreview = false returns each item's published version
    // even when a newer draft exists, which is what Content Sync would push.
    private static readonly ContentQueryExecutionOptions publishedOptions = new() { IncludeSecuredItems = true };

    // Items without a published version (unpublished, or Draft (Initial): never published, or
    // unpublished and edited again) need the latest versions, restricted to those statuses. A single
    // ForPreview query for everything would instead return a pending draft in place of a published
    // version, changing what published items are compared by.
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

        var contentTypeDisplayNames = await GetContentTypeDisplayNamesAsync(ClassContentTypeType.WEBSITE, cancellationToken);
        var keys = new ContentItemKeys();

        var published = await PageThroughAsync(
            afterId => QueryWebPagesAsync(websiteChannelName, languageName, contentTypeDisplayNames, keys, afterId, unpublishedOnly: false, cancellationToken), keys);
        var unpublished = await PageThroughAsync(
            afterId => QueryWebPagesAsync(websiteChannelName, languageName, contentTypeDisplayNames, keys, afterId, unpublishedOnly: true, cancellationToken), keys);

        return await WithDisplayNamesAsync(Merge(published, unpublished), keys, cancellationToken);
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
        var contentTypeDisplayNames = await GetContentTypeDisplayNamesAsync(ClassContentTypeType.REUSABLE, cancellationToken);

        if (contentTypeDisplayNames.Count == 0)
        {
            return [];
        }

        var keys = new ContentItemKeys();

        var published = await PageThroughAsync(
            afterId => QueryContentHubItemsAsync(workspaceName, languageName, contentTypeDisplayNames, keys, afterId, unpublishedOnly: false, cancellationToken), keys);
        var unpublished = await PageThroughAsync(
            afterId => QueryContentHubItemsAsync(workspaceName, languageName, contentTypeDisplayNames, keys, afterId, unpublishedOnly: true, cancellationToken), keys);

        return await WithDisplayNamesAsync(Merge(published, unpublished), keys, cancellationToken);
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

    // Only items with a variant in the language are listed. With fallbacks, the query would also return
    // an item that has none in the fallback language's data, labelled with this language; Content Sync
    // can't sync an item in a language it has no variant in. See docs/specs/content-inventory-foundation.md,
    // Inventory queries.
    private const bool UseLanguageFallbacks = false;

    private async Task<IReadOnlyList<ContentInventoryItem>> QueryWebPagesAsync(
        string websiteChannelName, string languageName, IReadOnlyDictionary<string, string> contentTypeDisplayNames,
        ContentItemKeys keys, int afterId, bool unpublishedOnly, CancellationToken cancellationToken)
    {
        var builder = new ContentItemQueryBuilder()
            .ForContentTypes(types => types.ForWebsite(websiteChannelName, PathMatch.Children("/")))
            .InLanguage(languageName, UseLanguageFallbacks)
            .Parameters(p => Page(p, afterId, unpublishedOnly).Columns(webPageColumns));

        return [.. await contentQueryExecutor.GetWebPageResult(
            builder,
            container => keys.Track(container, MapWebPage(container, websiteChannelName, languageName, contentTypeDisplayNames, unpublishedOnly)),
            unpublishedOnly ? unpublishedOptions : publishedOptions,
            cancellationToken)];
    }

    private async Task<IReadOnlyList<ContentInventoryItem>> QueryContentHubItemsAsync(
        string workspaceName, string languageName, IReadOnlyDictionary<string, string> contentTypeDisplayNames,
        ContentItemKeys keys, int afterId, bool unpublishedOnly, CancellationToken cancellationToken)
    {
        var builder = new ContentItemQueryBuilder()
            .ForContentTypes(types => types.OfContentType([.. contentTypeDisplayNames.Keys]))
            .InWorkspaces(workspaceName)
            .InLanguage(languageName, UseLanguageFallbacks)
            .Parameters(p => Page(p, afterId, unpublishedOnly).Columns(contentHubColumns));

        return [.. await contentQueryExecutor.GetResult(
            builder,
            container => keys.Track(container, MapContentHubItem(container, workspaceName, languageName, contentTypeDisplayNames, unpublishedOnly)),
            unpublishedOnly ? unpublishedOptions : publishedOptions,
            cancellationToken)];
    }

    // The batch of items after the given content item ID, in ID order.
    private static readonly string[] commonColumns =
    [
        nameof(IContentQueryDataContainer.ContentItemID),
        nameof(IContentQueryDataContainer.ContentItemCommonDataContentLanguageID),
        nameof(IContentQueryDataContainer.ContentItemCommonDataVersionStatus),
        nameof(IContentQueryDataContainer.ContentItemCommonDataLastPublishedWhen),
    ];

    private static readonly string[] webPageColumns =
    [
        .. commonColumns,
        nameof(IWebPageContentQueryDataContainer.WebPageItemGUID),
        nameof(IWebPageContentQueryDataContainer.WebPageItemTreePath),
        nameof(IWebPageContentQueryDataContainer.WebPageItemName),
        nameof(IWebPageContentQueryDataContainer.WebPageItemOrder),
    ];

    private static readonly string[] contentHubColumns =
    [
        .. commonColumns,
        nameof(IContentQueryDataContainer.ContentItemGUID),
        nameof(IContentQueryDataContainer.ContentItemName),
    ];

    private static ContentQueryParameters Page(ContentQueryParameters parameters, int afterId, bool unpublishedOnly)
    {
        if (unpublishedOnly)
        {
            parameters.Where(where => where
                .WhereEquals(nameof(IContentQueryDataContainer.ContentItemCommonDataVersionStatus), (int)VersionStatus.Unpublished)
                .Or()
                .WhereEquals(nameof(IContentQueryDataContainer.ContentItemCommonDataVersionStatus), (int)VersionStatus.InitialDraft));
        }

        parameters
            .Where(where => where.WhereGreater(nameof(IContentQueryDataContainer.ContentItemID), afterId))
            .OrderBy(nameof(IContentQueryDataContainer.ContentItemID))
            .TopN(PageSize);

        return parameters;
    }

    private static Task<IReadOnlyList<ContentInventoryItem>> PageThroughAsync(
        Func<int, Task<IReadOnlyList<ContentInventoryItem>>> queryPage, ContentItemKeys keys) =>
        PageThroughAsync(queryPage, item => keys.ContentItemIds[item.Guid], PageSize);

    // Reads batches until one comes back short. Each batch starts after the highest content item ID
    // in the previous one.
    internal static async Task<IReadOnlyList<T>> PageThroughAsync<T>(
        Func<int, Task<IReadOnlyList<T>>> queryPage, Func<T, int> contentItemIdOf, int pageSize)
    {
        var items = new List<T>();
        int afterId = 0;

        while (true)
        {
            var page = await queryPage(afterId);
            items.AddRange(page);

            if (page.Count < pageSize)
            {
                return items;
            }

            afterId = page.Max(contentItemIdOf);
        }
    }

    // Display names keyed by code name, for the given content type type (website or reusable).
    private static async Task<IReadOnlyDictionary<string, string>> GetContentTypeDisplayNamesAsync(
        string contentTypeType, CancellationToken cancellationToken)
    {
        // DataClassInfoProvider implements CMS.DataEngine.Internal.INotManagedByContainer, so
        // Kentico deliberately excludes it from DI — it has a public parameterless constructor and
        // is meant to be instantiated directly, following the classic (pre-DI) Kentico API pattern.
        var classes = await new DataClassInfoProvider().Get()
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return classes
            .Where(dataClass => dataClass.ClassContentTypeType == contentTypeType)
            .ToDictionary(dataClass => dataClass.ClassName, dataClass => dataClass.ClassDisplayName, StringComparer.OrdinalIgnoreCase);
    }

    // The name editors see in the Content hub and page tree is the item's language metadata display
    // name. The content query doesn't return it, and its Info class is in an Internal namespace, so
    // it's read in one extra query per inventory through the public generic ObjectQuery API, naming
    // the object type and columns as strings. See docs/specs/content-inventory-foundation.md,
    // Display names.
    private const string LanguageMetadataObjectType = "cms.contentitemlanguagemetadata";
    private const string MetadataContentItemIdColumn = "ContentItemLanguageMetadataContentItemID";
    private const string MetadataLanguageIdColumn = "ContentItemLanguageMetadataContentLanguageID";
    private const string MetadataDisplayNameColumn = "ContentItemLanguageMetadataDisplayName";

    // Keeps each WHERE IN list well below SQL Server's limits.
    private const int DisplayNameBatchSize = 1000;

    // Display names are a convenience: if the query fails (e.g. a version renamed the object type),
    // the inventory is returned with code names, which readers fall back to, and the error is logged.
    private async Task<IReadOnlyList<ContentInventoryItem>> WithDisplayNamesAsync(
        IReadOnlyList<ContentInventoryItem> items, ContentItemKeys keys, CancellationToken cancellationToken)
    {
        if (items.Count == 0 || keys.LanguageId is not int languageId)
        {
            return items;
        }

        var displayNames = new Dictionary<int, string>();
        try
        {
            foreach (int[] batch in keys.ContentItemIds.Values.Distinct().Chunk(DisplayNameBatchSize))
            {
                var rows = await new ObjectQuery(LanguageMetadataObjectType)
                    .Columns(MetadataContentItemIdColumn, MetadataDisplayNameColumn)
                    .WhereEquals(MetadataLanguageIdColumn, languageId)
                    .WhereIn(MetadataContentItemIdColumn, batch)
                    .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

                foreach (var row in rows)
                {
                    if (row.GetValue(MetadataDisplayNameColumn) is string displayName && displayName.Length > 0)
                    {
                        displayNames[Convert.ToInt32(row.GetValue(MetadataContentItemIdColumn))] = displayName;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            eventLogService.LogException(nameof(LocalContentInventoryService), "DISPLAYNAMES", ex);
            return items;
        }

        return [.. items.Select(item =>
            keys.ContentItemIds.TryGetValue(item.Guid, out int contentItemId) && displayNames.TryGetValue(contentItemId, out string? displayName)
                ? item with { DisplayName = displayName }
                : item)];
    }

    // Remembers each mapped item's content item ID and the query's language ID, which the inventory
    // doesn't carry but the display name query and paging need. The content query maps rows one at a
    // time.
    private sealed class ContentItemKeys
    {
        public Dictionary<Guid, int> ContentItemIds { get; } = [];

        public int? LanguageId { get; private set; }

        public ContentInventoryItem Track(IContentQueryDataContainer container, ContentInventoryItem item)
        {
            ContentItemIds[item.Guid] = container.ContentItemID;
            LanguageId ??= container.ContentItemCommonDataContentLanguageID;
            return item;
        }
    }

    private static string? ContentTypeDisplayNameOf(string contentTypeName, IReadOnlyDictionary<string, string> contentTypeDisplayNames) =>
        contentTypeDisplayNames.GetValueOrDefault(contentTypeName);

    // Unpublished rows get the status by constant rather than VersionStatus.ToString(): 30.8.0 also
    // declares Archived with the same value, and .NET doesn't guarantee which name ToString() picks.
    // The unpublished query's other rows are Draft (Initial): a last publish date tells an item
    // unpublished and edited again from one never published.
    private static string VersionStatusOf(IContentQueryDataContainer container, bool unpublished)
    {
        if (!unpublished)
        {
            return container.ContentItemCommonDataVersionStatus.ToString();
        }

        if (container.ContentItemCommonDataVersionStatus != VersionStatus.InitialDraft)
        {
            return ContentInventoryVersionStatus.Unpublished;
        }

        return container.ContentItemCommonDataLastPublishedWhen is null
            ? ContentInventoryVersionStatus.NeverPublished
            : ContentInventoryVersionStatus.UnpublishedDraft;
    }

    private static ContentInventoryItem MapWebPage(
        IWebPageContentQueryDataContainer container, string websiteChannelName, string languageName,
        IReadOnlyDictionary<string, string> contentTypeDisplayNames, bool unpublished) =>
        new(
            container.WebPageItemGUID,
            ContentInventoryItemKind.WebPage,
            container.ContentTypeName,
            websiteChannelName,
            languageName,
            container.WebPageItemTreePath,
            ContentInventoryTime.ToUtc(container.ContentItemCommonDataLastPublishedWhen),
            VersionStatusOf(container, unpublished))
        {
            Name = container.WebPageItemName,
            Order = container.WebPageItemOrder,
            ContentTypeDisplayName = ContentTypeDisplayNameOf(container.ContentTypeName, contentTypeDisplayNames),
        };

    private static ContentInventoryItem MapContentHubItem(
        IContentQueryDataContainer container, string workspaceName, string languageName,
        IReadOnlyDictionary<string, string> contentTypeDisplayNames, bool unpublished) =>
        new(
            container.ContentItemGUID,
            ContentInventoryItemKind.ContentHubItem,
            container.ContentTypeName,
            workspaceName,
            languageName,
            null,
            ContentInventoryTime.ToUtc(container.ContentItemCommonDataLastPublishedWhen),
            VersionStatusOf(container, unpublished))
        {
            Name = container.ContentItemName,
            ContentTypeDisplayName = ContentTypeDisplayNameOf(container.ContentTypeName, contentTypeDisplayNames),
        };
}
