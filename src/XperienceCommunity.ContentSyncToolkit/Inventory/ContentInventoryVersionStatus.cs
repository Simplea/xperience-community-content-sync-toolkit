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

    // 30.8.0 declares VersionStatus.Archived with Unpublished's value, so an instance on that
    // version that maps the enum with ToString() may send "Archived" for an unpublished item.
    private const string ArchivedAlias = "Archived";

    /// <summary>
    /// Whether the status is Unpublished, including its 30.8.0 alias "Archived".
    /// </summary>
    public static bool IsUnpublished(string? versionStatus) =>
        string.Equals(versionStatus, Unpublished, StringComparison.OrdinalIgnoreCase)
        || string.Equals(versionStatus, ArchivedAlias, StringComparison.OrdinalIgnoreCase);
}
