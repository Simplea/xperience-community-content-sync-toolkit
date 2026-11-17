using CMS.ContentEngine;
using CMS.Core;
using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base.Authentication;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>Who last modified a language version on this instance, for the Modified by column.</summary>
/// <param name="Name">The user's full name, or their user name when they have none.</param>
/// <param name="IsCurrentUser">Whether it's the signed-in user, for the Only my changes filter.</param>
internal sealed record ContentSyncModifier(string Name, bool IsCurrentUser);

/// <summary>
/// Who last modified each item's language version on this instance: Kentico's
/// <c>ContentItemLanguageMetadataModifiedByUserID</c>, which saving a draft also sets. See
/// docs/specs/sync-status-admin-page.md, Modified by and Only my changes.
/// </summary>
internal interface IContentSyncModifierLookup
{
    /// <summary>Keyed by <c>WebPageItemGUID</c>, for every page in the channel with a version in the language.</summary>
    public Task<IReadOnlyDictionary<Guid, ContentSyncModifier>> GetWebPagesAsync(
        int websiteChannelId, string languageName, CancellationToken cancellationToken);

    /// <summary>Keyed by <c>ContentItemGUID</c>, for every content item in the workspace with a version in the language.</summary>
    public Task<IReadOnlyDictionary<Guid, ContentSyncModifier>> GetContentItemsAsync(
        int workspaceId, string languageName, CancellationToken cancellationToken);
}

/// <summary>
/// Reads pages, content items and language version metadata by object type name, as for display names:
/// their Info classes are in Internal namespaces. It's a convenience, so a failure leaves the column
/// empty (and Only my changes matching nothing) rather than failing the page.
/// </summary>
internal sealed class ContentSyncModifierLookup(
    IAuthenticatedUserAccessor authenticatedUserAccessor,
    IInfoProvider<UserInfo> userInfoProvider,
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider,
    IEventLogService eventLogService) : IContentSyncModifierLookup
{
    private const string WebPageObjectType = "cms.webpageitem";
    private const string WebPageGuidColumn = "WebPageItemGUID";
    private const string WebPageChannelIdColumn = "WebPageItemWebsiteChannelID";
    private const string WebPageContentItemIdColumn = "WebPageItemContentItemID";
    private const string ContentItemObjectType = "cms.contentitem";
    private const string ContentItemIdColumn = "ContentItemID";
    private const string ContentItemGuidColumn = "ContentItemGUID";
    private const string ContentItemWorkspaceIdColumn = "ContentItemWorkspaceID";
    private const string LanguageMetadataObjectType = "cms.contentitemlanguagemetadata";
    private const string LanguageMetadataContentItemIdColumn = "ContentItemLanguageMetadataContentItemID";
    private const string LanguageMetadataLanguageIdColumn = "ContentItemLanguageMetadataContentLanguageID";
    private const string LanguageMetadataModifiedByColumn = "ContentItemLanguageMetadataModifiedByUserID";

    public Task<IReadOnlyDictionary<Guid, ContentSyncModifier>> GetWebPagesAsync(
        int websiteChannelId, string languageName, CancellationToken cancellationToken) =>
        GetAsync(
            new ObjectQuery(WebPageObjectType)
                .Columns(WebPageGuidColumn, WebPageContentItemIdColumn)
                .WhereEquals(WebPageChannelIdColumn, websiteChannelId),
            WebPageGuidColumn,
            WebPageContentItemIdColumn,
            languageName,
            cancellationToken);

    public Task<IReadOnlyDictionary<Guid, ContentSyncModifier>> GetContentItemsAsync(
        int workspaceId, string languageName, CancellationToken cancellationToken) =>
        GetAsync(
            new ObjectQuery(ContentItemObjectType)
                .Columns(ContentItemGuidColumn, ContentItemIdColumn)
                .WhereEquals(ContentItemWorkspaceIdColumn, workspaceId),
            ContentItemGuidColumn,
            ContentItemIdColumn,
            languageName,
            cancellationToken);

    // The scope's items, the language versions' last modifiers (with the items as a subquery, so a large
    // scope adds no long list), and those users' names: three queries, whatever the number of items.
    private async Task<IReadOnlyDictionary<Guid, ContentSyncModifier>> GetAsync(
        ObjectQuery scopeItems, string guidColumn, string contentItemIdColumn, string languageName, CancellationToken cancellationToken)
    {
        var none = new Dictionary<Guid, ContentSyncModifier>();

        try
        {
            var language = (await contentLanguageInfoProvider.Get()
                .WhereEquals(nameof(ContentLanguageInfo.ContentLanguageName), languageName)
                .TopN(1)
                .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)).FirstOrDefault();
            if (language is null)
            {
                return none;
            }

            var modifierIds = (await new ObjectQuery(LanguageMetadataObjectType)
                    .Columns(LanguageMetadataContentItemIdColumn, LanguageMetadataModifiedByColumn)
                    .WhereEquals(LanguageMetadataLanguageIdColumn, language.ContentLanguageID)
                    .WhereNotNull(LanguageMetadataModifiedByColumn)
                    .WhereIn(LanguageMetadataContentItemIdColumn, scopeItems.Clone().Column(contentItemIdColumn))
                    .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
                .ToDictionary(
                    row => Convert.ToInt32(row.GetValue(LanguageMetadataContentItemIdColumn)),
                    row => Convert.ToInt32(row.GetValue(LanguageMetadataModifiedByColumn)));
            if (modifierIds.Count == 0)
            {
                return none;
            }

            var users = await GetUsersAsync([.. modifierIds.Values.Distinct()], cancellationToken);
            int? currentUserId = await GetCurrentUserIdAsync(cancellationToken);

            var result = new Dictionary<Guid, ContentSyncModifier>();
            foreach (var row in await scopeItems.GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
            {
                if (modifierIds.TryGetValue(Convert.ToInt32(row.GetValue(contentItemIdColumn)), out int userId)
                    && users.TryGetValue(userId, out string? name))
                {
                    result[(Guid)row.GetValue(guidColumn)] = new ContentSyncModifier(name, userId == currentUserId);
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            eventLogService.LogException(nameof(ContentSyncModifierLookup), "MODIFIEDBY", ex);
            return none;
        }
    }

    // Full name as the administration shows it, or the user name when the user has none.
    private async Task<IReadOnlyDictionary<int, string>> GetUsersAsync(IReadOnlyCollection<int> userIds, CancellationToken cancellationToken)
    {
        var users = await userInfoProvider.Get()
            .WhereIn(nameof(UserInfo.UserID), userIds)
            .Columns(nameof(UserInfo.UserID), nameof(UserInfo.UserName), nameof(UserInfo.FirstName), nameof(UserInfo.LastName))
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return users.ToDictionary(
            user => user.UserID,
            user => $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } fullName ? fullName : user.UserName);
    }

    private async Task<int?> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var adminUser = await authenticatedUserAccessor.Get();
        if (adminUser is null || string.IsNullOrEmpty(adminUser.UserName))
        {
            return null;
        }

        var user = (await userInfoProvider.Get()
            .WhereEquals(nameof(UserInfo.UserName), adminUser.UserName)
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)).FirstOrDefault();

        return user?.UserID;
    }
}
