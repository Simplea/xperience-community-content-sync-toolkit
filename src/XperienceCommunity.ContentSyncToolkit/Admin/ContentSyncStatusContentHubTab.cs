using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

[assembly: UIPage(
    typeof(ContentSyncStatusApplication),
    "content-hub",
    typeof(ContentSyncStatusContentHubTab),
    "Content hub",
    TemplateNames.LISTING,
    200)]

namespace XperienceCommunity.ContentSyncToolkit.Admin;

[UIEvaluatePermission(SystemPermissions.VIEW)]
internal sealed class ContentSyncStatusContentHubTab(
    IContentSyncToolkitSettings settings,
    IOptions<ContentSyncToolkitOptions> options,
    IContentSyncStatusService syncStatusService,
    IContentSyncScopeProvider scopeProvider,
    IContentSyncFilterOptionsProvider filterOptionsProvider,
    IContentSyncItemIdResolver itemIdResolver,
    ContentSyncStatusRefreshRequestStore refreshRequestStore,
    IPageLinkGenerator pageLinkGenerator)
    : ContentSyncStatusTabBase(
        new ContentSyncStatusContentHubFilterModel(),
        "Name",
        RequiredObjectKind.Workspace,
        settings,
        options,
        syncStatusService,
        filterOptionsProvider,
        refreshRequestStore,
        pageLinkGenerator)
{
    protected override string TabKey => "content-hub";

    protected override string ScopeFilterFieldName => nameof(ContentSyncStatusContentHubFilterModel.Workspace);

    protected override string ScopeNoun => "workspace";

    protected override string NoScopesHeadline => "No workspaces to compare";

    protected override string NoScopesGuidance =>
        "Create a workspace in Configuration → Workspaces, or ask an administrator for access to one.";

    protected override Task<IReadOnlyList<ContentSyncScope>> GetScopesAsync(CancellationToken cancellationToken) =>
        scopeProvider.GetWorkspacesAsync(cancellationToken);

    protected override Task<ContentSyncStatusResult> GetStatusAsync(
        string scopeName, string languageName, bool forceRefresh, CancellationToken cancellationToken) =>
        SyncStatusService.GetContentHubSyncStatusAsync(scopeName, languageName, forceRefresh, cancellationToken);

    protected override Task<IReadOnlyDictionary<Guid, int>> GetLocalItemIdsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken) =>
        itemIdResolver.GetContentItemIdsAsync(
            scope.Name,
            languageName,
            [.. items.Select(item => item.Local!.ContentTypeName).Distinct(StringComparer.OrdinalIgnoreCase)],
            [.. items.Select(item => item.Guid)],
            cancellationToken);

    // A workspace scope's ID is its WorkspaceID.
    protected override ContentSyncStatusItemLink GetItemLink(ContentSyncScope scope, string languageName, int itemId) =>
        ContentSyncStatusItemLinks.ContentItem(scope.Id, languageName, itemId);
}
