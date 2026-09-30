namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

/// <summary>
/// Orchestrates local inventory, the cached/fetched remote inventory, and the comparer. The only
/// component the toolkit's editor-facing features depend on directly.
/// </summary>
public interface IContentSyncStatusService
{
    /// <summary>
    /// Equivalent to <see cref="GetWebPageSyncStatusAsync(string,string,bool,CancellationToken)"/>
    /// with <c>forceRefresh: false</c>.
    /// </summary>
    public Task<ContentSyncStatusResult> GetWebPageSyncStatusAsync(
        string channelName, string languageName, CancellationToken cancellationToken) =>
        GetWebPageSyncStatusAsync(channelName, languageName, forceRefresh: false, cancellationToken);

    /// <param name="channelName">Website channel name.</param>
    /// <param name="languageName">Language variant to check.</param>
    /// <param name="forceRefresh">
    /// When <see langword="true"/>, bypasses a cached remote fetch for this call only and re-primes
    /// the cache with the fresh result afterward, using the existing configured TTL — it does not
    /// change the TTL duration for other callers.
    /// </param>
    /// <param name="cancellationToken">Cancellation instruction.</param>
    public Task<ContentSyncStatusResult> GetWebPageSyncStatusAsync(
        string channelName, string languageName, bool forceRefresh, CancellationToken cancellationToken);

    /// <summary>
    /// Equivalent to <see cref="GetContentHubSyncStatusAsync(string,string,bool,CancellationToken)"/>
    /// with <c>forceRefresh: false</c>.
    /// </summary>
    public Task<ContentSyncStatusResult> GetContentHubSyncStatusAsync(
        string workspaceName, string languageName, CancellationToken cancellationToken) =>
        GetContentHubSyncStatusAsync(workspaceName, languageName, forceRefresh: false, cancellationToken);

    /// <param name="workspaceName">Content-hub workspace name.</param>
    /// <param name="languageName">Language variant to check.</param>
    /// <param name="forceRefresh">
    /// When <see langword="true"/>, bypasses a cached remote fetch for this call only and re-primes
    /// the cache with the fresh result afterward, using the existing configured TTL — it does not
    /// change the TTL duration for other callers.
    /// </param>
    /// <param name="cancellationToken">Cancellation instruction.</param>
    public Task<ContentSyncStatusResult> GetContentHubSyncStatusAsync(
        string workspaceName, string languageName, bool forceRefresh, CancellationToken cancellationToken);
}
