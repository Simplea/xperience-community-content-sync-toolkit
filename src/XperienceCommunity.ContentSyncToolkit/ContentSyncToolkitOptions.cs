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

    /// <summary>
    /// Where editors open the target's administration, for links to items that exist only on the
    /// target. Defaults to <c>{TargetUrl}/admin</c>, from Content Sync's source settings; set it
    /// when that address isn't one editors' browsers can reach.
    /// </summary>
    public string? TargetAdminUrl { get; set; }
}
