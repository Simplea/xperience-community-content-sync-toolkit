using CMS.ContentEngine;
using CMS.DataEngine;

namespace XperienceCommunity.ContentSyncToolkit.Inventory;

/// <summary>
/// Checks that a requested website channel and language exist before the inventory queries them.
/// Xperience's content query throws for an unknown website channel or language (an unknown
/// workspace just matches nothing), and scope names reach the target endpoint from a remote
/// caller, so they're checked up front rather than letting the query fail.
/// </summary>
internal interface IContentScopeLookup
{
    public Task<bool> WebsiteChannelExistsAsync(string channelName, CancellationToken cancellationToken);

    public Task<bool> ContentLanguageExistsAsync(string languageName, CancellationToken cancellationToken);
}

internal sealed class ContentScopeLookup(
    IInfoProvider<ChannelInfo> channelInfoProvider,
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider) : IContentScopeLookup
{
    public async Task<bool> WebsiteChannelExistsAsync(string channelName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(channelName))
        {
            return false;
        }

        var channels = await channelInfoProvider.Get()
            .WhereEquals(nameof(ChannelInfo.ChannelName), channelName)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return channels.Any(channel => channel.ChannelType == ChannelType.Website);
    }

    public async Task<bool> ContentLanguageExistsAsync(string languageName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(languageName))
        {
            return false;
        }

        var languages = await contentLanguageInfoProvider.Get()
            .WhereEquals(nameof(ContentLanguageInfo.ContentLanguageName), languageName)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return languages.Any();
    }
}
