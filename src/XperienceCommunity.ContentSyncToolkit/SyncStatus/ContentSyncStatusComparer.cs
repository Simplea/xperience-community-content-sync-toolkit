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

            var status = Classify(hasLocal, hasRemote, localItem, remoteItem);
            if (status is not null)
            {
                results.Add(new ContentSyncStatusItem(guid, status.Value, localItem, remoteItem));
            }
        }

        return results;
    }

    // Null means the item is left out: there's nothing Content Sync would do with it.
    private static ContentSyncStatus? Classify(
        bool hasLocal, bool hasRemote, ContentInventoryItem? local, ContentInventoryItem? remote)
    {
        if (!hasLocal)
        {
            return ContentSyncStatus.ExtraOnTarget;
        }

        bool localUnpublished = ContentInventoryVersionStatus.IsUnpublished(local!.VersionStatus);

        if (!hasRemote)
        {
            // Content Sync creates an unpublished page on the target, but makes no change for a new
            // unpublished content-hub item.
            return localUnpublished && local.Kind == ContentInventoryItemKind.ContentHubItem
                ? null
                : ContentSyncStatus.MissingOnTarget;
        }

        // Unpublishing keeps LastPublishedWhen, so a publish-state difference is invisible to the
        // timestamp rule below and has to be checked first.
        if (localUnpublished != ContentInventoryVersionStatus.IsUnpublished(remote!.VersionStatus))
        {
            return ContentSyncStatus.OutOfDateOnTarget;
        }

        var localPublished = local.LastPublishedWhen;
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
