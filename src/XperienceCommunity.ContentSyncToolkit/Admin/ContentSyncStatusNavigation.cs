using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Membership;
using CMS.Websites;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.Authentication;
using Kentico.Xperience.Admin.Base.UIPages;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Admin;

// Each status list sits under its menu entry, below two levels that put Kentico's own dropdowns in the
// top bar: the channel or workspace (Kentico's workspace dropdown) and the language. The list itself is
// at the segment Kentico's dropdowns navigate to. See docs/specs/header-dropdowns.md.
//   /admin/content-sync/pages/{websiteChannelId}/{language}/all
//   /admin/content-sync/content-hub/{workspaceId}/{language}/all
// The menu entries come before Kentico's own list (order 100), so the application opens on what's
// still pending.
[assembly: UIPage(
    typeof(ContentSynchronizationApplication),
    ContentSyncApplicationExtender.PagesSlug,
    typeof(ContentSyncPagesEntry),
    "Pages",
    ContentSyncToolkitAdminModule.EntryTemplate,
    10)]
[assembly: UIPage(
    typeof(ContentSyncPagesEntry),
    PageParameterConstants.PARAMETERIZED_SLUG,
    typeof(ContentSyncPagesChannelLevel),
    "Channel",
    TemplateNames.WORKSPACE,
    0)]
[assembly: UIPage(
    typeof(ContentSyncPagesChannelLevel),
    PageParameterConstants.PARAMETERIZED_SLUG,
    typeof(ContentSyncPagesLanguageLevel),
    "Language",
    TemplateNames.CONTENT_LANGUAGE,
    0)]
[assembly: UIPage(
    typeof(ContentSynchronizationApplication),
    ContentSyncApplicationExtender.ContentHubSlug,
    typeof(ContentSyncContentHubEntry),
    "Content hub",
    ContentSyncToolkitAdminModule.EntryTemplate,
    20)]
[assembly: UIPage(
    typeof(ContentSyncContentHubEntry),
    PageParameterConstants.PARAMETERIZED_SLUG,
    typeof(ContentSyncContentHubWorkspaceLevel),
    "Workspace",
    TemplateNames.WORKSPACE,
    0)]
[assembly: UIPage(
    typeof(ContentSyncContentHubWorkspaceLevel),
    PageParameterConstants.PARAMETERIZED_SLUG,
    typeof(ContentSyncContentHubLanguageLevel),
    "Language",
    TemplateNames.CONTENT_LANGUAGE,
    0)]

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>Addresses of the status lists and what the browser module needs to move between them.</summary>
internal static class ContentSyncStatusNavigation
{
    /// <summary>The list's segment, below the language: where Kentico's dropdowns navigate to.</summary>
    public const string ListSlug = "all";

    /// <summary>
    /// A status list's address below the admin path, e.g. <c>/content-sync/pages/1/en/all</c>, from its
    /// menu entry's address (as <see cref="IPageLinkGenerator"/> gives it), scope ID and language code name.
    /// </summary>
    public static string ListPath(string entryPath, int scopeId, string languageName) =>
        $"{entryPath.TrimEnd('/')}/{scopeId}/{Uri.EscapeDataString(languageName)}/{ListSlug}";

    /// <summary>
    /// Where the editor's last choice on a tab is kept in their browser: one entry per tab and user, so
    /// editors sharing a browser keep their own.
    /// </summary>
    public static string StorageKey(string tabKey, int userId) =>
        $"{ContentSyncToolkitAdminModule.OrgName}/{ContentSyncToolkitAdminModule.ProjectName}/last-choice/{tabKey}/{userId}";
}

/// <summary>What a status list's menu entry tells the browser module.</summary>
internal sealed class ContentSyncStatusEntryClientProperties : TemplateClientProperties
{
    /// <summary>The menu entry's own address below the admin path, e.g. <c>/content-sync/pages</c>.</summary>
    public string EntryPath { get; set; } = string.Empty;

    /// <summary><see cref="ContentSyncStatusNavigation.ListSlug"/>, for addresses built from a remembered choice.</summary>
    public string ListSlug { get; set; } = ContentSyncStatusNavigation.ListSlug;

    /// <summary>The channels or workspaces the editor can open: a remembered one is used only if it's still here.</summary>
    public IReadOnlyList<int> ScopeIds { get; set; } = [];

