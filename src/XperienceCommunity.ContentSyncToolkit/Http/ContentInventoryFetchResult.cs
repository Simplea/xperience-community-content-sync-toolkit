using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Http;

public enum ContentInventoryFetchStatus
{
    Success,
    Unreachable,
    Rejected,
    Error
}

/// <summary>
/// Result of a source-side attempt to fetch a target's content inventory. A failed fetch
/// (<see cref="ContentInventoryFetchStatus.Unreachable"/>, <see cref="ContentInventoryFetchStatus.Rejected"/>,
/// or <see cref="ContentInventoryFetchStatus.Error"/>) must never be treated as "target has zero
/// items" by a caller — <see cref="Items"/> is empty in every non-success case for exactly that
/// reason, and callers must check <see cref="Status"/> first.
/// </summary>
public sealed record ContentInventoryFetchResult(ContentInventoryFetchStatus Status, IReadOnlyList<ContentInventoryItem> Items)
{
    public static ContentInventoryFetchResult Success(IReadOnlyList<ContentInventoryItem> items) =>
        new(ContentInventoryFetchStatus.Success, items);

    public static ContentInventoryFetchResult Failed(ContentInventoryFetchStatus status) =>
        new(status, []);
}
