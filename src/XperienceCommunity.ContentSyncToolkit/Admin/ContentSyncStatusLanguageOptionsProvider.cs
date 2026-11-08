using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncStatusLanguageOptionsProvider(IContentSyncFilterOptionsProvider filterOptionsProvider) : IDropDownOptionsProvider
{
    public async Task<IEnumerable<DropDownOptionItem>> GetOptionItems()
    {
        var languages = await filterOptionsProvider.GetContentLanguagesAsync(CancellationToken.None);

        return [.. languages.Select(language => new DropDownOptionItem { Value = language.Name, Text = language.DisplayName })];
    }
}
