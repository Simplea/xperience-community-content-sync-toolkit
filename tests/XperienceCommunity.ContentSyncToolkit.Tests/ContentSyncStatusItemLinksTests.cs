using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.Admin.Websites.UIPages;

using XperienceCommunity.ContentSyncToolkit.Admin;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// IPageLinkGenerator writes each value's string form into its URL segment, so these pin the exact
// segment values the live admin accepted (see docs/specs/sync-status-admin-page.md).
public class ContentSyncStatusItemLinksTests
{
    private static Dictionary<Type, string?> Segments(PageParameterValues values) =>
        values.ToDictionary(value => value.Key, value => value.Value?.ToString());

    [Test]
    public void WebPage_TargetsThePageEditor_InItsWebsiteChannelApplication()
    {
        var link = ContentSyncStatusItemLinks.WebPage(websiteChannelId: 1, languageName: "en", webPageItemId: 68);

        Assert.That(link.PageType, Is.EqualTo(typeof(WebPageLayout)));
        Assert.That(Segments(link.ParameterValues), Is.EquivalentTo(new Dictionary<Type, string?>
        {
            [typeof(WebPagesApplication)] = "webpages-1",
            [typeof(WebPageLayout)] = "en_68",
        }));
    }

    [Test]
    public void ContentItem_TargetsTheContentItemEditor_InAllContentItems()
    {
        var link = ContentSyncStatusItemLinks.ContentItem(workspaceId: 1, languageName: "en", contentItemId: 184);

        Assert.That(link.PageType, Is.EqualTo(typeof(ContentItemEdit)));
        Assert.That(Segments(link.ParameterValues), Is.EquivalentTo(new Dictionary<Type, string?>
        {
            [typeof(ContentHubWorkspace)] = "1",
            [typeof(ContentHubContentLanguage)] = "en",
            [typeof(ContentHubFolder)] = "all",
            [typeof(ContentItemEditSection)] = "184",
        }));
    }
}
