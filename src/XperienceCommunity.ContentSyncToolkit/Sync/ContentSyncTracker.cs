using System.Collections.Concurrent;

using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Sync;

/// <summary>A sync started from the status list, and where it is.</summary>
/// <param name="SyncGuid">Kentico's identifier for it.</param>
/// <param name="Kind">Pages or Content hub items.</param>
/// <param name="Rows">Its rows, as (GUID, language).</param>
/// <param name="UserName">Who started it.</param>
/// <param name="StartedAt">When it started (UTC).</param>
/// <param name="ScheduledRestore">When the target restores it, if scheduled.</param>
internal sealed record ContentSyncTrackedSync(
    Guid SyncGuid,
    ContentInventoryItemKind Kind,
    IReadOnlyList<(Guid Guid, string LanguageName)> Rows,
    string UserName,
    DateTimeOffset StartedAt,
    DateTime? ScheduledRestore)
{
    /// <summary>The latest progress read; <see langword="null"/> before the first poll.</summary>
    public ContentSyncProgress? Progress { get; init; }

    /// <summary>When it completed or failed (UTC); <see langword="null"/> while it runs.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    public bool IsRunning => FinishedAt is null;
}

/// <summary>
/// The syncs started from the status list on this instance, so the list can show which rows are
/// syncing and which syncs failed, and <see cref="ContentSyncProgressService"/> can follow them to the
/// end. In memory: a restart forgets them (the syncs themselves carry on, and Kentico's list of content
/// synchronizations still shows them). See docs/specs/sync-actions.md, While a sync runs.
/// </summary>
internal sealed class ContentSyncTracker(TimeProvider timeProvider)
{
    /// <summary>How long a finished sync is remembered, for the failure callout.</summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<Guid, ContentSyncTrackedSync> syncs = new();

    // Syncs started together, under one GUID that Xperience's Sync dialog follows: a selection becomes
    // one sync per language and size limit, but the dialog shows one progress.
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<Guid>> groups = new();

    /// <summary>The GUID that follows these syncs together: the sync's own when there's one.</summary>
    public Guid AddGroup(IReadOnlyList<Guid> syncGuids)
    {
        if (syncGuids.Count == 1)
        {
            return syncGuids[0];
        }

        var groupGuid = Guid.NewGuid();
        groups[groupGuid] = [.. syncGuids];
        return groupGuid;
    }

    /// <summary>The syncs a GUID from <see cref="AddGroup"/> stands for.</summary>
    public IReadOnlyList<Guid> SyncsOf(Guid groupGuid) => groups.TryGetValue(groupGuid, out var members) ? members : [groupGuid];

    public void Add(ContentSyncTrackedSync sync)
    {
        // A new sync of the same rows supersedes an earlier failure: the callout is about rows still failing.
        var rows = sync.Rows.ToHashSet();
        foreach (var earlier in syncs.Values.Where(earlier => !earlier.IsRunning && earlier.Rows.Any(rows.Contains)))
        {
            syncs.TryRemove(earlier.SyncGuid, out _);
        }

        syncs[sync.SyncGuid] = sync;
    }

    public IReadOnlyList<ContentSyncTrackedSync> Running => [.. syncs.Values.Where(sync => sync.IsRunning)];

    public void Update(Guid syncGuid, ContentSyncProgress progress)
    {
        if (syncs.TryGetValue(syncGuid, out var sync))
        {
            syncs[syncGuid] = sync with
            {
                Progress = progress,
                FinishedAt = progress.IsFinished ? timeProvider.GetUtcNow() : null,
            };
        }
    }

    /// <summary>The running sync a row is in, if any.</summary>
    public ContentSyncTrackedSync? RunningSyncOf(ContentInventoryItemKind kind, Guid guid, string languageName) =>
        syncs.Values.FirstOrDefault(sync =>
            sync.IsRunning && sync.Kind == kind
            && sync.Rows.Any(row => row.Guid == guid && string.Equals(row.LanguageName, languageName, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Failed syncs of this kind from the last 24 hours, newest first; older ones are forgotten.</summary>
    public IReadOnlyList<ContentSyncTrackedSync> RecentFailures(ContentInventoryItemKind kind)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var old in syncs.Values.Where(sync => sync.FinishedAt is { } finished && now - finished > Retention))
        {
            syncs.TryRemove(old.SyncGuid, out _);
        }

        foreach (var (groupGuid, _) in groups.Where(group => group.Value.All(member => !syncs.ContainsKey(member))))
        {
            groups.TryRemove(groupGuid, out _);
        }

        return [.. syncs.Values
            .Where(sync => sync.Kind == kind && sync.Progress is { Failed: true })
            .OrderByDescending(sync => sync.FinishedAt)];
    }
}
