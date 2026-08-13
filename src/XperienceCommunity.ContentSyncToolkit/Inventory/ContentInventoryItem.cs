namespace XperienceCommunity.ContentSyncToolkit.Inventory;

/// <summary>
/// Distinguishes a website channel page from a content-hub reusable item within an inventory.
/// </summary>
public enum ContentInventoryItemKind
{
    WebPage,
    ContentHubItem
}

/// <summary>
/// A single content item's identity and sync-relevant metadata, as returned by either a local
/// inventory query or a target instance's inventory endpoint. Deliberately carries no field
/// values or other content data.
/// </summary>
/// <param name="Guid">The item's <c>WebPageItemGUID</c> (pages) or <c>ContentItemGUID</c> (content-hub items).</param>
/// <param name="Kind">Whether this is a website channel page or a content-hub item.</param>
/// <param name="ContentTypeName">The item's content type code name.</param>
/// <param name="ScopeName">The website channel name (pages) or workspace name (content-hub items) the item belongs to.</param>
/// <param name="LanguageName">The language variant this item's data represents.</param>
/// <param name="TreePath">The page's content tree path. Always <see langword="null"/> for content-hub items.</param>
/// <param name="LastPublishedWhen">The item's last publish timestamp (UTC), or <see langword="null"/> if it has never been published.</param>
/// <param name="VersionStatus">
/// A plain-string representation of the item's version status. Not Kentico's <c>VersionStatus</c>
/// enum, so this wire contract does not couple to Kentico's internal type layout across instances
/// that may run different toolkit or Xperience versions.
/// </param>
public sealed record ContentInventoryItem(
    Guid Guid,
    ContentInventoryItemKind Kind,
    string ContentTypeName,
    string ScopeName,
    string LanguageName,
    string? TreePath,
    DateTime? LastPublishedWhen,
    string? VersionStatus);
