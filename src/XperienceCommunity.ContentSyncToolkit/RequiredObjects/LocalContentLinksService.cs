using CMS.ContentEngine;
using CMS.Core;
using CMS.DataEngine;

using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.RequiredObjects;

/// <summary>Reads what this instance's content links to, for the required objects check.</summary>
internal interface ILocalContentLinksService
{
    /// <summary>The links of the published and unpublished versions in a language, which is what a sync sends.</summary>
    public Task<ContentLinkGraph> GetLinkGraphAsync(string languageName, ContentInventoryItemKind kind, CancellationToken cancellationToken);

    /// <summary>The display names of content items in a language, by content item ID.</summary>
    public Task<IReadOnlyDictionary<int, string>> GetDisplayNamesAsync(
        string languageName, IReadOnlyCollection<int> contentItemIds, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the tables by object type name, as for display names, since their Info classes are in Internal
/// namespaces. Only called when the target has an incompatible content type. If a query fails, items
/// are checked by their own objects only, as a status that's too optimistic beats no status; the error
/// is logged. See docs/specs/content-inventory-foundation.md, Required objects.
/// </summary>
internal sealed class LocalContentLinksService(
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider,
    IEventLogService eventLogService) : ILocalContentLinksService
{
    private const string ContentItemObjectType = "cms.contentitem";
    private const string ContentItemIdColumn = "ContentItemID";
    private const string ContentItemGuidColumn = "ContentItemGUID";
    private const string ContentItemTypeColumn = "ContentItemContentTypeID";

    private const string CommonDataObjectType = "cms.contentitemcommondata";
    private const string CommonDataIdColumn = "ContentItemCommonDataID";
    private const string CommonDataItemColumn = "ContentItemCommonDataContentItemID";
    private const string CommonDataLanguageColumn = "ContentItemCommonDataContentLanguageID";
    private const string CommonDataStatusColumn = "ContentItemCommonDataVersionStatus";

    private const string ReferenceObjectType = "cms.contentitemreference";
    private const string ReferenceSourceColumn = "ContentItemReferenceSourceCommonDataID";
    private const string ReferenceTargetColumn = "ContentItemReferenceTargetItemID";

    private const string WebPageObjectType = "cms.webpageitem";
    private const string WebPageGuidColumn = "WebPageItemGUID";
    private const string WebPageItemColumn = "WebPageItemContentItemID";

    private const string MetadataObjectType = "cms.contentitemlanguagemetadata";
    private const string MetadataItemColumn = "ContentItemLanguageMetadataContentItemID";
    private const string MetadataLanguageColumn = "ContentItemLanguageMetadataContentLanguageID";
    private const string MetadataDisplayNameColumn = "ContentItemLanguageMetadataDisplayName";

    public async Task<ContentLinkGraph> GetLinkGraphAsync(string languageName, ContentInventoryItemKind kind, CancellationToken cancellationToken)
    {
        try
        {
            if (await GetLanguageIdAsync(languageName, cancellationToken) is not int languageId)
            {
                return ContentLinkGraph.Empty;
            }

            // DataClassInfoProvider is meant to be instantiated directly; see LocalContentInventoryService.
            var typeNames = (await new DataClassInfoProvider().Get().GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
                .ToDictionary(dataClass => dataClass.ClassID, dataClass => dataClass.ClassName);

            var itemIds = new Dictionary<Guid, int>();
            var contentTypes = new Dictionary<int, string>();
            foreach (var row in await new ObjectQuery(ContentItemObjectType)
                .Columns(ContentItemIdColumn, ContentItemGuidColumn, ContentItemTypeColumn)
                .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
            {
                int itemId = Convert.ToInt32(row.GetValue(ContentItemIdColumn));
                itemIds[(Guid)row.GetValue(ContentItemGuidColumn)] = itemId;

                if (typeNames.TryGetValue(Convert.ToInt32(row.GetValue(ContentItemTypeColumn)), out string? typeName))
                {
                    contentTypes[itemId] = typeName;
                }
            }

            if (kind == ContentInventoryItemKind.WebPage)
            {
                foreach (var row in await new ObjectQuery(WebPageObjectType)
                    .Columns(WebPageGuidColumn, WebPageItemColumn)
                    .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
                {
                    itemIds[(Guid)row.GetValue(WebPageGuidColumn)] = Convert.ToInt32(row.GetValue(WebPageItemColumn));
                }
            }

            return new ContentLinkGraph(itemIds, contentTypes, await GetLinksAsync(languageId, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            eventLogService.LogException(nameof(LocalContentLinksService), "LINKS", ex);
            return ContentLinkGraph.Empty;
        }
    }

    public async Task<IReadOnlyDictionary<int, string>> GetDisplayNamesAsync(
        string languageName, IReadOnlyCollection<int> contentItemIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<int, string>();

        try
        {
            if (contentItemIds.Count == 0 || await GetLanguageIdAsync(languageName, cancellationToken) is not int languageId)
            {
                return names;
            }

            foreach (var row in await new ObjectQuery(MetadataObjectType)
                .Columns(MetadataItemColumn, MetadataDisplayNameColumn)
                .WhereEquals(MetadataLanguageColumn, languageId)
                .WhereIn(MetadataItemColumn, contentItemIds.ToList())
                .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
            {
                names[Convert.ToInt32(row.GetValue(MetadataItemColumn))] = Convert.ToString(row.GetValue(MetadataDisplayNameColumn)) ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            eventLogService.LogException(nameof(LocalContentLinksService), "LINKNAMES", ex);
        }

        return names;
    }

    private static async Task<IReadOnlyDictionary<int, IReadOnlyCollection<int>>> GetLinksAsync(int languageId, CancellationToken cancellationToken)
    {
        // Published and unpublished versions: a sync sends the published version of an item, or its
        // unpublish. Drafts aren't sent, so what only a draft links to doesn't count.
        var itemByVersion = new Dictionary<int, int>();
        foreach (var row in await new ObjectQuery(CommonDataObjectType)
            .Columns(CommonDataIdColumn, CommonDataItemColumn)
            .WhereEquals(CommonDataLanguageColumn, languageId)
            .WhereIn(CommonDataStatusColumn, new[] { (int)VersionStatus.Published, (int)VersionStatus.Unpublished })
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
        {
            itemByVersion[Convert.ToInt32(row.GetValue(CommonDataIdColumn))] = Convert.ToInt32(row.GetValue(CommonDataItemColumn));
        }

        var links = new Dictionary<int, HashSet<int>>();
        foreach (var row in await new ObjectQuery(ReferenceObjectType)
            .Columns(ReferenceSourceColumn, ReferenceTargetColumn)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
        {
            if (itemByVersion.TryGetValue(Convert.ToInt32(row.GetValue(ReferenceSourceColumn)), out int sourceItem))
            {
                if (!links.TryGetValue(sourceItem, out var targets))
                {
                    links[sourceItem] = targets = [];
                }

                targets.Add(Convert.ToInt32(row.GetValue(ReferenceTargetColumn)));
            }
        }

        return links.ToDictionary(link => link.Key, link => (IReadOnlyCollection<int>)link.Value);
    }

    private async Task<int?> GetLanguageIdAsync(string languageName, CancellationToken cancellationToken) =>
        (await contentLanguageInfoProvider.Get()
            .WhereEquals(nameof(ContentLanguageInfo.ContentLanguageName), languageName)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
            .FirstOrDefault()?.ContentLanguageID;
}
