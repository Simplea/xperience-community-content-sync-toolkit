namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// A selectable website channel or content-hub workspace, for the sync status admin page's filter
/// dropdowns. <paramref name="Id"/> is the underlying <c>WebsiteChannelID</c> or <c>WorkspaceID</c>;
/// <paramref name="Name"/> is the code name the toolkit's own services take as a scope parameter.
/// </summary>
public sealed record ContentSyncScope(int Id, string Name, string DisplayName);

/// <summary>
/// Enumerates the website channels and content-hub workspaces available on this instance, for the
/// sync status admin page's selector controls. The foundation deliberately has no equivalent — see
/// docs/specs/content-inventory-foundation.md's Out of scope — since it always takes an explicit
/// scope name from its caller; this is admin-UI-only surface.
/// </summary>
public interface IContentSyncScopeProvider
{
    public Task<IReadOnlyList<ContentSyncScope>> GetWebsiteChannelsAsync(CancellationToken cancellationToken);

    public Task<IReadOnlyList<ContentSyncScope>> GetWorkspacesAsync(CancellationToken cancellationToken);
}
