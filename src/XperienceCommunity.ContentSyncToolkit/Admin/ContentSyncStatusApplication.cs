using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

using XperienceCommunity.ContentSyncToolkit.Admin;

// Named and placed for editors: it reports what still needs syncing from this instance, next to
// where content is managed, and apart from Kentico's own Content synchronization application
// (Configuration), which lists the syncs a target has received. The identifier and the
// "content-sync-status" URL segment are kept, so bookmarks and granted permissions still work.
// The icon is a constant: an unknown icon name renders nothing, without an error.
[assembly: UIApplication(
    ContentSyncStatusConstants.ApplicationIdentifier,
    typeof(ContentSyncStatusApplication),
    "content-sync-status",
    "Sync status",
    BaseApplicationCategories.CONTENT_MANAGEMENT,
    Icons.ClipboardChecklist,
    TemplateNames.SECTION_LAYOUT)]

namespace XperienceCommunity.ContentSyncToolkit.Admin;

// UIPermission only declares which permissions Role management can grant for this application,
// it doesn't enforce them. Listing pages require VIEW by default, so VIEW is the one permission
// that grants access to the whole application.
[UIPermission(SystemPermissions.VIEW)]
internal sealed class ContentSyncStatusApplication : ApplicationPage
{
}
