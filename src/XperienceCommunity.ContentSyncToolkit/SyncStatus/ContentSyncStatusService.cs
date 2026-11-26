using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;

namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

internal sealed class ContentSyncStatusService(
    ILocalContentInventoryService localInventoryService,
    ILocalRequiredObjectsService localRequiredObjectsService,
    ILocalContentLinksService localContentLinksService,
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
            cancellationToken,
            (local, remote, ct) => GetMovedOnTargetAsync(workspaceName, languageName, local, remote, forceRefresh, ct));

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
        CancellationToken cancellationToken,
        Func<IReadOnlyList<ContentInventoryItem>, IReadOnlyList<ContentInventoryItem>, CancellationToken, Task<IReadOnlyList<ContentInventoryItem>>>? getElsewhere = null)
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
        if (getElsewhere is not null)
        {
            remoteItems = [.. remoteItems, .. await getElsewhere(localItems, remoteItems, cancellationToken)];
        }

        var items = ContentSyncStatusComparer.Compare(localItems, remoteItems, options.Value.ComparePageOrder);

        return new ContentSyncStatusResult(true, await AddRequiredObjectIssuesAsync(items, forceRefresh, cancellationToken));
    }

    // Only items Content Sync still has to push can be incompatible, so the target isn't asked otherwise.
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

        var linkedIssues = await GetLinkedIssuesAsync(items, check.Issues, cancellationToken);

        return [.. items.Select(item => NeedsPush(item)
            ? item with { RequiredObjectIssues = WithLinkedIssues(RequiredObjectsComparer.IssuesFor(item.Local!, check.Issues), linkedIssues.GetValueOrDefault(item.Guid)) }
            : item)];
    }

    // An issue the item has itself says it all; a linked item needing the same object adds nothing.
    private static IReadOnlyList<RequiredObjectIssue> WithLinkedIssues(
        IReadOnlyList<RequiredObjectIssue> own, IReadOnlyList<RequiredObjectIssue>? linked) =>
        linked is null
            ? own
            : [.. own, .. linked.Where(issue => own.All(ownIssue => ownIssue.Object != issue.Object))];

    // Content Sync syncs an item with everything it links to, and a page with its parents, and refuses
    // the whole sync if any of them has an incompatible content type. Only incompatible content types
    // are followed, and only when there are any, which is the exception. See
    // docs/specs/content-inventory-foundation.md, Required objects.
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<RequiredObjectIssue>>> GetLinkedIssuesAsync(
        IReadOnlyList<ContentSyncStatusItem> items, IReadOnlyList<RequiredObjectIssue> issues, CancellationToken cancellationToken)
    {
        var contentTypeIssues = issues.Where(issue => issue.Object.Kind == RequiredObjectKind.ContentType).ToList();
        var pushed = items.Where(NeedsPush).Select(item => item.Local!).ToList();
        if (contentTypeIssues.Count == 0 || pushed.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<RequiredObjectIssue>>();
        }

        string languageName = pushed[0].LanguageName;
        var graph = await localContentLinksService.GetLinkGraphAsync(languageName, pushed[0].Kind, cancellationToken);
        var pagesByPath = items
            .Select(item => item.Local)
            .OfType<ContentInventoryItem>()
            .Where(item => item.TreePath is not null)
            .DistinctBy(item => item.TreePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.TreePath!, StringComparer.OrdinalIgnoreCase);

        var causes = contentTypeIssues.ToDictionary(issue => issue, issue => graph.FindItemsLinkingTo(issue.Object.Name));
        var displayNames = await localContentLinksService.GetDisplayNamesAsync(
            languageName, [.. causes.Values.SelectMany(found => found.Values).Distinct()], cancellationToken);

        var result = new Dictionary<Guid, IReadOnlyList<RequiredObjectIssue>>();
        foreach (var item in pushed)
        {
            var linked = new List<RequiredObjectIssue>();
            foreach (var issue in contentTypeIssues)
            {
                if (graph.ItemIds.TryGetValue(item.Guid, out int itemId) && causes[issue].TryGetValue(itemId, out int causeId))
                {
                    linked.Add(issue with { ViaLinkedItem = displayNames.GetValueOrDefault(causeId, issue.Object.DisplayName) });
                }
                else if (RequiredObjectsComparer.FindParentOfType(item, issue.Object.Name, pagesByPath) is { } parent)
                {
                    linked.Add(issue with { ViaLinkedItem = parent.DisplayName ?? parent.Name ?? parent.TreePath });
                }
            }

            if (linked.Count > 0)
            {
                result[item.Guid] = linked;
            }
        }

        return result;
    }

    private static bool NeedsPush(ContentSyncStatusItem item) =>
        item.Local is not null && item.Status.NeedsSync();

    /// <summary>
    /// The items this workspace has that the target has in another workspace: Content Sync doesn't move
    /// items between workspaces, so the target keeps an item where it was first synced, and it's compared
    /// with that copy. Without this, a moved item would show New here forever, however often it's synced.
    /// A target on an older toolkit version doesn't answer, which leaves such items New, as before.
    /// See docs/specs/moved-between-workspaces.md.
    /// </summary>
    private async Task<IReadOnlyList<ContentInventoryItem>> GetMovedOnTargetAsync(
        string workspaceName, string languageName, IReadOnlyList<ContentInventoryItem> local, IReadOnlyList<ContentInventoryItem> remote,
        bool forceRefresh, CancellationToken cancellationToken)
    {
        var onTarget = remote.Select(item => item.Guid).ToHashSet();
        var missing = local.Select(item => item.Guid).Where(guid => !onTarget.Contains(guid)).Distinct().Order().ToList();
        if (missing.Count == 0)
        {
            return [];
        }

        // Keyed by the items asked about, so an item published here since is asked about too.
        string cacheKey = $"{CacheKey(ContentInventoryItemKind.ContentHubItem, workspaceName, languageName)}|elsewhere|{string.Join(',', missing).GetHashCode(StringComparison.Ordinal)}";
        if (!forceRefresh && cache.TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        var found = new List<ContentInventoryItem>();
        foreach (var batch in missing.Chunk(ContentSyncToolkitConstants.MaxItemIdGuids))
        {
            var result = await inventoryClient.GetContentHubItemsByGuidAsync(languageName, batch, cancellationToken);
            if (result.Status != ContentInventoryFetchStatus.Success)
            {
                // Unanswered: these items stay New, as before this lookup existed.
                found.Clear();
                break;
            }

            found.AddRange(result.Items.Where(item => !string.Equals(item.ScopeName, workspaceName, StringComparison.OrdinalIgnoreCase)));
        }

        cache.Set(cacheKey, found, options.Value.InventoryCacheDuration);
        return found;
    }

    private static string CacheKey(ContentInventoryItemKind kind, string scopeName, string languageName) =>
        $"{kind}|{scopeName}|{languageName}";
}
