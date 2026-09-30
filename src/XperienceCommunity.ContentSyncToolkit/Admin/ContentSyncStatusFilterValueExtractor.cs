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
    public static string? ExtractStringParameter(IWhereCondition? condition, string parameterName)
    {
        if (condition is not WhereCondition concrete || concrete.WhereIsEmpty)
        {
            return null;
        }

        return concrete.Parameters
            .FirstOrDefault(parameter => parameter.Name.TrimStart('@').Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            ?.Value as string;
    }
}
