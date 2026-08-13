using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

/// <summary>
/// Pure diff logic matching local and remote content inventories by GUID. See
/// docs/specs/content-inventory-foundation.md for the full classification table and the
/// documented clock-skew limitation of comparing publish timestamps across two servers.
/// </summary>
public static class ContentSyncStatusComparer
{
    public static IReadOnlyList<ContentSyncStatusItem> Compare(
        IReadOnlyList<ContentInventoryItem> local, IReadOnlyList<ContentInventoryItem> remote)
    {
        var localByGuid = ToLookup(local);
        var remoteByGuid = ToLookup(remote);

        var allGuids = new HashSet<Guid>(localByGuid.Keys);
        allGuids.UnionWith(remoteByGuid.Keys);

        var results = new List<ContentSyncStatusItem>(allGuids.Count);

        foreach (var guid in allGuids)
        {
            bool hasLocal = localByGuid.TryGetValue(guid, out var localItem);
            bool hasRemote = remoteByGuid.TryGetValue(guid, out var remoteItem);

            results.Add(new ContentSyncStatusItem(
                guid,
                Classify(hasLocal, hasRemote, localItem, remoteItem),
                localItem,
                remoteItem));
        }

        return results;
    }

    private static ContentSyncStatus Classify(
        bool hasLocal, bool hasRemote, ContentInventoryItem? local, ContentInventoryItem? remote)
    {
        if (!hasLocal)
        {
            return ContentSyncStatus.ExtraOnTarget;
        }

        if (!hasRemote)
        {
            return ContentSyncStatus.MissingOnTarget;
        }

        var localPublished = local!.LastPublishedWhen;
        var remotePublished = remote!.LastPublishedWhen;

        if (localPublished is null)
        {
            return ContentSyncStatus.InSync;
        }

        if (remotePublished is null)
        {
            return ContentSyncStatus.OutOfDateOnTarget;
        }

        return localPublished.Value > remotePublished.Value
            ? ContentSyncStatus.OutOfDateOnTarget
            : ContentSyncStatus.InSync;
    }

    private static Dictionary<Guid, ContentInventoryItem> ToLookup(IReadOnlyList<ContentInventoryItem> items)
    {
        var result = new Dictionary<Guid, ContentInventoryItem>(items.Count);

        foreach (var item in items)
        {
            // Later duplicates win; a well-formed inventory should never contain duplicate GUIDs,
            // but this must not throw if one somehow does.
            result[item.Guid] = item;
        }

        return result;
    }
}
