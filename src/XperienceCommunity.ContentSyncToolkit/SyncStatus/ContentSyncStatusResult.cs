namespace XperienceCommunity.ContentSyncToolkit.SyncStatus;

/// <summary>
/// Result of a sync status request. When <see cref="TargetAvailable"/> is <see langword="false"/>,
/// <see cref="Items"/> is always empty — a failed remote fetch must never be represented as "every
/// local item is missing on target."
/// </summary>
public sealed record ContentSyncStatusResult(bool TargetAvailable, IReadOnlyList<ContentSyncStatusItem> Items);
