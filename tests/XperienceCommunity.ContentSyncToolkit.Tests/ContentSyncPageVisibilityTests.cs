using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncPageVisibilityTests
{
    private static ContentInventoryItem Page(Guid guid, string treePath) =>
        new(guid, ContentInventoryItemKind.WebPage, "T", "Channel", "en", treePath, null, "Published");

    private static ContentSyncStatusItem Local(Guid guid, string treePath) =>
        new(guid, ContentSyncStatus.MissingOnTarget, Page(guid, treePath), null);

    private static ContentSyncStatusItem OnlyOnTarget(string treePath)
    {
        var guid = Guid.NewGuid();
        return new ContentSyncStatusItem(guid, ContentSyncStatus.ExtraOnTarget, null, Page(guid, treePath));
    }

    private static ContentSyncStatusResult Result(params ContentSyncStatusItem[] items) => new(true, items);

    [Test]
    public void All_SeesEveryPage()
    {
        var hidden = Local(Guid.NewGuid(), "/Secret");

        Assert.That(ContentSyncPageVisibility.All.CanSee(hidden), Is.True);
        Assert.That(ContentSyncPageVisibility.All.Apply(Result(hidden)).Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void Apply_RemovesPagesTheUserCantSee_BeforeCounting()
    {
        Guid visible = Guid.NewGuid(), hidden = Guid.NewGuid();
        var visibility = ContentSyncPageVisibility.For(rootVisible: true, [(visible, "/Articles", true), (hidden, "/Secret", false)]);

        var items = visibility.Apply(Result(Local(visible, "/Articles"), Local(hidden, "/Secret"))).Items;

        Assert.That(items.Select(item => item.Guid), Is.EqualTo(new[] { visible }));
    }

    // A page only on the target gets the permissions of its nearest ancestor here, as it would once
    // synced; with no ancestor here, the channel root's.
    [TestCase("/Secret/Plans", false)]
    [TestCase("/Secret/Plans/Q3", false)]
    [TestCase("/Articles/New", true)]
    [TestCase("/Elsewhere/Page", true)]
    public void CanSee_APageOnlyOnTheTarget_FollowsItsNearestAncestorHere(string treePath, bool expected)
    {
        var visibility = ContentSyncPageVisibility.For(rootVisible: true, [(Guid.NewGuid(), "/Articles", true), (Guid.NewGuid(), "/Secret", false)]);

        Assert.That(visibility.CanSee(OnlyOnTarget(treePath)), Is.EqualTo(expected));
    }

    [Test]
    public void CanSee_WithoutRootPermissions_HidesPagesWithNoAncestorHere()
    {
        var visibility = ContentSyncPageVisibility.For(rootVisible: false, []);

        Assert.That(visibility.CanSee(OnlyOnTarget("/Anything")), Is.False);
    }

    // A page that broke inheritance can be visible under a hidden parent, and the reverse.
    [Test]
    public void CanSee_UsesThePagesOwnPermissions_OverItsAncestors()
    {
        Guid parent = Guid.NewGuid(), child = Guid.NewGuid();
        var visibility = ContentSyncPageVisibility.For(rootVisible: true, [(parent, "/Team", false), (child, "/Team/Shared", true)]);

        Assert.That(visibility.CanSee(Local(child, "/Team/Shared")), Is.True);
        Assert.That(visibility.CanSee(Local(parent, "/Team")), Is.False);
    }

    // A reorder tooltip never names a page the user can't see, nor a parent they can't see.
    [Test]
    public void Apply_CountsHiddenPagesOutOfPlace_InsteadOfNamingThem()
    {
        Guid row = Guid.NewGuid(), shown = Guid.NewGuid(), hidden = Guid.NewGuid(), parent = Guid.NewGuid();
        var visibility = ContentSyncPageVisibility.For(rootVisible: true,
            [(row, "/A/Row", true), (shown, "/A/Shown", true), (hidden, "/A/Hidden", false), (parent, "/A", false)]);
        var item = Local(row, "/A/Row") with
        {
            Status = ContentSyncStatus.OutOfDateOnTarget,
            Reason = ContentSyncStatusReason.Reordered,
            Reorder = new ContentSyncReorder("/A", [Page(shown, "/A/Shown"), Page(hidden, "/A/Hidden")]) { Parent = Page(parent, "/A") },
        };

        var reorder = visibility.Apply(Result(item)).Items.Single().Reorder!;

        Assert.That(reorder.MisplacedPages.Select(page => page.Guid), Is.EqualTo(new[] { shown }));
        Assert.That(reorder.HiddenMisplacedCount, Is.EqualTo(1));
        Assert.That(reorder.Parent, Is.Null);
    }

    [Test]
    public void Apply_LeavesAnUnavailableTargetResultAlone()
    {
        var unavailable = new ContentSyncStatusResult(false, []);

        Assert.That(ContentSyncPageVisibility.For(false, []).Apply(unavailable), Is.SameAs(unavailable));
    }
}
