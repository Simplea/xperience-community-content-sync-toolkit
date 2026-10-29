using System.Net;
using System.Net.Http.Json;

using CMS.ContentSynchronization;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// Links from Only on target rows to the item in the target's administration. See
// docs/specs/target-item-links.md.
public class TargetItemLinksTests
{
    private static ContentSynchronizationOptions ContentSync(string targetUrl)
    {
        var options = new ContentSynchronizationOptions();
        options.Source.Enabled = true;
        options.Source.TargetUrl = targetUrl;
        options.Source.Secret = "content-sync-secret-at-least-32-characters";
        return options;
    }

    [TestCase("https://target.example.com", "https://target.example.com/admin/")]
    [TestCase("https://target.example.com/", "https://target.example.com/admin/")]
    [TestCase("https://target.example.com/site", "https://target.example.com/site/admin/")]
    public void TargetAdminUrl_DefaultsToTheTargetUrlsAdmin(string targetUrl, string expected) =>
        Assert.That(
            ContentSyncToolkitSettings.ResolveSource(new ContentSyncToolkitOptions(), ContentSync(targetUrl)).TargetAdminUrl,
            Is.EqualTo(new Uri(expected)));

    [TestCase("https://admin.example.com/admin", "https://admin.example.com/admin/")]
    [TestCase("https://admin.example.com/admin/", "https://admin.example.com/admin/")]
    public void TargetAdminUrl_UsesTheToolkitSetting_WhenSet(string setting, string expected) =>
        Assert.That(
            ContentSyncToolkitSettings.ResolveSource(new ContentSyncToolkitOptions { TargetAdminUrl = setting }, ContentSync("https://internal-host")).TargetAdminUrl,
            Is.EqualTo(new Uri(expected)));

    // "/admin" parses as an absolute file URI on Linux, so it's checked with an explicit file URI too.
    [TestCase("/admin")]
    [TestCase("file:///admin")]
    public void TargetAdminUrl_IgnoresASettingThatIsNotAnHttpUrl(string setting) =>
        Assert.That(
            ContentSyncToolkitSettings.ResolveSource(new ContentSyncToolkitOptions { TargetAdminUrl = setting }, ContentSync("https://target.example.com")).TargetAdminUrl,
            Is.EqualTo(new Uri("https://target.example.com/admin/")));

    [Test]
    public void TargetAdminUrl_IsNull_WhenThisInstanceIsNotASource()
    {
        var contentSync = ContentSync("https://target.example.com");
        contentSync.Source.Enabled = false;

        Assert.That(ContentSyncToolkitSettings.ResolveSource(new ContentSyncToolkitOptions(), contentSync).TargetAdminUrl, Is.Null);
    }

    [TestCase("/webpages-3/en_74")]
    [TestCase("webpages-3/en_74")]
    public void TargetLink_PutsTheAdminPathUnderTheTargetsAdministration(string path) =>
        Assert.That(
            ContentSyncStatusTabBase.TargetLink(new Uri("https://target.example.com/admin/"), path),
            Is.EqualTo("https://target.example.com/admin/webpages-3/en_74"));

    [Test]
    public void ParseGuids_SkipsValuesThatAreNotGuids_AndDuplicates()
    {
        var guid = Guid.NewGuid();

        Assert.That(ContentItemIdLookup.ParseGuids($"{guid}, nope,,{guid}"), Is.EqualTo(new[] { guid }));
    }

    [TestCase(null)]
    [TestCase("")]
    public void ParseGuids_NoValue_IsEmpty(string? guids) =>
        Assert.That(ContentItemIdLookup.ParseGuids(guids), Is.Empty);

    [Test]
    public void ParseGuids_TakesAtMostTheLimit()
    {
        string guids = string.Join(',', Enumerable.Range(0, ContentSyncToolkitConstants.MaxItemIdGuids + 5).Select(_ => Guid.NewGuid()));

        Assert.That(ContentItemIdLookup.ParseGuids(guids), Has.Count.EqualTo(ContentSyncToolkitConstants.MaxItemIdGuids));
    }

    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    private static ContentInventoryClient Client(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://target.example.com") });

    [Test]
    public async Task GetWebPageIdsAsync_RequestsTheItemIdRoute_AndReturnsTheIds()
    {
        var guid = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TargetItemIdsResponse(4, DateTimeOffset.UtcNow, 3, [new TargetItemId(guid, 74)])),
        });

        var result = await Client(handler).GetWebPageIdsAsync("My Channel", "en", [guid], CancellationToken.None);

        Assert.That(handler.LastRequest!.RequestUri!.AbsolutePath, Is.EqualTo("/xperience-community/content-sync-toolkit/inventory/web-page-ids"));
        Assert.That(handler.LastRequest.RequestUri.Query, Does.Contain("channelName=My%20Channel").And.Contain("languageName=en").And.Contain($"guids={guid}"));
        Assert.That(result.Status, Is.EqualTo(ContentInventoryFetchStatus.Success));
        Assert.That(result.ScopeId, Is.EqualTo(3));
        Assert.That(result.Ids, Is.EquivalentTo(new Dictionary<Guid, int> { [guid] = 74 }));
    }

    [Test]
    public async Task GetContentItemIdsAsync_RequestsTheItemIdRoute()
    {
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TargetItemIdsResponse(4, DateTimeOffset.UtcNow, null, [])),
        });

        var result = await Client(handler).GetContentItemIdsAsync("Workspace", "en", [Guid.NewGuid()], CancellationToken.None);

        Assert.That(handler.LastRequest!.RequestUri!.AbsolutePath, Is.EqualTo("/xperience-community/content-sync-toolkit/inventory/content-item-ids"));
        Assert.That(handler.LastRequest.RequestUri.Query, Does.Contain("workspaceName=Workspace"));
        Assert.That(result.Status, Is.EqualTo(ContentInventoryFetchStatus.Success));
        Assert.That(result.ScopeId, Is.Null);
    }

    // A target older than schema 4 has no item ID endpoint.
    [Test]
    public async Task GetWebPageIdsAsync_OlderTarget_IsRejected()
    {
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await Client(handler).GetWebPageIdsAsync("Channel", "en", [Guid.NewGuid()], CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(ContentInventoryFetchStatus.Rejected));
        Assert.That(result.Ids, Is.Empty);
    }

    private static ContentSyncStatusItem OnlyOnTarget(string versionStatus) =>
        new(Guid.NewGuid(), ContentSyncStatus.OnlyOnTarget, null,
            new ContentInventoryItem(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "T", "S", "en", "/a", null, versionStatus));

    [Test]
    public void StatusTooltip_OnlyOnTargetWithALink_SaysTheRowOpensItThere() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(OnlyOnTarget("Published"), opensOnTarget: true),
            Is.EqualTo("Only on the target. Open it there with the link on its name: Content Sync can't delete content, so delete or unpublish it on the target."));

    [Test]
    public void StatusTooltip_OnlyOnTargetWithoutALink_KeepsTheDeleteHint() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(OnlyOnTarget("Published")),
            Does.Not.Contain("link").And.Contain("delete it on the target"));
}
