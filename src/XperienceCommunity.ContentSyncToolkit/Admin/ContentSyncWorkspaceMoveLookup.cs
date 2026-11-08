using CMS.ContentEngine;
using CMS.Core;
using CMS.DataEngine;
using CMS.Workspaces;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>An item only on the target in one workspace that this instance has in another.</summary>
/// <param name="ContentItemGuid">The item's <c>ContentItemGUID</c>.</param>
/// <param name="WorkspaceDisplayName">Where it is on this instance; null when the user can't see that workspace.</param>
internal sealed record ContentSyncMovedItem(Guid ContentItemGuid, string? WorkspaceDisplayName);

/// <summary>One item found on this instance, before the listing's workspace, language and access are applied.</summary>
internal sealed record ContentSyncWorkspaceEntry(Guid ContentItemGuid, int ContentItemId, int WorkspaceId);

/// <summary>
/// Which items listed as only on the target this instance has in another workspace. A sync from there
/// moves them on the target, so they mustn't be deleted there. Each tab compares one workspace, so such
/// an item otherwise looks missing here.
/// </summary>
internal interface IContentSyncWorkspaceMoveLookup
{
    public Task<IReadOnlyDictionary<Guid, ContentSyncMovedItem>> GetMovedItemsAsync(
        int workspaceId, string languageName, IReadOnlyCollection<Guid> contentItemGuids, CancellationToken cancellationToken);
}

/// <summary>
/// Reads content items by object type name, as for display names: their Info classes are in an
/// Internal namespace. The hint is a convenience, so a failure leaves the rows without it.
/// </summary>
internal sealed class ContentSyncWorkspaceMoveLookup(
    IContentSyncScopeAccess scopeAccess,
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider,
    IInfoProvider<WorkspaceInfo> workspaceInfoProvider,
    IEventLogService eventLogService) : IContentSyncWorkspaceMoveLookup
{
    private const string ContentItemObjectType = "cms.contentitem";
    private const string ContentItemIdColumn = "ContentItemID";
    private const string ContentItemGuidColumn = "ContentItemGUID";
    private const string ContentItemWorkspaceIdColumn = "ContentItemWorkspaceID";
    private const string LanguageMetadataObjectType = "cms.contentitemlanguagemetadata";
    private const string LanguageMetadataContentItemIdColumn = "ContentItemLanguageMetadataContentItemID";
    private const string LanguageMetadataLanguageIdColumn = "ContentItemLanguageMetadataContentLanguageID";

    public async Task<IReadOnlyDictionary<Guid, ContentSyncMovedItem>> GetMovedItemsAsync(
        int workspaceId, string languageName, IReadOnlyCollection<Guid> contentItemGuids, CancellationToken cancellationToken)
    {
        var none = new Dictionary<Guid, ContentSyncMovedItem>();
        if (contentItemGuids.Count == 0)
        {
            return none;
        }

        try
        {
            var rows = await new ObjectQuery(ContentItemObjectType)
                .Columns(ContentItemIdColumn, ContentItemGuidColumn, ContentItemWorkspaceIdColumn)
                .WhereIn(ContentItemGuidColumn, contentItemGuids.ToList())
                .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

            var entries = rows
                .Select(row => new ContentSyncWorkspaceEntry(
                    (Guid)row.GetValue(ContentItemGuidColumn),
                    Convert.ToInt32(row.GetValue(ContentItemIdColumn)),
                    Convert.ToInt32(row.GetValue(ContentItemWorkspaceIdColumn))))
                .Where(entry => entry.WorkspaceId != workspaceId)
                .ToList();
            if (entries.Count == 0)
            {
                return none;
            }

            var variants = await GetItemsInLanguageAsync(entries, languageName, cancellationToken);
            var names = await GetVisibleWorkspaceNamesAsync(entries, cancellationToken);

            return Select(entries, workspaceId, variants, names);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            eventLogService.LogException(nameof(ContentSyncWorkspaceMoveLookup), "WORKSPACEMOVE", ex);
            return none;
        }
    }

    // The target's item is a language variant: only a variant in the listed language can be synced to
    // it. An item here without that variant, or in the listed workspace, isn't a move.
    internal static IReadOnlyDictionary<Guid, ContentSyncMovedItem> Select(
        IEnumerable<ContentSyncWorkspaceEntry> entries,
        int workspaceId,
        IReadOnlySet<int> contentItemIdsInLanguage,
        IReadOnlyDictionary<int, string> visibleWorkspaceNames) =>
        entries
            .Where(entry => entry.WorkspaceId != workspaceId && contentItemIdsInLanguage.Contains(entry.ContentItemId))
            .ToDictionary(
                entry => entry.ContentItemGuid,
                entry => new ContentSyncMovedItem(entry.ContentItemGuid, visibleWorkspaceNames.GetValueOrDefault(entry.WorkspaceId)));

    private async Task<HashSet<int>> GetItemsInLanguageAsync(
        IReadOnlyList<ContentSyncWorkspaceEntry> entries, string languageName, CancellationToken cancellationToken)
    {
        var language = (await contentLanguageInfoProvider.Get()
            .WhereEquals(nameof(ContentLanguageInfo.ContentLanguageName), languageName)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)).FirstOrDefault();
        if (language is null)
        {
            return [];
        }

        var rows = await new ObjectQuery(LanguageMetadataObjectType)
            .Column(LanguageMetadataContentItemIdColumn)
            .WhereEquals(LanguageMetadataLanguageIdColumn, language.ContentLanguageID)
            .WhereIn(LanguageMetadataContentItemIdColumn, entries.Select(entry => entry.ContentItemId).ToList())
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return [.. rows.Select(row => Convert.ToInt32(row.GetValue(LanguageMetadataContentItemIdColumn)))];
    }

    // A workspace the user can't open is never named, as on the rest of the page.
    private async Task<IReadOnlyDictionary<int, string>> GetVisibleWorkspaceNamesAsync(
        IReadOnlyList<ContentSyncWorkspaceEntry> entries, CancellationToken cancellationToken)
    {
        var workspaces = await workspaceInfoProvider.Get()
            .WhereIn(nameof(WorkspaceInfo.WorkspaceID), entries.Select(entry => entry.WorkspaceId).Distinct().ToList())
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        var names = new Dictionary<int, string>();
        foreach (var workspace in workspaces)
        {
            if (await scopeAccess.CanViewWorkspaceAsync(workspace.WorkspaceID, cancellationToken))
            {
                names[workspace.WorkspaceID] = workspace.WorkspaceDisplayName;
            }
        }

        return names;
    }
}
