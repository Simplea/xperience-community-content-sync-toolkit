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

            // A never-published draft on the target isn't something to compare with: a sync from
            // here sends the published version as if the target didn't have the item. Nothing of it
            // is live there either, so on its own it isn't listed.
            if (hasRemote && ContentInventoryVersionStatus.IsNeverPublished(remoteItem!.VersionStatus))
            {
                hasRemote = false;
                remoteItem = null;
            }

            if (!hasLocal && !hasRemote)
            {
                continue;
            }

            if (Classify(hasLocal, hasRemote, localItem, remoteItem) is { } status)
            {
                results.Add(new ContentSyncStatusItem(guid, status, localItem, remoteItem));
            }
        }

        return MarkReorderedSiblings(results);
    }

    // Null means the item is left out: there's nothing Content Sync would do with it.
    private static ContentSyncStatus? Classify(
        bool hasLocal, bool hasRemote, ContentInventoryItem? local, ContentInventoryItem? remote)
    {
        if (!hasLocal)
        {
            return ContentSyncStatus.OnlyOnTarget;
        }

        // Without a published version, Content Sync can't sync the item until it's published. An item
        // unpublished and edited again is listed whatever the target has; a never-published one only
        // when the target has it, so it isn't mistaken for deleted here.
        if (ContentInventoryVersionStatus.IsUnpublishedDraft(local!.VersionStatus))
        {
            return ContentSyncStatus.NotPublished;
        }

        if (ContentInventoryVersionStatus.IsNeverPublished(local.VersionStatus))
        {
            return hasRemote ? ContentSyncStatus.NotPublished : null;
        }

        bool localUnpublished = ContentInventoryVersionStatus.IsUnpublished(local.VersionStatus);

        if (!hasRemote)
        {
            // Content Sync creates an unpublished page on the target, but makes no change for a new
            // unpublished content-hub item.
            return localUnpublished && local.Kind == ContentInventoryItemKind.ContentHubItem
                ? null
                : ContentSyncStatus.New;
        }

        // Unpublishing keeps LastPublishedWhen, so a publish-state difference is invisible to the
        // timestamp rule below and has to be checked first.
        // A draft on the target has no published version there either.
        if (localUnpublished != ContentInventoryVersionStatus.HasNoPublishedVersion(remote!.VersionStatus))
        {
            return localUnpublished ? ContentSyncStatus.Unpublished : ContentSyncStatus.Changed;
        }

        if (IsPublishedMoreRecently(local.LastPublishedWhen, remote.LastPublishedWhen))
        {
            return ContentSyncStatus.Changed;
        }

        // Moving a page changes its tree path without a new version or publish date.
        if (local.Kind == ContentInventoryItemKind.WebPage
            && local.TreePath is not null
            && remote.TreePath is not null
            && !string.Equals(local.TreePath, remote.TreePath, StringComparison.OrdinalIgnoreCase))
        {
            return ContentSyncStatus.Moved;
        }

        return ContentSyncStatus.InSync;
    }

    // Both timestamps are UTC (see ContentInventoryTime). No local date means nothing to push.
    private static bool IsPublishedMoreRecently(DateTime? localPublished, DateTime? remotePublished) =>
        localPublished is not null && (remotePublished is null || localPublished.Value > remotePublished.Value);

    // Reordering pages doesn't change their publish dates either. Order values themselves can't be
    // compared directly: a page that exists on only one side shifts every later sibling's value. So
    // for each parent, the pages present on both sides are compared by their relative order, and if
    // it differs, every in-sync page on that level is marked — Content Sync needs all pages on the
    // level synced to transfer the order. Each marked page also says which pages are out of place,
    // so editors see what changed. Order is null from targets on schema version 1.
    //
    // Pages sharing an order value on the target count as out of order: a partial sync leaves such
    // ties (each synced page brings its own value, the others keep theirs), and Kentico then shows
    // them in whatever order the database returns them (seen on the rig: two tied pages showed in
    // the opposite order to the source). A GUID tie-break would hide that.
    private static List<ContentSyncStatusItem> MarkReorderedSiblings(List<ContentSyncStatusItem> results)
    {
        var levels = results
            .Where(item => item.Local is { Kind: ContentInventoryItemKind.WebPage, TreePath: not null, Order: not null }
                && item.Remote is { TreePath: not null, Order: not null }
                && item.Status != ContentSyncStatus.Moved)
            .GroupBy(item => ParentPath(item.Local!.TreePath!), StringComparer.OrdinalIgnoreCase);

        var reordered = new Dictionary<Guid, ContentSyncReorder>();

        // The parent page's name for messages, when it's in the inventory (a folder or an unpublished
        // parent isn't).
        var namesByPath = results
            .Where(item => item.Local?.TreePath is not null)
            .GroupBy(item => item.Local!.TreePath!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Local!, StringComparer.OrdinalIgnoreCase);

        foreach (var level in levels)
        {
            var localOrder = level.OrderBy(item => item.Local!.Order).ThenBy(item => item.Guid).ToList();
            var misplaced = FindMisplaced(localOrder, item => item.Remote!.Order!.Value);

            if (misplaced.Count == 0)
            {
                continue;
            }

            var reorder = new ContentSyncReorder(level.Key, [.. misplaced.Select(item => item.Local!)])
            {
                Parent = namesByPath.GetValueOrDefault(level.Key),
            };
            foreach (var item in level.Where(item => item.Status == ContentSyncStatus.InSync))
            {
                reordered[item.Guid] = reorder;
            }
        }

        if (reordered.Count == 0)
        {
            return results;
        }

        return [.. results.Select(item => reordered.TryGetValue(item.Guid, out var reorder)
            ? item with { Status = ContentSyncStatus.Reordered, Reorder = reorder }
            : item)];
    }

    // The fewest pages to take out so the rest are in the same order on both sides: the pages
    // outside a longest strictly increasing run of target order values, taken in local order. Pages
    // tied on the target can't both be in place, so all but one of a tied group are returned.
    // Returned in local order; with several equally short answers, the same one every time (the
    // local order has a GUID tie-break). Empty when the level is in the same order.
    internal static IReadOnlyList<ContentSyncStatusItem> FindMisplaced(
        IReadOnlyList<ContentSyncStatusItem> localOrder, Func<ContentSyncStatusItem, int> remoteOrderValue)
    {
        int[] positions = [.. localOrder.Select(remoteOrderValue)];

        // Patience sorting: tails[k] is the index (into positions) ending the best run of length k+1.
        var tails = new List<int>();
        int[] previous = new int[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            int low = 0;
            int high = tails.Count;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (positions[tails[middle]] < positions[i])
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            previous[i] = low > 0 ? tails[low - 1] : -1;
            if (low == tails.Count)
            {
                tails.Add(i);
            }
            else
            {
                tails[low] = i;
            }
        }

        var kept = new HashSet<int>();
        for (int i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = previous[i])
        {
            kept.Add(i);
        }

        return [.. localOrder.Where((_, index) => !kept.Contains(index))];
    }

    private static string ParentPath(string treePath)
    {
        int lastSlash = treePath.LastIndexOf('/');
        return lastSlash <= 0 ? string.Empty : treePath[..lastSlash];
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
