using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Websites;
using CMS.Workspaces;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncScopeProvider(
    IInfoProvider<WebsiteChannelInfo> websiteChannelInfoProvider,
    IInfoProvider<ChannelInfo> channelInfoProvider,
    IInfoProvider<WorkspaceInfo> workspaceInfoProvider) : IContentSyncScopeProvider
{
    public async Task<IReadOnlyList<ContentSyncScope>> GetWebsiteChannelsAsync(CancellationToken cancellationToken)
    {
        var websiteChannels = await websiteChannelInfoProvider.Get()
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        // WebsiteChannelInfo carries no name of its own; the channel name/display name Xperience's
        // own content-query APIs expect (e.g. ForWebsite(channelName, ...)) lives on the linked
        // ChannelInfo, joined by WebsiteChannelChannelID == ChannelID.
        var channels = await channelInfoProvider.Get()
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        var websiteChannelsById = channels
            .Where(channel => channel.ChannelType == ChannelType.Website)
            .ToDictionary(channel => channel.ChannelID);

        return [.. websiteChannels
            .Where(websiteChannel => websiteChannelsById.ContainsKey(websiteChannel.WebsiteChannelChannelID))
            .Select(websiteChannel =>
            {
                var channel = websiteChannelsById[websiteChannel.WebsiteChannelChannelID];
                return new ContentSyncScope(websiteChannel.WebsiteChannelID, channel.ChannelName, channel.ChannelDisplayName);
            })
            .OrderBy(scope => scope.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<IReadOnlyList<ContentSyncScope>> GetWorkspacesAsync(CancellationToken cancellationToken)
    {
        var workspaces = await workspaceInfoProvider.Get()
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return [.. workspaces
            .Select(workspace => new ContentSyncScope(workspace.WorkspaceID, workspace.WorkspaceName, workspace.WorkspaceDisplayName))
            .OrderBy(scope => scope.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }
}
