using Microsoft.AspNetCore.Mvc;

using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// Target-side endpoint answering inventory requests from a source instance. Service-to-service,
/// not an authenticated Xperience administration user — see <see cref="ContentSyncTargetSecretFilter"/>.
/// </summary>
[ApiController]
[Route(ContentSyncToolkitConstants.ControllerRoute)]
[TypeFilter(typeof(ContentSyncTargetSecretFilter))]
public sealed class ContentInventoryController(
    ILocalContentInventoryService inventoryService,
    TimeProvider timeProvider) : ControllerBase
{
    private const int SchemaVersion = 1;

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
}
