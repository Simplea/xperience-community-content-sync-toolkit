using System.Reflection;

using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Admin;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncStatusAdminWiringTests
{
    // Unspecified, as the filter's date inputs send them.
    private static DateTime Day(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    // The tabs are pages of Kentico's application, so its View permission is what a role is granted.
    // Without it, only administrators could ever open them.
    [Test]
    public void KenticosApplication_DeclaresTheViewPermission()
    {
        var declared = typeof(ContentSynchronizationApplication).GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType == typeof(UIPermissionAttribute))
            .Select(attribute => attribute.ConstructorArguments[0].Value as string);

        Assert.That(declared, Does.Contain(SystemPermissions.VIEW));
    }

    private static UIPageAttribute Registration(Type pageType) =>
        typeof(ContentSyncStatusTabBase).Assembly.GetCustomAttributes<UIPageAttribute>().Single(page => page.Type == pageType);

    // Before Kentico's own list (order 100): the application opens on its first page.
    [TestCase(typeof(ContentSyncPagesEntry), "pages", 10)]
    [TestCase(typeof(ContentSyncContentHubEntry), "content-hub", 20)]
    public void EveryMenuEntry_IsAPageOfKenticosApplication_BeforeItsList(Type entryType, string slug, int order)
    {
        var registration = Registration(entryType);

        Assert.That(registration.ParentType, Is.EqualTo(typeof(ContentSynchronizationApplication)));
        Assert.That(registration.Slug, Is.EqualTo(slug));
        Assert.That(registration.Order, Is.EqualTo(order));
        Assert.That(registration.TemplateName, Is.EqualTo(ContentSyncToolkitAdminModule.EntryTemplate));
    }

    // /content-sync/{tab}/{scope}/{language}/all: Kentico's workspace and language dropdowns in the top
    // bar, then the list at the segment they navigate to. See docs/specs/header-dropdowns.md.
    [TestCase(typeof(ContentSyncPagesEntry), typeof(ContentSyncPagesChannelLevel), typeof(ContentSyncPagesLanguageLevel), typeof(ContentSyncStatusPagesTab))]
    [TestCase(typeof(ContentSyncContentHubEntry), typeof(ContentSyncContentHubWorkspaceLevel), typeof(ContentSyncContentHubLanguageLevel), typeof(ContentSyncStatusContentHubTab))]
    public void EveryList_SitsUnderItsScopeAndLanguageLevels(Type entryType, Type scopeLevel, Type languageLevel, Type tabType)
    {
        Assert.That(Registration(scopeLevel).ParentType, Is.EqualTo(entryType));
        Assert.That(Registration(scopeLevel).TemplateName, Is.EqualTo(TemplateNames.WORKSPACE));
        Assert.That(Registration(languageLevel).ParentType, Is.EqualTo(scopeLevel));
        Assert.That(Registration(languageLevel).TemplateName, Is.EqualTo(TemplateNames.CONTENT_LANGUAGE));
        Assert.That(Registration(tabType).ParentType, Is.EqualTo(languageLevel));
        Assert.That(Registration(tabType).ParameterDefaultValue, Is.EqualTo(ContentSyncStatusNavigation.ListSlug));
        Assert.That(
            new[] { scopeLevel, languageLevel, tabType }.Select(Registration).Select(registration => registration.Slug),
            Is.All.EqualTo(PageParameterConstants.PARAMETERIZED_SLUG));
    }

    // The levels below a menu entry have no menu entry or breadcrumb of their own: the menu highlights the
    // tab, and the breadcrumb names it.
    [TestCase(typeof(ContentSyncPagesChannelLevel))]
    [TestCase(typeof(ContentSyncPagesLanguageLevel))]
    [TestCase(typeof(ContentSyncStatusPagesTab))]
    [TestCase(typeof(ContentSyncContentHubWorkspaceLevel))]
    [TestCase(typeof(ContentSyncContentHubLanguageLevel))]
    [TestCase(typeof(ContentSyncStatusContentHubTab))]
    public void LevelsBelowTheMenuEntry_AreLeftOutOfTheMenuAndBreadcrumbs(Type pageType)
    {
        Assert.That(pageType.GetCustomAttribute<UINavigationAttribute>()?.Display, Is.False);
        Assert.That(pageType.GetCustomAttribute<UIBreadcrumbsAttribute>()?.Display, Is.False);
    }

    [TestCase("pages", true)]
    [TestCase("/pages", true)]
    [TestCase("content-hub/", true)]
    [TestCase("synchronizations", false)]
    [TestCase(null, false)]
    public void Extender_RecognizesTheStatusPages(string? path, bool expected) =>
        Assert.That(ContentSyncApplicationExtender.IsStatusPage(path), Is.EqualTo(expected));

    // Kentico's list stays, so an instance that isn't a source keeps the application as it was.
    [Test]
    public void Extender_LeavesOnlyKenticosListInTheNavigation()
    {
        var properties = new TemplateClientProperties
        {
            Navigation = new Navigation
            {
                Items =
                [
                    new NavigationItem { Path = "pages" },
                    new NavigationItem { Path = "content-hub" },
                    new NavigationItem { Path = "synchronizations" },
                ],
            },
        };

        ContentSyncApplicationExtender.HideStatusPages(properties);

        Assert.That(properties.Navigation.Items.Select(item => item.Path), Is.EqualTo(new[] { "synchronizations" }));
    }

    // Enforced on every page, so direct navigation is refused, not just the menu entry hidden.
    [TestCase(typeof(ContentSyncStatusPagesTab))]
    [TestCase(typeof(ContentSyncStatusContentHubTab))]
    [TestCase(typeof(ContentSyncPagesEntry))]
    [TestCase(typeof(ContentSyncContentHubEntry))]
    [TestCase(typeof(ContentSyncPagesChannelLevel))]
    [TestCase(typeof(ContentSyncPagesLanguageLevel))]
    [TestCase(typeof(ContentSyncContentHubWorkspaceLevel))]
    [TestCase(typeof(ContentSyncContentHubLanguageLevel))]
    public void EveryPage_EvaluatesTheViewPermission(Type tabType)
    {
        var evaluated = tabType.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType == typeof(UIEvaluatePermissionAttribute))
            .Select(attribute => attribute.ConstructorArguments[0].Value as string);

        Assert.That(evaluated, Does.Contain(SystemPermissions.VIEW));
    }

    [TestCase(typeof(ContentSyncStatusPagesTab))]
    [TestCase(typeof(ContentSyncStatusContentHubTab))]
    public void RefreshCommand_RequiresTheViewPermission(Type tabType)
    {
        var command = tabType.GetMethod(nameof(ContentSyncStatusTabBase.Refresh), BindingFlags.Public | BindingFlags.Instance)
            ?.GetCustomAttribute<PageCommandAttribute>();

        Assert.That(command, Is.Not.Null, "Refresh must be exposed as a page command");
        Assert.That(command!.Permission, Is.EqualTo(SystemPermissions.VIEW));
    }

    // The menu entry template asks for the default only when there's no usable remembered choice.
    [TestCase(typeof(ContentSyncPagesEntry))]
    [TestCase(typeof(ContentSyncContentHubEntry))]
    public void DefaultTargetCommand_RequiresTheViewPermission(Type entryType)
    {
        var command = entryType.GetMethod(nameof(ContentSyncStatusEntryBase.GetDefaultTarget), BindingFlags.Public | BindingFlags.Instance)
            ?.GetCustomAttribute<PageCommandAttribute>();

        Assert.That(command, Is.Not.Null, "GetDefaultTarget must be exposed as a page command");
        Assert.That(command!.Permission, Is.EqualTo(SystemPermissions.VIEW));
    }

    // Role management stores a website channel's permissions under this application name (seen in
    // CMS_ApplicationPermission); a convention rather than a public API, so it's pinned here.
    [Test]
    public void WebsiteChannelApplicationIdentifier_MatchesRoleManagementsApplicationName()
    {
        var websiteChannelGuid = new Guid("55E3AE4B-3843-46B8-95D5-79611C371D6F");

        Assert.That(ContentSyncScopeAccess.WebsiteChannelApplicationIdentifier(websiteChannelGuid),
            Is.EqualTo("Kentico.Xperience.Application.WebPages_55e3ae4b-3843-46b8-95d5-79611c371d6f"));
    }

    // Which channels and workspaces are listed depends on the signed-in user, so these can't be
    // singletons that outlive a request.
    [Test]
    public void ScopeServices_AreRegisteredPerRequest()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddContentSyncToolkitAdmin();

        Assert.That(services.Single(service => service.ServiceType == typeof(IContentSyncScopeProvider)).Lifetime,
            Is.EqualTo(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped));
        Assert.That(services.Single(service => service.ServiceType == typeof(IContentSyncScopeAccess)).Lifetime,
            Is.EqualTo(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped));
    }

    [Test]
    public void RefreshRequest_IsConsumedExactlyOnce()
    {
        var store = new ContentSyncStatusRefreshRequestStore();
        store.RequestRefresh("pages");

        Assert.That(store.ConsumeRefreshRequest("pages"), Is.True);
        Assert.That(store.ConsumeRefreshRequest("pages"), Is.False);
    }

    [Test]
    public void RefreshRequest_IsIsolatedPerTab()
    {
        var store = new ContentSyncStatusRefreshRequestStore();
        store.RequestRefresh("pages");

        Assert.That(store.ConsumeRefreshRequest("content-hub"), Is.False);
        Assert.That(store.ConsumeRefreshRequest("pages"), Is.True);
    }

    [Test]
    public void FilterValueExtractor_ReadsTheNamedParameter()
    {
        var condition = new WhereCondition().WhereEquals("Channel", "DancingGoatPages");

        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractStringParameter(condition, "Channel"), Is.EqualTo("DancingGoatPages"));
    }

    [Test]
    public void FilterValueExtractor_ReadsEachFieldOfACombinedFilter()
    {
        var condition = new WhereCondition()
            .WhereEquals("Status", "New,incompatible")
            .WhereEquals("PublishedFrom", Day(2026, 1, 1));

        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractStringParameter(condition, "Status"), Is.EqualTo("New,incompatible"));
        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractDateParameter(condition, "PublishedFrom"), Is.EqualTo(Day(2026, 1, 1)));
        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractDateParameter(condition, "PublishedTo"), Is.Null);
        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractDateParameter(condition, "Status"), Is.Null, "a string value isn't read as a date");
    }

    // Every filter field is read back by its property name, so a rename must keep matching. The channel or
    // workspace and the language aren't filter fields: Kentico's dropdowns in the top bar choose them.
    [TestCase(typeof(ContentSyncStatusPagesFilterModel))]
    [TestCase(typeof(ContentSyncStatusContentHubFilterModel))]
    public void FilterModels_ExposeTheFieldsLoadDataReads(Type filterModelType)
    {
        string[] expected = ["OnlyMyChanges", "HideInSync", "Status", "ContentType", "PublishedFrom", "PublishedTo"];

        Assert.That(filterModelType.GetProperties().Select(property => property.Name), Is.EquivalentTo(expected));
    }

    // Kentico 31.2.0 added FilterInitialValues; on earlier versions the lists open on every item.
    [Test]
    public void InitialFilter_HidesItemsInSync_WhereKenticoSupportsIt()
    {
        var properties = new ListingTemplateClientProperties();
        var filterInitialValues = typeof(ListingTemplateClientProperties).GetProperty("FilterInitialValues");

        ContentSyncStatusTabBase.ApplyInitialFilter(properties);

        if (filterInitialValues is null)
        {
            Assert.Pass("This Kentico version has no FilterInitialValues; nothing to set.");
        }

        var values = (IDictionary<string, object>)filterInitialValues.GetValue(properties)!;
        Assert.That(values, Is.EqualTo(new Dictionary<string, object> { ["HideInSync"] = true }));
    }

    // The browser module builds the same addresses from a remembered choice (Client/src/navigation.ts).
    [TestCase("/content-sync/pages", 1, "en", "/content-sync/pages/1/en/all")]
    [TestCase("/content-sync/content-hub/", 12, "es-MX", "/content-sync/content-hub/12/es-MX/all")]
    public void ListPath_IsTheEntryThenScopeLanguageAndList(string entryPath, int scopeId, string languageName, string expected) =>
        Assert.That(ContentSyncStatusNavigation.ListPath(entryPath, scopeId, languageName), Is.EqualTo(expected));

    // One remembered choice per tab and user, so editors sharing a browser keep their own.
    [Test]
    public void RememberedChoice_IsKeptPerTabAndUser()
    {
        string[] keys =
        [
            ContentSyncStatusNavigation.StorageKey("pages", 1),
            ContentSyncStatusNavigation.StorageKey("content-hub", 1),
            ContentSyncStatusNavigation.StorageKey("pages", 2),
        ];

        Assert.That(keys, Is.Unique);
        Assert.That(keys, Is.All.StartWith("xperiencecommunity/content-sync-toolkit/"));
    }

    // The module is built and embedded by the project (see its .csproj), under the names the server uses.
    [Test]
    public void BrowserModule_IsEmbedded_WithTheComponentsThePagesName()
    {
        var assembly = typeof(ContentSyncToolkitAdminModule).Assembly;
        string? entry = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.Contains(".AdminResources.xperiencecommunity.content.sync.toolkit.entry.", StringComparison.Ordinal)
                && name.EndsWith(".js", StringComparison.Ordinal));

        Assert.That(entry, Is.Not.Null, "the browser module isn't embedded");
        using var reader = new StreamReader(assembly.GetManifestResourceStream(entry!)!);
        string module = reader.ReadToEnd();

        // A page template's export adds "Template" to its name; a component's is its name.
        Assert.That(module, Does.Contain(ContentSyncToolkitAdminModule.EntryTemplate.Split('/')[^1] + "Template"));
        Assert.That(module, Does.Contain(ContentSyncToolkitAdminModule.NavigationComponent.Split('/')[^1]));
    }

    [Test]
    public void FullReloadOnSwitch_IsOffByDefault() =>
        Assert.That(new ContentSyncToolkitOptions().FullReloadOnSwitch, Is.False);

    // The Status filter allows several options; its condition carries them as one parameter that
    // the extractor reads like any other field.
    [Test]
    public async Task MultiValueConditionBuilder_JoinsTheSelectedValuesIntoOneParameter()
    {
        var condition = await new ContentSyncStatusMultiValueConditionBuilder().Build("Status", new List<string> { "incompatible", "", "New" });

        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractStringParameter(condition, "Status"), Is.EqualTo("incompatible,New"));
    }

    [TestCase(null)]
    [TestCase(42)]
    public async Task MultiValueConditionBuilder_AddsNothing_WithoutASelection(object? value)
    {
        var empty = await new ContentSyncStatusMultiValueConditionBuilder().Build("Status", value!);
        var none = await new ContentSyncStatusMultiValueConditionBuilder().Build("Status", Array.Empty<string>());

        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractStringParameter(empty, "Status"), Is.Null);
        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractStringParameter(none, "Status"), Is.Null);
    }

    [Test]
    public async Task StatusFilterOptions_AreSearchable_AndShowUnknownSelectionsAsInvalid()
    {
        var provider = new ContentSyncStatusFilterOptionsDataProvider(Options.Create(new ContentSyncToolkitOptions { ComparePageOrder = true }));

        var all = await provider.GetItemsAsync(string.Empty, 0, CancellationToken.None);
        var searched = await provider.GetItemsAsync("on", 0, CancellationToken.None);
        var selected = (await provider.GetSelectedItemsAsync(["incompatible", "PublishPending"], CancellationToken.None)).ToList();

        Assert.That(all.Items.Select(item => item.Value), Is.EqualTo(ContentSyncStatusListingSupport.StatusFilterOptions.Select(option => option.Value)));
        Assert.That(searched.Items.Select(item => item.Text), Is.EqualTo(new[] { "Only on target" }));
        Assert.That(selected.Select(item => (item.Text, item.IsValid)), Is.EqualTo(new[] { ("Incompatible", true), ("PublishPending", false) }));
    }

    // Nothing can be Reordered when page order isn't compared, so it isn't offered, and a filter
    // remembered from before shows as invalid.
    [Test]
    public async Task StatusFilterOptions_LeaveOutReordered_ByDefault()
    {
        var provider = new ContentSyncStatusFilterOptionsDataProvider(Options.Create(new ContentSyncToolkitOptions()));

        var all = await provider.GetItemsAsync(string.Empty, 0, CancellationToken.None);
        var selected = (await provider.GetSelectedItemsAsync(["Reordered"], CancellationToken.None)).Single();

        Assert.That(all.Items.Select(item => item.Value), Does.Not.Contain("Reordered").And.Contain("Moved"));
        Assert.That(selected.IsValid, Is.False);
    }

    [Test]
    public void StatusLegend_MentionsReordered_OnlyWhenPageOrderIsCompared()
    {
        Assert.That(ContentSyncStatusListingSupport.StatusLegend(comparePageOrder: true), Does.Contain("<strong>Reordered</strong>"));
        Assert.That(ContentSyncStatusListingSupport.StatusLegend(comparePageOrder: false), Does.Not.Contain("Reordered").And.Contain("<strong>Moved</strong>"));
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    [TestCase("True", true)]
    public void FilterValueExtractor_ReadsACheckbox(object value, bool expected) =>
        Assert.That(
            ContentSyncStatusFilterValueExtractor.ExtractBoolParameter(new WhereCondition().WhereEquals("ShowInSync", value), "ShowInSync"),
            Is.EqualTo(expected));

    [Test]
    public void FilterValueExtractor_ReturnsNull_ForAnotherParameterOrNoCondition()
    {
        var condition = new WhereCondition().WhereEquals("Channel", "DancingGoatPages");

        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractStringParameter(condition, "Workspace"), Is.Null);
        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractStringParameter(null, "Channel"), Is.Null);
        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractStringParameter(new WhereCondition(), "Channel"), Is.Null);
    }
}
