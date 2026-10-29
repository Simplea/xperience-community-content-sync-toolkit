using CMS.ContentSynchronization;

using Microsoft.Extensions.Options;

namespace XperienceCommunity.ContentSyncToolkit;

/// <summary>Source settings, from Xperience's Content Sync source configuration.</summary>
/// <param name="TargetUrl">Where the target is; this instance acts as a source only when set.</param>
/// <param name="Secret">Sent to the target with every inventory request.</param>
/// <param name="RequestTimeout">Timeout for requests to the target (the toolkit's own setting).</param>
internal sealed record EffectiveSourceSettings(Uri? TargetUrl, string? Secret, TimeSpan RequestTimeout)
{
    /// <summary>
    /// The target's administration, ending in a slash so admin paths resolve under it; null when
    /// this instance isn't a source.
    /// </summary>
    public Uri? TargetAdminUrl { get; init; }
}

/// <summary>Target settings, from Xperience's Content Sync target configuration.</summary>
/// <param name="Enabled">Whether inventory requests are answered.</param>
/// <param name="Secret">Required on every inventory request.</param>
internal sealed record EffectiveTargetSettings(bool Enabled, string? Secret);

/// <summary>
/// Reads the toolkit's source and target settings from Xperience's Content Sync configuration
/// (<see cref="ContentSynchronizationOptions"/>): the toolkit complements Content Sync, so it
/// compares against exactly the target Content Sync pushes to, with the same secret, and has no
/// duplicate configuration of its own. Using that secret on the inventory endpoint adds no
/// exposure: whoever holds it can already push content to the target.
/// </summary>
internal interface IContentSyncToolkitSettings
{
    public EffectiveSourceSettings Source { get; }

    public EffectiveTargetSettings Target { get; }
}

internal sealed class ContentSyncToolkitSettings(
    IOptions<ContentSyncToolkitOptions> toolkitOptions,
    IOptions<ContentSynchronizationOptions> contentSynchronizationOptions) : IContentSyncToolkitSettings
{
    public EffectiveSourceSettings Source => ResolveSource(toolkitOptions.Value, contentSynchronizationOptions.Value);

    public EffectiveTargetSettings Target => ResolveTarget(contentSynchronizationOptions.Value);

    // A source only when Content Sync's source role is enabled with a usable target URL.
    internal static EffectiveSourceSettings ResolveSource(ContentSyncToolkitOptions toolkit, ContentSynchronizationOptions contentSync)
    {
        var source = contentSync.Source?.Enabled == true ? contentSync.Source : null;

        var targetUrl = ToAbsoluteUri(source?.TargetUrl);

        return new EffectiveSourceSettings(
            targetUrl,
            NullIfEmpty(source?.Secret),
            toolkit.RequestTimeout)
        {
            TargetAdminUrl = targetUrl is null ? null : ResolveTargetAdminUrl(toolkit, targetUrl),
        };
    }

    // Xperience's administration path, the same on every instance.
    private const string AdminPath = "admin";

    // The toolkit's TargetAdminUrl, or the target's /admin.
    private static Uri ResolveTargetAdminUrl(ContentSyncToolkitOptions toolkit, Uri targetUrl) =>
        WithTrailingSlash(ToAbsoluteUri(toolkit.TargetAdminUrl) ?? new Uri(WithTrailingSlash(targetUrl), AdminPath));

    // A base URL must end in a slash for a relative path to resolve under it rather than replace its
    // last segment.
#pragma warning disable S1075 // URL syntax, not an environment-specific path.
    private static Uri WithTrailingSlash(Uri uri) =>
        uri.AbsolutePath.EndsWith('/') ? uri : new UriBuilder(uri) { Path = uri.AbsolutePath + '/' }.Uri;
#pragma warning restore S1075

    // A target exactly when Content Sync's target role is enabled.
    internal static EffectiveTargetSettings ResolveTarget(ContentSynchronizationOptions contentSync)
    {
        var target = contentSync.Target?.Enabled == true ? contentSync.Target : null;

        return new EffectiveTargetSettings(target is not null, NullIfEmpty(target?.Secret));
    }

    // HTTP(S) only: on Linux, a rooted path such as "/admin" also parses as an absolute (file) URI.
    private static Uri? ToAbsoluteUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
