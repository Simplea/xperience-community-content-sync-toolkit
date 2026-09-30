using Kentico.Xperience.Admin.Base.FormAnnotations;

using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncStatusReusableTypeOptionsProvider(IContentSyncFilterOptionsProvider filterOptionsProvider) : IDropDownOptionsProvider
{
    public async Task<IEnumerable<DropDownOptionItem>> GetOptionItems()
    {
        var contentTypes = await filterOptionsProvider.GetContentTypesAsync(ContentInventoryItemKind.ContentHubItem, CancellationToken.None);

        return [.. contentTypes.Select(contentType => new DropDownOptionItem { Value = contentType.Name, Text = contentType.DisplayName })];
    }
}
