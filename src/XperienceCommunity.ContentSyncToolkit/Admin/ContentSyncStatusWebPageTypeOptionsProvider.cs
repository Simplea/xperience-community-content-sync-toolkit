using Kentico.Xperience.Admin.Base.FormAnnotations;

using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncStatusWebPageTypeOptionsProvider(IContentSyncFilterOptionsProvider filterOptionsProvider) : IDropDownOptionsProvider
{
    public async Task<IEnumerable<DropDownOptionItem>> GetOptionItems()
    {
        var contentTypes = await filterOptionsProvider.GetContentTypesAsync(ContentInventoryItemKind.WebPage, CancellationToken.None);

        return [.. contentTypes.Select(contentType => new DropDownOptionItem { Value = contentType.Name, Text = contentType.DisplayName })];
    }
}
