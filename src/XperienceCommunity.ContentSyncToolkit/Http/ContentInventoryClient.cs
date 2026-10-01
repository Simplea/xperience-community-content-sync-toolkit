using System.Net.Http.Json;

using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Http;

internal sealed class ContentInventoryClient(HttpClient httpClient) : IContentInventoryClient
{
    public Task<ContentInventoryFetchResult> GetWebPagesAsync(
        string channelName, string languageName, CancellationToken cancellationToken) =>
        FetchAsync(
            $"{ContentSyncToolkitConstants.ControllerRoute}/web-pages" +
            $"?channelName={Uri.EscapeDataString(channelName)}&languageName={Uri.EscapeDataString(languageName)}",
            cancellationToken);

    public Task<ContentInventoryFetchResult> GetContentHubItemsAsync(
        string workspaceName, string languageName, CancellationToken cancellationToken) =>
        FetchAsync(
            $"{ContentSyncToolkitConstants.ControllerRoute}/content-hub-items" +
            $"?workspaceName={Uri.EscapeDataString(workspaceName)}&languageName={Uri.EscapeDataString(languageName)}",
            cancellationToken);

    private async Task<ContentInventoryFetchResult> FetchAsync(string requestUri, CancellationToken cancellationToken)
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
            return ContentInventoryFetchResult.Failed(ContentInventoryFetchStatus.Unreachable);
        }
        catch (HttpRequestException)
        {
            return ContentInventoryFetchResult.Failed(ContentInventoryFetchStatus.Unreachable);
        }

        if (!response.IsSuccessStatusCode)
        {
            return ContentInventoryFetchResult.Failed(ContentInventoryFetchStatus.Rejected);
        }

        ContentInventoryResponse? body;

        try
        {
            body = await response.Content.ReadFromJsonAsync<ContentInventoryResponse>(cancellationToken);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return ContentInventoryFetchResult.Failed(ContentInventoryFetchStatus.Error);
        }

        return body is null
            ? ContentInventoryFetchResult.Failed(ContentInventoryFetchStatus.Error)
            : ContentInventoryFetchResult.Success([.. body.Items.Select(NormalizeTime)]);
    }

    // Schema 2 targets send UTC; schema 1 targets send server-local time with no time zone, which is
    // taken as this server's local time (how they were compared before).
    private static ContentInventoryItem NormalizeTime(ContentInventoryItem item) =>
        item with { LastPublishedWhen = ContentInventoryTime.ToUtc(item.LastPublishedWhen) };
}
