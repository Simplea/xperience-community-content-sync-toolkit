using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// The content queries themselves need a live instance (see docs/Contributing-Setup.md); these cover
// the guard in front of them. The query executor is null, so reaching a query would throw.
public class LocalContentInventoryServiceTests
{
    private sealed class StubScopeLookup(bool channelExists, bool languageExists) : IContentScopeLookup
    {
        public Task<bool> WebsiteChannelExistsAsync(string channelName, CancellationToken cancellationToken) =>
            Task.FromResult(channelExists);

        public Task<bool> ContentLanguageExistsAsync(string languageName, CancellationToken cancellationToken) =>
            Task.FromResult(languageExists);
    }

    private static LocalContentInventoryService CreateService(bool channelExists, bool languageExists) =>
        new(null!, new StubScopeLookup(channelExists, languageExists), null!);

    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public async Task GetWebPagesAsync_UnknownChannelOrLanguage_IsAnEmptyInventory(bool channelExists, bool languageExists)
    {
        var items = await CreateService(channelExists, languageExists)
            .GetWebPagesAsync("Channel", "en", CancellationToken.None);

        Assert.That(items, Is.Empty);
    }

    private static ContentInventoryItem Item(Guid guid, string versionStatus) =>
        new(guid, ContentInventoryItemKind.WebPage, "T", "S", "en", "/p", null, versionStatus);

    [Test]
    public void Merge_AddsUnpublishedItems_AfterThePublishedOnes()
    {
        var published = Item(Guid.NewGuid(), "Published");
        var unpublished = Item(Guid.NewGuid(), "Unpublished");

        var merged = LocalContentInventoryService.Merge([published], [unpublished]);

        Assert.That(merged, Is.EqualTo(new[] { published, unpublished }));
    }

    // Shouldn't happen (an item has one or the other), but a GUID must never be listed twice.
    [Test]
    public void Merge_KeepsThePublishedRow_WhenAGuidIsInBoth()
    {
        var guid = Guid.NewGuid();
        var published = Item(guid, "Published");

        var merged = LocalContentInventoryService.Merge([published], [Item(guid, "Unpublished")]);

        Assert.That(merged, Is.EqualTo(new[] { published }));
    }

    // A fake content query over item IDs: the batch after the given ID, in ID order.
    private static Func<int, Task<IReadOnlyList<int>>> BatchesOf(IReadOnlyList<int> ids, int pageSize, List<int> requestedAfterIds) =>
        afterId =>
        {
            requestedAfterIds.Add(afterId);
            return Task.FromResult<IReadOnlyList<int>>([.. ids.Where(id => id > afterId).Order().Take(pageSize)]);
        };

    [Test]
    public async Task PageThroughAsync_ReadsEveryItem_StartingEachBatchAfterTheLastId()
    {
        // IDs with gaps, as deleted items and other languages leave them.
        int[] ids = [3, 4, 9, 10, 12, 20, 21];
        var requestedAfterIds = new List<int>();

        var items = await LocalContentInventoryService.PageThroughAsync(BatchesOf(ids, 3, requestedAfterIds), id => id, 3);

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.EqualTo(ids));
            Assert.That(requestedAfterIds, Is.EqualTo(new[] { 0, 9, 20 }));
        });
    }

    // A full last batch can't tell there's nothing more, so one more (empty) batch is read.
    [Test]
    public async Task PageThroughAsync_ExactMultipleOfTheBatchSize_StopsAfterAnEmptyBatch()
    {
        int[] ids = [1, 2, 3, 4];
        var requestedAfterIds = new List<int>();

        var items = await LocalContentInventoryService.PageThroughAsync(BatchesOf(ids, 2, requestedAfterIds), id => id, 2);

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.EqualTo(ids));
            Assert.That(requestedAfterIds, Is.EqualTo(new[] { 0, 2, 4 }));
        });
    }

    [Test]
    public async Task PageThroughAsync_NoItems_ReadsOneBatch()
    {
        var requestedAfterIds = new List<int>();

        var items = await LocalContentInventoryService.PageThroughAsync(BatchesOf([], 2, requestedAfterIds), id => id, 2);

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.Empty);
            Assert.That(requestedAfterIds, Is.EqualTo(new[] { 0 }));
        });
    }

    [Test]
    public async Task GetContentHubItemsAsync_UnknownLanguage_IsAnEmptyInventory()
    {
        var items = await CreateService(channelExists: true, languageExists: false)
            .GetContentHubItemsAsync("Workspace", "xx", CancellationToken.None);

        Assert.That(items, Is.Empty);
    }
}
