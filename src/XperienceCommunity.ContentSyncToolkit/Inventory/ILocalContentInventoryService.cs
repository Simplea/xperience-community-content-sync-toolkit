namespace XperienceCommunity.ContentSyncToolkit.Inventory;

/// <summary>
/// Enumerates this instance's own local content — web pages within a website channel, or
/// content-hub items within a workspace — into inventory items suitable for cross-instance
/// comparison. Present on every installation regardless of source/target role.
/// </summary>
public interface ILocalContentInventoryService
{
    /// <summary>
    /// Returns every web page in the given website channel and language, across the whole
    /// content tree.
    /// </summary>
    public Task<IReadOnlyList<ContentInventoryItem>> GetWebPagesAsync(
        string websiteChannelName, string languageName, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every content-hub item in the given workspace and language.
    /// </summary>
    public Task<IReadOnlyList<ContentInventoryItem>> GetContentHubItemsAsync(
        string workspaceName, string languageName, CancellationToken cancellationToken);
}
