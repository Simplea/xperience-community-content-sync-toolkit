using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;

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
    ContentInventoryItem? Remote)
{
    /// <summary>
    /// Why the item is <see cref="ContentSyncStatus.OutOfDateOnTarget"/>; <see cref="ContentSyncStatusReason.None"/>
    /// for every other status.
    /// </summary>
    public ContentSyncStatusReason Reason { get; init; }

    /// <summary>
    /// For an item Content Sync still has to push (<see cref="ContentSyncStatus.MissingOnTarget"/> or
    /// <see cref="ContentSyncStatus.OutOfDateOnTarget"/>), the objects it needs that the target is
    /// missing or has differently, so a sync of it would fail. Empty when there are none, for every
    /// other status, and when the target couldn't be checked.
    /// </summary>
    public IReadOnlyList<RequiredObjectIssue> RequiredObjectIssues { get; init; } = [];
}

/// <summary>Why an item is <see cref="ContentSyncStatus.OutOfDateOnTarget"/>.</summary>
public enum ContentSyncStatusReason
{
    None,

    /// <summary>Published on this instance after the target's copy.</summary>
    PublishedMoreRecently,

    /// <summary>Published on one instance and unpublished on the other.</summary>
    PublishStateDiffers,

    /// <summary>The page's tree path differs: moved to another parent on one instance.</summary>
    Moved,

    /// <summary>The page's position among its siblings differs.</summary>
    Reordered,
}
