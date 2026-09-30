using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using XperienceCommunity.ContentSyncToolkit.Admin;

[assembly: UIApplication(
    ContentSyncStatusConstants.ApplicationIdentifier,
    typeof(ContentSyncStatusApplication),
    "content-sync-status",
    "Content sync status",
    BaseApplicationCategories.CONFIGURATION,
    "xp-refresh",
    TemplateNames.SECTION_LAYOUT)]

namespace XperienceCommunity.ContentSyncToolkit.Admin;

// UIPermission only declares which permissions Role management can grant for this application,
// it doesn't enforce them. Listing pages require VIEW by default, so VIEW is the one permission
// that grants access to the whole application.
[UIPermission(SystemPermissions.VIEW)]
internal sealed class ContentSyncStatusApplication : ApplicationPage
{
}
