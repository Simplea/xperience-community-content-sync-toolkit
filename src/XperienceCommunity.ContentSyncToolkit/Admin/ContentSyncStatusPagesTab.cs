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
    IContentSyncToolkitSettings settings,
    IOptions<ContentSyncToolkitOptions> options,
    IContentSyncStatusService syncStatusService,
    IContentSyncScopeProvider scopeProvider,
    IContentSyncScopeAccess scopeAccess,
    IContentSyncFilterOptionsProvider filterOptionsProvider,
    IContentSyncItemIdResolver itemIdResolver,
    ContentSyncStatusRefreshRequestStore refreshRequestStore,
    IPageLinkGenerator pageLinkGenerator)
    : ContentSyncStatusTabBase(
        new ContentSyncStatusPagesFilterModel(),
        "Path",
        settings,
        options,
        syncStatusService,
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

    // Only the pages the user can see in Xperience's page tree, before anything is counted, sorted
    // or paged. See ContentSyncPageVisibility.
    protected override async Task<ContentSyncStatusResult> GetStatusAsync(
        string scopeName, string languageName, bool forceRefresh, CancellationToken cancellationToken)
    {
        var result = await SyncStatusService.GetWebPageSyncStatusAsync(scopeName, languageName, forceRefresh, cancellationToken);
        if (!result.TargetAvailable || result.Items.Count == 0)
        {
            return result;
        }

        var channel = (await GetScopesAsync(cancellationToken))
            .First(scope => string.Equals(scope.Name, scopeName, StringComparison.OrdinalIgnoreCase));
        var visibility = await scopeAccess.GetPageVisibilityAsync(channel, languageName, result.Items, cancellationToken);

        return visibility.Apply(result);
    }

    protected override Task<IReadOnlyDictionary<Guid, ContentSyncLocalItem>> GetLocalItemsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken) =>
        itemIdResolver.GetWebPageItemsAsync(scope.Name, languageName, [.. items.Select(item => item.Guid)], cancellationToken);

    // A website channel scope's ID is its WebsiteChannelID.
    protected override ContentSyncStatusItemLink GetItemLink(ContentSyncScope scope, string languageName, int itemId) =>
        ContentSyncStatusItemLinks.WebPage(scope.Id, languageName, itemId);
}
