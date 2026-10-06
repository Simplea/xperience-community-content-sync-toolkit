using System.Collections.Concurrent;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Tracks a pending "bypass cache on the next load" request per tab, set by that tab's Refresh
/// header action and consumed by the very next <c>LoadData</c> call for the same tab — the client's
/// standard reload-after-command behavior guarantees that next call follows immediately. Scoped by a
/// fixed per-tab key (not per-channel or per-user): a low-stakes tradeoff for an admin convenience
/// feature — worst case, a concurrent viewer's load also gets an unrequested cache bypass.
/// </summary>
internal sealed class ContentSyncStatusRefreshRequestStore
{
    private readonly ConcurrentDictionary<string, bool> pendingRefreshes = new();

    public void RequestRefresh(string tabKey) => pendingRefreshes[tabKey] = true;

    public bool ConsumeRefreshRequest(string tabKey) => pendingRefreshes.TryRemove(tabKey, out _);
}
