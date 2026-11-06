using System.Net;
using System.Net.Http.Json;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// Hints on Only on target items that are still in this instance's recycle bin. See
// docs/specs/recycle-bin-hints.md.
public class RecycleBinHintsTests
{
    private static readonly Guid itemGuid = Guid.NewGuid();
    private static readonly DateTime deletedWhen = new(2026, 10, 29, 9, 30, 0, DateTimeKind.Unspecified);

    private static ContentSyncRecycleBinEntry Entry(int deletedBy, DateTime? when = null, Guid? guid = null) =>
        new(guid ?? itemGuid, when ?? deletedWhen, deletedBy);

    [Test]
    public void SelectVisible_WithoutViewPermission_ShowsNothing() =>
        Assert.That(
            ContentSyncRecycleBinLookup.SelectVisible([Entry(7)], new ContentSyncRecycleBinAccess(false, true, 7)),
            Is.Empty);

    [Test]
    public void SelectVisible_WithoutManagePermission_ShowsOnlyTheUsersOwnDeletions()
    {
        var own = Entry(7, guid: Guid.NewGuid());

        var visible = ContentSyncRecycleBinLookup.SelectVisible([own, Entry(8)], new ContentSyncRecycleBinAccess(true, false, 7));

        Assert.That(visible, Is.EqualTo(new[] { own }));
    }

    [Test]
    public void SelectVisible_WithManagePermission_ShowsEveryonesDeletions() =>
        Assert.That(
            ContentSyncRecycleBinLookup.SelectVisible([Entry(8)], new ContentSyncRecycleBinAccess(true, true, 7)),
            Has.Count.EqualTo(1));

    [Test]
    public void SelectVisible_ItemDeletedTwice_KeepsTheLatestDeletion()
    {
        var latest = Entry(7, deletedWhen.AddDays(3));

        var visible = ContentSyncRecycleBinLookup.SelectVisible([Entry(7), latest], new ContentSyncRecycleBinAccess(true, true, 7));

        Assert.That(visible, Is.EqualTo(new[] { latest }));
    }

    [TestCase("30", 30)]
    [TestCase("0", null)]
    [TestCase("-1", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    [TestCase("soon", null)]
    public void ParseRetentionDays_OnlyAcceptsAPositiveNumber(string? value, int? expected) =>
        Assert.That(ContentSyncRecycleBinLookup.ParseRetentionDays(value), Is.EqualTo(expected));

    private static ContentSyncStatusItem OnlyOnTarget(string versionStatus) =>
        new(Guid.NewGuid(), ContentSyncStatus.OnlyOnTarget, null,
            new ContentInventoryItem(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "T", "S", "en", "/a", null, versionStatus));

    private static readonly ContentSyncDeletedItem deleted = new(itemGuid, deletedWhen, "Ana Pérez", deletedWhen.AddDays(30));

    [Test]
    public void StatusTooltip_InTheRecycleBin_SaysWhoDeletedItAndWhatToDo() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(OnlyOnTarget("Published"), opensOnTarget: true, deletedItem: deleted),
            Is.EqualTo("Deleted here on Oct 29, 2026 by Ana Pérez, but still on the target. Content Sync can't delete it: "
                + "delete it on the target, or restore it from the recycle bin (until Nov 28, 2026), publish and unpublish it, and sync it."));

    [Test]
    public void StatusTooltip_InTheRecycleBin_WithoutDetails_LeavesThemOut() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(OnlyOnTarget("Published"), deletedItem: deleted with { DeletedBy = null, ExpiresWhen = null }),
            Does.StartWith("Deleted here on Oct 29, 2026, but still on the target.").And.Not.Contain("until"));

    [Test]
    public void StatusLabel_InTheRecycleBin_SaysDeletedHere() =>
        Assert.That(ContentSyncStatusListingSupport.StatusLabel(OnlyOnTarget("Published"), deleted), Is.EqualTo("Deleted here"));

    [Test]
    public void StatusLabel_NotInTheRecycleBin_StaysOnlyOnTarget() =>
        Assert.That(ContentSyncStatusListingSupport.StatusLabel(OnlyOnTarget("Published"), null), Is.EqualTo("Only on target"));

    // Already unpublished there, so restoring it to unpublish it again would change nothing.
    [Test]
    public void StatusTooltip_InTheRecycleBin_AndUnpublishedOnTheTarget_SkipsTheSteps() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(OnlyOnTarget("Unpublished"), opensOnTarget: true, deletedItem: deleted),
            Does.Contain("and unpublished on the target.").And.Not.Contain("restore it"));

    [Test]
    public void StatusTooltip_NotInTheRecycleBin_KeepsTheUsualText() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(OnlyOnTarget("Published"), opensOnTarget: true),
            Does.StartWith("Only on the target.").And.Not.Contain("recycle bin"));

    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private static ContentInventoryClient Client(TargetItemIdsResponse body) =>
        new(new HttpClient(new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) }))
        {
            BaseAddress = new Uri("https://target.example.com"),
        });

    [Test]
    public async Task GetWebPageIdsAsync_ReturnsTheContentItemGuids()
    {
        var pageGuid = Guid.NewGuid();
        var contentItemGuid = Guid.NewGuid();

        var result = await Client(new TargetItemIdsResponse(5, DateTimeOffset.UtcNow, 1, [new TargetItemId(pageGuid, 74, contentItemGuid)]))
            .GetWebPageIdsAsync("Channel", "en", [pageGuid], CancellationToken.None);

        Assert.That(result.ContentItemGuids[pageGuid], Is.EqualTo(contentItemGuid));
    }

    // A schema 4 target sends no content item GUID: the link still works, only the hint is missing.
    [Test]
    public async Task GetWebPageIdsAsync_OlderTarget_HasIdsButNoContentItemGuids()
    {
        var pageGuid = Guid.NewGuid();

        var result = await Client(new TargetItemIdsResponse(4, DateTimeOffset.UtcNow, 1, [new TargetItemId(pageGuid, 74)]))
            .GetWebPageIdsAsync("Channel", "en", [pageGuid], CancellationToken.None);

        Assert.That(result.Ids, Does.ContainKey(pageGuid));
        Assert.That(result.ContentItemGuids, Is.Empty);
    }
}
