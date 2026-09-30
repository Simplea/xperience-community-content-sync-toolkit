using System.Reflection;

using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using XperienceCommunity.ContentSyncToolkit.Admin;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncStatusAdminWiringTests
{
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
    public void FilterValueExtractor_ReturnsNull_ForAnotherParameterOrNoCondition()
    {
        var condition = new WhereCondition().WhereEquals("Channel", "DancingGoatPages");

        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractStringParameter(condition, "Workspace"), Is.Null);
        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractStringParameter(null, "Channel"), Is.Null);
        Assert.That(ContentSyncStatusFilterValueExtractor.ExtractStringParameter(new WhereCondition(), "Channel"), Is.Null);
    }
}
