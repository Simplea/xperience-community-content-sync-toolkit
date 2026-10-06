using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// The Status filter's options for Kentico's general selector, which allows selecting several:
/// Incompatible, then each status in listing order. The list is short, so it isn't paged.
/// </summary>
internal sealed class ContentSyncStatusFilterOptionsDataProvider : IGeneralSelectorDataProvider
{
    public Task<PagedSelectListItems<string>> GetItemsAsync(string searchTerm, int pageIndex, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedSelectListItems<string>
        {
            NextPageAvailable = false,
            Items = ContentSyncStatusListingSupport.StatusFilterOptions
                .Where(option => string.IsNullOrEmpty(searchTerm) || option.Text.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .Select(option => ToListItem(option.Value, option.Text)),
        });

    // A value that's no longer an option (for example, a status renamed since it was selected) is
    // shown as invalid, as Kentico's selectors do for deleted objects.
    public Task<IEnumerable<ObjectSelectorListItem<string>>> GetSelectedItemsAsync(IEnumerable<string> selectedValues, CancellationToken cancellationToken) =>
        Task.FromResult((selectedValues ?? []).Select(value =>
        {
            var option = ContentSyncStatusListingSupport.StatusFilterOptions
                .FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase));
            return option.Value is null
                ? new ObjectSelectorListItem<string> { Value = value, Text = value, IsValid = false }
                : ToListItem(option.Value, option.Text);
        }));

    private static ObjectSelectorListItem<string> ToListItem(string value, string text) =>
        new() { Value = value, Text = text, IsValid = true };
}
