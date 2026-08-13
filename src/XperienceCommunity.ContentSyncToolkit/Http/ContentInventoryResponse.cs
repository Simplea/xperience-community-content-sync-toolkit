using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Wire response returned by the target instance's inventory endpoint.
/// </summary>
/// <param name="SchemaVersion">
/// Forward-compatibility hook for a source and target running different toolkit versions.
/// Always <c>1</c> in the initial version.
/// </param>
/// <param name="GeneratedAtUtc">When the target instance produced this inventory.</param>
/// <param name="Items">The requested scope's content inventory.</param>
public sealed record ContentInventoryResponse(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ContentInventoryItem> Items);
