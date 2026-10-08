using System.Reflection;

using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using XperienceCommunity.ContentSyncToolkit.Admin;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncStatusAdminWiringTests
{
    // Unspecified, as the filter's date inputs send them.
    private static DateTime Day(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    // Without UIPermission(VIEW) on the application, Role management can't grant VIEW for it, and
    // the listing template requires VIEW, so only administrators could ever open the page.
    [Test]
    public void Application_DeclaresTheViewPermission()
    {
        var declared = typeof(ContentSyncStatusApplication).GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType == typeof(UIPermissionAttribute))
            .Select(attribute => attribute.ConstructorArguments[0].Value as string);

        Assert.That(declared, Does.Contain(SystemPermissions.VIEW));
    }

    // Enforced on every tab, so direct navigation is refused, not just the menu entry hidden.
    [TestCase(typeof(ContentSyncStatusPagesTab))]
    [TestCase(typeof(ContentSyncStatusContentHubTab))]
    public void EveryTab_EvaluatesTheViewPermission(Type tabType)
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

    // Every filter field is read back by its property name, so a rename must keep matching.
    [TestCase(typeof(ContentSyncStatusPagesFilterModel), "Channel")]
    [TestCase(typeof(ContentSyncStatusContentHubFilterModel), "Workspace")]
    public void FilterModels_ExposeTheFieldsLoadDataReads(Type filterModelType, string scopeField)
    {
        string[] expected = [scopeField, "Language", "HideInSync", "Status", "ContentType", "PublishedFrom", "PublishedTo"];

        Assert.That(filterModelType.GetProperties().Select(property => property.Name), Is.EquivalentTo(expected));
    }

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
        var provider = new ContentSyncStatusFilterOptionsDataProvider();

        var all = await provider.GetItemsAsync(string.Empty, 0, CancellationToken.None);
        var searched = await provider.GetItemsAsync("on", 0, CancellationToken.None);
        var selected = (await provider.GetSelectedItemsAsync(["incompatible", "PublishPending"], CancellationToken.None)).ToList();

        Assert.That(all.Items.Select(item => item.Value), Is.EqualTo(ContentSyncStatusListingSupport.StatusFilterOptions.Select(option => option.Value)));
        Assert.That(searched.Items.Select(item => item.Text), Is.EqualTo(new[] { "Only on target" }));
        Assert.That(selected.Select(item => (item.Text, item.IsValid)), Is.EqualTo(new[] { ("Incompatible", true), ("PublishPending", false) }));
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    [TestCase("True", true)]
    public void FilterValueExtractor_ReadsACheckbox(object value, bool expected) =>
        Assert.That(
            ContentSyncStatusFilterValueExtractor.ExtractBoolParameter(new WhereCondition().WhereEquals("HideInSync", value), "HideInSync"),
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
