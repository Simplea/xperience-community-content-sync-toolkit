using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Which pages of a website channel the signed-in user may see on the sync status page: the pages
/// they can see in Xperience's own page tree (the Display page permission), so the status page
/// never lists a page, even just its path, that the page tree hides from them. Pure, so the rules
/// are unit tested; <see cref="IContentSyncScopeAccess.GetPageVisibilityAsync"/> gathers the data.
/// </summary>
internal sealed class ContentSyncPageVisibility
{
    private readonly bool seesAll;
    private readonly bool rootVisible;
    private readonly IReadOnlyDictionary<Guid, bool> visibleByGuid;
    private readonly IReadOnlyDictionary<string, bool> visibleByPath;

    private ContentSyncPageVisibility(
        bool seesAll, bool rootVisible, IReadOnlyDictionary<Guid, bool> visibleByGuid, IReadOnlyDictionary<string, bool> visibleByPath)
    {
        this.seesAll = seesAll;
        this.rootVisible = rootVisible;
        this.visibleByGuid = visibleByGuid;
        this.visibleByPath = visibleByPath;
    }

    /// <summary>Administrators and users with Manage permissions on the channel, who bypass page permissions.</summary>
    public static ContentSyncPageVisibility All { get; } =
        new(true, true, new Dictionary<Guid, bool>(), new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

    /// <param name="rootVisible">Whether the user can see pages under the channel's root permissions.</param>
    /// <param name="localPages">This instance's pages: GUID, tree path, and whether the user can see them.</param>
    public static ContentSyncPageVisibility For(bool rootVisible, IEnumerable<(Guid Guid, string TreePath, bool Visible)> localPages)
    {
        var byGuid = new Dictionary<Guid, bool>();
        var byPath = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var (guid, treePath, visible) in localPages)
        {
            byGuid[guid] = visible;
            byPath[treePath] = visible;
        }

        return new ContentSyncPageVisibility(false, rootVisible, byGuid, byPath);
    }

    public bool CanSee(ContentSyncStatusItem item)
    {
        if (seesAll)
        {
            return true;
        }

        if (visibleByGuid.TryGetValue(item.Guid, out bool visible))
        {
            return visible;
        }

        // A page only on the target has no permissions here: it gets those of its nearest ancestor
        // that exists here, as it would once synced, or the channel root's.
        return CanSeePath((item.Local ?? item.Remote)?.TreePath);
    }

    /// <summary>
    /// The items the user may see, with reorder details that never name a page they can't see.
    /// Filtering happens before the listing counts, sorts and pages, so counts don't reveal hidden
    /// pages either.
    /// </summary>
    public ContentSyncStatusResult Apply(ContentSyncStatusResult result)
    {
        if (seesAll || !result.TargetAvailable)
        {
            return result;
        }

        return result with
        {
            Items = [.. result.Items.Where(CanSee).Select(item => item.Reorder is null ? item : item with { Reorder = Sanitize(item.Reorder) })],
        };
    }

    private ContentSyncReorder Sanitize(ContentSyncReorder reorder)
    {
        var visiblePages = reorder.MisplacedPages.Where(page => CanSeePage(page.Guid, page.TreePath)).ToList();

        return reorder with
        {
            MisplacedPages = visiblePages,
            HiddenMisplacedCount = reorder.HiddenMisplacedCount + reorder.MisplacedPages.Count - visiblePages.Count,
            Parent = reorder.Parent is { } parent && CanSeePage(parent.Guid, parent.TreePath) ? parent : null,
        };
    }

    private bool CanSeePage(Guid guid, string? treePath) =>
        visibleByGuid.TryGetValue(guid, out bool visible) ? visible : CanSeePath(treePath);

    private bool CanSeePath(string? treePath)
    {
        for (string? path = Parent(treePath); path is not null; path = Parent(path))
        {
            if (visibleByPath.TryGetValue(path, out bool visible))
            {
                return visible;
            }
        }

        return rootVisible;
    }

    // "/Articles/Coffee" → "/Articles"; "/Articles" → null (the root).
    private static string? Parent(string? treePath)
    {
        if (string.IsNullOrEmpty(treePath))
        {
            return null;
        }

        int lastSlash = treePath.LastIndexOf('/');
        return lastSlash <= 0 ? null : treePath[..lastSlash];
    }
}
