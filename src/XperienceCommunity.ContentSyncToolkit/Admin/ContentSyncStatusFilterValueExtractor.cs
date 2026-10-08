using CMS.DataEngine;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Reads a single filter-form value back out of a listing page's compiled <see cref="IWhereCondition"/>.
/// The public <see cref="IWhereCondition"/> interface only exposes a compiled SQL string (e.g.
/// <c>"[Channel] = @Channel"</c>), but the concrete <see cref="WhereCondition"/> class Kentico actually
/// returns also exposes the parameterized values behind that SQL — confirmed live against a real
/// instance, since <c>ListingPageBase&lt;,&gt;.BuildFilterWhereCondition</c>/<c>GetFilterModel</c> (the
/// methods that would otherwise let a page read the raw submitted filter values directly) are not
/// virtual and so cannot be overridden.
/// </summary>
internal static class ContentSyncStatusFilterValueExtractor
{
    public static string? ExtractStringParameter(IWhereCondition? condition, string parameterName) =>
        ExtractParameter(condition, parameterName) as string;

    // Date inputs arrive as DateTime (confirmed live), at midnight of the chosen day.
    public static DateTime? ExtractDateParameter(IWhereCondition? condition, string parameterName) =>
        ExtractParameter(condition, parameterName) as DateTime?;

    // A checkbox arrives as a bool; read leniently in case a version sends its text form.
    public static bool? ExtractBoolParameter(IWhereCondition? condition, string parameterName) =>
        ExtractParameter(condition, parameterName) switch
        {
            bool value => value,
            string text when bool.TryParse(text, out bool parsed) => parsed,
            _ => null,
        };

    // A field left empty adds no parameter at all, so a missing parameter means "not filtered".
    // Parameters is null, not empty, when no field is set.
    private static object? ExtractParameter(IWhereCondition? condition, string parameterName)
    {
        if (condition is not WhereCondition concrete || concrete.WhereIsEmpty || concrete.Parameters is null)
        {
            return null;
        }

        return concrete.Parameters
            .FirstOrDefault(parameter => parameter.Name.TrimStart('@').Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }
}
