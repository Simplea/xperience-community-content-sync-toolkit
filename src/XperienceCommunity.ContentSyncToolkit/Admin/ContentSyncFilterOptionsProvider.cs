using CMS.ContentEngine;
using CMS.DataEngine;

using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

internal sealed class ContentSyncFilterOptionsProvider(
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider) : IContentSyncFilterOptionsProvider
{
    public async Task<IReadOnlyList<ContentSyncLanguage>> GetContentLanguagesAsync(CancellationToken cancellationToken)
    {
        var languages = await contentLanguageInfoProvider.Get()
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return [.. languages
            .Select(language => new ContentSyncLanguage(language.ContentLanguageName, language.ContentLanguageDisplayName, language.ContentLanguageIsDefault))
            .OrderBy(language => language.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<IReadOnlyList<ContentSyncContentType>> GetContentTypesAsync(ContentInventoryItemKind kind, CancellationToken cancellationToken)
    {
        string contentTypeType = kind == ContentInventoryItemKind.WebPage
            ? ClassContentTypeType.WEBSITE
            : ClassContentTypeType.REUSABLE;

        // DataClassInfoProvider is deliberately excluded from DI by Kentico; see
        // LocalContentInventoryService.GetReusableContentTypeNamesAsync.
        var classes = await new DataClassInfoProvider().Get()
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return [.. classes
            .Where(dataClass => dataClass.ClassContentTypeType == contentTypeType)
            .Select(dataClass => new ContentSyncContentType(dataClass.ClassName, dataClass.ClassDisplayName))
            .OrderBy(contentType => contentType.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }
}
