using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Sync;

/// <summary>
/// Whether a row can be synced, and if not, why: <paramref name="Reason"/> in a sentence for its disabled
/// Sync button, <paramref name="Summary"/> in a few words for a command's "Skipped" count.
/// </summary>
internal readonly record struct ContentSyncAvailability(bool CanSync, string? Reason, string? Summary)
{
    public static readonly ContentSyncAvailability Available = new(true, null, null);

    public static ContentSyncAvailability Unavailable(string reason, string summary) => new(false, reason, summary);
}

/// <summary>
/// Which rows the Sync actions may send, and how they're grouped into syncs. The same rules decide the
/// buttons and, again on the server, what a command actually sends. See docs/specs/sync-actions.md,
/// Which rows can be synced.
/// </summary>
internal static class ContentSyncRules
{
    /// <summary>Kentico's recommended maximum of pages per sync.</summary>
    public const int MaxPagesPerSync = 200;

    /// <summary>Kentico's recommended maximum of content items per sync.</summary>
    public const int MaxItemsPerSync = 1000;

    internal const string NothingToSync = "Nothing to sync.";
    internal const string NoPublishedVersion = "No published version to sync: publish it first.";
    internal const string NotOnThisInstance = "It isn't on this instance.";
    internal const string Incompatible = "It can't be synced until a developer deploys what the target is missing (see its status).";

    // The short forms, for "Skipped 3: 2 in sync, 1 incompatible".
    internal const string InSyncSummary = "in sync";
    internal const string NotPublishedSummary = "not published";
    internal const string OnlyOnTargetSummary = "only on the target";
    internal const string MovedSummary = "moved to another workspace";
    internal const string IncompatibleSummary = "incompatible";
    internal const string NoPermissionSummary = "without the Synchronize permission";
    internal const string AlreadySyncingSummary = "already syncing";

    /// <param name="item">The row's comparison.</param>
    /// <param name="canSynchronize">Whether the user has the Synchronize permission for it.</param>
    /// <param name="movedTo">For an item only on the target that moved to another workspace here, where it is now.</param>
    /// <param name="scopeNoun">"page" or "workspace", for the permission message.</param>
    public static ContentSyncAvailability Evaluate(ContentSyncStatusItem item, bool canSynchronize, ContentSyncMovedItem? movedTo, string scopeNoun)
    {
        if (item.Local is null)
        {
            return ContentSyncAvailability.Unavailable(OnlyOnTargetReason(movedTo), movedTo is null ? OnlyOnTargetSummary : MovedSummary);
        }

        if (item.Status == ContentSyncStatus.NotPublished)
        {
            return ContentSyncAvailability.Unavailable(NoPublishedVersion, NotPublishedSummary);
        }

        if (!item.Status.NeedsSync())
        {
            return ContentSyncAvailability.Unavailable(NothingToSync, InSyncSummary);
        }

        if (item.HasCompatibilityIssues)
        {
            return ContentSyncAvailability.Unavailable(Incompatible, IncompatibleSummary);
        }

        return canSynchronize
            ? ContentSyncAvailability.Available
            : ContentSyncAvailability.Unavailable($"You don't have the Synchronize permission for this {scopeNoun}.", NoPermissionSummary);
    }

    // An item only on the target can't be synced from here. One that moved to another workspace here
    // syncs its changes from there, but a sync doesn't move it on the target: Content Sync doesn't move
    // items between workspaces (see docs/specs/moved-between-workspaces.md).
    private static string OnlyOnTargetReason(ContentSyncMovedItem? movedTo)
    {
        if (movedTo is null)
        {
            return NotOnThisInstance;
        }

        return movedTo.WorkspaceDisplayName is { } workspace
            ? $"Its changes sync from {workspace}. A sync doesn't move it: move it to {workspace} on the target."
            : "Its changes sync from the workspace it moved to. A sync doesn't move it: move it there on the target.";
    }

    /// <summary>
    /// Splits what's to be sent into syncs: one language each, as Content Sync syncs one language at a
    /// time, and no more than Kentico's recommended size. Order is kept within each language.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<T>> Batches<T>(IEnumerable<T> items, Func<T, string> languageOf, ContentInventoryItemKind kind)
    {
        int size = kind == ContentInventoryItemKind.WebPage ? MaxPagesPerSync : MaxItemsPerSync;

        return [.. items
            .GroupBy(languageOf, StringComparer.OrdinalIgnoreCase)
            .SelectMany(language => language.Chunk(size))
            .Select(chunk => (IReadOnlyList<T>)chunk)];
    }

