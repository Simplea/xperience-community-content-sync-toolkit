using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncStatusPagesFilterModel
{
    [DropDownComponent(DataProviderType = typeof(ContentSyncStatusChannelOptionsProvider), Label = "Channel")]
    public string? Channel { get; set; }
}
