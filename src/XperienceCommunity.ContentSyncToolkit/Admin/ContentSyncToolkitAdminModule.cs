using CMS;

using Kentico.Xperience.Admin.Base;

using XperienceCommunity.ContentSyncToolkit.Admin;

[assembly: RegisterModule(typeof(ContentSyncToolkitAdminModule))]

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Registers the toolkit's browser module (built from <c>Client/</c> and embedded in the package), as
/// Kentico documents for custom administration code. Its names must match the module's webpack
/// configuration and the project's <c>AdminOrgName</c> and <c>AdminClientPath</c>.
/// </summary>
internal sealed class ContentSyncToolkitAdminModule : AdminModule
{
    public const string OrgName = "xperiencecommunity";
    public const string ProjectName = "content-sync-toolkit";

    /// <summary>
    /// The status lists' menu entry template: the module's <c>StatusEntryTemplate</c> export, as the admin
    /// adds "Template" to a page template's name (Client/src/StatusEntryTemplate.tsx).
    /// </summary>
    public const string EntryTemplate = "@" + OrgName + "/" + ProjectName + "/StatusEntry";

    /// <summary>The status lists' navigation component: the module's export of that name (Client/src/StatusNavigationComponent.tsx).</summary>
    public const string NavigationComponent = "@" + OrgName + "/" + ProjectName + "/StatusNavigationComponent";

    public ContentSyncToolkitAdminModule()
        : base("XperienceCommunity.ContentSyncToolkit.Admin")
    {
    }

    protected override void OnInit()
    {
        base.OnInit();
        RegisterClientModule(OrgName, ProjectName);
    }
}
