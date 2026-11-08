using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// Hints on Only on target items that this instance has in another workspace.
public class WorkspaceMoveHintsTests
{
    private const int ListedWorkspace = 3;
    private const int OtherWorkspace = 2;

    private static readonly Guid itemGuid = Guid.NewGuid();

    private static ContentSyncWorkspaceEntry Entry(int workspaceId = OtherWorkspace, int contentItemId = 107) =>
        new(itemGuid, contentItemId, workspaceId);

    private static ContentSyncStatusItem OnlyOnTarget() =>
        new(itemGuid, ContentSyncStatus.OnlyOnTarget, null, new ContentInventoryItem(
            itemGuid, ContentInventoryItemKind.ContentHubItem, "DG.Social", "Ltd", "en", null, DateTime.UtcNow, "Published"));

    private static ContentSyncMovedItem Moved(string? workspace = "Events") => new(itemGuid, workspace);

    [Test]
    public void Select_AnItemInAnotherWorkspace_NamesTheWorkspace()
    {
        var moved = ContentSyncWorkspaceMoveLookup.Select(
            [Entry()], ListedWorkspace, new HashSet<int> { 107 }, new Dictionary<int, string> { [OtherWorkspace] = "Events" });

        Assert.That(moved[itemGuid].WorkspaceDisplayName, Is.EqualTo("Events"));
    }

    // Its name isn't shown, but the warning not to delete the item still applies.
    [Test]
    public void Select_AWorkspaceTheUserCantSee_KeepsTheItem_WithoutAName()
    {
        var moved = ContentSyncWorkspaceMoveLookup.Select([Entry()], ListedWorkspace, new HashSet<int> { 107 }, new Dictionary<int, string>());

        Assert.That(moved[itemGuid].WorkspaceDisplayName, Is.Null);
    }

    [Test]
    public void Select_AnItemWithoutTheListedLanguage_IsNotAMove() =>
        Assert.That(
            ContentSyncWorkspaceMoveLookup.Select(
                [Entry()], ListedWorkspace, new HashSet<int>(), new Dictionary<int, string> { [OtherWorkspace] = "Events" }),
            Is.Empty);

    [Test]
    public void Select_AnItemInTheListedWorkspace_IsNotAMove() =>
        Assert.That(
            ContentSyncWorkspaceMoveLookup.Select(
                [Entry(ListedWorkspace)], ListedWorkspace, new HashSet<int> { 107 }, new Dictionary<int, string>()),
            Is.Empty);

    [Test]
    public void StatusLabel_AMovedItem_ReadsMovedHere() =>
        Assert.That(ContentSyncStatusListingSupport.StatusLabel(OnlyOnTarget(), null, Moved()), Is.EqualTo("Moved here"));

    [Test]
    public void StatusLabel_WithoutAMove_KeepsTheOtherLabels() =>
        Assert.That(ContentSyncStatusListingSupport.StatusLabel(OnlyOnTarget(), null, null), Is.EqualTo("Only on target"));

    // A sync can fix it, unlike the rest of Only on target.
    [Test]
    public void StatusColor_AMovedItem_IsTheColorOfWhatASyncChanges()
    {
        Assert.That(
            ContentSyncStatusListingSupport.StatusColor(OnlyOnTarget(), Moved()),
            Is.EqualTo(ContentSyncStatusListingSupport.StatusColor(ContentSyncStatus.Moved)));
        Assert.That(
            ContentSyncStatusListingSupport.StatusColor(OnlyOnTarget(), null),
            Is.EqualTo(ContentSyncStatusListingSupport.StatusColor(ContentSyncStatus.OnlyOnTarget)));
    }

    [Test]
    public void StatusTooltip_AMovedItem_SaysWhereAndNotToDeleteIt()
    {
        string? tooltip = ContentSyncStatusListingSupport.StatusTooltip(OnlyOnTarget(), opensOnTarget: true, movedItem: Moved());

        Assert.That(tooltip, Is.EqualTo(
            "Moved to Events here, but still in this workspace on the target. Don't delete it: syncing it from Events moves it there."));
    }

    [Test]
    public void StatusTooltip_AWorkspaceTheUserCantSee_IsNotNamed()
    {
        string? tooltip = ContentSyncStatusListingSupport.StatusTooltip(OnlyOnTarget(), movedItem: Moved(null));

        Assert.That(tooltip, Does.StartWith("Moved to another workspace here").And.Contain("Don't delete it"));
    }

    [Test]
    public void StatusLegend_ExplainsMovedHere() =>
        Assert.That(ContentSyncStatusListingSupport.StatusLegend(comparePageOrder: false), Does.Contain("<strong>Moved here</strong>"));
}
