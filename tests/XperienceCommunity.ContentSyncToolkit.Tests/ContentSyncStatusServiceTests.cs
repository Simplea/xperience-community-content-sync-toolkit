using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncStatusServiceTests
{
    private sealed class StubLocalContentInventoryService : ILocalContentInventoryService
    {
        public int WebPageCallCount { get; private set; }

        public int ContentHubCallCount { get; private set; }

        public IReadOnlyList<ContentInventoryItem> WebPages { get; set; } = [];

        public IReadOnlyList<ContentInventoryItem> ContentHubItems { get; set; } = [];

        public Task<IReadOnlyList<ContentInventoryItem>> GetWebPagesAsync(
            string websiteChannelName, string languageName, CancellationToken cancellationToken)
        {
            WebPageCallCount++;
            return Task.FromResult(WebPages);
        }

        public Task<IReadOnlyList<ContentInventoryItem>> GetContentHubItemsAsync(
            string workspaceName, string languageName, CancellationToken cancellationToken)
        {
            ContentHubCallCount++;
            return Task.FromResult(ContentHubItems);
        }
    }

    private sealed class StubContentInventoryClient : IContentInventoryClient
    {
        public int WebPageCallCount { get; private set; }

        public int ContentHubCallCount { get; private set; }

        public ContentInventoryFetchResult WebPageResult { get; set; } = ContentInventoryFetchResult.Success([]);

        public ContentInventoryFetchResult ContentHubResult { get; set; } = ContentInventoryFetchResult.Success([]);

        public Task<ContentInventoryFetchResult> GetWebPagesAsync(
            string channelName, string languageName, CancellationToken cancellationToken)
        {
            WebPageCallCount++;
            return Task.FromResult(WebPageResult);
        }

        public Task<ContentInventoryFetchResult> GetContentHubItemsAsync(
            string workspaceName, string languageName, CancellationToken cancellationToken)
        {
            ContentHubCallCount++;
            return Task.FromResult(ContentHubResult);
        }
    }

    private static ContentSyncStatusService CreateService(
        StubLocalContentInventoryService local, StubContentInventoryClient client, ContentInventoryCache cache) =>
        new(
            local,
            client,
            cache,
            Options.Create(new ContentSyncToolkitOptions
            {
                Source = new ContentSyncToolkitSourceOptions { InventoryCacheDuration = TimeSpan.FromSeconds(90) }
            }));

    [Test]
    public async Task GetWebPageSyncStatusAsync_ReusesCachedRemoteResult_ButAlwaysRequeriesLocal()
    {
        var itemGuid = Guid.NewGuid();
        var local = new StubLocalContentInventoryService
        {
            WebPages = [new ContentInventoryItem(itemGuid, ContentInventoryItemKind.WebPage, "T", "S", "en-US", "/", null, "Published")]
        };
        var client = new StubContentInventoryClient
        {
            WebPageResult = ContentInventoryFetchResult.Success(local.WebPages)
        };
        var service = CreateService(local, client, new ContentInventoryCache(TimeProvider.System));

        await service.GetWebPageSyncStatusAsync("Channel", "en-US", CancellationToken.None);
        await service.GetWebPageSyncStatusAsync("Channel", "en-US", CancellationToken.None);

        Assert.That(local.WebPageCallCount, Is.EqualTo(2));
        Assert.That(client.WebPageCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SyncStatus_CacheKeys_DoNotCrossContaminateWebPageAndContentHubScopes()
    {
        var local = new StubLocalContentInventoryService();
        var client = new StubContentInventoryClient();
        var service = CreateService(local, client, new ContentInventoryCache(TimeProvider.System));

        await service.GetWebPageSyncStatusAsync("SameName", "en-US", CancellationToken.None);
        await service.GetContentHubSyncStatusAsync("SameName", "en-US", CancellationToken.None);

        Assert.That(client.WebPageCallCount, Is.EqualTo(1));
        Assert.That(client.ContentHubCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetWebPageSyncStatusAsync_ReportsTargetUnavailable_WithoutQueryingLocalOrReturningItems()
    {
        var local = new StubLocalContentInventoryService
        {
            WebPages = [new ContentInventoryItem(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "T", "S", "en-US", "/", null, "Published")]
        };
        var client = new StubContentInventoryClient
        {
            WebPageResult = ContentInventoryFetchResult.Failed(ContentInventoryFetchStatus.Unreachable)
        };
        var service = CreateService(local, client, new ContentInventoryCache(TimeProvider.System));

        var result = await service.GetWebPageSyncStatusAsync("Channel", "en-US", CancellationToken.None);

        Assert.That(result.TargetAvailable, Is.False);
        Assert.That(result.Items, Is.Empty);
        Assert.That(local.WebPageCallCount, Is.Zero,
            "a failed fetch must never be silently diffed against local content as if the target were empty");
    }

    [Test]
    public async Task GetContentHubSyncStatusAsync_ReportsTargetUnavailable_WhenRejected()
    {
        var local = new StubLocalContentInventoryService();
        var client = new StubContentInventoryClient
        {
            ContentHubResult = ContentInventoryFetchResult.Failed(ContentInventoryFetchStatus.Rejected)
        };
        var service = CreateService(local, client, new ContentInventoryCache(TimeProvider.System));

        var result = await service.GetContentHubSyncStatusAsync("Workspace", "en-US", CancellationToken.None);

        Assert.That(result.TargetAvailable, Is.False);
        Assert.That(result.Items, Is.Empty);
    }
}
