using System.Collections;
using System.Reflection;

using CMS.ContentEngine;
using CMS.DataEngine;

using Kentico.Xperience.Admin.Base;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>A content item in Xperience's Detailed selection: a selected row or something it takes along.</summary>
/// <param name="ContentItemId">The content item.</param>
/// <param name="Name">Its name in the sync's language.</param>
/// <param name="ContentType">Its content type's display name.</param>
/// <param name="Channel">The channel of a page; <see langword="null"/> for a reusable item.</param>
/// <param name="Workspace">The workspace of a reusable item; <see langword="null"/> for a page.</param>
/// <param name="Link">Where it opens in the administration.</param>
/// <param name="SentStatus">The version Content Sync sends: Published or Unpublished; <see langword="null"/> if it has none.</param>
/// <param name="HasNewerDraft">Whether a newer draft exists, which isn't sent.</param>
internal sealed record ContentSyncDialogItem(
    int ContentItemId, string Name, string ContentType, string? Channel, string? Workspace, string? Link, VersionStatus? SentStatus, bool HasNewerDraft);

/// <summary>What Xperience's Sync dialog shows beyond the status list's own rows.</summary>
internal interface IContentSyncDialogData
{
    /// <summary>Whether this Xperience version can list a sync's dependencies (from <c>31.x</c>).</summary>
    public bool SupportsDetailedSelection { get; }

    /// <summary>The content items a sync of these items takes along, as Content Sync works them out; the items included.</summary>
    public Task<IReadOnlyList<int>> GetDependenciesAsync(
        IReadOnlyCollection<int> contentItemIds, int languageId, bool includeSubPages, CancellationToken cancellationToken);

    /// <summary>The details Detailed selection shows for each content item, in one language.</summary>
    public Task<IReadOnlyList<ContentSyncDialogItem>> GetItemsAsync(
        IReadOnlyCollection<int> contentItemIds, ContentLanguageInfo language, CancellationToken cancellationToken);
}

/// <summary>
/// Dependencies come from Xperience's <c>IDependencyRetriever</c>, the service its own Detailed selection
/// uses. It's public but in an unsupported (<c>.Internal</c>) namespace and only exists from <c>31.x</c>,
/// so it's found by name and called by reflection, as <see cref="Sync.ContentSyncEngine"/> calls
/// <c>Create</c>. Item details are read by object type name, as elsewhere in the toolkit.
/// </summary>
internal sealed class ContentSyncDialogData(IServiceProvider services, IPageLinkGenerator pageLinkGenerator) : IContentSyncDialogData
{
    private static readonly Type? retrieverType =
        Type.GetType("CMS.ContentSynchronization.Internal.IDependencyRetriever, CMS.ContentSynchronization", throwOnError: false);

    private static readonly MethodInfo? getDependencies = retrieverType?.GetMethod(
        "GetDependencies", [typeof(IEnumerable<int>), typeof(int), typeof(bool), typeof(CancellationToken)]);

    public bool SupportsDetailedSelection => getDependencies is not null;

