using XperienceCommunity.ContentSyncToolkit.RequiredObjects;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Wire response of the target's required-objects endpoint (schema version 3 and later).
/// </summary>
/// <param name="SchemaVersion">The toolkit wire schema; see <see cref="ContentInventoryResponse"/>.</param>
/// <param name="GeneratedAtUtc">When the target produced this list.</param>
/// <param name="Objects">The target's content types, languages, website channels and workspaces.</param>
public sealed record RequiredObjectsResponse(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<RequiredObject> Objects);
