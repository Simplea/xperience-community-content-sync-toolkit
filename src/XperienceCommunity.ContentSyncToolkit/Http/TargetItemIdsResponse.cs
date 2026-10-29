namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Wire response of the target's item ID endpoints (schema version 4 and later): the target's own IDs
/// for items the source knows by GUID, which the source needs to link to them in the target's
/// administration. See docs/specs/target-item-links.md.
/// </summary>
/// <param name="SchemaVersion">The toolkit wire schema; see <see cref="ContentInventoryResponse"/>.</param>
/// <param name="GeneratedAtUtc">When the target produced the answer.</param>
/// <param name="ScopeId">
/// The target's <c>WebsiteChannelID</c> or <c>WorkspaceID</c>; <see langword="null"/> when the
/// channel, workspace or language doesn't exist there.
/// </param>
/// <param name="Items">The requested items the target has, with their <c>WebPageItemID</c> or <c>ContentItemID</c>.</param>
public sealed record TargetItemIdsResponse(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    int? ScopeId,
    IReadOnlyList<TargetItemId> Items);

/// <summary>One item's ID on the target.</summary>
public sealed record TargetItemId(Guid Guid, int Id);

/// <summary>
/// Result of a source-side request for target item IDs. As with
/// <see cref="ContentInventoryFetchResult"/>, check <see cref="Status"/> first. A target on schema
/// version 3 or earlier has no such endpoint and answers <see cref="ContentInventoryFetchStatus.Rejected"/>.
/// </summary>
public sealed record TargetItemIdsFetchResult(ContentInventoryFetchStatus Status, int? ScopeId, IReadOnlyDictionary<Guid, int> Ids)
{
    public static TargetItemIdsFetchResult Success(int? scopeId, IReadOnlyDictionary<Guid, int> ids) =>
        new(ContentInventoryFetchStatus.Success, scopeId, ids);

    public static TargetItemIdsFetchResult Failed(ContentInventoryFetchStatus status) =>
        new(status, null, new Dictionary<Guid, int>());
}
