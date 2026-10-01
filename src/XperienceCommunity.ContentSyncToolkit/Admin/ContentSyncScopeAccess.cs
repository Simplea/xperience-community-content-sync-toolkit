using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.Authentication;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.Admin.Websites.UIPages;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Which website channels and workspaces the signed-in administration user may see, so the sync
/// status page never lists content (even just names and paths) from a channel or workspace the user
/// can't open in Xperience itself.
/// </summary>
internal interface IContentSyncScopeAccess
{
    public Task<bool> CanViewWebsiteChannelAsync(Guid websiteChannelGuid, CancellationToken cancellationToken);

    public Task<bool> CanViewWorkspaceAsync(int workspaceId, CancellationToken cancellationToken);
}

internal sealed class ContentSyncScopeAccess(
    IAuthenticatedUserAccessor authenticatedUserAccessor,
    IWorkspacePermissionEvaluator workspacePermissionEvaluator,
    IInfoProvider<UserInfo> userInfoProvider,
    IInfoProvider<UserRoleInfo> userRoleInfoProvider,
    IInfoProvider<ApplicationPermissionInfo> applicationPermissionInfoProvider) : IContentSyncScopeAccess
{
    // Each website channel is its own Pages application. Xperience's evaluator for an arbitrary
    // application isn't public, so this reads the same data Role management writes: administrators
    // see every channel; anyone else needs View on the channel's application through one of their
    // roles. The application name, "<WebPagesApplication.IDENTIFIER>_<WebsiteChannelGUID>", is a
    // convention rather than a public API (confirmed in CMS_ApplicationPermission), like the
    // webpages-{id} URL segment.
    public async Task<bool> CanViewWebsiteChannelAsync(Guid websiteChannelGuid, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return false;
        }

        if (user.IsAdministrator())
        {
            return true;
        }

        var roleIds = (await userRoleInfoProvider.Get()
            .WhereEquals(nameof(UserRoleInfo.UserID), user.UserID)
            .Column(nameof(UserRoleInfo.RoleID))
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
            .Select(userRole => userRole.RoleID)
            .ToList();

        if (roleIds.Count == 0)
        {
            return false;
        }

        var permissions = await applicationPermissionInfoProvider.Get()
            .WhereEquals(nameof(ApplicationPermissionInfo.ApplicationName), WebsiteChannelApplicationIdentifier(websiteChannelGuid))
            .WhereEquals(nameof(ApplicationPermissionInfo.PermissionName), SystemPermissions.VIEW)
            .WhereIn(nameof(ApplicationPermissionInfo.RoleID), roleIds)
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return permissions.Any();
    }

    // Xperience's own workspace check for the Content hub, as used by the Content hub application.
    public async Task<bool> CanViewWorkspaceAsync(int workspaceId, CancellationToken cancellationToken) =>
        (await workspacePermissionEvaluator.Evaluate(SystemPermissions.VIEW, typeof(ContentHubApplication), workspaceId)).Succeeded;

    internal static string WebsiteChannelApplicationIdentifier(Guid websiteChannelGuid) =>
        $"{WebPagesApplication.IDENTIFIER}_{websiteChannelGuid:D}";

    private async Task<UserInfo?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var adminUser = await authenticatedUserAccessor.Get();
        if (adminUser is null || string.IsNullOrEmpty(adminUser.UserName))
        {
            return null;
        }

        var users = await userInfoProvider.Get()
            .WhereEquals(nameof(UserInfo.UserName), adminUser.UserName)
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return users.FirstOrDefault();
    }
}