    /// <summary>The languages that can be opened: a remembered one is used only if it's still here.</summary>
    public IReadOnlyList<string> LanguageNames { get; set; } = [];

    /// <summary>See <see cref="ContentSyncStatusNavigation.StorageKey"/>.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary><see cref="ContentSyncToolkitOptions.FullReloadOnSwitch"/>.</summary>
    public bool FullReload { get; set; }
}

/// <summary>What <see cref="ContentSyncStatusEntryBase.GetDefaultTarget"/> answers.</summary>
/// <param name="Target">Where the entry opens without a remembered choice; empty when nothing can be opened.</param>
internal sealed record ContentSyncStatusDefaultTarget(string Target);

/// <summary>
/// A status list's menu entry (Pages, Content hub). It shows the levels below it; opened on its own, the
/// browser module goes on to the editor's last choice on the tab, if it's still available, or asks for
/// the default channel or workspace and language (<see cref="GetDefaultTarget"/>). The default is only
/// worked out then: finding the first scope with content can take a moment on a large site. A redirect
/// from the server can't do this: the admin ignores it on a menu entry's own page (see
/// docs/specs/header-dropdowns.md, Research findings).
/// </summary>
internal abstract class ContentSyncStatusEntryBase(
    IContentSyncToolkitSettings settings,
    IOptions<ContentSyncToolkitOptions> options,
    IContentSyncFilterOptionsProvider filterOptionsProvider,
    IAuthenticatedUserAccessor userAccessor,
    IPageLinkGenerator pageLinkGenerator)
    : Page<ContentSyncStatusEntryClientProperties>
{
    protected abstract string TabKey { get; }

    protected abstract Task<IReadOnlyList<ContentSyncScope>> GetScopesAsync(CancellationToken cancellationToken);

    /// <summary>Whether the scope has any content on this instance, to choose where the tab opens.</summary>
    protected abstract Task<bool> HasContentAsync(ContentSyncScope scope, CancellationToken cancellationToken);

    /// <summary>The language the scope opens in.</summary>
    protected abstract Task<string?> DefaultLanguageAsync(ContentSyncScope scope, IReadOnlyList<ContentSyncLanguage> languages, CancellationToken cancellationToken);

    public override async Task<ContentSyncStatusEntryClientProperties> ConfigureTemplateProperties(ContentSyncStatusEntryClientProperties properties)
    {
        // Without a target there's nothing to compare: Kentico's own list, as the status lists do.
        if (settings.Source.TargetUrl is null)
        {
            properties.RedirectUrl = pageLinkGenerator.GetPath(typeof(SynchronizationsList));
            return properties;
        }

        var scopes = await GetScopesAsync(CancellationToken.None);
        var languages = await filterOptionsProvider.GetContentLanguagesAsync(CancellationToken.None);

        properties.EntryPath = pageLinkGenerator.GetPath(GetType());
        properties.ScopeIds = [.. scopes.Select(scope => scope.Id)];
        properties.LanguageNames = [.. languages.Select(language => language.Name)];
        properties.StorageKey = ContentSyncStatusNavigation.StorageKey(TabKey, (await userAccessor.Get()).UserID);
        properties.FullReload = options.Value.FullReloadOnSwitch;

        return properties;
    }

    /// <summary>Where the tab opens without a usable remembered choice.</summary>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse<ContentSyncStatusDefaultTarget>> GetDefaultTarget(CancellationToken cancellationToken)
    {
        if (settings.Source.TargetUrl is null)
        {
            return ResponseFrom(new ContentSyncStatusDefaultTarget(string.Empty));
        }

        var scopes = await GetScopesAsync(cancellationToken);
        var languages = await filterOptionsProvider.GetContentLanguagesAsync(cancellationToken);

        // Without scopes, the list explains that there's nothing to compare (scope 0 is never one).
        var scope = await ContentSyncStatusViewBuilder.DefaultScopeAsync(scopes, HasContentAsync, cancellationToken);
        string? language = (scope is null ? null : await DefaultLanguageAsync(scope, languages, cancellationToken))
            ?? DefaultContentLanguage(languages);

        return ResponseFrom(new ContentSyncStatusDefaultTarget(language is null
            ? string.Empty
            : ContentSyncStatusNavigation.ListPath(pageLinkGenerator.GetPath(GetType()), scope?.Id ?? 0, language)));
    }

    /// <summary>The instance's default content language, or the first if none is marked.</summary>
    internal static string? DefaultContentLanguage(IReadOnlyList<ContentSyncLanguage> languages) =>
        (languages.FirstOrDefault(language => language.IsDefault) ?? languages.FirstOrDefault())?.Name;
}

