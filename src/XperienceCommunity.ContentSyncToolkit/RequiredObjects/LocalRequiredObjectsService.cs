using System.Security.Cryptography;
using System.Text;

using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Workspaces;

namespace XperienceCommunity.ContentSyncToolkit.RequiredObjects;

/// <summary>
/// Lists this instance's objects that Content Sync requires on the target but doesn't transfer:
/// website and reusable content types, languages, website channels and workspaces. Runs on both
/// instances: the target serves its list, and the source compares its own against it.
/// </summary>
public interface ILocalRequiredObjectsService
{
    public Task<IReadOnlyList<RequiredObject>> GetRequiredObjectsAsync(CancellationToken cancellationToken);
}

internal sealed class LocalRequiredObjectsService(
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider,
    IInfoProvider<ChannelInfo> channelInfoProvider,
    IInfoProvider<WorkspaceInfo> workspaceInfoProvider) : ILocalRequiredObjectsService
{
    public async Task<IReadOnlyList<RequiredObject>> GetRequiredObjectsAsync(CancellationToken cancellationToken)
    {
        // DataClassInfoProvider is deliberately excluded from DI by Kentico; see
        // LocalContentInventoryService.GetReusableContentTypeNamesAsync.
        var classes = await new DataClassInfoProvider().Get()
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);
        var languages = await contentLanguageInfoProvider.Get()
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);
        var channels = await channelInfoProvider.Get()
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);
        var workspaces = await workspaceInfoProvider.Get()
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        // Email and headless content isn't synced, so only website and reusable types are required.
        return [
            .. classes
                .Where(dataClass => dataClass.ClassContentTypeType is ClassContentTypeType.WEBSITE or ClassContentTypeType.REUSABLE)
                .Select(dataClass => new RequiredObject(RequiredObjectKind.ContentType, dataClass.ClassGUID, dataClass.ClassName, dataClass.ClassDisplayName)
                {
                    DefinitionHash = HashDefinition(dataClass.ClassFormDefinition),
                }),
            .. languages.Select(language => new RequiredObject(
                RequiredObjectKind.Language, language.ContentLanguageGUID, language.ContentLanguageName, language.ContentLanguageDisplayName)),
            .. channels
                .Where(channel => channel.ChannelType == ChannelType.Website)
                .Select(channel => new RequiredObject(RequiredObjectKind.WebsiteChannel, channel.ChannelGUID, channel.ChannelName, channel.ChannelDisplayName)),
            .. workspaces.Select(workspace => new RequiredObject(
                RequiredObjectKind.Workspace, workspace.WorkspaceGUID, workspace.WorkspaceName, workspace.WorkspaceDisplayName)),
        ];
    }

    // Content Sync needs an exactly matching type, and a type deployed with CI/CD or a deployment
    // package has a byte-identical definition on both instances, so comparing hashes finds a type
    // whose fields were changed on one side only without sending the definition itself.
    internal static string HashDefinition(string? formDefinition) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(formDefinition ?? string.Empty)));
}
