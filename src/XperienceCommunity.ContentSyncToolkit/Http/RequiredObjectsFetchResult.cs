using XperienceCommunity.ContentSyncToolkit.RequiredObjects;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Result of a source-side attempt to fetch the target's required objects. As with
/// <see cref="ContentInventoryFetchResult"/>, <see cref="Objects"/> is empty in every non-success
/// case and must never be read as "the target has none". A target on schema version 2 or earlier
/// has no such endpoint and answers <see cref="ContentInventoryFetchStatus.Rejected"/>.
/// </summary>
public sealed record RequiredObjectsFetchResult(ContentInventoryFetchStatus Status, IReadOnlyList<RequiredObject> Objects)
{
    public static RequiredObjectsFetchResult Success(IReadOnlyList<RequiredObject> objects) =>
        new(ContentInventoryFetchStatus.Success, objects);

    public static RequiredObjectsFetchResult Failed(ContentInventoryFetchStatus status) =>
        new(status, []);
}
