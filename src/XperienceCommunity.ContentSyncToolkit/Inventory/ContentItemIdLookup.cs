using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Websites;
using CMS.Workspaces;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Http;

namespace XperienceCommunity.ContentSyncToolkit.Inventory;

/// <summary>This instance's IDs for items a source knows by GUID, and the scope's ID.</summary>
/// <param name="ScopeId">The <c>WebsiteChannelID</c> or <c>WorkspaceID</c>; null when the scope or language doesn't exist.</param>
/// <param name="Ids">The <c>WebPageItemID</c> or <c>ContentItemID</c> of each GUID found.</param>
public sealed record ContentItemIds(int? ScopeId, IReadOnlyDictionary<Guid, int> Ids)
{
    public static ContentItemIds None { get; } = new(null, new Dictionary<Guid, int>());
}

/// <summary>
/// Answers the target's item ID endpoints, which let a source link to items that exist only here
/// (see docs/specs/target-item-links.md). The IDs come from the same lookup the source uses for its
/// own rows. As with the inventory, an unknown channel, workspace or language is an empty answer,
/// not an error.
/// </summary>
public interface IContentItemIdLookup
{
    public Task<ContentItemIds> GetWebPageIdsAsync(
        string channelName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken);

    public Task<ContentItemIds> GetContentItemIdsAsync(
        string workspaceName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken);
}

internal sealed class ContentItemIdLookup(
    IContentScopeLookup scopeLookup,
    IContentSyncItemIdResolver itemIdResolver,
    IInfoProvider<ChannelInfo> channelInfoProvider,
    IInfoProvider<WebsiteChannelInfo> websiteChannelInfoProvider,
    IInfoProvider<WorkspaceInfo> workspaceInfoProvider) : IContentItemIdLookup
{
    public async Task<ContentItemIds> GetWebPageIdsAsync(
        string channelName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken)
    {
        if (!await scopeLookup.WebsiteChannelExistsAsync(channelName, cancellationToken)
            || !await scopeLookup.ContentLanguageExistsAsync(languageName, cancellationToken))
        {
            return ContentItemIds.None;
        }

        // A website channel's name is on its ChannelInfo; admin URLs use the WebsiteChannelID.
        var channel = (await channelInfoProvider.Get()
            .WhereEquals(nameof(ChannelInfo.ChannelName), channelName)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)).First();
        var websiteChannel = (await websiteChannelInfoProvider.Get()
            .WhereEquals(nameof(WebsiteChannelInfo.WebsiteChannelChannelID), channel.ChannelID)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)).First();

        var items = await itemIdResolver.GetWebPageItemsAsync(channelName, languageName, guids, cancellationToken);

        return new ContentItemIds(websiteChannel.WebsiteChannelID, items.ToDictionary(item => item.Key, item => item.Value.Id));
    }

    public async Task<ContentItemIds> GetContentItemIdsAsync(
        string workspaceName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken)
    {
        if (!await scopeLookup.ContentLanguageExistsAsync(languageName, cancellationToken))
        {
            return ContentItemIds.None;
        }

        var workspace = (await workspaceInfoProvider.Get()
            .WhereEquals(nameof(WorkspaceInfo.WorkspaceName), workspaceName)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)).FirstOrDefault();
        if (workspace is null)
        {
            return ContentItemIds.None;
        }

        // Every reusable type, so the source doesn't have to name them (see LocalContentInventoryService).
        // DataClassInfoProvider is meant to be instantiated directly; see LocalContentInventoryService.
        var reusableTypes = (await new DataClassInfoProvider().Get()
            .WhereEquals(nameof(DataClassInfo.ClassContentTypeType), ClassContentTypeType.REUSABLE)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
            .Select(dataClass => dataClass.ClassName)
            .ToList();

        var items = await itemIdResolver.GetContentItemsAsync(workspaceName, languageName, reusableTypes, guids, cancellationToken);

        return new ContentItemIds(workspace.WorkspaceID, items.ToDictionary(item => item.Key, item => item.Value.Id));
    }

    // GUIDs come comma-separated in the query string from a remote caller: values that aren't GUIDs
    // are skipped, and at most MaxItemIdGuids are looked up.
    internal static IReadOnlyCollection<Guid> ParseGuids(string? guids) =>
        string.IsNullOrEmpty(guids)
            ? []
            : [.. guids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Guid.TryParse(value, out var guid) ? guid : (Guid?)null)
                .OfType<Guid>()
                .Distinct()
                .Take(ContentSyncToolkitConstants.MaxItemIdGuids)];
}
