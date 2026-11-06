using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;
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

    private sealed class StubContentLinksService : ILocalContentLinksService
    {
        public int GraphCallCount { get; private set; }

        public ContentLinkGraph Graph { get; set; } = ContentLinkGraph.Empty;

        public IReadOnlyDictionary<int, string> DisplayNames { get; set; } = new Dictionary<int, string>();

        public Task<ContentLinkGraph> GetLinkGraphAsync(string languageName, ContentInventoryItemKind kind, CancellationToken cancellationToken)
        {
            GraphCallCount++;
            return Task.FromResult(Graph);
        }

        public Task<IReadOnlyDictionary<int, string>> GetDisplayNamesAsync(
            string languageName, IReadOnlyCollection<int> contentItemIds, CancellationToken cancellationToken) =>
            Task.FromResult(DisplayNames);
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

        public int RequiredObjectsCallCount { get; private set; }

        public RequiredObjectsFetchResult RequiredObjectsResult { get; set; } = RequiredObjectsFetchResult.Success([]);

        public Task<RequiredObjectsFetchResult> GetRequiredObjectsAsync(CancellationToken cancellationToken)
        {
            RequiredObjectsCallCount++;
            return Task.FromResult(RequiredObjectsResult);
        }

        // The sync status service doesn't look up target IDs; only the admin page does.
        public Task<TargetItemIdsFetchResult> GetWebPageIdsAsync(
            string channelName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TargetItemIdsFetchResult> GetContentItemIdsAsync(
            string workspaceName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubLocalRequiredObjectsService : ILocalRequiredObjectsService
    {
        public IReadOnlyList<RequiredObject> Objects { get; set; } = [];

        public Task<IReadOnlyList<RequiredObject>> GetRequiredObjectsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Objects);
    }

    private static IContentSyncStatusService CreateService(
        StubLocalContentInventoryService local,
        StubContentInventoryClient client,
        ContentInventoryCache cache,
        StubLocalRequiredObjectsService? localRequiredObjects = null,
        StubContentLinksService? contentLinks = null) =>
        new ContentSyncStatusService(
            local,
            localRequiredObjects ?? new StubLocalRequiredObjectsService(),
            contentLinks ?? new StubContentLinksService(),
            client,
            cache,
            Options.Create(new ContentSyncToolkitOptions { InventoryCacheDuration = TimeSpan.FromSeconds(90) }));

    private static readonly RequiredObject articleType =
        new(RequiredObjectKind.ContentType, Guid.NewGuid(), "DG.Article", "Article") { DefinitionHash = "A" };

    private static ContentInventoryItem Page(Guid guid, string contentTypeName, DateTime? published) =>
        new(guid, ContentInventoryItemKind.WebPage, contentTypeName, "Channel", "en", "/" + guid, published, "Published");

    // A page missing on the target whose type the target lacks gets the issue; an in-sync page of the
    // same type doesn't, since there's nothing to push.
    [Test]
    public async Task GetWebPageSyncStatusAsync_AttachesRequiredObjectIssues_ToItemsThatNeedAPush()
    {
        var missingGuid = Guid.NewGuid();
        var inSyncGuid = Guid.NewGuid();
        var published = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var local = new StubLocalContentInventoryService
        {
            WebPages = [Page(missingGuid, "DG.Article", published), Page(inSyncGuid, "DG.Article", published)],
        };
        var client = new StubContentInventoryClient
        {
            WebPageResult = ContentInventoryFetchResult.Success([Page(inSyncGuid, "DG.Article", published)]),
        };
        var service = CreateService(local, client, new ContentInventoryCache(TimeProvider.System), new() { Objects = [articleType] });

        var result = await service.GetWebPageSyncStatusAsync("Channel", "en", CancellationToken.None);

        var missing = result.Items.Single(item => item.Guid == missingGuid);
        Assert.That(missing.RequiredObjectIssues.Single().Object, Is.EqualTo(articleType));
        Assert.That(missing.RequiredObjectIssues.Single().Problem, Is.EqualTo(RequiredObjectProblem.MissingOnTarget));
        Assert.That(result.Items.Single(item => item.Guid == inSyncGuid).RequiredObjectIssues, Is.Empty);
    }

    // Content Sync refuses the sync because of what the page links to, so the page must say so.
    [Test]
    public async Task GetWebPageSyncStatusAsync_AttachesTheIssueOfALinkedItem()
    {
        var pageGuid = Guid.NewGuid();
        var links = new StubContentLinksService
        {
            Graph = new ContentLinkGraph(
                new Dictionary<Guid, int> { [pageGuid] = 1 },
                new Dictionary<int, string> { [1] = "DG.Product", [2] = "DG.Article" },
                new Dictionary<int, IReadOnlyCollection<int>> { [1] = [2] }),
            DisplayNames = new Dictionary<int, string> { [2] = "Brewing tips" },
        };
        var service = CreateService(
            new StubLocalContentInventoryService { WebPages = [Page(pageGuid, "DG.Product", null)] },
            new StubContentInventoryClient(),
            new ContentInventoryCache(TimeProvider.System),
            new() { Objects = [articleType] },
            links);

        var result = await service.GetWebPageSyncStatusAsync("Channel", "en", CancellationToken.None);

        var issue = result.Items.Single().RequiredObjectIssues.Single();
        Assert.That(issue.Object, Is.EqualTo(articleType));
        Assert.That(issue.ViaLinkedItem, Is.EqualTo("Brewing tips"));
    }

    [Test]
    public async Task GetWebPageSyncStatusAsync_AttachesTheIssueOfAParentPage()
    {
        var parent = Page(Guid.NewGuid(), "DG.Article", null) with { TreePath = "/parent", DisplayName = "Parent" };
        var child = Page(Guid.NewGuid(), "DG.Product", null) with { TreePath = "/parent/child" };
        var service = CreateService(
            new StubLocalContentInventoryService { WebPages = [parent, child] },
            new StubContentInventoryClient(),
            new ContentInventoryCache(TimeProvider.System),
            new() { Objects = [articleType] });

        var result = await service.GetWebPageSyncStatusAsync("Channel", "en", CancellationToken.None);

        Assert.That(result.Items.Single(item => item.Guid == child.Guid).RequiredObjectIssues.Single().ViaLinkedItem, Is.EqualTo("Parent"));
    }

    // Its own issue says it all: a link to another item of the same type adds nothing.
    [Test]
    public async Task GetWebPageSyncStatusAsync_DoesNotRepeatAnIssueTheItemHasItself()
    {
        var pageGuid = Guid.NewGuid();
        var links = new StubContentLinksService
        {
            Graph = new ContentLinkGraph(
                new Dictionary<Guid, int> { [pageGuid] = 1 },
                new Dictionary<int, string> { [1] = "DG.Article", [2] = "DG.Article" },
                new Dictionary<int, IReadOnlyCollection<int>> { [1] = [2] }),
        };
        var service = CreateService(
            new StubLocalContentInventoryService { WebPages = [Page(pageGuid, "DG.Article", null)] },
            new StubContentInventoryClient(),
            new ContentInventoryCache(TimeProvider.System),
            new() { Objects = [articleType] },
            links);

        var result = await service.GetWebPageSyncStatusAsync("Channel", "en", CancellationToken.None);

        Assert.That(result.Items.Single().RequiredObjectIssues.Single().ViaLinkedItem, Is.Null);
    }

    // The usual case: nothing is incompatible, so links aren't read at all.
    [Test]
    public async Task GetWebPageSyncStatusAsync_DoesNotReadLinks_WhenNoContentTypeIsIncompatible()
    {
        var links = new StubContentLinksService();
        var client = new StubContentInventoryClient { RequiredObjectsResult = RequiredObjectsFetchResult.Success([articleType]) };
        var service = CreateService(
            new StubLocalContentInventoryService { WebPages = [Page(Guid.NewGuid(), "DG.Article", null)] },
            client,
            new ContentInventoryCache(TimeProvider.System),
            new() { Objects = [articleType] },
            links);

        await service.GetWebPageSyncStatusAsync("Channel", "en", CancellationToken.None);

        Assert.That(links.GraphCallCount, Is.Zero);
    }

    // The check is an addition: a target that can't answer it (e.g. an older toolkit version) still
    // gets compared, just without issues.
    [Test]
    public async Task GetWebPageSyncStatusAsync_StillComparesItems_WhenTheRequiredObjectsCheckFails()
    {
        var local = new StubLocalContentInventoryService { WebPages = [Page(Guid.NewGuid(), "DG.Article", null)] };
        var client = new StubContentInventoryClient
        {
            RequiredObjectsResult = RequiredObjectsFetchResult.Failed(ContentInventoryFetchStatus.Rejected),
        };
        var service = CreateService(local, client, new ContentInventoryCache(TimeProvider.System), new() { Objects = [articleType] });

        var result = await service.GetWebPageSyncStatusAsync("Channel", "en", CancellationToken.None);

        Assert.That(result.TargetAvailable, Is.True);
        Assert.That(result.Items.Single().Status, Is.EqualTo(ContentSyncStatus.New));
        Assert.That(result.Items.Single().RequiredObjectIssues, Is.Empty);
    }

    [Test]
    public async Task GetWebPageSyncStatusAsync_DoesNotAskForRequiredObjects_WhenNothingNeedsAPush()
    {
        var guid = Guid.NewGuid();
        var local = new StubLocalContentInventoryService { WebPages = [Page(guid, "DG.Article", null)] };
        var client = new StubContentInventoryClient { WebPageResult = ContentInventoryFetchResult.Success(local.WebPages) };
        var service = CreateService(local, client, new ContentInventoryCache(TimeProvider.System));

        await service.GetWebPageSyncStatusAsync("Channel", "en", CancellationToken.None);

        Assert.That(client.RequiredObjectsCallCount, Is.Zero);
    }

    [Test]
    public async Task CheckRequiredObjectsAsync_ReportsNotChecked_WhenTheTargetCantBeAsked()
    {
        var client = new StubContentInventoryClient
        {
            RequiredObjectsResult = RequiredObjectsFetchResult.Failed(ContentInventoryFetchStatus.Unreachable),
        };
        var service = CreateService(new StubLocalContentInventoryService(), client, new ContentInventoryCache(TimeProvider.System), new() { Objects = [articleType] });

        var check = await service.CheckRequiredObjectsAsync(forceRefresh: false, CancellationToken.None);

        Assert.That(check.Checked, Is.False);
        Assert.That(check.Issues, Is.Empty);
    }

    [Test]
    public async Task CheckRequiredObjectsAsync_CachesTheTargetsList_UnlessForced()
    {
        var client = new StubContentInventoryClient();
        var service = CreateService(new StubLocalContentInventoryService(), client, new ContentInventoryCache(TimeProvider.System), new() { Objects = [articleType] });

        var first = await service.CheckRequiredObjectsAsync(forceRefresh: false, CancellationToken.None);
        await service.CheckRequiredObjectsAsync(forceRefresh: false, CancellationToken.None);
        await service.CheckRequiredObjectsAsync(forceRefresh: true, CancellationToken.None);

        Assert.That(first.Checked, Is.True);
        Assert.That(first.Issues.Single().Object, Is.EqualTo(articleType));
        Assert.That(client.RequiredObjectsCallCount, Is.EqualTo(2));
    }

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

    [Test]
    public async Task GetWebPageSyncStatusAsync_ForceRefreshTrue_BypassesAWarmCache()
    {
        var local = new StubLocalContentInventoryService();
        var client = new StubContentInventoryClient();
        var service = CreateService(local, client, new ContentInventoryCache(TimeProvider.System));

        await service.GetWebPageSyncStatusAsync("Channel", "en-US", forceRefresh: false, CancellationToken.None);
        await service.GetWebPageSyncStatusAsync("Channel", "en-US", forceRefresh: true, CancellationToken.None);

        Assert.That(client.WebPageCallCount, Is.EqualTo(2),
            "the second call requested forceRefresh, so it must not be served from the cache primed by the first call");
    }

    [Test]
    public async Task GetWebPageSyncStatusAsync_ForceRefreshTrue_RePrimesTheCacheForSubsequentCalls()
    {
        var local = new StubLocalContentInventoryService();
        var client = new StubContentInventoryClient();
        var service = CreateService(local, client, new ContentInventoryCache(TimeProvider.System));

        await service.GetWebPageSyncStatusAsync("Channel", "en-US", forceRefresh: true, CancellationToken.None);
        await service.GetWebPageSyncStatusAsync("Channel", "en-US", forceRefresh: false, CancellationToken.None);

        Assert.That(client.WebPageCallCount, Is.EqualTo(1),
            "the forced-refresh result must be cached, so the following non-forced call is served from it");
    }

    [Test]
    public async Task GetWebPageSyncStatusAsync_ThreeArgOverload_BehavesAsForceRefreshFalse()
    {
        var local = new StubLocalContentInventoryService();
        var client = new StubContentInventoryClient();
        var service = CreateService(local, client, new ContentInventoryCache(TimeProvider.System));

        await service.GetWebPageSyncStatusAsync("Channel", "en-US", forceRefresh: true, CancellationToken.None);
        await service.GetWebPageSyncStatusAsync("Channel", "en-US", CancellationToken.None);

        Assert.That(client.WebPageCallCount, Is.EqualTo(1),
            "the existing 3-arg overload must behave exactly as forceRefresh: false, reusing the primed cache");
    }

    [Test]
    public async Task GetContentHubSyncStatusAsync_ForceRefreshTrue_BypassesAWarmCache()
    {
        var local = new StubLocalContentInventoryService();
        var client = new StubContentInventoryClient();
        var service = CreateService(local, client, new ContentInventoryCache(TimeProvider.System));

        await service.GetContentHubSyncStatusAsync("Workspace", "en-US", forceRefresh: false, CancellationToken.None);
        await service.GetContentHubSyncStatusAsync("Workspace", "en-US", forceRefresh: true, CancellationToken.None);

        Assert.That(client.ContentHubCallCount, Is.EqualTo(2));
    }
}
