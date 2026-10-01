namespace XperienceCommunity.ContentSyncToolkit;

/// <summary>
/// The toolkit's own settings. The toolkit complements Xperience's Content Sync and has no source
/// or target configuration of its own: where the target is, the shared secret, and whether this
/// instance is a source or a target all come from Xperience's
/// <c>CMS.ContentSynchronization.ContentSynchronizationOptions</c> (see
/// <see cref="IContentSyncToolkitSettings"/>). Only what Content Sync has no equivalent for is
/// configured here.
/// </summary>
public sealed class ContentSyncToolkitOptions
{
    /// <summary>
    /// Timeout applied to inventory requests sent to the target instance.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a fetched remote inventory is cached before a repeat request re-fetches it. Local
    /// inventory is never cached; only the cross-instance call is.
    /// </summary>
    public TimeSpan InventoryCacheDuration { get; set; } = TimeSpan.FromSeconds(90);
}