    public async Task<IReadOnlyList<int>> GetDependenciesAsync(
        IReadOnlyCollection<int> contentItemIds, int languageId, bool includeSubPages, CancellationToken cancellationToken)
    {
        if (getDependencies is null || services.GetService(retrieverType!) is not { } retriever)
        {
            throw new NotSupportedException("This Xperience version can't list a sync's dependencies.");
        }

        var task = (Task)getDependencies.Invoke(retriever, [contentItemIds.ToList(), languageId, includeSubPages, cancellationToken])!;
        await task;

        // Each result has a ContentItemId (always set) and a ContentItemCommonDataId (null for folders).
        var results = (IEnumerable)task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task)!;
        var ids = new List<int>();
        foreach (object result in results)
        {
            if (result.GetType().GetProperty("ContentItemId")?.GetValue(result) is int id && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public async Task<IReadOnlyList<ContentSyncDialogItem>> GetItemsAsync(
        IReadOnlyCollection<int> contentItemIds, ContentLanguageInfo language, CancellationToken cancellationToken)
    {
        if (contentItemIds.Count == 0)
        {
            return [];
        }

        var ids = contentItemIds.Distinct().ToList();
        var items = (await new ObjectQuery("cms.contentitem")
            .Columns("ContentItemID", "ContentItemName", "ContentItemContentTypeID", "ContentItemWorkspaceID", "ContentItemChannelID", "ContentItemIsReusable")
            .WhereIn("ContentItemID", ids)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)).ToList();

        var names = await ReadAsync(
            new ObjectQuery("cms.contentitemlanguagemetadata")
                .Columns("ContentItemLanguageMetadataContentItemID", "ContentItemLanguageMetadataDisplayName", "ContentItemLanguageMetadataLatestVersionStatus")
                .WhereIn("ContentItemLanguageMetadataContentItemID", ids)
                .WhereEquals("ContentItemLanguageMetadataContentLanguageID", language.ContentLanguageID),
            "ContentItemLanguageMetadataContentItemID",
            info => (Name: info.GetValue("ContentItemLanguageMetadataDisplayName") as string, Latest: Convert.ToInt32(info.GetValue("ContentItemLanguageMetadataLatestVersionStatus"))),
            cancellationToken);

        var sent = await ReadAsync(
            new ObjectQuery("cms.contentitemcommondata")
                .Columns("ContentItemCommonDataContentItemID", "ContentItemCommonDataVersionStatus")
                .WhereIn("ContentItemCommonDataContentItemID", ids)
                .WhereEquals("ContentItemCommonDataContentLanguageID", language.ContentLanguageID)
                .WhereIn("ContentItemCommonDataVersionStatus", new[] { (int)VersionStatus.Published, (int)VersionStatus.Unpublished }),
            "ContentItemCommonDataContentItemID",
            info => (VersionStatus)Convert.ToInt32(info.GetValue("ContentItemCommonDataVersionStatus")),
            cancellationToken);

        var pages = await ReadAsync(
            new ObjectQuery("cms.webpageitem")
                .Columns("WebPageItemContentItemID", "WebPageItemID", "WebPageItemWebsiteChannelID")
                .WhereIn("WebPageItemContentItemID", ids),
            "WebPageItemContentItemID",
            info => (Id: Convert.ToInt32(info.GetValue("WebPageItemID")), WebsiteChannelId: Convert.ToInt32(info.GetValue("WebPageItemWebsiteChannelID"))),
            cancellationToken);

        var types = await DisplayNamesAsync("cms.class", "ClassID", "ClassDisplayName", items.Select(item => item.GetValue("ContentItemContentTypeID")), cancellationToken);
        var channels = await DisplayNamesAsync("cms.channel", "ChannelID", "ChannelDisplayName", items.Select(item => item.GetValue("ContentItemChannelID")), cancellationToken);
        var workspaces = await DisplayNamesAsync("cms.workspace", "WorkspaceID", "WorkspaceDisplayName", items.Select(item => item.GetValue("ContentItemWorkspaceID")), cancellationToken);

        return [.. items.Select(item =>
        {
            int id = Convert.ToInt32(item.GetValue("ContentItemID"));
            var (name, latest) = names.GetValueOrDefault(id, (null, -1));
            var sentStatus = sent.TryGetValue(id, out var status) ? status : (VersionStatus?)null;
            string? link = null;
            if (pages.TryGetValue(id, out var page))
            {
                link = ContentSyncStatusItemLinks.WebPage(page.WebsiteChannelId, language.ContentLanguageName, page.Id).GetPath(pageLinkGenerator);
            }
            else if (item.GetValue("ContentItemWorkspaceID") is { } workspaceId and not DBNull)
            {
                link = ContentSyncStatusItemLinks.ContentItem(Convert.ToInt32(workspaceId), language.ContentLanguageName, id).GetPath(pageLinkGenerator);
            }

            return new ContentSyncDialogItem(
                id,
                name ?? (string)item.GetValue("ContentItemName"),
                types.GetValueOrDefault(ToId(item.GetValue("ContentItemContentTypeID"))) ?? string.Empty,
                channels.GetValueOrDefault(ToId(item.GetValue("ContentItemChannelID"))),
                workspaces.GetValueOrDefault(ToId(item.GetValue("ContentItemWorkspaceID"))),
                link,
                sentStatus,
                HasNewerDraft: sentStatus is not null && latest >= 0 && latest != (int)sentStatus);
        })];
    }

    private static int ToId(object? value) => value is null or DBNull ? 0 : Convert.ToInt32(value);

    private static async Task<Dictionary<int, T>> ReadAsync<T>(
        ObjectQuery query, string keyColumn, Func<BaseInfo, T> read, CancellationToken cancellationToken)
    {
        var values = new Dictionary<int, T>();
        foreach (var info in await query.GetEnumerableTypedResultAsync(cancellationToken: cancellationToken))
        {
            values[Convert.ToInt32(info.GetValue(keyColumn))] = read(info);
        }

        return values;
    }

    private static async Task<Dictionary<int, string>> DisplayNamesAsync(
        string objectType, string idColumn, string nameColumn, IEnumerable<object?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Select(ToId).Where(id => id > 0).Distinct().ToList();
        return wanted.Count == 0
            ? []
            : await ReadAsync(
                new ObjectQuery(objectType).Columns(idColumn, nameColumn).WhereIn(idColumn, wanted),
                idColumn,
                info => (string)info.GetValue(nameColumn),
                cancellationToken);
    }
}
