using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentLinkGraphTests
{
    private static ContentLinkGraph Graph(Dictionary<int, string> types, Dictionary<int, int[]> links) =>
        new(new Dictionary<Guid, int>(), types, links.ToDictionary(link => link.Key, link => (IReadOnlyCollection<int>)link.Value));

    [Test]
    public void FindItemsLinkingTo_FollowsLinksAtAnyDepth()
    {
        // Page 1 -> product 2 -> image 3, as /Products/Clone_AeroPress on the rig.
        var graph = Graph(new() { [1] = "DG.Page", [2] = "DG.Product", [3] = "DG.Image" }, new() { [1] = [2], [2] = [3] });

        var found = graph.FindItemsLinkingTo("DG.Image");

        Assert.That(found, Is.EquivalentTo(new Dictionary<int, int> { [1] = 3, [2] = 3 }));
    }

    [Test]
    public void FindItemsLinkingTo_LeavesOutTheItemsOfThatTypeThemselves()
    {
        var graph = Graph(new() { [1] = "DG.Image", [2] = "DG.Image" }, new() { [1] = [2] });

        Assert.That(graph.FindItemsLinkingTo("DG.Image"), Is.Empty);
    }

    [Test]
    public void FindItemsLinkingTo_HandlesCycles()
    {
        var graph = Graph(new() { [1] = "DG.Page", [2] = "DG.Page", [3] = "DG.Image" }, new() { [1] = [2], [2] = [1, 3] });

        Assert.That(graph.FindItemsLinkingTo("DG.Image").Keys, Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public void FindItemsLinkingTo_IgnoresTheCaseOfTheTypeName()
    {
        var graph = Graph(new() { [1] = "DG.Page", [2] = "DancingGoat.Image" }, new() { [1] = [2] });

        Assert.That(graph.FindItemsLinkingTo("dancinggoat.image").Keys, Is.EquivalentTo(new[] { 1 }));
    }

    [Test]
    public void FindItemsLinkingTo_NoItemsOfThatType_FindsNothing()
    {
        var graph = Graph(new() { [1] = "DG.Page", [2] = "DG.Product" }, new() { [1] = [2] });

        Assert.That(graph.FindItemsLinkingTo("DG.Image"), Is.Empty);
    }

    private static ContentInventoryItem PageAt(string path, string type) =>
        new(Guid.NewGuid(), ContentInventoryItemKind.WebPage, type, "Channel", "en", path, null, "Published");

    [Test]
    public void FindParentOfType_FindsTheNearestParentOfTheType()
    {
        var pages = new[] { PageAt("/a", "DG.Section"), PageAt("/a/b", "DG.Other") }.ToDictionary(page => page.TreePath!);

        var parent = RequiredObjectsComparer.FindParentOfType(PageAt("/a/b/c", "DG.Page"), "DG.Section", pages);

        Assert.That(parent?.TreePath, Is.EqualTo("/a"));
    }

    [Test]
    public void FindParentOfType_TopLevelPage_HasNoParent() =>
        Assert.That(
            RequiredObjectsComparer.FindParentOfType(PageAt("/a", "DG.Page"), "DG.Page", new Dictionary<string, ContentInventoryItem>()),
            Is.Null);

    private static readonly RequiredObject imageType =
        new(RequiredObjectKind.ContentType, Guid.NewGuid(), "DG.Image", "Image") { DefinitionHash = "A" };

    private static ContentSyncStatusItem New(params RequiredObjectIssue[] issues) =>
        new(Guid.NewGuid(), ContentSyncStatus.New, PageAt("/a", "DG.Page"), null) { RequiredObjectIssues = issues };

    [Test]
    public void StatusTooltip_LinkedItem_NamesItAndMentionsDetailedSelection() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(New(new RequiredObjectIssue(imageType, RequiredObjectProblem.DefinitionDiffers) { ViaLinkedItem = "AeroPress" })),
            Is.EqualTo("Linked item AeroPress: Content type Image has different field definitions on the source and target instance. "
                + "A developer needs to deploy it to the target first. "
                + "If the target already has the linked item, you can leave it out in Detailed selection instead. "
                + "Then a sync creates it."));

    [Test]
    public void StatusTooltip_ItemsOwnIssue_DoesNotMentionDetailedSelection() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(New(new RequiredObjectIssue(imageType, RequiredObjectProblem.DefinitionDiffers))),
            Does.Not.Contain("Detailed selection").And.Not.Contain("Linked item"));
}