[UIEvaluatePermission(SystemPermissions.VIEW)]
internal sealed class ContentSyncPagesEntry(
    IContentSyncToolkitSettings settings,
    IOptions<ContentSyncToolkitOptions> options,
    IContentSyncFilterOptionsProvider filterOptionsProvider,
    IAuthenticatedUserAccessor userAccessor,
    IPageLinkGenerator pageLinkGenerator,
    IContentSyncScopeProvider scopeProvider,
    IContentSyncScopeAccess scopeAccess,
    IInfoProvider<WebsiteChannelInfo> websiteChannelProvider,
    IInfoProvider<ContentLanguageInfo> languageProvider)
    : ContentSyncStatusEntryBase(settings, options, filterOptionsProvider, userAccessor, pageLinkGenerator)
{
    protected override string TabKey => ContentSyncApplicationExtender.PagesSlug;

    protected override Task<IReadOnlyList<ContentSyncScope>> GetScopesAsync(CancellationToken cancellationToken) =>
        scopeProvider.GetWebsiteChannelsAsync(cancellationToken);

    // Pages the user can't see in the page tree don't count, as they aren't listed.
    protected override Task<bool> HasContentAsync(ContentSyncScope scope, CancellationToken cancellationToken) =>
        scopeAccess.HasVisiblePagesAsync(scope, cancellationToken);

    // The channel's primary language, which Kentico's page tree opens on. A website channel scope's ID is
    // its WebsiteChannelID.
    protected override async Task<string?> DefaultLanguageAsync(
        ContentSyncScope scope, IReadOnlyList<ContentSyncLanguage> languages, CancellationToken cancellationToken)
    {
        var channel = await websiteChannelProvider.GetAsync(scope.Id, cancellationToken);
        if (channel is null)
        {
            return null;
        }

        var primary = await languageProvider.GetAsync(channel.WebsiteChannelPrimaryContentLanguageID, cancellationToken);
        return languages.FirstOrDefault(language => string.Equals(language.Name, primary?.ContentLanguageName, StringComparison.OrdinalIgnoreCase))?.Name;
    }
}

[UIEvaluatePermission(SystemPermissions.VIEW)]
internal sealed class ContentSyncContentHubEntry(
    IContentSyncToolkitSettings settings,
    IOptions<ContentSyncToolkitOptions> options,
    IContentSyncFilterOptionsProvider filterOptionsProvider,
    IAuthenticatedUserAccessor userAccessor,
    IPageLinkGenerator pageLinkGenerator,
    IContentSyncScopeProvider scopeProvider,
    IContentSyncScopeAccess scopeAccess)
    : ContentSyncStatusEntryBase(settings, options, filterOptionsProvider, userAccessor, pageLinkGenerator)
{
    protected override string TabKey => ContentSyncApplicationExtender.ContentHubSlug;

    protected override Task<IReadOnlyList<ContentSyncScope>> GetScopesAsync(CancellationToken cancellationToken) =>
        scopeProvider.GetWorkspacesAsync(cancellationToken);

    protected override Task<bool> HasContentAsync(ContentSyncScope scope, CancellationToken cancellationToken) =>
        scopeAccess.HasContentItemsAsync(scope, cancellationToken);

    // The instance's default content language, as Kentico's Content hub opens on it.
    protected override Task<string?> DefaultLanguageAsync(
        ContentSyncScope scope, IReadOnlyList<ContentSyncLanguage> languages, CancellationToken cancellationToken) =>
        Task.FromResult(DefaultContentLanguage(languages));
}

/// <summary>Kentico's workspace dropdown in the top bar, listing the editor's channels or workspaces.</summary>
internal abstract class ContentSyncScopeLevelBase : Page<ContentHubWorkspaceClientProperties>
{
    /// <summary>The selected channel's or workspace's ID.</summary>
    [PageParameter(typeof(IntPageModelBinder))]
    public int ScopeId { get; set; }

    protected abstract Task<IReadOnlyList<ContentSyncScope>> GetScopesAsync(CancellationToken cancellationToken);

