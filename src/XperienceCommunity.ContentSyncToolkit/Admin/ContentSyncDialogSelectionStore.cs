using System.Collections.Concurrent;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// The rows an editor opened Xperience's Sync dialog with, and which of them its review list has shown.
/// The dialog passes the rows to its first commands only: later ones carry content item IDs (pages) or
/// only the chosen dependencies (Detailed selection), and never the language, which the status list
/// keeps in its row identifiers. Its Sync sends only the ticked rows of the review pages the editor
/// opened, so a row never shown counts as ticked. See <see cref="Ticked"/>.
/// </summary>
/// <param name="rowIds">The rows, as the status list identifies them.</param>
internal sealed class ContentSyncDialogSelection(IReadOnlyList<string> rowIds)
{
    private readonly ConcurrentDictionary<string, bool> shown = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> RowIds { get; } = rowIds;

    public void MarkShown(IEnumerable<string> shownRowIds)
    {
        foreach (string rowId in shownRowIds)
        {
            shown[rowId] = true;
        }
    }

    /// <summary>
    /// The rows to sync, from the ones the dialog sent as ticked: those, and the rows its review never
    /// showed (an editor can't untick what they haven't seen). Nothing ticked sends nothing.
    /// </summary>
    /// <param name="ticked">The rows the dialog sent; <see langword="null"/> when it sent none, which means all.</param>
    public IReadOnlyList<string> Ticked(IEnumerable<string>? ticked)
    {
        if (ticked is null)
        {
            return RowIds;
        }

        var tickedSet = ticked.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (tickedSet.Count == 0)
        {
            return [];
        }

        return [.. RowIds.Where(rowId => tickedSet.Contains(rowId) || !shown.ContainsKey(rowId))];
    }
}

/// <summary>
/// Each editor's open Sync dialog, per tab. Kept in this instance's memory, like
/// <see cref="ContentSyncStatusRefreshRequestStore"/>. See docs/specs/sync-actions.md, Using Xperience's
/// own Sync dialog.
/// </summary>
internal sealed class ContentSyncDialogSelectionStore
{
    private readonly ConcurrentDictionary<(int UserId, string TabKey), ContentSyncDialogSelection> selections = new();

    /// <summary>
    /// The dialog opened with these rows. The review sends them again with each page, so the same rows
    /// keep what has been shown; different rows start a new selection.
    /// </summary>
    public ContentSyncDialogSelection Remember(int userId, string tabKey, IReadOnlyList<string> rowIds) =>
        selections.AddOrUpdate(
            (userId, tabKey),
            _ => new ContentSyncDialogSelection(rowIds),
            (_, current) => current.RowIds.SequenceEqual(rowIds, StringComparer.OrdinalIgnoreCase) ? current : new ContentSyncDialogSelection(rowIds));

    public ContentSyncDialogSelection? Get(int userId, string tabKey) =>
        selections.TryGetValue((userId, tabKey), out var selection) ? selection : null;
}
