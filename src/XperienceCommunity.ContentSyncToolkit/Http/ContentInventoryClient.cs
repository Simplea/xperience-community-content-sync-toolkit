using System.Net.Http.Json;

using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Http;

internal sealed class ContentInventoryClient(HttpClient httpClient) : IContentInventoryClient
{
    public async Task<ContentInventoryFetchResult> GetWebPagesAsync(
        string channelName, string languageName, CancellationToken cancellationToken)
    {
        var (status, body) = await FetchAsync<ContentInventoryResponse>(
            $"{ContentSyncToolkitConstants.ControllerRoute}/web-pages" +
            $"?channelName={Uri.EscapeDataString(channelName)}&languageName={Uri.EscapeDataString(languageName)}",
            cancellationToken);

        return ToInventoryResult(status, body);
    }

    public async Task<ContentInventoryFetchResult> GetContentHubItemsAsync(
        string workspaceName, string languageName, CancellationToken cancellationToken)
    {
        var (status, body) = await FetchAsync<ContentInventoryResponse>(
            $"{ContentSyncToolkitConstants.ControllerRoute}/content-hub-items" +
            $"?workspaceName={Uri.EscapeDataString(workspaceName)}&languageName={Uri.EscapeDataString(languageName)}",
            cancellationToken);

        return ToInventoryResult(status, body);
    }

    public async Task<RequiredObjectsFetchResult> GetRequiredObjectsAsync(CancellationToken cancellationToken)
    {
        var (status, body) = await FetchAsync<RequiredObjectsResponse>(
            $"{ContentSyncToolkitConstants.ControllerRoute}/required-objects", cancellationToken);

        return body?.Objects is not null
            ? RequiredObjectsFetchResult.Success(body.Objects)
            : RequiredObjectsFetchResult.Failed(FailureStatus(status));
    }

    public async Task<TargetItemIdsFetchResult> GetWebPageIdsAsync(
        string channelName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken)
    {
        var (status, body) = await FetchAsync<TargetItemIdsResponse>(
            $"{ContentSyncToolkitConstants.ControllerRoute}/web-page-ids" +
            $"?channelName={Uri.EscapeDataString(channelName)}&languageName={Uri.EscapeDataString(languageName)}&guids={string.Join(',', guids)}",
            cancellationToken);

        return ToItemIdsResult(status, body);
    }

    public async Task<TargetItemIdsFetchResult> GetContentItemIdsAsync(
        string workspaceName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken)
    {
        var (status, body) = await FetchAsync<TargetItemIdsResponse>(
            $"{ContentSyncToolkitConstants.ControllerRoute}/content-item-ids" +
            $"?workspaceName={Uri.EscapeDataString(workspaceName)}&languageName={Uri.EscapeDataString(languageName)}&guids={string.Join(',', guids)}",
            cancellationToken);

        return ToItemIdsResult(status, body);
    }

    private static TargetItemIdsFetchResult ToItemIdsResult(ContentInventoryFetchStatus status, TargetItemIdsResponse? body) =>
        body?.Items is not null
            ? TargetItemIdsFetchResult.Success(body.ScopeId, body.Items.DistinctBy(item => item.Guid).ToDictionary(item => item.Guid, item => item.Id))
            : TargetItemIdsFetchResult.Failed(FailureStatus(status));

    private static ContentInventoryFetchResult ToInventoryResult(ContentInventoryFetchStatus status, ContentInventoryResponse? body)
    {
        if (body?.Items is null)
        {
            return ContentInventoryFetchResult.Failed(FailureStatus(status));
        }

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        return ContentInventoryFetchResult.Success([.. body.Items.Select(item => Normalize(item, strings))]);
    }

    // A body only comes with Success; Success without one (a 200 that isn't the expected JSON, or a
    // JSON null) is an Error, never an empty result.
    private static ContentInventoryFetchStatus FailureStatus(ContentInventoryFetchStatus status) =>
        status == ContentInventoryFetchStatus.Success ? ContentInventoryFetchStatus.Error : status;

    private async Task<(ContentInventoryFetchStatus Status, TResponse? Body)> FetchAsync<TResponse>(
        string requestUri, CancellationToken cancellationToken)
        where TResponse : class
    {
        if (httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(
                "Xperience's Content Sync source settings (ContentSynchronizationOptions.Source: Enabled and " +
                "TargetUrl) must be configured before calling a target instance.");
        }

        HttpResponseMessage response;

        try
        {
            response = await httpClient.GetAsync(requestUri, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out; the caller did not request cancellation.
            return (ContentInventoryFetchStatus.Unreachable, null);
        }
        catch (HttpRequestException)
        {
            return (ContentInventoryFetchStatus.Unreachable, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return (ContentInventoryFetchStatus.Rejected, null);
        }

        try
        {
            return (ContentInventoryFetchStatus.Success, await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return (ContentInventoryFetchStatus.Error, null);
        }
    }

    // Schema 2 targets send UTC; schema 1 targets send server-local time with no time zone, which is
    // taken as this server's local time (how they were compared before).
    // The response is cached for the whole cache duration, so values most items share (content type,
    // channel or workspace, language, publish state) are kept once each rather than once per item, as
    // JSON deserialization creates them. On a 20,000-item inventory, that's most of the cached list's
    // memory. See docs/specs/content-inventory-foundation.md, Large inventories.
    private static ContentInventoryItem Normalize(ContentInventoryItem item, Dictionary<string, string> strings) =>
        item with
        {
            LastPublishedWhen = ContentInventoryTime.ToUtc(item.LastPublishedWhen),
            ContentTypeName = Shared(item.ContentTypeName, strings)!,
            ScopeName = Shared(item.ScopeName, strings)!,
            LanguageName = Shared(item.LanguageName, strings)!,
            VersionStatus = Shared(item.VersionStatus, strings),
            ContentTypeDisplayName = Shared(item.ContentTypeDisplayName, strings),
        };

    private static string? Shared(string? value, Dictionary<string, string> strings)
    {
        if (value is null)
        {
            return null;
        }

        if (strings.TryGetValue(value, out string? shared))
        {
            return shared;
        }

        strings[value] = value;
        return value;
    }
}
