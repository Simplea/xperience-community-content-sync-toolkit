using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncStatusLanguageOptionsProvider(IContentSyncFilterOptionsProvider filterOptionsProvider) : IDropDownOptionsProvider
{
    public async Task<IEnumerable<DropDownOptionItem>> GetOptionItems()
    {
        var languages = await filterOptionsProvider.GetContentLanguagesAsync(CancellationToken.None);

        // With one language, All languages would list the same items.
        IEnumerable<DropDownOptionItem> all = languages.Count > 1
            ? [new DropDownOptionItem { Value = ContentSyncStatusListingSupport.AllLanguagesFilter, Text = ContentSyncStatusListingSupport.AllLanguagesLabel }]
            : [];

        return [.. all, .. languages.Select(language => new DropDownOptionItem { Value = language.Name, Text = language.DisplayName })];
    }
}
