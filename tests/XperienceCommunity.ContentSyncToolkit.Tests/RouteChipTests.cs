using CMS.Core;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.Sync;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// The route chip above the status lists, and the feedback links. See docs/specs/route-chip-and-feedback.md.
public class RouteChipTests
{
    private static readonly Uri targetUrl = new("https://target.example.com/");

    private sealed class NoEventLog : IEventLogService
    {
        public void LogEvent(EventLogData eventLogData)
        {
            // The chip doesn't look at entries.
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    // ---- What each side shows ----------------------------------------------------------------------

    [Test]
    public void Side_WithALabel_ShowsTheLabel_AndTheHostOnHover() =>
        Assert.That(ContentSyncRoute.Side("staging.example.com", " Staging ", "This instance"),
            Is.EqualTo(new ContentSyncRouteSide("Staging", "staging.example.com", IsLabel: true)));

    [Test]
    public void Side_WithoutALabel_ShowsTheHost_WithNothingOnHover() =>
        Assert.That(ContentSyncRoute.Side("localhost:27300", null, "This instance"),
            Is.EqualTo(new ContentSyncRouteSide("localhost:27300", string.Empty, IsLabel: false)));

    [Test]
    public void Side_WithoutAHostOrALabel_ShowsTheFallbackName() =>
        Assert.That(ContentSyncRoute.Side(null, " ", "This instance"),
            Is.EqualTo(new ContentSyncRouteSide("This instance", string.Empty, IsLabel: false)));

    [Test]
    public void Side_ALabelWithoutAHost_HasNothingOnHover() =>
        Assert.That(ContentSyncRoute.Side(null, "Staging", "This instance"),
            Is.EqualTo(new ContentSyncRouteSide("Staging", string.Empty, IsLabel: true)));

    [Test]
    public void Side_ALongHostWithoutALabel_IsShortened_WithTheFullHostOnHover()
    {
        const string host = "content-staging-west-europe.internal.dancinggoat-coffee.example.com";

        var side = ContentSyncRoute.Side(host, null, "This instance");

        Assert.That(side.Name, Has.Length.EqualTo(ContentSyncRoute.MaxHostLength));
        Assert.That(side.Name, Does.StartWith("content-staging").And.EndWith("goat-coffee.example.com").And.Contains("…"));
        Assert.That(side.Hover, Is.EqualTo(host));
    }

    [Test]
    public void ShortenHost_AHostUpToTheLimit_IsUnchanged()
    {
        string host = new string('a', ContentSyncRoute.MaxHostLength - 4) + ".com";

        Assert.That(ContentSyncRoute.ShortenHost(host), Is.EqualTo(host));
    }

    // ---- The feedback links ---------------------------------------------------------------------------

    [Test]
    public void FeedbackUrl_OpensTheFeedbackCategory_WithBothVersions() =>
        Assert.That(ContentSyncRoute.FeedbackUrl("1.0.0-beta.6", "31.9.1"), Is.EqualTo(
            "https://github.com/Simplea/xperience-community-content-sync-toolkit/discussions/new?category=feedback" +
            "&toolkit-version=1.0.0-beta.6&kentico-version=31.9.1"));

    [Test]
    public void AfterSyncUrl_GoesToTheReleaseDiscussion_OrToTheFormWithoutOne()
    {
        Assert.That(ContentSyncRoute.AfterSyncUrl("https://github.com/o/r/discussions/50", "form"), Is.EqualTo("https://github.com/o/r/discussions/50"));
        Assert.That(ContentSyncRoute.AfterSyncUrl("", "form"), Is.EqualTo("form"));
    }

    [Test]
    public void FeedbackStorageKey_IsPerVersionAndUser()
    {
        Assert.That(ContentSyncRoute.FeedbackStorageKey("1.0.0-beta.6", 53), Is.EqualTo("cstk-feedback-1.0.0-beta.6-53"));
        Assert.That(ContentSyncRoute.FeedbackStorageKey("1.0.0-beta.7", 53), Is.Not.EqualTo(ContentSyncRoute.FeedbackStorageKey("1.0.0-beta.6", 53)));
    }

    [Test]
    public void ToolkitVersion_HasNoBuildMetadata()
    {
        Assert.That(ContentSyncRoute.ToolkitVersion, Is.Not.Empty);
        Assert.That(ContentSyncRoute.ToolkitVersion, Does.Not.Contain("+"));
    }

    [Test]
    public void KenticoVersion_IsMajorMinorPatch() =>
        Assert.That(ContentSyncRoute.KenticoVersion, Does.Match(@"^\d+\.\d+\.\d+$"));

    // Xperience 30.x places the list by the caption's height, so the chip needs a caption line there.
    [TestCase("30.8.0", true)]
    [TestCase("30.12.3", true)]
    [TestCase("31.0.0", false)]
    [TestCase("31.9.1", false)]
    [TestCase("", false)]
    public void NeedsCaptionLine_OnlyOn30(string kenticoVersion, bool expected) =>
        Assert.That(ContentSyncRoute.NeedsCaptionLine(kenticoVersion), Is.EqualTo(expected));

    // ---- The dot ------------------------------------------------------------------------------------

    [Test]
    public void Current_BeforeAnyListRequest_IsUnknown() =>
        Assert.That(new ContentSyncTargetHealth(new NoEventLog()).Current(targetUrl), Is.EqualTo(ContentSyncTargetState.Unknown));

    [Test]
    public void Current_FollowsTheLastListRequest()
    {
        var health = new ContentSyncTargetHealth(new NoEventLog());

        health.ListAnswered(targetUrl, ContentSyncToolkitConstants.SchemaVersion);
        Assert.That(health.Current(targetUrl), Is.EqualTo(ContentSyncTargetState.Answered));

        health.ListFailed(targetUrl, ContentSyncTargetFailure.Unreachable, "connection refused");
        Assert.That(health.Current(targetUrl), Is.EqualTo(ContentSyncTargetState.Unreachable));

        health.ListFailed(targetUrl, ContentSyncTargetFailure.Rejected, "404 Not Found");
        Assert.That(health.Current(targetUrl), Is.EqualTo(ContentSyncTargetState.Rejected));

        health.ListFailed(targetUrl, ContentSyncTargetFailure.Error, "not JSON");
        Assert.That(health.Current(targetUrl), Is.EqualTo(ContentSyncTargetState.Error));

        health.ListAnswered(targetUrl, ContentSyncToolkitConstants.SchemaVersion);
        Assert.That(health.Current(targetUrl), Is.EqualTo(ContentSyncTargetState.Answered));
    }

    [Test]
    public void Current_IsPerTarget()
    {
        var health = new ContentSyncTargetHealth(new NoEventLog());

        health.ListFailed(targetUrl, ContentSyncTargetFailure.Unreachable, "connection refused");

        Assert.That(health.Current(new Uri("https://other.example.com/")), Is.EqualTo(ContentSyncTargetState.Unknown));
    }

    // ---- The one-time line after a sync -------------------------------------------------------------------

    private static ContentSyncTrackedSync Sync(string userName) =>
        new(Guid.NewGuid(), ContentInventoryItemKind.WebPage, [(Guid.NewGuid(), "en")], userName, DateTimeOffset.UnixEpoch, null);

    [Test]
    public void HasRecentCompletedSync_OnlyForThisUsersCompletedSync()
    {
        var time = new ManualTimeProvider();
        var tracker = new ContentSyncTracker(time);
        var mine = Sync("editor");
        var theirs = Sync("someone-else");
        tracker.Add(mine);
        tracker.Add(theirs);

        Assert.That(tracker.HasRecentCompletedSync("editor"), Is.False, "still running");
        Assert.That(tracker.HasRunningSync("Editor"), Is.True);

        tracker.Update(theirs.SyncGuid, new ContentSyncProgress(ContentSyncProgressState.Completed, null, null, []));
        Assert.That(tracker.HasRecentCompletedSync("editor"), Is.False, "someone else's sync");

        tracker.Update(mine.SyncGuid, new ContentSyncProgress(ContentSyncProgressState.Completed, null, null, []));
        Assert.That(tracker.HasRecentCompletedSync("EDITOR"), Is.True);
        Assert.That(tracker.HasRunningSync("editor"), Is.False);

        time.Now += ContentSyncTracker.Retention + TimeSpan.FromMinutes(1);
        Assert.That(tracker.HasRecentCompletedSync("editor"), Is.False, "older than 24 hours");
    }

    [Test]
    public void HasRecentCompletedSync_AFailedSync_DoesntCount()
    {
        var tracker = new ContentSyncTracker(new ManualTimeProvider());
        var sync = Sync("editor");
        tracker.Add(sync);

        tracker.Update(sync.SyncGuid, new ContentSyncProgress(ContentSyncProgressState.Failed, "Validating", null, ["Compatibility error."]));

        Assert.That(tracker.HasRecentCompletedSync("editor"), Is.False);
    }
}
