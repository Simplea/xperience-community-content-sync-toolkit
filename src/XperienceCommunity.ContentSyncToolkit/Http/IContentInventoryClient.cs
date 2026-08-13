namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Calls a configured target instance's inventory endpoint. Requires <see cref="ContentSyncToolkitSourceOptions.TargetUrl"/>
/// to be configured; every implementation must fail fast with a clear error if it is not, rather
/// than silently reporting the target as unreachable.
/// </summary>
public interface IContentInventoryClient
{
    public Task<ContentInventoryFetchResult> GetWebPagesAsync(string channelName, string languageName, CancellationToken cancellationToken);

    public Task<ContentInventoryFetchResult> GetContentHubItemsAsync(string workspaceName, string languageName, CancellationToken cancellationToken);
}
