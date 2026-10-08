using CMS.DataEngine;

using Kentico.Xperience.Admin.Base.Filters;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Compiles a multi-select filter field into one named parameter holding its values joined by
/// <see cref="ContentSyncStatusListingSupport.StatusFilterSeparator"/>, so
/// <see cref="ContentSyncStatusFilterValueExtractor"/> reads it back like any single-value field. The
/// condition never reaches a database: the page filters its in-memory result.
/// </summary>
internal sealed class ContentSyncStatusMultiValueConditionBuilder : IWhereConditionBuilder
{
    public Task<IWhereCondition> Build(string columnName, object value)
    {
        var condition = new WhereCondition();
        var values = value switch
        {
            string single => [single],
            IEnumerable<string> many => many,
            _ => [],
        };

        string joined = string.Join(ContentSyncStatusListingSupport.StatusFilterSeparator,
            values.Where(selected => !string.IsNullOrWhiteSpace(selected)));

        if (joined.Length > 0)
        {
            condition.WhereEquals(columnName, joined);
        }

        return Task.FromResult<IWhereCondition>(condition);
    }
}
