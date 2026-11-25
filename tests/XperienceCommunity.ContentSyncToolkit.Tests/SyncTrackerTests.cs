using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.Sync;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// What the list shows about syncs started from it: rows syncing, and recent failures.
public class SyncTrackerTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private static readonly Guid rowGuid = Guid.NewGuid();

    private static readonly ContentSyncProgress failedProgress =
        new(ContentSyncProgressState.Failed, "Validating", null, ["Compatibility error: Content type Home page differs."]);

    private static readonly ContentSyncProgress completedProgress = new(ContentSyncProgressState.Completed, "Restoration", null, []);

    private static ContentSyncTrackedSync Sync(Guid? row = null, ContentInventoryItemKind kind = ContentInventoryItemKind.WebPage) =>
        new(Guid.NewGuid(), kind, [(row ?? rowGuid, "en")], "editor", DateTimeOffset.UnixEpoch, null);

    [Test]
    public void RunningSyncOf_ARowInARunningSync_FindsIt_InAnyLetterCase()
    {
        var tracker = new ContentSyncTracker(new ManualTimeProvider());
        var sync = Sync();
        tracker.Add(sync);

        Assert.That(tracker.RunningSyncOf(ContentInventoryItemKind.WebPage, rowGuid, "EN"), Is.EqualTo(sync));
        Assert.That(tracker.RunningSyncOf(ContentInventoryItemKind.ContentHubItem, rowGuid, "en"), Is.Null, "other tab");
    }

    [Test]
    public void Update_Completed_TheRowIsNoLongerSyncing_AndIsntAFailure()
    {
        var tracker = new ContentSyncTracker(new ManualTimeProvider());
        var sync = Sync();
        tracker.Add(sync);

        tracker.Update(sync.SyncGuid, completedProgress);

        Assert.That(tracker.RunningSyncOf(ContentInventoryItemKind.WebPage, rowGuid, "en"), Is.Null);
        Assert.That(tracker.RecentFailures(ContentInventoryItemKind.WebPage), Is.Empty);
    }

    [Test]
    public void RecentFailures_AFailedSync_IsShown()
    {
        var tracker = new ContentSyncTracker(new ManualTimeProvider());
        var sync = Sync();
        tracker.Add(sync);

        tracker.Update(sync.SyncGuid, failedProgress);

        Assert.That(tracker.RecentFailures(ContentInventoryItemKind.WebPage).Single().SyncGuid, Is.EqualTo(sync.SyncGuid));
    }

    [Test]
    public void RecentFailures_After24Hours_IsForgotten()
    {
        var clock = new ManualTimeProvider();
        var tracker = new ContentSyncTracker(clock);
        var sync = Sync();
        tracker.Add(sync);
        tracker.Update(sync.SyncGuid, failedProgress);

        clock.Advance(ContentSyncTracker.Retention + TimeSpan.FromMinutes(1));

        Assert.That(tracker.RecentFailures(ContentInventoryItemKind.WebPage), Is.Empty);
    }

    // Syncing the rows again replaces the failure: the callout is about rows still failing.
    [Test]
    public void Add_TheSameRowsAgain_ReplacesTheFailure()
    {
        var tracker = new ContentSyncTracker(new ManualTimeProvider());
        var failed = Sync();
        tracker.Add(failed);
        tracker.Update(failed.SyncGuid, failedProgress);

        tracker.Add(Sync());

        Assert.That(tracker.RecentFailures(ContentInventoryItemKind.WebPage), Is.Empty);
    }

    [Test]
    public void Add_OtherRows_KeepsTheFailure()
    {
        var tracker = new ContentSyncTracker(new ManualTimeProvider());
        var failed = Sync();
        tracker.Add(failed);
        tracker.Update(failed.SyncGuid, failedProgress);

        tracker.Add(Sync(Guid.NewGuid()));

        Assert.That(tracker.RecentFailures(ContentInventoryItemKind.WebPage), Has.Count.EqualTo(1));
    }
}
