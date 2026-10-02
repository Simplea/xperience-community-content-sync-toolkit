using CMS.Core;
using CMS.DataEngine;
using CMS.Membership;
using CMS.Websites;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.Authentication;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.Admin.Websites.UIPages;

using XperienceCommunity.ContentSyncToolkit.SyncStatus;

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

    /// <summary>
    /// Which of a website channel's pages the user can see in Xperience's page tree, for filtering
    /// a status result. Throws if the permissions can't be read, so a failure hides pages rather
    /// than showing them.
    /// </summary>
    public Task<ContentSyncPageVisibility> GetPageVisibilityAsync(
        ContentSyncScope websiteChannel, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken);
}

internal sealed class ContentSyncScopeAccess(
    IAuthenticatedUserAccessor authenticatedUserAccessor,
    IWorkspacePermissionEvaluator workspacePermissionEvaluator,
    IInfoProvider<UserInfo> userInfoProvider,
    IInfoProvider<UserRoleInfo> userRoleInfoProvider,
    IInfoProvider<ApplicationPermissionInfo> applicationPermissionInfoProvider,
    IInfoProvider<WebsiteChannelInfo> websiteChannelInfoProvider,
    IWebPageAclManagerFactory webPageAclManagerFactory,
    IContentSyncItemIdResolver itemIdResolver,
    IEventLogService eventLogService) : IContentSyncScopeAccess
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

        var roleIds = await GetRoleIdsAsync(user, cancellationToken);

        // Role management shows View on a channel's application as "Access channel".
        return await HasChannelPermissionAsync(websiteChannelGuid, SystemPermissions.VIEW, roleIds, cancellationToken);
    }

    // Xperience's own workspace check for the Content hub, as used by the Content hub application.
    public async Task<bool> CanViewWorkspaceAsync(int workspaceId, CancellationToken cancellationToken) =>
        (await workspacePermissionEvaluator.Evaluate(SystemPermissions.VIEW, typeof(ContentHubApplication), workspaceId)).Succeeded;

    // Page permissions, as Kentico's page permission management describes them: the page tree shows
    // a page to users whose roles have Display on it, through the page's access-control list (set on
    // the channel root and inherited down the tree until a page breaks inheritance). Administrators
    // and roles with Manage permissions on the channel bypass page permissions.
    //
    // Kentico's public IWebPageAclManager.GetPermissions reads one page's list in about three queries
    // and doesn't cache, which is too slow for every page of a large channel. So the page-to-list
    // mapping is read in one batched query (its Info class is internal, so by object type name, as
    // for display names), and the public API is called once per distinct list. If the mapping query
    // fails, the public API is called for every page instead: slower, never wrong.
    public async Task<ContentSyncPageVisibility> GetPageVisibilityAsync(
        ContentSyncScope websiteChannel, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken)
            ?? throw new InvalidOperationException("No signed-in administration user to evaluate page permissions for.");

        if (user.IsAdministrator())
        {
            return ContentSyncPageVisibility.All;
        }

        var roleIds = await GetRoleIdsAsync(user, cancellationToken);
        var channel = await websiteChannelInfoProvider.GetAsync(websiteChannel.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Website channel {websiteChannel.Id} doesn't exist.");

        if (await HasChannelPermissionAsync(channel.WebsiteChannelGUID, WebsiteChannelUIPermissions.MANAGE_PERMISSIONS, roleIds, cancellationToken))
        {
            return ContentSyncPageVisibility.All;
        }

        var aclManager = webPageAclManagerFactory.Create(websiteChannel.Id);
        var roleSet = roleIds.ToHashSet();
        bool CanDisplay(WebPageAclConfigurationDescriptor descriptor) =>
            descriptor.PermissionsConfiguration.Roles.Any(role =>
                roleSet.Contains(role.RoleID) && role.Permissions.Contains(WebPageAclPermissions.DISPLAY, StringComparer.OrdinalIgnoreCase));

        // Page ID 0 is the channel root.
        bool rootVisible = CanDisplay(await aclManager.GetPermissions(0, cancellationToken));

        var localPages = items
            .Where(item => item.Local?.TreePath is not null)
            .Select(item => (item.Guid, TreePath: item.Local!.TreePath!))
            .ToList();
        if (localPages.Count == 0)
        {
            return ContentSyncPageVisibility.For(rootVisible, []);
        }

        var pageIds = new Dictionary<Guid, int>();
        foreach (var batch in localPages.Select(page => page.Guid).Chunk(BatchSize))
        {
            foreach (var (guid, id) in await itemIdResolver.GetWebPageItemIdsAsync(websiteChannel.Name, languageName, batch, cancellationToken))
            {
                pageIds[guid] = id;
            }
        }

        var visibleById = await GetDisplayableAsync([.. pageIds.Values], aclManager, CanDisplay, cancellationToken);

        // A page whose ID didn't resolve falls back to its ancestors' permissions.
        return ContentSyncPageVisibility.For(rootVisible, localPages
            .Where(page => pageIds.ContainsKey(page.Guid))
            .Select(page => (page.Guid, page.TreePath, visibleById[pageIds[page.Guid]])));
    }

    private const string AclMappingObjectType = "cms.webpageaclmapping";
    private const string AclMappingPageColumn = "WebPageAclMappingWebPageItemID";
    private const string AclMappingAclColumn = "WebPageAclMappingWebPageAclID";

    // Keeps each WHERE IN list well below SQL Server's limits.
    private const int BatchSize = 1000;

    private async Task<IReadOnlyDictionary<int, bool>> GetDisplayableAsync(
        IReadOnlyList<int> pageIds,
        IWebPageAclManager aclManager,
        Func<WebPageAclConfigurationDescriptor, bool> canDisplay,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, bool>();

        Dictionary<int, int> aclByPage;
        try
        {
            aclByPage = await GetAclIdsAsync(pageIds, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            eventLogService.LogException(nameof(ContentSyncScopeAccess), "PAGEACLMAPPING", ex);
            foreach (int pageId in pageIds)
            {
                result[pageId] = canDisplay(await aclManager.GetPermissions(pageId, cancellationToken));
            }

            return result;
        }

        // One public lookup per distinct list, through any page that uses it.
        var visibleByAcl = new Dictionary<int, bool>();
        foreach (var group in aclByPage.GroupBy(pair => pair.Value))
        {
            visibleByAcl[group.Key] = canDisplay(await aclManager.GetPermissions(group.First().Key, cancellationToken));
        }

        foreach (int pageId in pageIds)
        {
            result[pageId] = aclByPage.TryGetValue(pageId, out int aclId)
                ? visibleByAcl[aclId]
                : canDisplay(await aclManager.GetPermissions(pageId, cancellationToken));
        }

        return result;
    }

    private static async Task<Dictionary<int, int>> GetAclIdsAsync(IReadOnlyList<int> pageIds, CancellationToken cancellationToken)
    {
        var aclByPage = new Dictionary<int, int>();

        foreach (int[] batch in pageIds.Chunk(BatchSize))
        {
            var rows = await new ObjectQuery(AclMappingObjectType)
                .Columns(AclMappingPageColumn, AclMappingAclColumn)
                .WhereIn(AclMappingPageColumn, batch)
                .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

            foreach (var row in rows)
            {
                aclByPage[Convert.ToInt32(row.GetValue(AclMappingPageColumn))] = Convert.ToInt32(row.GetValue(AclMappingAclColumn));
            }
        }

        return aclByPage;
    }

    private async Task<IReadOnlyList<int>> GetRoleIdsAsync(UserInfo user, CancellationToken cancellationToken) =>
        [.. (await userRoleInfoProvider.Get()
            .WhereEquals(nameof(UserRoleInfo.UserID), user.UserID)
            .Column(nameof(UserRoleInfo.RoleID))
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
            .Select(userRole => userRole.RoleID)];

    private async Task<bool> HasChannelPermissionAsync(
        Guid websiteChannelGuid, string permissionName, IReadOnlyList<int> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return false;
        }

        var permissions = await applicationPermissionInfoProvider.Get()
            .WhereEquals(nameof(ApplicationPermissionInfo.ApplicationName), WebsiteChannelApplicationIdentifier(websiteChannelGuid))
            .WhereEquals(nameof(ApplicationPermissionInfo.PermissionName), permissionName)
            .WhereIn(nameof(ApplicationPermissionInfo.RoleID), roleIds.ToList())
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return permissions.Any();
    }

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
