namespace XperienceCommunity.ContentSyncToolkit;

/// <summary>
/// Configuration for the content sync toolkit's source and target roles. An instance may act as
/// either, both, or neither, depending on which sub-options are configured.
/// </summary>
public sealed class ContentSyncToolkitOptions
{
    /// <summary>
    /// Configuration used when this instance calls a target instance's inventory endpoint.
    /// </summary>
    public ContentSyncToolkitSourceOptions Source { get; set; } = new();

    /// <summary>
    /// Configuration used when this instance answers inventory requests from a source instance.
    /// </summary>
    public ContentSyncToolkitTargetOptions Target { get; set; } = new();
}

/// <summary>
/// Source-side configuration: where the target instance is and how to authenticate to it.
/// </summary>
public sealed class ContentSyncToolkitSourceOptions
{
    /// <summary>
    /// Base URL of the target instance's content sync toolkit endpoint. This instance acts as a
    /// source only when this is set.
    /// </summary>
    public Uri? TargetUrl { get; set; }

    /// <summary>
    /// Shared secret sent to the target instance to authenticate inventory requests. Must match
    /// the target's <see cref="ContentSyncToolkitTargetOptions.Secret"/>.
    /// </summary>
    public string? Secret { get; set; }

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

/// <summary>
/// Target-side configuration: whether this instance accepts inbound inventory requests.
/// </summary>
public sealed class ContentSyncToolkitTargetOptions
{
    /// <summary>
    /// Whether this instance accepts inbound inventory requests. When <see langword="false"/>,
    /// every request is rejected identically to a request with a missing or incorrect secret.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Shared secret required on inbound inventory requests. Must match the calling source's
    /// <see cref="ContentSyncToolkitSourceOptions.Secret"/>.
    /// </summary>
    public string? Secret { get; set; }
}
