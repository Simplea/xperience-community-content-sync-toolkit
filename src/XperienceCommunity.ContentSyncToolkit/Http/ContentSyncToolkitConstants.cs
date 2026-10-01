namespace XperienceCommunity.ContentSyncToolkit.Http;

internal static class ContentSyncToolkitConstants
{
    public const string ControllerRoute = "xperience-community/content-sync-toolkit/inventory";
    public const string SecretHeaderName = "X-ContentSyncToolkit-Secret";

    // The wire schema both endpoints send. 2: publish dates in UTC, and page order. 3: display
    // names, and the required-objects endpoint. See ContentInventoryResponse.
    public const int SchemaVersion = 3;
}
