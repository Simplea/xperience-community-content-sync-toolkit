using Kentico.Xperience.Admin.Base.Filters;
using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Filter fields both tabs share. Each property compiles to a named parameter of the listing's
/// filter where condition, which <see cref="ContentSyncStatusFilterValueExtractor"/> reads back by
/// property name; an empty field adds no parameter. Subclasses add the content type dropdown
/// (order 30). The channel or workspace and the language aren't filter fields: Kentico's dropdowns
/// in the top bar choose them (see docs/specs/header-dropdowns.md).
/// </summary>
internal abstract class ContentSyncStatusFilterModelBase
{
    // Unchecked by default, so the page shows everyone's changes; see docs/specs/sync-status-admin-page.md.
    [CheckBoxComponent(Label = "Only my changes", Order = 12)]
    public bool OnlyMyChanges { get; set; }

    // Applied when a list first opens, where Kentico supports it (31.2.0 and later; see
    // ContentSyncStatusTabBase.ApplyInitialFilter). As an applied filter, an empty result keeps the toolbar
    // (issue #33), and Xperience remembers the editor's choice per browser.
    [CheckBoxComponent(Label = "Hide items in sync", Order = 15)]
    public bool HideInSync { get; set; }

    // Several statuses can be selected; the listing shows items matching any of them.
    [GeneralSelectorComponent(dataProviderType: typeof(ContentSyncStatusFilterOptionsDataProvider), Label = "Status", Placeholder = "All", Order = 20)]
    [FilterCondition(BuilderType = typeof(ContentSyncStatusMultiValueConditionBuilder))]
    public IEnumerable<string>? Status { get; set; }

    [DateInputComponent(Label = "Published from", Order = 40)]
    public DateTime? PublishedFrom { get; set; }

    [DateInputComponent(Label = "Published to", Order = 50)]
    public DateTime? PublishedTo { get; set; }
}
