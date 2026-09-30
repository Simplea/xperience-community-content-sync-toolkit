using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.Admin.Websites;
using Kentico.Xperience.Admin.Websites.UIPages;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>An admin page and the URL parameter values that locate one item in it.</summary>
internal sealed record ContentSyncStatusItemLink(Type PageType, PageParameterValues ParameterValues)
{
    public string GetPath(IPageLinkGenerator pageLinkGenerator) => pageLinkGenerator.GetPath(PageType, ParameterValues);
}

/// <summary>
/// Links to an item in Xperience's own editors, for <see cref="IPageLinkGenerator"/>, against
/// Kentico's public page types rather than hardcoded URLs. The generator writes each value's
/// string form into its URL segment, so values are given in the form the segment expects
/// (confirmed live on 31.7.2), not as Kentico's URL identifier objects, whose string form is their
/// type name. Kept apart from the generator, which Kentico doesn't allow implementing, so the
/// segment values can be unit tested.
/// </summary>
internal static class ContentSyncStatusItemLinks
{
    // e.g. /webpages-1/en_68, which opens the page's Content view.
    public static ContentSyncStatusItemLink WebPage(int websiteChannelId, string languageName, int webPageItemId) =>
        new(typeof(WebPageLayout), new PageParameterValues
        {
            // The one URL convention this depends on; Kentico's own identifier type parses it
            // into the application slug.
            { typeof(WebPagesApplication), new WebPagesApplicationUrlIdentifier($"webpages-{websiteChannelId}").Slug },
            { typeof(WebPageLayout), new WebPageUrlIdentifier(languageName, webPageItemId).ToString() },
        });

    // e.g. /content-hub/1/en/all/list/184/content
    public static ContentSyncStatusItemLink ContentItem(int workspaceId, string languageName, int contentItemId) =>
        new(typeof(ContentItemEdit), new PageParameterValues
        {
            { typeof(ContentHubWorkspace), workspaceId },
            { typeof(ContentHubContentLanguage), languageName },
            { typeof(ContentHubFolder), ContentFolderId.AllContentItems },
            { typeof(ContentItemEditSection), contentItemId },
        });
}
