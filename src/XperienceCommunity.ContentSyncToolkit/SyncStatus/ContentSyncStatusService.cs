using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;

namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

internal sealed class ContentSyncStatusService(
    ILocalContentInventoryService localInventoryService,
    ILocalRequiredObjectsService localRequiredObjectsService,
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

    public async Task<RequiredObjectsCheckResult> CheckRequiredObjectsAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (forceRefresh || !cache.TryGetRequiredObjects(out var remoteObjects))
        {
            var fetchResult = await inventoryClient.GetRequiredObjectsAsync(cancellationToken);

            // Unknown is not "nothing missing": the caller is told the check didn't happen.
            if (fetchResult.Status != ContentInventoryFetchStatus.Success)
            {
                return RequiredObjectsCheckResult.NotChecked;
            }

            remoteObjects = fetchResult.Objects;
            cache.SetRequiredObjects(remoteObjects, options.Value.InventoryCacheDuration);
        }

        var localObjects = await localRequiredObjectsService.GetRequiredObjectsAsync(cancellationToken);

        return new RequiredObjectsCheckResult(true, RequiredObjectsComparer.Compare(localObjects, remoteObjects));
    }

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
            cache.Set(cacheKey, remoteItems, options.Value.InventoryCacheDuration);
        }

        // Local is always re-queried fresh, even when the remote half came from cache — an editor
        // who just published locally should see that reflected immediately.
        var localItems = await getLocal(cancellationToken);
        var items = ContentSyncStatusComparer.Compare(localItems, remoteItems);

        return new ContentSyncStatusResult(true, await AddRequiredObjectIssuesAsync(items, forceRefresh, cancellationToken));
    }

    // Only items Content Sync still has to push can be blocked, so the target isn't asked otherwise.
    private async Task<IReadOnlyList<ContentSyncStatusItem>> AddRequiredObjectIssuesAsync(
        IReadOnlyList<ContentSyncStatusItem> items, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!items.Any(NeedsPush))
        {
            return items;
        }

        var check = await CheckRequiredObjectsAsync(forceRefresh, cancellationToken);
        if (check.Issues.Count == 0)
        {
            return items;
        }

        return [.. items.Select(item => NeedsPush(item)
            ? item with { RequiredObjectIssues = RequiredObjectsComparer.IssuesFor(item.Local!, check.Issues) }
            : item)];
    }

    private static bool NeedsPush(ContentSyncStatusItem item) =>
        item.Local is not null && item.Status is ContentSyncStatus.MissingOnTarget or ContentSyncStatus.OutOfDateOnTarget;

    private static string CacheKey(ContentInventoryItemKind kind, string scopeName, string languageName) =>
        $"{kind}|{scopeName}|{languageName}";
}
