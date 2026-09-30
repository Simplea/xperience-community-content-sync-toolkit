using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

internal sealed class ContentSyncStatusService(
    ILocalContentInventoryService localInventoryService,
    IContentInventoryClient inventoryClient,
    IContentInventoryCache cache,
    IOptions<ContentSyncToolkitOptions> options) : IContentSyncStatusService
{
    public Task<ContentSyncStatusResult> GetWebPageSyncStatusAsync(
        string channelName, string languageName, bool forceRefresh, CancellationToken cancellationToken) =>
        GetStatusAsync(
            ct => localInventoryService.GetWebPagesAsync(channelName, languageName, ct),
            ct => inventoryClient.GetWebPagesAsync(channelName, languageName, ct),
            CacheKey(ContentInventoryItemKind.WebPage, channelName, languageName),
            forceRefresh,
            cancellationToken);

    public Task<ContentSyncStatusResult> GetContentHubSyncStatusAsync(
        string workspaceName, string languageName, bool forceRefresh, CancellationToken cancellationToken) =>
        GetStatusAsync(
            ct => localInventoryService.GetContentHubItemsAsync(workspaceName, languageName, ct),
            ct => inventoryClient.GetContentHubItemsAsync(workspaceName, languageName, ct),
            CacheKey(ContentInventoryItemKind.ContentHubItem, workspaceName, languageName),
            forceRefresh,
            cancellationToken);

    private async Task<ContentSyncStatusResult> GetStatusAsync(
        Func<CancellationToken, Task<IReadOnlyList<ContentInventoryItem>>> getLocal,
        Func<CancellationToken, Task<ContentInventoryFetchResult>> getRemote,
        string cacheKey,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (forceRefresh || !cache.TryGet(cacheKey, out var remoteItems))
        {
            var fetchResult = await getRemote(cancellationToken);

            if (fetchResult.Status != ContentInventoryFetchStatus.Success)
            {
                // A failed fetch must never be silently diffed as "target has zero items" — skip
                // the local query entirely rather than discard its result, and report unavailable.
                return new ContentSyncStatusResult(false, []);
            }

            remoteItems = fetchResult.Items;
            cache.Set(cacheKey, remoteItems, options.Value.Source.InventoryCacheDuration);
        }

        // Local is always re-queried fresh, even when the remote half came from cache — an editor
        // who just published locally should see that reflected immediately.
        var localItems = await getLocal(cancellationToken);

        return new ContentSyncStatusResult(true, ContentSyncStatusComparer.Compare(localItems, remoteItems));
    }

    private static string CacheKey(ContentInventoryItemKind kind, string scopeName, string languageName) =>
        $"{kind}|{scopeName}|{languageName}";
}
