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
    [DropDownComponent(DataProviderType = typeof(ContentSyncStatusLanguageOptionsProvider), Label = "Language", Placeholder = "Default language", Order = 10)]
    public string? Language { get; set; }

    // The common view in one click; combines with Status like every other field (AND).
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
