using System.Net;
using System.Net.Http.Json;

using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentInventoryClientTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://target.example.com") };

    [Test]
    public async Task GetWebPagesAsync_RequestsExpectedRouteAndQueryString()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ContentInventoryResponse(1, DateTimeOffset.UtcNow, []))
        });
        var client = new ContentInventoryClient(CreateHttpClient(handler));

        await client.GetWebPagesAsync("My Channel", "en-US", CancellationToken.None);

        Assert.That(handler.LastRequest, Is.Not.Null);
        Assert.That(handler.LastRequest!.RequestUri!.AbsolutePath, Is.EqualTo("/xperience-community/content-sync-toolkit/inventory/web-pages"));
        Assert.That(handler.LastRequest.RequestUri.Query, Does.Contain("channelName=My%20Channel"));
        Assert.That(handler.LastRequest.RequestUri.Query, Does.Contain("languageName=en-US"));
    }

    [Test]
    public async Task GetContentHubItemsAsync_RequestsExpectedRouteAndQueryString()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ContentInventoryResponse(1, DateTimeOffset.UtcNow, []))
        });
        var client = new ContentInventoryClient(CreateHttpClient(handler));

        await client.GetContentHubItemsAsync("MyWorkspace", "en-US", CancellationToken.None);

        Assert.That(handler.LastRequest!.RequestUri!.AbsolutePath, Is.EqualTo("/xperience-community/content-sync-toolkit/inventory/content-hub-items"));
        Assert.That(handler.LastRequest.RequestUri.Query, Does.Contain("workspaceName=MyWorkspace"));
    }

    [Test]
    public async Task GetWebPagesAsync_ReturnsSuccessWithItems_OnValidResponse()
    {
        var itemGuid = Guid.NewGuid();
        var items = new List<ContentInventoryItem>
        {
            new(itemGuid, ContentInventoryItemKind.WebPage, "T", "S", "en-US", "/", null, "Published")
        };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ContentInventoryResponse(1, DateTimeOffset.UtcNow, items))
        });
        var client = new ContentInventoryClient(CreateHttpClient(handler));

        var result = await client.GetWebPagesAsync("Channel", "en-US", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(ContentInventoryFetchStatus.Success));
        Assert.That(result.Items, Has.Count.EqualTo(1));
        Assert.That(result.Items[0].Guid, Is.EqualTo(itemGuid));
    }

    private static FakeHttpMessageHandler RawJson(string json) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    private static string InventoryJson(int schemaVersion, string lastPublishedWhen, string? order = null) =>
        "{\"schemaVersion\":" + schemaVersion + ",\"generatedAtUtc\":\"2026-03-01T12:00:00+00:00\",\"items\":[{"
        + "\"guid\":\"6f1f2c1e-0000-0000-0000-000000000001\",\"kind\":0,\"contentTypeName\":\"T\",\"scopeName\":\"S\","
        + "\"languageName\":\"en\",\"treePath\":\"/a\",\"lastPublishedWhen\":\"" + lastPublishedWhen + "\","
        + "\"versionStatus\":\"Published\",\"name\":\"a\"" + (order is null ? string.Empty : ",\"order\":" + order) + "}]}";

    // Schema 2 targets send UTC with a Z suffix.
    [Test]
    public async Task GetWebPagesAsync_ReadsUtcTimestampsAndOrder_FromASchema2Target()
    {
        var client = new ContentInventoryClient(CreateHttpClient(RawJson(InventoryJson(2, "2026-03-01T17:30:00Z", order: "4"))));

        var item = (await client.GetWebPagesAsync("Channel", "en", CancellationToken.None)).Items.Single();

        Assert.That(item.LastPublishedWhen, Is.EqualTo(new DateTime(2026, 3, 1, 17, 30, 0, DateTimeKind.Utc)));
        Assert.That(item.LastPublishedWhen!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(item.Order, Is.EqualTo(4));
    }

    // Schema 1 targets send server-local time without a time zone; it's read as this server's local
    // time, which is how it was compared before.
    [Test]
    public async Task GetWebPagesAsync_ReadsATimestampWithoutTimeZone_AsThisServersLocalTime()
    {
        var client = new ContentInventoryClient(CreateHttpClient(RawJson(InventoryJson(1, "2026-03-01T12:30:00"))));

        var item = (await client.GetWebPagesAsync("Channel", "en", CancellationToken.None)).Items.Single();

        var expected = new DateTime(2026, 3, 1, 12, 30, 0, DateTimeKind.Local).ToUniversalTime();
        Assert.That(item.LastPublishedWhen, Is.EqualTo(expected));
        Assert.That(item.LastPublishedWhen!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(item.Order, Is.Null);
    }

    [Test]
    public async Task GetWebPagesAsync_ReturnsRejected_OnNonSuccessStatusCode()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new ContentInventoryClient(CreateHttpClient(handler));

        var result = await client.GetWebPagesAsync("Channel", "en-US", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(ContentInventoryFetchStatus.Rejected));
        Assert.That(result.Items, Is.Empty);
    }

    // A 200 that isn't an inventory (e.g. a proxy's HTML page, or a JSON null) is an Error, never
    // an empty inventory, which would report every local item as missing.
    [TestCase("<html><body>Not an inventory</body></html>", "text/html")]
    [TestCase("{\"schemaVersion\": ", "application/json")]
    [TestCase("null", "application/json")]
    public async Task GetWebPagesAsync_ReturnsError_OnAnUnreadableSuccessResponse(string body, string mediaType)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType)
        });
        var client = new ContentInventoryClient(CreateHttpClient(handler));

        var result = await client.GetWebPagesAsync("Channel", "en-US", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(ContentInventoryFetchStatus.Error));
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task GetWebPagesAsync_ReturnsUnreachable_OnNetworkFailure()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("simulated network failure"));
        var client = new ContentInventoryClient(CreateHttpClient(handler));

        var result = await client.GetWebPagesAsync("Channel", "en-US", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(ContentInventoryFetchStatus.Unreachable));
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public void GetWebPagesAsync_ThrowsImmediately_WhenTargetUrlNotConfigured()
    {
        var client = new ContentInventoryClient(new HttpClient { BaseAddress = null });

        Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetWebPagesAsync("Channel", "en-US", CancellationToken.None));
    }
}
