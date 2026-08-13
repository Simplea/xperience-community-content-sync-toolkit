using System.Collections.Concurrent;

using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

internal sealed class ContentInventoryCache(TimeProvider timeProvider) : IContentInventoryCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> entries = new();

    public bool TryGet(string key, out IReadOnlyList<ContentInventoryItem> items)
    {
        if (entries.TryGetValue(key, out var entry) && entry.ExpiresAt > timeProvider.GetUtcNow())
        {
            items = entry.Items;
            return true;
        }

        entries.TryRemove(key, out _);
        items = [];
        return false;
    }

    public void Set(string key, IReadOnlyList<ContentInventoryItem> items, TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        entries[key] = new CacheEntry(items, timeProvider.GetUtcNow() + ttl);
    }

    private sealed record CacheEntry(IReadOnlyList<ContentInventoryItem> Items, DateTimeOffset ExpiresAt);
}
