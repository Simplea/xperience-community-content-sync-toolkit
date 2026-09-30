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
    IContentSyncFilterOptionsProvider filterOptionsProvider,
    IContentSyncItemIdResolver itemIdResolver,
    ContentSyncStatusRefreshRequestStore refreshRequestStore,
    IPageLinkGenerator pageLinkGenerator)
    : ContentSyncStatusTabBase(
        new ContentSyncStatusPagesFilterModel(),
        "Path",
        options,
        filterOptionsProvider,
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

    protected override Task<IReadOnlyDictionary<Guid, int>> GetLocalItemIdsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken) =>
        itemIdResolver.GetWebPageItemIdsAsync(scope.Name, languageName, [.. items.Select(item => item.Guid)], cancellationToken);

    // A website channel scope's ID is its WebsiteChannelID.
    protected override ContentSyncStatusItemLink GetItemLink(ContentSyncScope scope, string languageName, int itemId) =>
        ContentSyncStatusItemLinks.WebPage(scope.Id, languageName, itemId);
}
