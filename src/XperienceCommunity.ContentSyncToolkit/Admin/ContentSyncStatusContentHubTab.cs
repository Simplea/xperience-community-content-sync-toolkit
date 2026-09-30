using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Admin;
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
    IOptions<ContentSyncToolkitOptions> options,
    IContentSyncStatusService syncStatusService,
    IContentSyncScopeProvider scopeProvider,
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider,
    ContentSyncStatusRefreshRequestStore refreshRequestStore,
    IPageLinkGenerator pageLinkGenerator)
    : ContentSyncStatusTabBase(
        new ContentSyncStatusContentHubFilterModel(),
        "Name",
        options,
        contentLanguageInfoProvider,
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
        syncStatusService.GetContentHubSyncStatusAsync(scopeName, languageName, forceRefresh, cancellationToken);
}
