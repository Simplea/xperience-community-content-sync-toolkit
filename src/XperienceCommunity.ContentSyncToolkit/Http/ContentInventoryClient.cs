using System.Net.Http.Json;

using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Http;

// Every request to the target goes through FetchAsync. The two list requests report to the target's health
// (the Event log entries); the endpoints added later report only that a target doesn't have them yet. See
// docs/specs/event-log-diagnostics.md.
internal sealed class ContentInventoryClient(HttpClient httpClient, IContentSyncTargetHealth? health = null) : IContentInventoryClient
{
    private const string MovedItemsFeature = "items moved to another workspace";
    private const string RequiredObjectsFeature = "the check of content types, languages and channels before a sync";
    private const string ItemLinksFeature = "links to items only on the target";

    public async Task<ContentInventoryFetchResult> GetWebPagesAsync(
        string channelName, string languageName, CancellationToken cancellationToken)
    {
        var fetched = await FetchAsync<ContentInventoryResponse>(
            $"{ContentSyncToolkitConstants.ControllerRoute}/web-pages" +
            $"?channelName={Uri.EscapeDataString(channelName)}&languageName={Uri.EscapeDataString(languageName)}",
            cancellationToken);

        return ToInventoryResult(ReportList(fetched));
    }

    public async Task<ContentInventoryFetchResult> GetContentHubItemsAsync(
        string workspaceName, string languageName, CancellationToken cancellationToken)
    {
        var fetched = await FetchAsync<ContentInventoryResponse>(
            $"{ContentSyncToolkitConstants.ControllerRoute}/content-hub-items" +
            $"?workspaceName={Uri.EscapeDataString(workspaceName)}&languageName={Uri.EscapeDataString(languageName)}",
            cancellationToken);

        return ToInventoryResult(ReportList(fetched));
    }

    public async Task<ContentInventoryFetchResult> GetContentHubItemsByGuidAsync(
        string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken)
    {
        var fetched = await FetchAsync<ContentInventoryResponse>(
            $"{ContentSyncToolkitConstants.ControllerRoute}/content-hub-items-by-guid" +
            $"?languageName={Uri.EscapeDataString(languageName)}&guids={string.Join(',', guids)}",
            cancellationToken);

        return ToInventoryResult(ReportFeature(fetched, MovedItemsFeature));
    }

    public async Task<RequiredObjectsFetchResult> GetRequiredObjectsAsync(CancellationToken cancellationToken)
    {
        var (status, body, _, _) = ReportFeature(
            await FetchAsync<RequiredObjectsResponse>($"{ContentSyncToolkitConstants.ControllerRoute}/required-objects", cancellationToken),
            RequiredObjectsFeature);

        return body?.Objects is not null
            ? RequiredObjectsFetchResult.Success(body.Objects)
            : RequiredObjectsFetchResult.Failed(FailureStatus(status));
    }

    public async Task<TargetItemIdsFetchResult> GetWebPageIdsAsync(
        string channelName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken)
    {
        var fetched = await FetchAsync<TargetItemIdsResponse>(
            $"{ContentSyncToolkitConstants.ControllerRoute}/web-page-ids" +
            $"?channelName={Uri.EscapeDataString(channelName)}&languageName={Uri.EscapeDataString(languageName)}&guids={string.Join(',', guids)}",
            cancellationToken);

        return ToItemIdsResult(ReportFeature(fetched, ItemLinksFeature));
    }

    public async Task<TargetItemIdsFetchResult> GetContentItemIdsAsync(
        string workspaceName, string languageName, IReadOnlyCollection<Guid> guids, CancellationToken cancellationToken)
    {
        var fetched = await FetchAsync<TargetItemIdsResponse>(
            $"{ContentSyncToolkitConstants.ControllerRoute}/content-item-ids" +
            $"?workspaceName={Uri.EscapeDataString(workspaceName)}&languageName={Uri.EscapeDataString(languageName)}&guids={string.Join(',', guids)}",
            cancellationToken);

        return ToItemIdsResult(ReportFeature(fetched, ItemLinksFeature));
    }

    private static TargetItemIdsFetchResult ToItemIdsResult(Fetched<TargetItemIdsResponse> fetched)
    {
        var (status, body, _, _) = fetched;
        if (body?.Items is null)
        {
            return TargetItemIdsFetchResult.Failed(FailureStatus(status));
        }

        var items = body.Items.DistinctBy(item => item.Guid).ToList();

        return TargetItemIdsFetchResult.Success(body.ScopeId, items.ToDictionary(item => item.Guid, item => item.Id)) with
        {
            ContentItemGuids = items
                .Where(item => item.ContentItemGuid is not null)
                .ToDictionary(item => item.Guid, item => item.ContentItemGuid!.Value),
        };
    }

    private static ContentInventoryFetchResult ToInventoryResult(Fetched<ContentInventoryResponse> fetched)
    {
        var (status, body, _, _) = fetched;
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

    /// <summary>A request's outcome: the body on success, otherwise what went wrong, for the Event log.</summary>
    private readonly record struct Fetched<TResponse>(ContentInventoryFetchStatus Status, TResponse? Body, string? Detail, int? StatusCode)
        where TResponse : class;

    private Fetched<ContentInventoryResponse> ReportList(Fetched<ContentInventoryResponse> fetched)
    {
        if (health is not null && httpClient.BaseAddress is { } target)
        {
            if (fetched.Body?.Items is not null)
            {
                health.ListAnswered(target, fetched.Body.SchemaVersion);
            }
            else
            {
                var (failure, detail) = fetched.Status switch
                {
                    ContentInventoryFetchStatus.Unreachable => (ContentSyncTargetFailure.Unreachable, fetched.Detail ?? "no answer"),
                    ContentInventoryFetchStatus.Rejected => (ContentSyncTargetFailure.Rejected, fetched.Detail ?? "an error status"),
                    ContentInventoryFetchStatus.Success or ContentInventoryFetchStatus.Error or _ =>
                        (ContentSyncTargetFailure.Error, fetched.Detail ?? "an unexpected answer"),
                };
                health.ListFailed(target, failure, detail);
            }
        }

        return fetched;
    }

    // A 404 from an endpoint added after the first release: an older target, as long as the lists answer.
    private Fetched<TResponse> ReportFeature<TResponse>(Fetched<TResponse> fetched, string feature)
        where TResponse : class
    {
        if (health is not null && httpClient.BaseAddress is { } target && fetched.StatusCode == 404)
        {
            health.FeatureMissing(target, feature);
        }

        return fetched;
    }

    private async Task<Fetched<TResponse>> FetchAsync<TResponse>(
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
            return new(ContentInventoryFetchStatus.Unreachable, null,
                $"no answer within {httpClient.Timeout.TotalSeconds:0} seconds (ContentSyncToolkit:RequestTimeout)", null);
        }
        catch (HttpRequestException ex)
        {
            return new(ContentInventoryFetchStatus.Unreachable, null, ex.Message, null);
        }

        int statusCode = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            return new(ContentInventoryFetchStatus.Rejected, null, $"{statusCode} {response.ReasonPhrase ?? response.StatusCode.ToString()}", statusCode);
        }

        try
        {
            return new(ContentInventoryFetchStatus.Success, await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken), null, statusCode);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return new(ContentInventoryFetchStatus.Error, null, null, statusCode);
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
