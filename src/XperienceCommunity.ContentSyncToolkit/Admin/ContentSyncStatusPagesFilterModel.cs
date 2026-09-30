using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncStatusPagesFilterModel : ContentSyncStatusFilterModelBase
{
    [DropDownComponent(DataProviderType = typeof(ContentSyncStatusChannelOptionsProvider), Label = "Channel", Order = 0)]
    public string? Channel { get; set; }

    [DropDownComponent(DataProviderType = typeof(ContentSyncStatusWebPageTypeOptionsProvider), Label = "Content type", Placeholder = "All", Order = 30)]
    public string? ContentType { get; set; }
}
