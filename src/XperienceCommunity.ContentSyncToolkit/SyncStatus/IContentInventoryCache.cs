using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;

namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

/// <summary>
/// Short-TTL cache for successfully fetched remote inventories and required objects only. Local
/// data is never cached (always re-queried fresh), and failed fetches are never cached either — a
/// briefly unreachable target should be retried on the very next status check rather than staying
/// reported as unavailable for a full TTL after it recovers.
/// </summary>
public interface IContentInventoryCache
{
    public bool TryGet(string key, out IReadOnlyList<ContentInventoryItem> items);

    /// <summary>
    /// Stores <paramref name="items"/> under <paramref name="key"/> for <paramref name="ttl"/>.
    /// A zero or negative <paramref name="ttl"/> stores nothing, so a subsequent <see cref="TryGet"/>
    /// always misses.
    /// </summary>
    public void Set(string key, IReadOnlyList<ContentInventoryItem> items, TimeSpan ttl);

    public bool TryGetRequiredObjects(out IReadOnlyList<RequiredObject> objects);

    /// <summary>As <see cref="Set"/>, for the target's required objects.</summary>
    public void SetRequiredObjects(IReadOnlyList<RequiredObject> objects, TimeSpan ttl);
}
