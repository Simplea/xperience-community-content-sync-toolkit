using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncStatusChannelOptionsProvider(IContentSyncScopeProvider scopeProvider) : IDropDownOptionsProvider
{
    public async Task<IEnumerable<DropDownOptionItem>> GetOptionItems()
    {
        var channels = await scopeProvider.GetWebsiteChannelsAsync(CancellationToken.None);

        return [.. channels.Select(channel => new DropDownOptionItem { Value = channel.Name, Text = channel.DisplayName })];
    }
}
