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

    /// <summary>The target's content types, languages, website channels and workspaces.</summary>
    public Task<RequiredObjectsFetchResult> GetRequiredObjectsAsync(CancellationToken cancellationToken);

    /// <summary>The target's <c>WebPageItemID</c> for each page GUID it has, and its <c>WebsiteChannelID</c>.</summary>
    public Task<TargetItemIdsFetchResult> GetWebPageIdsAsync(
        string channelName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken);

    /// <summary>The target's <c>ContentItemID</c> for each content item GUID it has, and its <c>WorkspaceID</c>.</summary>
    public Task<TargetItemIdsFetchResult> GetContentItemIdsAsync(
        string workspaceName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken);
}
