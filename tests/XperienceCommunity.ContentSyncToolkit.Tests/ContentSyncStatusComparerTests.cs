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