    // With one scope, Kentico hides the dropdown, as in its own applications.
    public override async Task<ContentHubWorkspaceClientProperties> ConfigureTemplateProperties(ContentHubWorkspaceClientProperties properties)
    {
        properties.WorkspaceId = ScopeId;
        properties.Workspaces = [.. (await GetScopesAsync(CancellationToken.None))
            .Select(scope => new Workspace { WorkspaceId = scope.Id, Name = scope.Name, DisplayName = scope.DisplayName })];

        return properties;
    }
}

[UIEvaluatePermission(SystemPermissions.VIEW)]
[UINavigation(false)]
[UIBreadcrumbs(false)]
internal sealed class ContentSyncPagesChannelLevel(IContentSyncScopeProvider scopeProvider) : ContentSyncScopeLevelBase
{
    protected override Task<IReadOnlyList<ContentSyncScope>> GetScopesAsync(CancellationToken cancellationToken) =>
        scopeProvider.GetWebsiteChannelsAsync(cancellationToken);
}

[UIEvaluatePermission(SystemPermissions.VIEW)]
[UINavigation(false)]
[UIBreadcrumbs(false)]
internal sealed class ContentSyncContentHubWorkspaceLevel(IContentSyncScopeProvider scopeProvider) : ContentSyncScopeLevelBase
{
    protected override Task<IReadOnlyList<ContentSyncScope>> GetScopesAsync(CancellationToken cancellationToken) =>
        scopeProvider.GetWorkspacesAsync(cancellationToken);
}

/// <summary>Kentico's language dropdown in the top bar, with the instance's languages in Kentico's order.</summary>
internal abstract class ContentSyncLanguageLevelBase(IContentLanguageModelRetriever languageRetriever)
    : Page<ContentHubContentLanguageClientProperties>
{
    /// <summary>The selected language's code name.</summary>
    [PageParameter(typeof(StringPageModelBinder))]
    public string LanguageName { get; set; } = string.Empty;

    public override async Task<ContentHubContentLanguageClientProperties> ConfigureTemplateProperties(ContentHubContentLanguageClientProperties properties)
    {
        properties.LanguageName = LanguageName;
        properties.Languages = await languageRetriever.Get(CancellationToken.None);

        return properties;
    }
}

[UIEvaluatePermission(SystemPermissions.VIEW)]
[UINavigation(false)]
[UIBreadcrumbs(false)]
internal sealed class ContentSyncPagesLanguageLevel(IContentLanguageModelRetriever languageRetriever)
    : ContentSyncLanguageLevelBase(languageRetriever);

[UIEvaluatePermission(SystemPermissions.VIEW)]
[UINavigation(false)]
[UIBreadcrumbs(false)]
internal sealed class ContentSyncContentHubLanguageLevel(IContentLanguageModelRetriever languageRetriever)
    : ContentSyncLanguageLevelBase(languageRetriever);

internal sealed class ContentSyncStatusNavigationProperties
{
    public string EntryPath { get; set; } = string.Empty;

    public string ApplicationPath { get; set; } = string.Empty;

    public bool FullReload { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public int ScopeId { get; set; }

    public string LanguageName { get; set; } = string.Empty;
}

/// <summary>
/// Renders nothing, next to a status list's caption. It keeps Content synchronization's left menu when
/// Kentico's dropdowns switch the list (the admin would otherwise draw the list without it), and saves
/// the list's channel or workspace and language as the editor's last choice on the tab.
/// </summary>
internal sealed class ContentSyncStatusNavigationComponent
    : SubcaptionComponentBase<ContentSyncStatusNavigationProperties, ContentSyncStatusNavigationProperties>
{
    public override string ComponentName => ContentSyncToolkitAdminModule.NavigationComponent;

    protected override Task ConfigureComponentClientProperties(ContentSyncStatusNavigationProperties componentClientProperties)
    {
        componentClientProperties.EntryPath = ComponentProperties.EntryPath;
        componentClientProperties.ApplicationPath = ComponentProperties.ApplicationPath;
        componentClientProperties.FullReload = ComponentProperties.FullReload;
        componentClientProperties.StorageKey = ComponentProperties.StorageKey;
        componentClientProperties.ScopeId = ComponentProperties.ScopeId;
        componentClientProperties.LanguageName = ComponentProperties.LanguageName;

        return base.ConfigureComponentClientProperties(componentClientProperties);
    }
}
