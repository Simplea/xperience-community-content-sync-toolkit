using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentInventoryCacheTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private static readonly IReadOnlyList<ContentInventoryItem> sampleItems =
    [
        new(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "Test.Type", "Scope", "en-US", "/Test", null, "Published")
    ];

    [Test]
    public void TryGet_ReturnsFalse_WhenKeyWasNeverSet()
    {
        var cache = new ContentInventoryCache(new ManualTimeProvider());

        bool hit = cache.TryGet("missing-key", out var items);

        Assert.That(hit, Is.False);
        Assert.That(items, Is.Empty);
    }

    [Test]
    public void TryGet_ReturnsTrue_WithinTtl()
    {
        var time = new ManualTimeProvider();
        var cache = new ContentInventoryCache(time);

        cache.Set("key", sampleItems, TimeSpan.FromSeconds(90));
        time.Advance(TimeSpan.FromSeconds(89));

        bool hit = cache.TryGet("key", out var items);

        Assert.That(hit, Is.True);
        Assert.That(items, Is.SameAs(sampleItems));
    }

    [Test]
    public void TryGet_ReturnsFalse_AfterTtlExpires()
    {
        var time = new ManualTimeProvider();
        var cache = new ContentInventoryCache(time);

        cache.Set("key", sampleItems, TimeSpan.FromSeconds(90));
        time.Advance(TimeSpan.FromSeconds(90));

        bool hit = cache.TryGet("key", out _);

        Assert.That(hit, Is.False);
    }

    [Test]
    public void Set_WithZeroTtl_NeverProducesAHit()
    {
        var cache = new ContentInventoryCache(new ManualTimeProvider());

        cache.Set("key", sampleItems, TimeSpan.Zero);

        Assert.That(cache.TryGet("key", out _), Is.False);
    }

    [Test]
    public void Set_WithNegativeTtl_NeverProducesAHit()
    {
        var cache = new ContentInventoryCache(new ManualTimeProvider());

        cache.Set("key", sampleItems, TimeSpan.FromSeconds(-1));

        Assert.That(cache.TryGet("key", out _), Is.False);
    }

    [Test]
    public void DistinctKeys_DoNotCollide()
    {
        var cache = new ContentInventoryCache(new ManualTimeProvider());
        var otherItems = new List<ContentInventoryItem>();

        cache.Set("web-page|ChannelA|en-US", sampleItems, TimeSpan.FromSeconds(90));
        cache.Set("content-hub-item|WorkspaceA|en-US", otherItems, TimeSpan.FromSeconds(90));

        cache.TryGet("web-page|ChannelA|en-US", out var first);
        cache.TryGet("content-hub-item|WorkspaceA|en-US", out var second);

        Assert.That(first, Is.SameAs(sampleItems));
        Assert.That(second, Is.SameAs(otherItems));
    }
}
