using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Wire response returned by the target instance's inventory endpoint.
/// </summary>
/// <param name="SchemaVersion">
/// Forward-compatibility hook for a source and target running different toolkit versions.
/// <c>1</c>: publish dates in server-local time without a time zone. <c>2</c>: publish dates in
/// UTC, plus each page's <see cref="ContentInventoryItem.Order"/>. <c>3</c>: plus
/// <see cref="ContentInventoryItem.DisplayName"/> and <see cref="ContentInventoryItem.ContentTypeDisplayName"/>,
/// and the target serves <see cref="RequiredObjectsResponse"/>. Readers accept all three.
/// </param>
/// <param name="GeneratedAtUtc">When the target instance produced this inventory.</param>
/// <param name="Items">The requested scope's content inventory.</param>
public sealed record ContentInventoryResponse(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ContentInventoryItem> Items);
