namespace XperienceCommunity.ContentSyncToolkit.Http;

internal static class ContentSyncToolkitConstants
{
    public const string ControllerRoute = "xperience-community/content-sync-toolkit/inventory";
    public const string SecretHeaderName = "X-ContentSyncToolkit-Secret";

    // The wire schema the endpoints send. 2: publish dates in UTC, and page order. 3: display
    // names, and the required-objects endpoint. 4: the item ID endpoints. See ContentInventoryResponse.
    public const int SchemaVersion = 4;

    // The most GUIDs one item ID request may ask about; the source sends one listing page.
    public const int MaxItemIdGuids = 100;
}
