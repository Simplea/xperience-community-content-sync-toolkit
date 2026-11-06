using CMS.ContentEngine;
using CMS.Core;
using CMS.DataEngine;
using CMS.Membership;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>An item deleted on this instance that's still in its recycle bin.</summary>
/// <param name="ContentItemGuid">The deleted item's <c>ContentItemGUID</c>.</param>
/// <param name="DeletedWhen">When it was deleted.</param>
/// <param name="DeletedBy">Who deleted it, when the user still exists.</param>
/// <param name="ExpiresWhen">When it's permanently deleted, when the retention setting can be read.</param>
internal sealed record ContentSyncDeletedItem(Guid ContentItemGuid, DateTime DeletedWhen, string? DeletedBy, DateTime? ExpiresWhen);

/// <summary>What the signed-in user can see in the recycle bin, which Xperience limits by who deleted an item.</summary>
/// <param name="CanView">Whether the user can open the Recycle bin application at all.</param>
/// <param name="SeesAllItems">Whether they see items other users deleted, not just their own.</param>
/// <param name="UserId">The signed-in user.</param>
internal sealed record ContentSyncRecycleBinAccess(bool CanView, bool SeesAllItems, int UserId)
{
    public static ContentSyncRecycleBinAccess None { get; } = new(false, false, 0);
}

/// <summary>One row of the recycle bin, before access is applied.</summary>
internal sealed record ContentSyncRecycleBinEntry(Guid ContentItemGuid, DateTime DeletedWhen, int DeletedByUserId);

/// <summary>
/// Which items the user could find in this instance's recycle bin, for the Only on target rows of one
/// listing page. See docs/specs/recycle-bin-hints.md.
/// </summary>
internal interface IContentSyncRecycleBinLookup
{
    public Task<IReadOnlyDictionary<Guid, ContentSyncDeletedItem>> GetDeletedItemsAsync(
        string languageName, IReadOnlyCollection<Guid> contentItemGuids, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the recycle bin by object type name, as for display names: it was added in 30.12.0, so its
/// Info class doesn't exist on the toolkit's minimum version. Before that version the object type isn't
/// registered, and there's nothing to look up. The hint is a convenience, so a failure leaves the rows
/// without it.
/// </summary>
internal sealed class ContentSyncRecycleBinLookup(
    IContentSyncScopeAccess scopeAccess,
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider,
    IInfoProvider<UserInfo> userInfoProvider,
    ISettingsService settingsService,
    IEventLogService eventLogService) : IContentSyncRecycleBinLookup
{
    private const string RecycleBinObjectType = "cms.recyclebincontentitem";
    private const string ContentItemGuidColumn = "RecycleBinContentItemContentItemGUID";
    private const string LanguageIdColumn = "RecycleBinContentItemContentLanguageID";
    private const string CreatedWhenColumn = "RecycleBinContentItemCreatedWhen";
    private const string CreatedByUserIdColumn = "RecycleBinContentItemCreatedByUserID";
    private const string RetentionDaysSettingKey = "CMSDeleteRecycleBinItemsAfterXDays";

    public async Task<IReadOnlyDictionary<Guid, ContentSyncDeletedItem>> GetDeletedItemsAsync(
        string languageName, IReadOnlyCollection<Guid> contentItemGuids, CancellationToken cancellationToken)
    {
        var none = new Dictionary<Guid, ContentSyncDeletedItem>();
        if (contentItemGuids.Count == 0 || !ObjectTypeManager.ExistingObjectTypes.Contains(RecycleBinObjectType, StringComparer.OrdinalIgnoreCase))
        {
            return none;
        }

        try
        {
            var access = await scopeAccess.GetRecycleBinAccessAsync(cancellationToken);
            if (!access.CanView)
            {
                return none;
            }

            var language = (await contentLanguageInfoProvider.Get()
                .WhereEquals(nameof(ContentLanguageInfo.ContentLanguageName), languageName)
                .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)).FirstOrDefault();
            if (language is null)
            {
                return none;
            }

            var rows = await new ObjectQuery(RecycleBinObjectType)
                .Columns(ContentItemGuidColumn, CreatedWhenColumn, CreatedByUserIdColumn)
                .WhereEquals(LanguageIdColumn, language.ContentLanguageID)
                .WhereIn(ContentItemGuidColumn, contentItemGuids.ToList())
                .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

            var entries = SelectVisible(
                rows.Select(row => new ContentSyncRecycleBinEntry(
                    (Guid)row.GetValue(ContentItemGuidColumn),
                    Convert.ToDateTime(row.GetValue(CreatedWhenColumn)),
                    Convert.ToInt32(row.GetValue(CreatedByUserIdColumn)))),
                access);

            var userNames = await GetUserNamesAsync(entries, cancellationToken);
            int? retentionDays = ParseRetentionDays(settingsService[RetentionDaysSettingKey]);

            return entries.ToDictionary(
                entry => entry.ContentItemGuid,
                entry => new ContentSyncDeletedItem(
                    entry.ContentItemGuid,
                    entry.DeletedWhen,
                    userNames.GetValueOrDefault(entry.DeletedByUserId),
                    retentionDays is int days ? entry.DeletedWhen.AddDays(days) : null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            eventLogService.LogException(nameof(ContentSyncRecycleBinLookup), "RECYCLEBIN", ex);
            return none;
        }
    }

    // An item deleted, restored and deleted again has a row per deletion: the latest counts. Users who
    // can't manage the recycle bin only see what they deleted themselves, as in Xperience.
    internal static IReadOnlyList<ContentSyncRecycleBinEntry> SelectVisible(
        IEnumerable<ContentSyncRecycleBinEntry> entries, ContentSyncRecycleBinAccess access) =>
        [.. entries
            .Where(entry => access.CanView && (access.SeesAllItems || entry.DeletedByUserId == access.UserId))
            .GroupBy(entry => entry.ContentItemGuid)
            .Select(group => group.MaxBy(entry => entry.DeletedWhen)!)];

    // Zero or less, or an unreadable value, means no date can be promised.
    internal static int? ParseRetentionDays(string? value) =>
        int.TryParse(value, out int days) && days > 0 ? days : null;

    private async Task<IReadOnlyDictionary<int, string>> GetUserNamesAsync(
        IReadOnlyList<ContentSyncRecycleBinEntry> entries, CancellationToken cancellationToken)
    {
        var userIds = entries.Select(entry => entry.DeletedByUserId).Distinct().ToList();
        if (userIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var users = await userInfoProvider.Get()
            .WhereIn(nameof(UserInfo.UserID), userIds)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);

        return users.ToDictionary(
            user => user.UserID,
            user => string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName);
    }
}
