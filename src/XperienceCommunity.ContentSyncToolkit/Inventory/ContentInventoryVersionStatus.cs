namespace XperienceCommunity.ContentSyncToolkit.Inventory;

/// <summary>
/// The <see cref="ContentInventoryItem.VersionStatus"/> values the toolkit acts on. The wire
/// contract carries the status as a plain string, not Kentico's enum, so instances on different
/// versions can compare.
/// </summary>
internal static class ContentInventoryVersionStatus
{
    public const string Published = "Published";

    public const string Unpublished = "Unpublished";

    /// <summary>
    /// A draft of an item that was published before but has no published version now: unpublished,
    /// then edited again (Kentico's Draft (Initial) with a last publish date). Content Sync can't sync
    /// it until it's published again. Not Kentico's "Draft", which is a new version of an item that's
    /// still published.
    /// </summary>
    public const string UnpublishedDraft = "UnpublishedDraft";

    /// <summary>
    /// An item never published (Kentico's Draft (Initial) without a last publish date, or a workflow
    /// step before the first publish). Content Sync can't sync it. Listed only so the comparer can
    /// tell that an item the other instance has does exist here; it's never shown on its own.
    /// </summary>
    public const string NeverPublished = "NeverPublished";

    // 30.8.0 declares VersionStatus.Archived with Unpublished's value, so an instance on that
    // version that maps the enum with ToString() may send "Archived" for an unpublished item.
    private const string ArchivedAlias = "Archived";

    /// <summary>
    /// Whether the status is Unpublished, including its 30.8.0 alias "Archived".
    /// </summary>
    public static bool IsUnpublished(string? versionStatus) =>
        string.Equals(versionStatus, Unpublished, StringComparison.OrdinalIgnoreCase)
        || string.Equals(versionStatus, ArchivedAlias, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the status is <see cref="UnpublishedDraft"/>: no published version, and not syncable.</summary>
    public static bool IsUnpublishedDraft(string? versionStatus) =>
        string.Equals(versionStatus, UnpublishedDraft, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the status is <see cref="NeverPublished"/>.</summary>
    public static bool IsNeverPublished(string? versionStatus) =>
        string.Equals(versionStatus, NeverPublished, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the item has no published version: unpublished, or a draft after unpublishing.</summary>
    public static bool HasNoPublishedVersion(string? versionStatus) => IsUnpublished(versionStatus) || IsUnpublishedDraft(versionStatus);
}
