using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

public enum ContentSyncStatus
{
    InSync,
    MissingOnTarget,
    OutOfDateOnTarget,
    ExtraOnTarget
}

/// <summary>
/// One content item's classified sync status, produced by <see cref="ContentSyncStatusComparer"/>.
/// </summary>
/// <param name="Guid">The item's identity, shared by <paramref name="Local"/> and <paramref name="Remote"/> when both are present.</param>
/// <param name="Status">The classification.</param>
/// <param name="Local">The local item, or <see langword="null"/> when the item only exists on the target (<see cref="ContentSyncStatus.ExtraOnTarget"/>).</param>
/// <param name="Remote">The remote item, or <see langword="null"/> when the item only exists locally (<see cref="ContentSyncStatus.MissingOnTarget"/>).</param>
public sealed record ContentSyncStatusItem(
    Guid Guid,
    ContentSyncStatus Status,
    ContentInventoryItem? Local,
    ContentInventoryItem? Remote);
