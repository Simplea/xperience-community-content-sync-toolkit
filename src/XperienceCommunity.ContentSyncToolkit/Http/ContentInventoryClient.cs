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

    private static ContentInventoryFetchResult ToInventoryResult(ContentInventoryFetchStatus status, ContentInventoryResponse? body) =>
        body?.Items is not null
            ? ContentInventoryFetchResult.Success([.. body.Items.Select(NormalizeTime)])
            : ContentInventoryFetchResult.Failed(FailureStatus(status));

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
    private static ContentInventoryItem NormalizeTime(ContentInventoryItem item) =>
        item with { LastPublishedWhen = ContentInventoryTime.ToUtc(item.LastPublishedWhen) };
}
