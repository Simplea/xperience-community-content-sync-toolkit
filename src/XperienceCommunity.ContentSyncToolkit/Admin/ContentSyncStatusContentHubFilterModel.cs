using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncStatusContentHubFilterModel : ContentSyncStatusFilterModelBase
{
    [DropDownComponent(DataProviderType = typeof(ContentSyncStatusReusableTypeOptionsProvider), Label = "Content type", Placeholder = "All", Order = 30)]
    public string? ContentType { get; set; }
}
