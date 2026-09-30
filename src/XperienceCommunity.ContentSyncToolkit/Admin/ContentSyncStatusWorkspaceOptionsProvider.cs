using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncStatusWorkspaceOptionsProvider(IContentSyncScopeProvider scopeProvider) : IDropDownOptionsProvider
{
    public async Task<IEnumerable<DropDownOptionItem>> GetOptionItems()
    {
        var workspaces = await scopeProvider.GetWorkspacesAsync(CancellationToken.None);

        return [.. workspaces.Select(workspace => new DropDownOptionItem { Value = workspace.Name, Text = workspace.DisplayName })];
    }
}
