namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Calls the target instance's inventory endpoint. Requires Xperience's Content Sync source settings
/// (enabled, with a target URL); every implementation must fail fast with a clear error if they're
/// missing, rather than silently reporting the target as unreachable.
/// </summary>
public interface IContentInventoryClient
{
    public Task<ContentInventoryFetchResult> GetWebPagesAsync(string channelName, string languageName, CancellationToken cancellationToken);

    public Task<ContentInventoryFetchResult> GetContentHubItemsAsync(string workspaceName, string languageName, CancellationToken cancellationToken);
}
