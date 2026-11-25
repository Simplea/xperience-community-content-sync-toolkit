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

    /// <summary>
    /// Whether pages in a different position among their siblings on the target show as Reordered.
    /// Off by default: the order only matters on a site that displays pages in tree order, such as
    /// a menu built from the page tree, and a partial sync often changes it. Turn it on for such a
    /// site. A page under a different parent always shows as Moved.
    /// </summary>
    public bool ComparePageOrder { get; set; }

    /// <summary>
    /// Whether the status pages offer Sync actions: a Sync button per row, and Sync for the selected rows. On by
    /// default; editors also need Xperience's Synchronize permission for the workspace or the pages.
    /// Turn it off to keep the status pages read-only. See docs/specs/sync-actions.md.
    /// </summary>
    public bool SyncActions { get; set; } = true;

    /// <summary>
    /// A name for this instance, such as "Staging", shown with its host at the top of the status pages.
    /// Optional; without it the host alone is shown. See docs/specs/target-banner.md.
    /// </summary>
    public string? InstanceLabel { get; set; }

    /// <summary>
    /// A name for the target, such as "Production", shown with its host at the top of the status pages
    /// and in the Sync confirmations. Optional; without it the host alone is shown.
    /// </summary>
    public string? TargetLabel { get; set; }
}
