namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

/// <summary>
/// Orchestrates local inventory, the cached/fetched remote inventory, and the comparer. The only
/// component the toolkit's editor-facing features depend on directly.
/// </summary>
public interface IContentSyncStatusService
{
    public Task<ContentSyncStatusResult> GetWebPageSyncStatusAsync(
        string channelName, string languageName, CancellationToken cancellationToken);

    public Task<ContentSyncStatusResult> GetContentHubSyncStatusAsync(
        string workspaceName, string languageName, CancellationToken cancellationToken);
}
