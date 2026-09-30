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

    [DropDownComponent(Label = "Status", Options = ContentSyncStatusListingSupport.StatusFilterOptions, Placeholder = "All", Order = 20)]
    public string? Status { get; set; }

    [DateInputComponent(Label = "Published from", Order = 40)]
    public DateTime? PublishedFrom { get; set; }

    [DateInputComponent(Label = "Published to", Order = 50)]
    public DateTime? PublishedTo { get; set; }
}
