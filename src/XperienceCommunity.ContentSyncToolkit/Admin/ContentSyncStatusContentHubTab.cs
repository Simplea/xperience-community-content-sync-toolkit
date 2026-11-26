using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.Sync;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

// Under the menu entry, the workspace and the language (see ContentSyncStatusNavigation.cs), at the
// segment Kentico's dropdowns navigate to. See docs/specs/sync-status-admin-page.md.
[assembly: UIPage(
    typeof(ContentSyncContentHubLanguageLevel),
    PageParameterConstants.PARAMETERIZED_SLUG,
    typeof(ContentSyncStatusContentHubTab),
    "Content hub",
    TemplateNames.LISTING,
    0,
    ParameterDefaultValue = ContentSyncStatusNavigation.ListSlug)]

namespace XperienceCommunity.ContentSyncToolkit.Admin;

[UIEvaluatePermission(SystemPermissions.VIEW)]
[UINavigation(false)]
[UIBreadcrumbs(false)]
internal sealed class ContentSyncStatusContentHubTab(
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
    IContentSyncWorkspaceMoveLookup workspaceMoveLookup,
    IContentSyncModifierLookup modifierLookup,
    IContentSyncActionService actionService,
    ContentSyncTracker tracker,
    Kentico.Xperience.Admin.Base.Authentication.IAuthenticatedUserAccessor userAccessor,
    ContentSyncDialogBackend dialog,
    IPageLinkGenerator pageLinkGenerator)
    : ContentSyncStatusTabBase(
        new ContentSyncStatusContentHubFilterModel(),
        "Name",
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
    /// <summary>The workspace in the address. A workspace scope's ID is its WorkspaceID.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(ContentSyncContentHubWorkspaceLevel))]
    public int WorkspaceId { get; set; }

    [PageParameter(typeof(StringPageModelBinder), typeof(ContentSyncContentHubLanguageLevel))]
    public string LanguageName { get; set; } = string.Empty;

    protected override string TabKey => ContentSyncApplicationExtender.ContentHubSlug;

    protected override Type EntryType => typeof(ContentSyncContentHubEntry);

    protected override int SelectedScopeId => WorkspaceId;

    protected override string SelectedLanguageName => LanguageName;

    protected override bool RowGuidsAreContentItemGuids => true;

    protected override ContentInventoryItemKind Kind => ContentInventoryItemKind.ContentHubItem;

    protected override string SynchronizeScopeNoun => "workspace";

    // Content hub items are synced by workspace: the Synchronize permission for Content hub there.
    protected override async Task<IReadOnlySet<Guid>> GetSynchronizableAsync(
        ContentSyncScope scope, IReadOnlyDictionary<Guid, ContentSyncLocalItem> localItems, CancellationToken cancellationToken) =>
        await scopeAccess.CanSynchronizeWorkspaceAsync(scope.Id, cancellationToken)
            ? localItems.Keys.ToHashSet()
            : [];

    protected override string ScopeNoun => "workspace";

    protected override string NoScopesHeadline => "No workspaces to compare";

    protected override string NoScopesGuidance =>
        "Create a workspace in Configuration → Workspaces, or ask an administrator for access to one.";

    protected override Task<IReadOnlyList<ContentSyncScope>> GetScopesAsync(CancellationToken cancellationToken) =>
        scopeProvider.GetWorkspacesAsync(cancellationToken);

    // A workspace scope's ID is its WorkspaceID.
    protected override Task<IReadOnlyDictionary<Guid, ContentSyncModifier>> GetModifiersAsync(
        ContentSyncScope scope, string languageName, CancellationToken cancellationToken) =>
        modifierLookup.GetContentItemsAsync(scope.Id, languageName, cancellationToken);

    protected override Task<ContentSyncStatusResult> GetStatusAsync(
        string scopeName, string languageName, bool forceRefresh, CancellationToken cancellationToken) =>
        SyncStatusService.GetContentHubSyncStatusAsync(scopeName, languageName, forceRefresh, cancellationToken);

    protected override Task<IReadOnlyDictionary<Guid, ContentSyncLocalItem>> GetLocalItemsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken) =>
        itemIdResolver.GetContentItemsAsync(
            scope.Name,
            languageName,
            [.. items.Select(item => item.Local!.ContentTypeName).Distinct(StringComparer.OrdinalIgnoreCase)],
            [.. items.Select(item => item.Guid)],
            cancellationToken);

    protected override Task<TargetItemIdsFetchResult> GetTargetItemIdsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyList<ContentSyncStatusItem> items, CancellationToken cancellationToken) =>
        inventoryClient.GetContentItemIdsAsync(scope.Name, languageName, [.. items.Select(item => item.Guid)], cancellationToken);

    protected override Task<IReadOnlyDictionary<Guid, ContentSyncMovedItem>> GetMovedItemsAsync(
        ContentSyncScope scope, string languageName, IReadOnlyCollection<Guid> contentItemGuids, CancellationToken cancellationToken) =>
        workspaceMoveLookup.GetMovedItemsAsync(scope.Id, languageName, contentItemGuids, cancellationToken);

    // A workspace scope's ID is its WorkspaceID.
    protected override ContentSyncStatusItemLink GetItemLink(ContentSyncScope scope, string languageName, int itemId) =>
        ContentSyncStatusItemLinks.ContentItem(scope.Id, languageName, itemId);
}
