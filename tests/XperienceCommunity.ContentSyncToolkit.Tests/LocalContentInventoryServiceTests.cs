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
        new(null!, new StubScopeLookup(channelExists, languageExists));

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

    [Test]
    public async Task GetContentHubItemsAsync_UnknownLanguage_IsAnEmptyInventory()
    {
        var items = await CreateService(channelExists: true, languageExists: false)
            .GetContentHubItemsAsync("Workspace", "xx", CancellationToken.None);

        Assert.That(items, Is.Empty);
    }
}
