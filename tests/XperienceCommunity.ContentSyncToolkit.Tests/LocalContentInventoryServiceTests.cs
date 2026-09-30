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

    [Test]
    public async Task GetContentHubItemsAsync_UnknownLanguage_IsAnEmptyInventory()
    {
        var items = await CreateService(channelExists: true, languageExists: false)
            .GetContentHubItemsAsync("Workspace", "xx", CancellationToken.None);

        Assert.That(items, Is.Empty);
    }
}
