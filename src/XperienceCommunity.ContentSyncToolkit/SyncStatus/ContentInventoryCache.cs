using System.Collections.Concurrent;

using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;

namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

internal sealed class ContentInventoryCache(TimeProvider timeProvider) : IContentInventoryCache
{
    // Inventory keys are "{kind}|{scope}|{language}", so this one can't collide with them.
    private const string RequiredObjectsKey = "required-objects";

    private readonly ConcurrentDictionary<string, CacheEntry> entries = new();

    public bool TryGet(string key, out IReadOnlyList<ContentInventoryItem> items) => TryGetValue(key, out items);

    public void Set(string key, IReadOnlyList<ContentInventoryItem> items, TimeSpan ttl) => SetValue(key, items, ttl);

    public bool TryGetRequiredObjects(out IReadOnlyList<RequiredObject> objects) => TryGetValue(RequiredObjectsKey, out objects);

    public void SetRequiredObjects(IReadOnlyList<RequiredObject> objects, TimeSpan ttl) => SetValue(RequiredObjectsKey, objects, ttl);

    private bool TryGetValue<T>(string key, out IReadOnlyList<T> values)
    {
        values = [];

        if (!entries.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (entry.ExpiresAt <= timeProvider.GetUtcNow())
        {
            entries.TryRemove(key, out _);
            return false;
        }

        if (entry.Value is not IReadOnlyList<T> cached)
        {
            return false;
        }

        values = cached;
        return true;
    }

    private void SetValue(string key, object value, TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        entries[key] = new CacheEntry(value, timeProvider.GetUtcNow() + ttl);
    }

    private sealed record CacheEntry(object Value, DateTimeOffset ExpiresAt);
}
