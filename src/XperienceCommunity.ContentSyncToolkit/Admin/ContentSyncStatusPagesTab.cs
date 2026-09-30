using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

[assembly: UIPage(
    typeof(ContentSyncStatusApplication),
    "pages",
    typeof(ContentSyncStatusPagesTab),
    "Pages",
    TemplateNames.LISTING,
    100)]

namespace XperienceCommunity.ContentSyncToolkit.Admin;

[UIEvaluatePermission(SystemPermissions.VIEW)]
internal sealed class ContentSyncStatusPagesTab(
    IOptions<ContentSyncToolkitOptions> options,
    IContentSyncStatusService syncStatusService,
    IContentSyncScopeProvider scopeProvider,
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider,
    ContentSyncStatusRefreshRequestStore refreshRequestStore,
    IPageLinkGenerator pageLinkGenerator)
    : ContentSyncStatusTabBase(
        new ContentSyncStatusPagesFilterModel(),
        "Path",
        options,
        contentLanguageInfoProvider,
        refreshRequestStore,
        pageLinkGenerator)
{
    protected override string TabKey => "pages";

    protected override string ScopeFilterFieldName => nameof(ContentSyncStatusPagesFilterModel.Channel);

    protected override string ScopeNoun => "channel";

    protected override string NoScopesHeadline => "No website channels to compare";

    protected override string NoScopesGuidance =>
        "Create a website channel in Configuration → Channel management, or ask an administrator for access to one.";

    protected override Task<IReadOnlyList<ContentSyncScope>> GetScopesAsync(CancellationToken cancellationToken) =>
        scopeProvider.GetWebsiteChannelsAsync(cancellationToken);

    protected override Task<ContentSyncStatusResult> GetStatusAsync(
        string scopeName, string languageName, bool forceRefresh, CancellationToken cancellationToken) =>
        syncStatusService.GetWebPageSyncStatusAsync(scopeName, languageName, forceRefresh, cancellationToken);
}
