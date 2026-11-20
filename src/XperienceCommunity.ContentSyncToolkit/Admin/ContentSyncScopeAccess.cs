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

    /// <summary>What the user can see in the Recycle bin application.</summary>
    public Task<ContentSyncRecycleBinAccess> GetRecycleBinAccessAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether the website channel has a page on this instance that was ever published and that the user
    /// can see in the page tree, with the same rules as <see cref="GetPageVisibilityAsync"/>. Chooses
    /// where the Pages tab opens: a page never published isn't listed, so it doesn't count.
    /// </summary>
    public Task<bool> HasVisiblePagesAsync(ContentSyncScope websiteChannel, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the workspace has a content item on this instance that was ever published. Access is per
    /// workspace, and the Content hub tab only lists workspaces the user can view, so no item-level
    /// check applies.
    /// </summary>
    public Task<bool> HasContentItemsAsync(ContentSyncScope workspace, CancellationToken cancellationToken);
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
        var CanDisplay = DisplayCheck(roleIds);

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
            foreach (var (guid, page) in await itemIdResolver.GetWebPageItemsAsync(websiteChannel.Name, languageName, batch, cancellationToken))
            {
                pageIds[guid] = page.Id;
            }
        }

        var visibleById = await GetDisplayableAsync([.. pageIds.Values], aclManager, CanDisplay, cancellationToken);

        // A page whose ID didn't resolve falls back to its ancestors' permissions.
        return ContentSyncPageVisibility.For(rootVisible, localPages
            .Where(page => pageIds.ContainsKey(page.Guid))
            .Select(page => (page.Guid, page.TreePath, visibleById[pageIds[page.Guid]])));
    }

    private const string WebPageObjectType = "cms.webpageitem";
    private const string WebPageIdColumn = "WebPageItemID";
    private const string WebPageChannelIdColumn = "WebPageItemWebsiteChannelID";
    private const string WebPageContentItemIdColumn = "WebPageItemContentItemID";
    private const string ContentItemObjectType = "cms.contentitem";
    private const string ContentItemIdColumn = "ContentItemID";
    private const string ContentItemWorkspaceIdColumn = "ContentItemWorkspaceID";
    private const string ContentItemIsReusableColumn = "ContentItemIsReusable";
    private const string CommonDataObjectType = "cms.contentitemcommondata";
    private const string CommonDataContentItemIdColumn = "ContentItemCommonDataContentItemID";
    private const string CommonDataLastPublishedWhenColumn = "ContentItemCommonDataLastPublishedWhen";

    // Content items with a version that was published at some point, in any language: the list leaves
    // out items never published, so a scope with only those would open empty. A subquery, so a large
    // scope adds no long list.
    private static ObjectQuery EverPublishedContentItemIds() =>
        new ObjectQuery(CommonDataObjectType)
            .Column(CommonDataContentItemIdColumn)
            .WhereNotNull(CommonDataLastPublishedWhenColumn);

    // Users who see every page need one row. Anyone else is checked as for the listing, batch by batch,
    // stopping at the first page they can see: a channel whose pages are all hidden from them would
    // otherwise open on an empty list.
    public async Task<bool> HasVisiblePagesAsync(ContentSyncScope websiteChannel, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return false;
        }

        var pages = new ObjectQuery(WebPageObjectType)
            .Column(WebPageIdColumn)
            .WhereEquals(WebPageChannelIdColumn, websiteChannel.Id)
            .WhereIn(WebPageContentItemIdColumn, EverPublishedContentItemIds());

        if (user.IsAdministrator())
        {
            return await AnyAsync(pages, cancellationToken);
        }

        var roleIds = await GetRoleIdsAsync(user, cancellationToken);
        var channel = await websiteChannelInfoProvider.GetAsync(websiteChannel.Id, cancellationToken);
        if (channel is null)
        {
            return false;
        }

        if (await HasChannelPermissionAsync(channel.WebsiteChannelGUID, WebsiteChannelUIPermissions.MANAGE_PERMISSIONS, roleIds, cancellationToken))
        {
            return await AnyAsync(pages, cancellationToken);
        }

        var pageIds = (await pages.GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
            .Select(row => Convert.ToInt32(row.GetValue(WebPageIdColumn)))
            .ToList();
        var aclManager = webPageAclManagerFactory.Create(websiteChannel.Id);
        var canDisplay = DisplayCheck(roleIds);

        for (int start = 0; start < pageIds.Count; start += BatchSize)
        {
            var batch = pageIds.GetRange(start, Math.Min(BatchSize, pageIds.Count - start));
            var visibleById = await GetDisplayableAsync(batch, aclManager, canDisplay, cancellationToken);
            if (visibleById.Values.Any(visible => visible))
            {
                return true;
            }
        }

        return false;
    }

    public Task<bool> HasContentItemsAsync(ContentSyncScope workspace, CancellationToken cancellationToken) =>
        AnyAsync(
            new ObjectQuery(ContentItemObjectType)
                .Column(ContentItemIdColumn)
                .WhereEquals(ContentItemWorkspaceIdColumn, workspace.Id)
                .WhereTrue(ContentItemIsReusableColumn)
                .WhereIn(ContentItemIdColumn, EverPublishedContentItemIds()),
            cancellationToken);

    private static async Task<bool> AnyAsync(ObjectQuery query, CancellationToken cancellationToken) =>
        (await query.TopN(1).GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)).Any();

    // The page tree's Display permission, through any of the user's roles.
    private static Func<WebPageAclConfigurationDescriptor, bool> DisplayCheck(IEnumerable<int> roleIds)
    {
        var roleSet = roleIds.ToHashSet();
        return descriptor => descriptor.PermissionsConfiguration.Roles.Any(role =>
            roleSet.Contains(role.RoleID) && role.Permissions.Contains(WebPageAclPermissions.DISPLAY, StringComparer.OrdinalIgnoreCase));
    }

    // The Recycle bin application was added in 30.12.0, so its identifier and permission aren't
    // available as constants on the toolkit's minimum version.
    private const string RecycleBinApplicationIdentifier = "Kentico.Xperience.Application.RecycleBin";
    private const string ManageRecycleBinPermission = "ManageRecycleBin";

    public async Task<ContentSyncRecycleBinAccess> GetRecycleBinAccessAsync(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return ContentSyncRecycleBinAccess.None;
        }

        if (user.IsAdministrator())
        {
            return new ContentSyncRecycleBinAccess(true, true, user.UserID);
        }

        var roleIds = await GetRoleIdsAsync(user, cancellationToken);

        return new ContentSyncRecycleBinAccess(
            await HasApplicationPermissionAsync(RecycleBinApplicationIdentifier, SystemPermissions.VIEW, roleIds, cancellationToken),
            await HasApplicationPermissionAsync(RecycleBinApplicationIdentifier, ManageRecycleBinPermission, roleIds, cancellationToken),
            user.UserID);
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

    private Task<bool> HasChannelPermissionAsync(
        Guid websiteChannelGuid, string permissionName, IReadOnlyList<int> roleIds, CancellationToken cancellationToken) =>
        HasApplicationPermissionAsync(WebsiteChannelApplicationIdentifier(websiteChannelGuid), permissionName, roleIds, cancellationToken);

    private async Task<bool> HasApplicationPermissionAsync(
        string applicationIdentifier, string permissionName, IReadOnlyList<int> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return false;
        }

        var permissions = await applicationPermissionInfoProvider.Get()
            .WhereEquals(nameof(ApplicationPermissionInfo.ApplicationName), applicationIdentifier)
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
