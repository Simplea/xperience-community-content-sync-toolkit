using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;

namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

/// <summary>
/// What differs between this instance and the target for one item, in the terms of what a sync
/// would do. See docs/specs/content-inventory-foundation.md, Comparison rules.
/// </summary>
public enum ContentSyncStatus
{
    /// <summary>The target has the same published version, publish state and position.</summary>
    InSync,

    /// <summary>On this instance, not on the target yet. A sync creates it.</summary>
    New,

    /// <summary>
    /// The target's copy is out of date: published on this instance after it, or unpublished there
    /// while published here. A sync updates (and publishes) it.
    /// </summary>
    Changed,

    /// <summary>Unpublished on this instance, still published on the target. A sync unpublishes it there.</summary>
    Unpublished,

    /// <summary>A page at a different place in the tree on the target. Syncing its old and new levels moves it.</summary>
    Moved,

    /// <summary>A page on a level whose order differs on the target. Syncing the whole level reorders it.</summary>
    Reordered,

    /// <summary>On the target only. Content Sync can't delete, so it has to be deleted on the target by hand.</summary>
    OnlyOnTarget,

    /// <summary>
    /// Has no published version on this instance (unpublished and then edited again, or never
    /// published), while the target has it or it was published here before. Content Sync can't sync
    /// it until it's published.
    /// </summary>
    NotPublished,
}

public static class ContentSyncStatusExtensions
{
    /// <summary>Whether a sync from this instance would change the item on the target.</summary>
    public static bool NeedsSync(this ContentSyncStatus status) =>
        status is not ContentSyncStatus.InSync and not ContentSyncStatus.OnlyOnTarget and not ContentSyncStatus.NotPublished;
}

/// <summary>
/// One content item's classified sync status, produced by <see cref="ContentSyncStatusComparer"/>.
/// </summary>
/// <param name="Guid">The item's identity, shared by <paramref name="Local"/> and <paramref name="Remote"/> when both are present.</param>
/// <param name="Status">The classification.</param>
/// <param name="Local">The local item, or <see langword="null"/> when the item only exists on the target (<see cref="ContentSyncStatus.OnlyOnTarget"/>).</param>
/// <param name="Remote">The remote item, or <see langword="null"/> when the item only exists locally (<see cref="ContentSyncStatus.New"/>).</param>
public sealed record ContentSyncStatusItem(
    Guid Guid,
    ContentSyncStatus Status,
    ContentInventoryItem? Local,
    ContentInventoryItem? Remote)
{
    /// <summary>
    /// For an item that needs a sync (<see cref="ContentSyncStatusExtensions.NeedsSync"/>), the objects
    /// it needs that the target is missing or has differently, so a sync of it would fail. Empty when
    /// there are none, for every other status, and when the target couldn't be checked.
    /// </summary>
    public IReadOnlyList<RequiredObjectIssue> RequiredObjectIssues { get; init; } = [];

    /// <summary>
    /// Whether a sync of this item would fail until a developer deploys the objects in
    /// <see cref="RequiredObjectIssues"/> to the target. Independent of <see cref="Status"/>, which
    /// still says what the sync would do once it can run; the admin page shows an incompatible item as
    /// Incompatible instead of its status.
    /// </summary>
    public bool HasCompatibilityIssues => RequiredObjectIssues.Count > 0;

    /// <summary>
    /// For a <see cref="ContentSyncStatus.Reordered"/> item, its level and the pages on it that are
    /// out of place on the target; <see langword="null"/> otherwise.
    /// </summary>
    public ContentSyncReorder? Reorder { get; init; }
}

/// <summary>A level of the page tree whose pages are in a different order on the target.</summary>
/// <param name="ParentPath">The tree path of the level's parent page; empty for the channel's top level.</param>
/// <param name="MisplacedPages">
/// The fewest pages (this instance's copies, in this instance's order) whose positions differ: with
/// them left out, the rest of the level is in the same order on both sides.
/// </param>
public sealed record ContentSyncReorder(string ParentPath, IReadOnlyList<ContentInventoryItem> MisplacedPages)
{
    /// <summary>The parent page, when it's in this instance's inventory, for its display name.</summary>
    public ContentInventoryItem? Parent { get; init; }

    /// <summary>
    /// Pages out of place that aren't in <see cref="MisplacedPages"/> because the signed-in user
    /// can't see them; counted so the message stays true without naming them.
    /// </summary>
    public int HiddenMisplacedCount { get; init; }
}
