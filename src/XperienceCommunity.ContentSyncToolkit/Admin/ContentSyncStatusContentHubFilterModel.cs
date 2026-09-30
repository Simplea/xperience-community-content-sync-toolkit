using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncStatusContentHubFilterModel
{
    [DropDownComponent(DataProviderType = typeof(ContentSyncStatusWorkspaceOptionsProvider), Label = "Workspace")]
    public string? Workspace { get; set; }
}
