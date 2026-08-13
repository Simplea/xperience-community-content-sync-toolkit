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

    [Test]
    public async Task GetWebPagesAsync_ReturnsRejected_OnNonSuccessStatusCode()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new ContentInventoryClient(CreateHttpClient(handler));

        var result = await client.GetWebPagesAsync("Channel", "en-US", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(ContentInventoryFetchStatus.Rejected));
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