    /// <summary>
    /// For the Reordered pages being sent, the other pages on their level that this instance has, so the
    /// level's whole order goes across (Kentico's guidance: sync all pages on the same level). Pages that
    /// can't be sent (no published version, incompatible) and those already included are left out.
    /// </summary>
    /// <param name="sending">The pages being sent.</param>
    /// <param name="comparison">Every page of the same channel and language, as compared.</param>
    public static IReadOnlyList<ContentSyncStatusItem> SiblingsToInclude(
        IReadOnlyCollection<ContentSyncStatusItem> sending, IEnumerable<ContentSyncStatusItem> comparison)
    {
        var levels = sending
            .Where(item => item.Status == ContentSyncStatus.Reordered && item.Local?.TreePath is not null)
            .Select(item => ContentSyncStatusComparer.ParentPath(item.Local!.TreePath!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (levels.Count == 0)
        {
            return [];
        }

        var included = sending.Select(item => item.Guid).ToHashSet();

        return [.. comparison.Where(item =>
            item.Local?.TreePath is { } path
            && levels.Contains(ContentSyncStatusComparer.ParentPath(path))
            && !included.Contains(item.Guid)
            && item.Status != ContentSyncStatus.NotPublished
            && !item.HasCompatibilityIssues)];
    }

    /// <summary>
    /// The levels (parent paths) where the new pages being sent join pages the target already has, so the
    /// target's order there changes and those pages will show Reordered. For the warning; only relevant
    /// when page order is compared.
    /// </summary>
    public static IReadOnlyList<string> LevelsReorderedByNewPages(
        IReadOnlyCollection<ContentSyncStatusItem> sending, IEnumerable<ContentSyncStatusItem> comparison)
    {
        var newLevels = sending
            .Where(item => item.Status == ContentSyncStatus.New && item.Local?.TreePath is not null)
            .Select(item => ContentSyncStatusComparer.ParentPath(item.Local!.TreePath!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sent = sending.Select(item => item.Guid).ToHashSet();

        return [.. comparison
            .Where(item => item.Remote?.TreePath is not null && !sent.Contains(item.Guid))
            .Select(item => ContentSyncStatusComparer.ParentPath(item.Remote!.TreePath!))
            .Where(newLevels.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A row identifier, "{guid}_{language}", split; <see langword="null"/> for anything else.</summary>
    public static (Guid Guid, string LanguageName)? ParseRowId(string rowId)
    {
        int split = rowId.LastIndexOf('_');
        return split > 0 && split < rowId.Length - 1 && Guid.TryParse(rowId[..split], out var guid)
            ? (guid, rowId[(split + 1)..])
            : null;
    }

    public static string RowId(Guid guid, string languageName) => $"{guid}_{languageName}";

    /// <summary>
    /// How an instance is named to editors: its host, with the port when the address has one (two
    /// instances on one host, as on a developer's machine, differ only by port).
    /// </summary>
    public static string TargetName(Uri? url)
    {
        if (url is null)
        {
            return "the target";
        }

        return url.IsDefaultPort ? url.Host : url.Authority;
    }

    /// <summary>An instance as editors read it: its label with the host, or the host alone.</summary>
    public static string InstanceName(string host, string? label) =>
        string.IsNullOrWhiteSpace(label) ? host : $"{label.Trim()} ({host})";

    /// <summary>
    /// When a scheduled sync restores, in the server's time zone with its offset, as Xperience's own
    /// dialog shows it: "11/20/2026 8:02 PM (UTC-05:00)". Xperience's dialog sends the time in UTC; the
    /// toolkit's own confirmation sends the server's local time.
    /// </summary>
    public static string RestoreTimeText(DateTime when, TimeZoneInfo zone)
    {
        var local = when.Kind == DateTimeKind.Utc ? TimeZoneInfo.ConvertTimeFromUtc(when, zone) : DateTime.SpecifyKind(when, DateTimeKind.Unspecified);
        var offset = zone.GetUtcOffset(local);
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"{local:g} (UTC{sign}{offset.Duration():hh}:{offset.Duration():mm})";
    }
}
