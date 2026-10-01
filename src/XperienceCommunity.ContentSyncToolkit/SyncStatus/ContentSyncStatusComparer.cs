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

            var classification = Classify(hasLocal, hasRemote, localItem, remoteItem);
            if (classification is { } c)
            {
                results.Add(new ContentSyncStatusItem(guid, c.Status, localItem, remoteItem) { Reason = c.Reason });
            }
        }

        return MarkReorderedSiblings(results);
    }

    // Null means the item is left out: there's nothing Content Sync would do with it.
    private static (ContentSyncStatus Status, ContentSyncStatusReason Reason)? Classify(
        bool hasLocal, bool hasRemote, ContentInventoryItem? local, ContentInventoryItem? remote)
    {
        if (!hasLocal)
        {
            return (ContentSyncStatus.ExtraOnTarget, ContentSyncStatusReason.None);
        }

        bool localUnpublished = ContentInventoryVersionStatus.IsUnpublished(local!.VersionStatus);

        if (!hasRemote)
        {
            // Content Sync creates an unpublished page on the target, but makes no change for a new
            // unpublished content-hub item.
            return localUnpublished && local.Kind == ContentInventoryItemKind.ContentHubItem
                ? null
                : (ContentSyncStatus.MissingOnTarget, ContentSyncStatusReason.None);
        }

        // Unpublishing keeps LastPublishedWhen, so a publish-state difference is invisible to the
        // timestamp rule below and has to be checked first.
        if (localUnpublished != ContentInventoryVersionStatus.IsUnpublished(remote!.VersionStatus))
        {
            return (ContentSyncStatus.OutOfDateOnTarget, ContentSyncStatusReason.PublishStateDiffers);
        }

        if (IsPublishedMoreRecently(local.LastPublishedWhen, remote.LastPublishedWhen))
        {
            return (ContentSyncStatus.OutOfDateOnTarget, ContentSyncStatusReason.PublishedMoreRecently);
        }

        // Moving a page changes its tree path without a new version or publish date.
        if (local.Kind == ContentInventoryItemKind.WebPage
            && local.TreePath is not null
            && remote.TreePath is not null
            && !string.Equals(local.TreePath, remote.TreePath, StringComparison.OrdinalIgnoreCase))
        {
            return (ContentSyncStatus.OutOfDateOnTarget, ContentSyncStatusReason.Moved);
        }

        return (ContentSyncStatus.InSync, ContentSyncStatusReason.None);
    }

    // Both timestamps are UTC (see ContentInventoryTime). No local date means nothing to push.
    private static bool IsPublishedMoreRecently(DateTime? localPublished, DateTime? remotePublished) =>
        localPublished is not null && (remotePublished is null || localPublished.Value > remotePublished.Value);

    // Reordering pages doesn't change their publish dates either. Order values themselves can't be
    // compared directly: a page that exists on only one side shifts every later sibling's value. So
    // for each parent, the pages present on both sides are compared by their relative order, and if
    // it differs, every in-sync page on that level is marked — Content Sync needs all pages on the
    // level synced to transfer the order. Order is null from targets on schema version 1.
    private static List<ContentSyncStatusItem> MarkReorderedSiblings(List<ContentSyncStatusItem> results)
    {
        var levels = results
            .Where(item => item.Local is { Kind: ContentInventoryItemKind.WebPage, TreePath: not null, Order: not null }
                && item.Remote is { TreePath: not null, Order: not null }
                && item.Reason != ContentSyncStatusReason.Moved)
            .GroupBy(item => ParentPath(item.Local!.TreePath!), StringComparer.OrdinalIgnoreCase);

        var reordered = new HashSet<Guid>();

        foreach (var level in levels)
        {
            var localOrder = level.OrderBy(item => item.Local!.Order).ThenBy(item => item.Guid).Select(item => item.Guid);
            var remoteOrder = level.OrderBy(item => item.Remote!.Order).ThenBy(item => item.Guid).Select(item => item.Guid);

            if (!localOrder.SequenceEqual(remoteOrder))
            {
                reordered.UnionWith(level.Where(item => item.Status == ContentSyncStatus.InSync).Select(item => item.Guid));
            }
        }

        if (reordered.Count == 0)
        {
            return results;
        }

        return [.. results.Select(item => reordered.Contains(item.Guid)
            ? item with { Status = ContentSyncStatus.OutOfDateOnTarget, Reason = ContentSyncStatusReason.Reordered }
            : item)];
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
