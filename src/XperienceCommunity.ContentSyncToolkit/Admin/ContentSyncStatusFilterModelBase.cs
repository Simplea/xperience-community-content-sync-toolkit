using Kentico.Xperience.Admin.Base.Filters;
using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Filter fields both tabs share. Each property compiles to a named parameter of the listing's
/// filter where condition, which <see cref="ContentSyncStatusFilterValueExtractor"/> reads back by
/// property name; an empty field adds no parameter. Subclasses add the scope (order 0) and
/// content type (order 30) dropdowns.
/// </summary>
internal abstract class ContentSyncStatusFilterModelBase
{
    // No selection lists every language; see ContentSyncStatusListingSupport.IsAllLanguages.
    [DropDownComponent(DataProviderType = typeof(ContentSyncStatusLanguageOptionsProvider), Label = "Language", Placeholder = ContentSyncStatusListingSupport.AllLanguagesLabel, Order = 10)]
    public string? Language { get; set; }

    // Unchecked is the default view, what's still pending, so Clear filter keeps it too. A Status
    // selection or a search looks at every item; see ContentSyncStatusListingSupport.HidesInSync.
    [CheckBoxComponent(Label = "Show items in sync", Order = 15)]
    public bool ShowInSync { get; set; }

    // Several statuses can be selected; the listing shows items matching any of them.
    [GeneralSelectorComponent(dataProviderType: typeof(ContentSyncStatusFilterOptionsDataProvider), Label = "Status", Placeholder = "All", Order = 20)]
    [FilterCondition(BuilderType = typeof(ContentSyncStatusMultiValueConditionBuilder))]
    public IEnumerable<string>? Status { get; set; }

    [DateInputComponent(Label = "Published from", Order = 40)]
    public DateTime? PublishedFrom { get; set; }

    [DateInputComponent(Label = "Published to", Order = 50)]
    public DateTime? PublishedTo { get; set; }
}
