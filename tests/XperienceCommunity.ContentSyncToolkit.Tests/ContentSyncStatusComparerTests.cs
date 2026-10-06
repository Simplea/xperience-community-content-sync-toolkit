using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncStatusComparerTests
{
    private static readonly DateTime earlier = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime later = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private static ContentInventoryItem CreateItem(
        Guid guid,
        DateTime? lastPublishedWhen = null,
        string contentTypeName = "Test.ContentType",
        string scopeName = "TestScope",
        string? treePath = "/Test",
        string languageName = "en-US") =>
        new(guid, ContentInventoryItemKind.WebPage, contentTypeName, scopeName, languageName, treePath, lastPublishedWhen, "Published");

    private static ContentInventoryItem CreateItem(
        Guid guid, ContentInventoryItemKind kind, string versionStatus, DateTime? lastPublishedWhen = null) =>
        new(guid, kind, "Test.ContentType", "TestScope", "en-US", kind == ContentInventoryItemKind.WebPage ? "/Test" : null, lastPublishedWhen, versionStatus);

    private static ContentInventoryItem Page(Guid guid, string treePath, int? order, DateTime? lastPublishedWhen = null) =>
        new(guid, ContentInventoryItemKind.WebPage, "Test.ContentType", "TestScope", "en-US", treePath, lastPublishedWhen ?? earlier, "Published")
        {
            Order = order,
        };

    [Test]
    public void Compare_SetsTheStatus_ForEachKindOfChange()
    {
        Guid newer = Guid.NewGuid(), state = Guid.NewGuid(), same = Guid.NewGuid();

        var result = ContentSyncStatusComparer.Compare(
            [CreateItem(newer, later), CreateItem(state, ContentInventoryItemKind.WebPage, "Unpublished", earlier), CreateItem(same, earlier)],
            [CreateItem(newer, earlier), CreateItem(state, ContentInventoryItemKind.WebPage, "Published", earlier), CreateItem(same, earlier)]);

        var statuses = result.ToDictionary(item => item.Guid, item => item.Status);
        Assert.That(statuses[newer], Is.EqualTo(ContentSyncStatus.Changed));
        Assert.That(statuses[state], Is.EqualTo(ContentSyncStatus.Unpublished));
        Assert.That(statuses[same], Is.EqualTo(ContentSyncStatus.InSync));
    }

    // Moving a page changes its path but not its publish date.
    [Test]
    public void Compare_PageWithADifferentTreePath_IsOutOfDate_AsMoved()
    {
        var guid = Guid.NewGuid();

        var result = ContentSyncStatusComparer.Compare([Page(guid, "/Store/Coffee", 1)], [Page(guid, "/Articles/Coffee", 1)]);

        Assert.That(result.Single().Status, Is.EqualTo(ContentSyncStatus.Moved));
    }

    [Test]
    public void Compare_TreePathCase_IsNotAMove()
    {
        var guid = Guid.NewGuid();

        var result = ContentSyncStatusComparer.Compare([Page(guid, "/Articles/Coffee", 1)], [Page(guid, "/articles/coffee", 1)]);

        Assert.That(result.Single().Status, Is.EqualTo(ContentSyncStatus.InSync));
    }

    // Kentico needs every page on the level synced to transfer an order change, so all in-sync
    // pages on that level are marked.
    [Test]
    public void Compare_SiblingsInADifferentOrder_MarksTheWholeLevelAsReordered()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), other = Guid.NewGuid();

        var result = ContentSyncStatusComparer.Compare(
            [Page(a, "/Articles/A", 1), Page(b, "/Articles/B", 2), Page(c, "/Articles/C", 3), Page(other, "/Store/X", 1)],
            [Page(a, "/Articles/A", 2), Page(b, "/Articles/B", 1), Page(c, "/Articles/C", 3), Page(other, "/Store/X", 1)]);

        var byGuid = result.ToDictionary(item => item.Guid);
        Assert.That(new[] { a, b, c }.Select(guid => byGuid[guid].Status), Is.All.EqualTo(ContentSyncStatus.Reordered));
        Assert.That(byGuid[other].Status, Is.EqualTo(ContentSyncStatus.InSync), "another level is unaffected");
    }

    // A page that exists on only one side shifts its later siblings' order values; that alone isn't
    // a reorder, because the relative order of the shared pages is the same.
    [Test]
    public void Compare_OrderValuesShiftedByAPageOnOnlyOneSide_IsNotAReorder()
    {
        Guid a = Guid.NewGuid(), extra = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();

        var result = ContentSyncStatusComparer.Compare(
            [Page(a, "/Articles/A", 1), Page(extra, "/Articles/New", 2), Page(b, "/Articles/B", 3), Page(c, "/Articles/C", 4)],
            [Page(a, "/Articles/A", 1), Page(b, "/Articles/B", 2), Page(c, "/Articles/C", 3)]);

        var byGuid = result.ToDictionary(item => item.Guid);
        Assert.That(new[] { a, b, c }.Select(guid => byGuid[guid].Status), Is.All.EqualTo(ContentSyncStatus.InSync));
        Assert.That(byGuid[extra].Status, Is.EqualTo(ContentSyncStatus.New));
    }

    // A level that's reordered keeps a page's more specific reason: it's already out of date.
    [Test]
    public void Compare_Reorder_DoesNotOverrideAMoreSpecificStatus()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        var result = ContentSyncStatusComparer.Compare(
            [Page(a, "/Articles/A", 1, later), Page(b, "/Articles/B", 2)],
            [Page(a, "/Articles/A", 2), Page(b, "/Articles/B", 1)]);

        var byGuid = result.ToDictionary(item => item.Guid);
        Assert.That(byGuid[a].Status, Is.EqualTo(ContentSyncStatus.Changed));
        Assert.That(byGuid[b].Status, Is.EqualTo(ContentSyncStatus.Reordered));
    }

    // The case seen on the rig: a partial sync left one page at its old position on the target. Only
    // that page is out of place; the rest are in the same order on both sides.
    [Test]
    public void Compare_Reorder_NamesOnlyThePageOutOfPlace()
    {
        Guid parent = Guid.NewGuid(), processing = Guid.NewGuid(), clone = Guid.NewGuid(), beverages = Guid.NewGuid(), donate = Guid.NewGuid();
        var articles = Page(parent, "/Articles", 1) with { DisplayName = "Articles" };

        var result = ContentSyncStatusComparer.Compare(
            [articles, Page(processing, "/Articles/Processing", 2), Page(clone, "/Articles/Clone", 3), Page(beverages, "/Articles/Beverages", 4), Page(donate, "/Articles/Donate", 5)],
            [articles, Page(beverages, "/Articles/Beverages", 1), Page(processing, "/Articles/Processing", 2), Page(clone, "/Articles/Clone", 3), Page(donate, "/Articles/Donate", 4)]);

        var reorder = result.Single(item => item.Guid == donate).Reorder!;
        Assert.That(reorder.MisplacedPages.Select(page => page.Guid), Is.EqualTo(new[] { beverages }));
        Assert.That(reorder.ParentPath, Is.EqualTo("/Articles"));
        Assert.That(reorder.Parent!.Guid, Is.EqualTo(parent));
        Assert.That(result.Where(item => item.Status == ContentSyncStatus.Reordered).Select(item => item.Reorder), Is.All.SameAs(reorder));
        Assert.That(result.Single(item => item.Guid == parent).Reorder, Is.Null, "the parent's own level is in order");
    }

    // Two swapped pages: either one could be called out of place; the choice is always the same.
    [Test]
    public void Compare_Reorder_OfTwoSwappedPages_NamesOnePage_Deterministically()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        ContentInventoryItem[] local = [Page(a, "/L/A", 1), Page(b, "/L/B", 2), Page(c, "/L/C", 3)];
        ContentInventoryItem[] remote = [Page(a, "/L/A", 2), Page(b, "/L/B", 1), Page(c, "/L/C", 3)];

        var first = ContentSyncStatusComparer.Compare(local, remote)[0].Reorder!.MisplacedPages;
        var second = ContentSyncStatusComparer.Compare(local, remote)[0].Reorder!.MisplacedPages;

        Assert.That(first, Has.Count.EqualTo(1));
        Assert.That(first.Single().Guid, Is.EqualTo(second.Single().Guid));
    }

    [Test]
    public void Compare_Reorder_NamesEveryPageOutOfPlace_InThisInstancesOrder()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid(), e = Guid.NewGuid();

        // Here: A B C D E. Target: D A E B C — keeping A B C in order leaves D and E out of place.
        var result = ContentSyncStatusComparer.Compare(
            [Page(a, "/L/A", 1), Page(b, "/L/B", 2), Page(c, "/L/C", 3), Page(d, "/L/D", 4), Page(e, "/L/E", 5)],
            [Page(d, "/L/D", 1), Page(a, "/L/A", 2), Page(e, "/L/E", 3), Page(b, "/L/B", 4), Page(c, "/L/C", 5)]);

        Assert.That(result[0].Reorder!.MisplacedPages.Select(page => page.Guid), Is.EqualTo(new[] { d, e }));
    }

    [Test]
    public void FindMisplaced_ReturnsNothing_WhenTheOrderIsTheSame()
    {
        var items = new[] { Guid.NewGuid(), Guid.NewGuid() }
            .Select(guid => new ContentSyncStatusItem(guid, ContentSyncStatus.InSync, null, null))
            .ToList();
        var values = new Dictionary<Guid, int> { [items[0].Guid] = 5, [items[1].Guid] = 9 };

        Assert.That(ContentSyncStatusComparer.FindMisplaced(items, item => values[item.Guid]), Is.Empty);
    }

    // Seen on the rig after syncing one page: Kentico also synced linked pages, which brought their
    // source order values, so two pairs of pages ended up sharing a value on the target and Kentico
    // showed one pair in the opposite order. Tied pages are out of order, whatever their GUIDs.
    [Test]
    public void Compare_PagesSharingAnOrderValueOnTheTarget_AreAReorder()
    {
        Guid which = Guid.NewGuid(), processing = Guid.NewGuid(), clone = Guid.NewGuid(), donate = Guid.NewGuid();

        var result = ContentSyncStatusComparer.Compare(
            [Page(which, "/Articles/Which", 2), Page(processing, "/Articles/Processing", 3), Page(clone, "/Articles/Clone", 4), Page(donate, "/Articles/Donate", 6)],
            [Page(which, "/Articles/Which", 2), Page(processing, "/Articles/Processing", 3), Page(clone, "/Articles/Clone", 3), Page(donate, "/Articles/Donate", 5)]);

        Assert.That(result.Select(item => item.Status), Is.All.EqualTo(ContentSyncStatus.Reordered));
        Assert.That(result[0].Reorder!.MisplacedPages, Has.Count.EqualTo(1), "one page of the tied pair");
        Assert.That(result[0].Reorder!.MisplacedPages.Single().Guid, Is.AnyOf(processing, clone));
    }

    // A target on schema version 1 sends no order, so a level can't be compared.
    [Test]
    public void Compare_MissingOrderOnTheTarget_IsNotAReorder()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        var result = ContentSyncStatusComparer.Compare(
            [Page(a, "/Articles/A", 1), Page(b, "/Articles/B", 2)],
            [Page(a, "/Articles/A", null), Page(b, "/Articles/B", null)]);

        Assert.That(result.Select(item => item.Status), Is.All.EqualTo(ContentSyncStatus.InSync));
    }

    private static ContentSyncStatus? StatusOf(ContentInventoryItem? local, ContentInventoryItem? remote)
    {
        var result = ContentSyncStatusComparer.Compare(local is null ? [] : [local], remote is null ? [] : [remote]);
        return result.Count == 0 ? null : result.Single().Status;
    }

    // The publication-state rules from docs/specs/content-inventory-foundation.md, evaluated before
    // the timestamp rule. Unpublishing keeps LastPublishedWhen, so every pair here has the same date.
    [TestCase(ContentInventoryItemKind.WebPage, "Unpublished", "Published", ContentSyncStatus.Unpublished)]
    [TestCase(ContentInventoryItemKind.WebPage, "Published", "Unpublished", ContentSyncStatus.Changed)]
    [TestCase(ContentInventoryItemKind.ContentHubItem, "Unpublished", "Published", ContentSyncStatus.Unpublished)]
    [TestCase(ContentInventoryItemKind.ContentHubItem, "Published", "Unpublished", ContentSyncStatus.Changed)]
    [TestCase(ContentInventoryItemKind.WebPage, "Unpublished", "Unpublished", ContentSyncStatus.InSync)]
    [TestCase(ContentInventoryItemKind.WebPage, "Published", "Published", ContentSyncStatus.InSync)]
    public void Compare_PublicationStateDifference_IsUnpublishedOrChanged_EvenWithEqualTimestamps(
        ContentInventoryItemKind kind, string localStatus, string remoteStatus, ContentSyncStatus expected)
    {
        var guid = Guid.NewGuid();

        var status = StatusOf(CreateItem(guid, kind, localStatus, earlier), CreateItem(guid, kind, remoteStatus, earlier));

        Assert.That(status, Is.EqualTo(expected));
    }

    // An item unpublished here and then edited again has no published version, and Content Sync
    // can't sync it until it's published again, whatever the target has.
    [TestCase(ContentInventoryItemKind.WebPage, "Published")]
    [TestCase(ContentInventoryItemKind.WebPage, "Unpublished")]
    [TestCase(ContentInventoryItemKind.ContentHubItem, "Published")]
    [TestCase(ContentInventoryItemKind.ContentHubItem, null)]
    public void Compare_LocalUnpublishedDraft_IsNotPublished(ContentInventoryItemKind kind, string? remoteStatus)
    {
        var guid = Guid.NewGuid();
        var local = CreateItem(guid, kind, "UnpublishedDraft", later);

        var status = StatusOf(local, remoteStatus is null ? null : CreateItem(guid, kind, remoteStatus, earlier));

        Assert.That(status, Is.EqualTo(ContentSyncStatus.NotPublished));
    }

    // A never-published draft here is listed only when the target has the item, so the target's
    // copy isn't mistaken for one deleted here.
    [TestCase("Published", ContentSyncStatus.NotPublished)]
    [TestCase("Unpublished", ContentSyncStatus.NotPublished)]
    [TestCase(null, null)]
    public void Compare_LocalNeverPublished_IsListedOnlyWhenTheTargetHasIt(string? remoteStatus, ContentSyncStatus? expected)
    {
        var guid = Guid.NewGuid();

        var status = StatusOf(
            CreateItem(guid, ContentInventoryItemKind.WebPage, "NeverPublished", null),
            remoteStatus is null ? null : CreateItem(guid, ContentInventoryItemKind.WebPage, remoteStatus, earlier));

        Assert.That(status, Is.EqualTo(expected));
    }

    // A never-published draft on the target is ignored: a sync sends the published version as if the
    // target didn't have the item, and on its own there's nothing live there to list.
    [TestCase("Published", ContentSyncStatus.New)]
    [TestCase(null, null)]
    public void Compare_RemoteNeverPublished_CountsAsAbsent(string? localStatus, ContentSyncStatus? expected)
    {
        var guid = Guid.NewGuid();

        var status = StatusOf(
            localStatus is null ? null : CreateItem(guid, ContentInventoryItemKind.WebPage, localStatus, earlier),
            CreateItem(guid, ContentInventoryItemKind.WebPage, "NeverPublished", null));

        Assert.That(status, Is.EqualTo(expected));
    }

    // A draft after unpublishing on the target has no published version there, like an unpublished
    // item: a sync publishes the version from here.
    [Test]
    public void Compare_RemoteUnpublishedDraft_CountsAsUnpublishedThere()
    {
        var guid = Guid.NewGuid();

        var status = StatusOf(
            CreateItem(guid, ContentInventoryItemKind.WebPage, "Published", earlier),
            CreateItem(guid, ContentInventoryItemKind.WebPage, "UnpublishedDraft", earlier));

        Assert.That(status, Is.EqualTo(ContentSyncStatus.Changed));
    }

    // Both unpublished falls through to the timestamp rule.
    [Test]
    public void Compare_BothUnpublished_UsesTheTimestampRule()
    {
        var guid = Guid.NewGuid();

        var status = StatusOf(
            CreateItem(guid, ContentInventoryItemKind.WebPage, "Unpublished", later),
            CreateItem(guid, ContentInventoryItemKind.WebPage, "Unpublished", earlier));

        Assert.That(status, Is.EqualTo(ContentSyncStatus.Changed));
    }

    [Test]
    public void Compare_UnpublishedPageAbsentOnTarget_IsNew()
    {
        var status = StatusOf(CreateItem(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "Unpublished", earlier), null);

        Assert.That(status, Is.EqualTo(ContentSyncStatus.New));
    }

    // Content Sync makes no change for a new unpublished content-hub item, so it isn't listed.
    [Test]
    public void Compare_UnpublishedContentHubItemAbsentOnTarget_IsLeftOut()
    {
        var status = StatusOf(CreateItem(Guid.NewGuid(), ContentInventoryItemKind.ContentHubItem, "Unpublished", earlier), null);

        Assert.That(status, Is.Null);
    }

    [TestCase(ContentInventoryItemKind.WebPage)]
    [TestCase(ContentInventoryItemKind.ContentHubItem)]
    public void Compare_UnpublishedOnlyOnTarget_IsExtra(ContentInventoryItemKind kind)
    {
        var status = StatusOf(null, CreateItem(Guid.NewGuid(), kind, "Unpublished", earlier));

        Assert.That(status, Is.EqualTo(ContentSyncStatus.OnlyOnTarget));
    }

    // 30.8.0 may name the unpublished state "Archived" (same enum value); it must compare as unpublished.
    [Test]
    public void Compare_TreatsArchivedAsUnpublished()
    {
        var guid = Guid.NewGuid();

        var status = StatusOf(
            CreateItem(guid, ContentInventoryItemKind.WebPage, "Unpublished", earlier),
            CreateItem(guid, ContentInventoryItemKind.WebPage, "Archived", earlier));

        Assert.That(status, Is.EqualTo(ContentSyncStatus.InSync));
    }

    // An older target sends no unpublished items and a null status never counts as unpublished, so
    // results against it stay what they were before unpublished items were supported.
    [Test]
    public void Compare_NullVersionStatus_CountsAsPublished()
    {
        var guid = Guid.NewGuid();

        var status = StatusOf(
            CreateItem(guid, ContentInventoryItemKind.WebPage, "Published", earlier),
            new ContentInventoryItem(guid, ContentInventoryItemKind.WebPage, "T", "S", "en-US", "/Test", earlier, null));

        Assert.That(status, Is.EqualTo(ContentSyncStatus.InSync));
    }

    [Test]
    public void Compare_ReturnsEmpty_WhenBothInventoriesAreEmpty()
    {
        var result = ContentSyncStatusComparer.Compare([], []);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Compare_ClassifiesAsNew_WhenItemExistsOnlyLocally()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid) };

        var result = ContentSyncStatusComparer.Compare(local, []);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.New));
        Assert.That(result[0].Local, Is.Not.Null);
        Assert.That(result[0].Remote, Is.Null);
    }

    [Test]
    public void Compare_ClassifiesAsOnlyOnTarget_WhenItemExistsOnlyRemotely()
    {
        var guid = Guid.NewGuid();
        var remote = new[] { CreateItem(guid) };

        var result = ContentSyncStatusComparer.Compare([], remote);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.OnlyOnTarget));
        Assert.That(result[0].Local, Is.Null);
        Assert.That(result[0].Remote, Is.Not.Null);
    }

    [Test]
    public void Compare_ClassifiesAsInSync_WhenTimestampsAreEqual()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid, earlier) };
        var remote = new[] { CreateItem(guid, earlier) };

        var result = ContentSyncStatusComparer.Compare(local, remote);

        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.InSync));
    }

    [Test]
    public void Compare_ClassifiesAsChanged_WhenLocalIsNewer()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid, later) };
        var remote = new[] { CreateItem(guid, earlier) };

        var result = ContentSyncStatusComparer.Compare(local, remote);

        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.Changed));
    }

    [Test]
    public void Compare_ClassifiesAsInSync_WhenRemoteIsNewer()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid, earlier) };
        var remote = new[] { CreateItem(guid, later) };

        var result = ContentSyncStatusComparer.Compare(local, remote);

        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.InSync));
    }

    [Test]
    public void Compare_ClassifiesAsInSync_WhenBothTimestampsAreNull()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid, lastPublishedWhen: null) };
        var remote = new[] { CreateItem(guid, lastPublishedWhen: null) };

        var result = ContentSyncStatusComparer.Compare(local, remote);

        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.InSync));
    }

    [Test]
    public void Compare_ClassifiesAsInSync_WhenLocalTimestampIsNullAndRemoteIsPresent()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid, lastPublishedWhen: null) };
        var remote = new[] { CreateItem(guid, later) };

        var result = ContentSyncStatusComparer.Compare(local, remote);

        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.InSync));
    }

    [Test]
    public void Compare_ClassifiesAsChanged_WhenLocalTimestampIsPresentAndRemoteIsNull()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid, earlier) };
        var remote = new[] { CreateItem(guid, lastPublishedWhen: null) };

        var result = ContentSyncStatusComparer.Compare(local, remote);

        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.Changed));
    }

    [Test]
    public void Compare_DoesNotThrow_WhenOneSideHasDuplicateGuids()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid, earlier), CreateItem(guid, later) };

        IReadOnlyList<ContentSyncStatusItem>? result = null;
        Assert.DoesNotThrow(() => result = ContentSyncStatusComparer.Compare(local, []));

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void Compare_ClassifiesMixedBatchIndependently()
    {
        var missingGuid = Guid.NewGuid();
        var extraGuid = Guid.NewGuid();
        var inSyncGuid = Guid.NewGuid();
        var outOfDateGuid = Guid.NewGuid();

        var local = new[]
        {
            CreateItem(missingGuid),
            CreateItem(inSyncGuid, earlier),
            CreateItem(outOfDateGuid, later)
        };

        var remote = new[]
        {
            CreateItem(extraGuid),
            CreateItem(inSyncGuid, earlier),
            CreateItem(outOfDateGuid, earlier)
        };

        var result = ContentSyncStatusComparer.Compare(local, remote);

        Assert.That(result, Has.Count.EqualTo(4));
        Assert.That(result.Single(i => i.Guid == missingGuid).Status, Is.EqualTo(ContentSyncStatus.New));
        Assert.That(result.Single(i => i.Guid == extraGuid).Status, Is.EqualTo(ContentSyncStatus.OnlyOnTarget));
        Assert.That(result.Single(i => i.Guid == inSyncGuid).Status, Is.EqualTo(ContentSyncStatus.InSync));
        Assert.That(result.Single(i => i.Guid == outOfDateGuid).Status, Is.EqualTo(ContentSyncStatus.Changed));
    }

    [Test]
    public void Compare_PassesThroughItemFieldsUnchanged()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid, earlier, contentTypeName: "My.Type", scopeName: "MyChannel", treePath: "/A/B") };
        var remote = new[] { CreateItem(guid, earlier, contentTypeName: "My.Type", scopeName: "MyChannel", treePath: "/A/B") };

        var result = ContentSyncStatusComparer.Compare(local, remote);

        Assert.That(result[0].Local!.ContentTypeName, Is.EqualTo("My.Type"));
        Assert.That(result[0].Local!.ScopeName, Is.EqualTo("MyChannel"));
        Assert.That(result[0].Local!.TreePath, Is.EqualTo("/A/B"));
        Assert.That(result[0].Remote!.ContentTypeName, Is.EqualTo("My.Type"));
        Assert.That(result[0].Remote!.ScopeName, Is.EqualTo("MyChannel"));
        Assert.That(result[0].Remote!.TreePath, Is.EqualTo("/A/B"));
    }
}
