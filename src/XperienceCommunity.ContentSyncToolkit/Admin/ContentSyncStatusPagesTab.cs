using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.Sync;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

// Under the menu entry, the channel and the language (see ContentSyncStatusNavigation.cs), at the
// segment Kentico's dropdowns navigate to. See docs/specs/sync-status-admin-page.md.
[assembly: UIPage(
    typeof(ContentSyncPagesLanguageLevel),
    PageParameterConstants.PARAMETERIZED_SLUG,
    typeof(ContentSyncStatusPagesTab),
    "Pages",
    TemplateNames.LISTING,
    0,
    ParameterDefaultValue = ContentSyncStatusNavigation.ListSlug)]

namespace XperienceCommunity.ContentSyncToolkit.Admin;

[UIEvaluatePermission(SystemPermissions.VIEW)]
[UINavigation(false)]
[UIBreadcrumbs(false)]
internal sealed class ContentSyncStatusPagesTab(
    IContentSyncToolkitSettings settings,
    IOptions<ContentSyncToolkitOptions> options,
    IContentSyncStatusService syncStatusService,
    IContentSyncScopeProvider scopeProvider,
    IContentSyncScopeAccess scopeAccess,
    IContentSyncFilterOptionsProvider filterOptionsProvider,
    IContentSyncItemIdResolver itemIdResolver,
    IContentInventoryClient inventoryClient,
    ContentSyncStatusRefreshRequestStore refreshRequestStore,
    IContentSyncRecycleBinLookup recycleBinLookup,
    IContentSyncModifierLookup modifierLookup,
    IContentSyncActionService actionService,
    ContentSyncTracker tracker,
    Kentico.Xperience.Admin.Base.Authentication.IAuthenticatedUserAccessor userAccessor,
    ContentSyncDialogBackend dialog,
    IPageLinkGenerator pageLinkGenerator)
    : ContentSyncStatusTabBase(
        new ContentSyncStatusPagesFilterModel(),
        "Path",
        settings,
        options,
        syncStatusService,
        filterOptionsProvider,
        refreshRequestStore,
        recycleBinLookup,
        actionService,
        tracker,
        userAccessor,
        dialog,
        pageLinkGenerator)
{
    /// <summary>The channel in the address. A website channel scope's ID is its WebsiteChannelID.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(ContentSyncPagesChannelLevel))]
    public int ChannelId { get; set; }

    [PageParameter(typeof(StringPageModelBinder), typeof(ContentSyncPagesLanguageLevel))]
    public string LanguageName { get; set; } = string.Empty;

    protected override string TabKey => ContentSyncApplicationExtender.PagesSlug;

    protected override Type EntryType => typeof(ContentSyncPagesEntry);

    protected override int SelectedScopeId => ChannelId;

    protected override string SelectedLanguageName => LanguageName;

    protected override bool RowGuidsAreContentItemGuids => false;

    protected override ContentInventoryItemKind Kind => ContentInventoryItemKind.WebPage;

    protected override string SynchronizeScopeNoun => "page";

    // Pages are synced by page permission: Synchronize (with Read), or Manage permissions on the channel.
    protected override async Task<IReadOnlySet<Guid>> GetSynchronizableAsync(
        ContentSyncScope scope, IReadOnlyDictionary<Guid, ContentSyncLocalItem> localItems, CancellationToken cancellationToken)
    {
        var allowedPageIds = await scopeAccess.GetSynchronizablePagesAsync(scope, [.. localItems.Values.Select(item => item.Id)], cancellationToken);

        return localItems.Where(pair => allowedPageIds.Contains(pair.Value.Id)).Select(pair => pair.Key).ToHashSet();
    }

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

    // A website channel scope's ID is its WebsiteChannelID.
    protected override Task<IReadOnlyDictionary<Guid, ContentSyncModifier>> GetModifiersAsync(
        ContentSyncScope scope, string languageName, CancellationToken cancellationToken) =>
        modifierLookup.GetWebPagesAsync(scope.Id, languageName, cancellationToken);

    protected override Task<IReadOnlyDictionary<Guid, ContentSyncLocalItem>> GetLocalItemsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken) =>
        itemIdResolver.GetWebPageItemsAsync(scope.Name, languageName, [.. items.Select(item => item.Guid)], cancellationToken);

    protected override Task<TargetItemIdsFetchResult> GetTargetItemIdsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken) =>
        inventoryClient.GetWebPageIdsAsync(scope.Name, languageName, [.. items.Select(item => item.Guid)], cancellationToken);

    // A website channel scope's ID is its WebsiteChannelID.
    protected override ContentSyncStatusItemLink GetItemLink(ContentSyncScope scope, string languageName, int itemId) =>
        ContentSyncStatusItemLinks.WebPage(scope.Id, languageName, itemId);
}
