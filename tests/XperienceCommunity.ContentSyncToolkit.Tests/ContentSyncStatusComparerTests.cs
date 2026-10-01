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
    public void Compare_SetsTheReason_ForEachOutOfDateCause()
    {
        Guid newer = Guid.NewGuid(), state = Guid.NewGuid(), same = Guid.NewGuid();

        var result = ContentSyncStatusComparer.Compare(
            [CreateItem(newer, later), CreateItem(state, ContentInventoryItemKind.WebPage, "Unpublished", earlier), CreateItem(same, earlier)],
            [CreateItem(newer, earlier), CreateItem(state, ContentInventoryItemKind.WebPage, "Published", earlier), CreateItem(same, earlier)]);

        var reasons = result.ToDictionary(item => item.Guid, item => item.Reason);
        Assert.That(reasons[newer], Is.EqualTo(ContentSyncStatusReason.PublishedMoreRecently));
        Assert.That(reasons[state], Is.EqualTo(ContentSyncStatusReason.PublishStateDiffers));
        Assert.That(reasons[same], Is.EqualTo(ContentSyncStatusReason.None));
    }

    // Moving a page changes its path but not its publish date.
    [Test]
    public void Compare_PageWithADifferentTreePath_IsOutOfDate_AsMoved()
    {
        var guid = Guid.NewGuid();

        var result = ContentSyncStatusComparer.Compare([Page(guid, "/Store/Coffee", 1)], [Page(guid, "/Articles/Coffee", 1)]);

        Assert.That(result.Single().Status, Is.EqualTo(ContentSyncStatus.OutOfDateOnTarget));
        Assert.That(result.Single().Reason, Is.EqualTo(ContentSyncStatusReason.Moved));
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
        Assert.That(new[] { a, b, c }.Select(guid => byGuid[guid].Reason), Is.All.EqualTo(ContentSyncStatusReason.Reordered));
        Assert.That(new[] { a, b, c }.Select(guid => byGuid[guid].Status), Is.All.EqualTo(ContentSyncStatus.OutOfDateOnTarget));
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
        Assert.That(byGuid[extra].Status, Is.EqualTo(ContentSyncStatus.MissingOnTarget));
    }

    // A level that's reordered keeps a page's more specific reason: it's already out of date.
    [Test]
    public void Compare_Reorder_DoesNotOverrideAnotherReason()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        var result = ContentSyncStatusComparer.Compare(
            [Page(a, "/Articles/A", 1, later), Page(b, "/Articles/B", 2)],
            [Page(a, "/Articles/A", 2), Page(b, "/Articles/B", 1)]);

        var byGuid = result.ToDictionary(item => item.Guid);
        Assert.That(byGuid[a].Reason, Is.EqualTo(ContentSyncStatusReason.PublishedMoreRecently));
        Assert.That(byGuid[b].Reason, Is.EqualTo(ContentSyncStatusReason.Reordered));
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
    [TestCase(ContentInventoryItemKind.WebPage, "Unpublished", "Published", ContentSyncStatus.OutOfDateOnTarget)]
    [TestCase(ContentInventoryItemKind.WebPage, "Published", "Unpublished", ContentSyncStatus.OutOfDateOnTarget)]
    [TestCase(ContentInventoryItemKind.ContentHubItem, "Unpublished", "Published", ContentSyncStatus.OutOfDateOnTarget)]
    [TestCase(ContentInventoryItemKind.ContentHubItem, "Published", "Unpublished", ContentSyncStatus.OutOfDateOnTarget)]
    [TestCase(ContentInventoryItemKind.WebPage, "Unpublished", "Unpublished", ContentSyncStatus.InSync)]
    [TestCase(ContentInventoryItemKind.WebPage, "Published", "Published", ContentSyncStatus.InSync)]
    public void Compare_PublicationStateDifference_IsOutOfDate_EvenWithEqualTimestamps(
        ContentInventoryItemKind kind, string localStatus, string remoteStatus, ContentSyncStatus expected)
    {
        var guid = Guid.NewGuid();

        var status = StatusOf(CreateItem(guid, kind, localStatus, earlier), CreateItem(guid, kind, remoteStatus, earlier));

        Assert.That(status, Is.EqualTo(expected));
    }

    // Both unpublished falls through to the timestamp rule.
    [Test]
    public void Compare_BothUnpublished_UsesTheTimestampRule()
    {
        var guid = Guid.NewGuid();

        var status = StatusOf(
            CreateItem(guid, ContentInventoryItemKind.WebPage, "Unpublished", later),
            CreateItem(guid, ContentInventoryItemKind.WebPage, "Unpublished", earlier));

        Assert.That(status, Is.EqualTo(ContentSyncStatus.OutOfDateOnTarget));
    }

    [Test]
    public void Compare_UnpublishedPageAbsentOnTarget_IsMissing()
    {
        var status = StatusOf(CreateItem(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "Unpublished", earlier), null);

        Assert.That(status, Is.EqualTo(ContentSyncStatus.MissingOnTarget));
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

        Assert.That(status, Is.EqualTo(ContentSyncStatus.ExtraOnTarget));
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
    public void Compare_ClassifiesAsMissingOnTarget_WhenItemExistsOnlyLocally()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid) };

        var result = ContentSyncStatusComparer.Compare(local, []);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.MissingOnTarget));
        Assert.That(result[0].Local, Is.Not.Null);
        Assert.That(result[0].Remote, Is.Null);
    }

    [Test]
    public void Compare_ClassifiesAsExtraOnTarget_WhenItemExistsOnlyRemotely()
    {
        var guid = Guid.NewGuid();
        var remote = new[] { CreateItem(guid) };

        var result = ContentSyncStatusComparer.Compare([], remote);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.ExtraOnTarget));
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
    public void Compare_ClassifiesAsOutOfDateOnTarget_WhenLocalIsNewer()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid, later) };
        var remote = new[] { CreateItem(guid, earlier) };

        var result = ContentSyncStatusComparer.Compare(local, remote);

        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.OutOfDateOnTarget));
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
    public void Compare_ClassifiesAsOutOfDateOnTarget_WhenLocalTimestampIsPresentAndRemoteIsNull()
    {
        var guid = Guid.NewGuid();
        var local = new[] { CreateItem(guid, earlier) };
        var remote = new[] { CreateItem(guid, lastPublishedWhen: null) };

        var result = ContentSyncStatusComparer.Compare(local, remote);

        Assert.That(result[0].Status, Is.EqualTo(ContentSyncStatus.OutOfDateOnTarget));
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
        Assert.That(result.Single(i => i.Guid == missingGuid).Status, Is.EqualTo(ContentSyncStatus.MissingOnTarget));
        Assert.That(result.Single(i => i.Guid == extraGuid).Status, Is.EqualTo(ContentSyncStatus.ExtraOnTarget));
        Assert.That(result.Single(i => i.Guid == inSyncGuid).Status, Is.EqualTo(ContentSyncStatus.InSync));
        Assert.That(result.Single(i => i.Guid == outOfDateGuid).Status, Is.EqualTo(ContentSyncStatus.OutOfDateOnTarget));
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
