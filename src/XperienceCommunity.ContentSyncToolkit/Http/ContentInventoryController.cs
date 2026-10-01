using Microsoft.AspNetCore.Mvc;

using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Target-side endpoint answering inventory requests from a source instance. Service-to-service,
/// not an authenticated Xperience administration user — see <see cref="ContentSyncTargetSecretFilter"/>.
/// Responses are never cacheable: they're secret-gated, so a shared cache (such as the SaaS CDN) must
/// not serve them to another caller.
/// </summary>
[ApiController]
[Route(ContentSyncToolkitConstants.ControllerRoute)]
[TypeFilter(typeof(ContentSyncTargetSecretFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContentInventoryController(
    ILocalContentInventoryService inventoryService,
    ILocalRequiredObjectsService requiredObjectsService,
    TimeProvider timeProvider) : ControllerBase
{
    private const int SchemaVersion = ContentSyncToolkitConstants.SchemaVersion;

    [HttpGet("web-pages")]
    public async Task<ActionResult<ContentInventoryResponse>> GetWebPages(
        [FromQuery] string channelName, [FromQuery] string languageName, CancellationToken cancellationToken)
    {
        var items = await inventoryService.GetWebPagesAsync(channelName, languageName, cancellationToken);
        return new ContentInventoryResponse(SchemaVersion, timeProvider.GetUtcNow(), items);
    }

    [HttpGet("content-hub-items")]
    public async Task<ActionResult<ContentInventoryResponse>> GetContentHubItems(
        [FromQuery] string workspaceName, [FromQuery] string languageName, CancellationToken cancellationToken)
    {
        var items = await inventoryService.GetContentHubItemsAsync(workspaceName, languageName, cancellationToken);
        return new ContentInventoryResponse(SchemaVersion, timeProvider.GetUtcNow(), items);
    }

    // What Content Sync needs on this instance but doesn't transfer, so the source can warn before a
    // sync fails. Only a caller holding Content Sync's secret, who can already push content here,
    // gets the list.
    [HttpGet("required-objects")]
    public async Task<ActionResult<RequiredObjectsResponse>> GetRequiredObjects(CancellationToken cancellationToken)
    {
        var objects = await requiredObjectsService.GetRequiredObjectsAsync(cancellationToken);
        return new RequiredObjectsResponse(SchemaVersion, timeProvider.GetUtcNow(), objects);
    }
}
