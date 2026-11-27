using System.Reflection;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>One side of the route chip: what it shows, and the host on hover when that's a label.</summary>
/// <param name="Name">The label, or the host shortened to <see cref="ContentSyncRoute.MaxHostLength"/>.</param>
/// <param name="Hover">The full host; empty when <paramref name="Name"/> already is it.</param>
/// <param name="IsLabel">Whether <paramref name="Name"/> is a label (shown in bold).</param>
internal sealed record ContentSyncRouteSide(string Name, string Hover, bool IsLabel);

/// <summary>What <c>GetRouteState</c> answers: the target's state, and whether to show the one-time line.</summary>
/// <param name="Target">"unknown", "answered", "unreachable", "rejected" or "error".</param>
/// <param name="SyncedRecently">This user has a sync from the lists that completed in the last 24 hours.</param>
/// <param name="SyncRunning">This user has a sync running: the chip asks again until it finishes.</param>
internal sealed record ContentSyncRouteState(string Target, bool SyncedRecently, bool SyncRunning);

/// <summary>
/// What the route chip above the status lists shows, and the feedback links next to it. See
/// docs/specs/route-chip-and-feedback.md.
/// </summary>
internal static class ContentSyncRoute
{
    internal const int MaxHostLength = 40;

#pragma warning disable S1075 // The toolkit's own GitHub Discussions, the same for every installation.
    internal const string FeedbackFormUrl =
        "https://github.com/Simplea/xperience-community-content-sync-toolkit/discussions/new?category=feedback";
#pragma warning restore S1075

    /// <summary>
    /// The pinned feedback discussion of this release, created before the package is built (see the
    /// release checklist). Empty for a build without one: the one-time line then opens the form.
    /// </summary>
    internal const string ReleaseFeedbackUrl = "";

    /// <summary>One side: the label when it's set, otherwise the host; the host on hover when it's hidden or shortened.</summary>
    public static ContentSyncRouteSide Side(string? host, string? label, string fallbackName)
    {
        string fullHost = string.IsNullOrWhiteSpace(host) ? string.Empty : host.Trim();
        if (!string.IsNullOrWhiteSpace(label))
        {
            return new ContentSyncRouteSide(label.Trim(), fullHost, IsLabel: true);
        }

        if (fullHost.Length == 0)
        {
            return new ContentSyncRouteSide(fallbackName, string.Empty, IsLabel: false);
        }

        string shown = ShortenHost(fullHost);
        return new ContentSyncRouteSide(shown, shown == fullHost ? string.Empty : fullHost, IsLabel: false);
    }

    /// <summary>A host longer than <see cref="MaxHostLength"/>, shortened in the middle, keeping its end.</summary>
    public static string ShortenHost(string host)
    {
        if (host.Length <= MaxHostLength)
        {
            return host;
        }

        // The end says which site it is (the domain), the start which environment.
        int tail = (MaxHostLength - 1) * 3 / 5;
        int head = MaxHostLength - 1 - tail;
        return string.Concat(host.AsSpan(0, head), "…", host.AsSpan(host.Length - tail));
    }

    /// <summary>The toolkit's version, without build metadata: "1.0.0-beta.6".</summary>
    public static string ToolkitVersion { get; } = VersionOf(typeof(ContentSyncRoute).Assembly);

    /// <summary>Xperience's version, from the CMS.Core assembly: "31.9.1".</summary>
    public static string KenticoVersion { get; } = typeof(CMS.Core.Service).Assembly.GetName().Version?.ToString(3) ?? string.Empty;

    /// <summary>Whether this Xperience version needs a caption to make room for the chip (30.x; see ConfigurePage).</summary>
    public static bool NeedsCaptionLine(string kenticoVersion) =>
        Version.TryParse(kenticoVersion, out var version) && version.Major < 31;

    /// <summary>The feedback form, with the two versions for its fields of the same IDs.</summary>
    public static string FeedbackUrl(string toolkitVersion, string kenticoVersion) =>
        $"{FeedbackFormUrl}&toolkit-version={Uri.EscapeDataString(toolkitVersion)}&kentico-version={Uri.EscapeDataString(kenticoVersion)}";

    /// <summary>Where the one-time line after a sync goes: the release's discussion, or the form without one.</summary>
    public static string AfterSyncUrl(string releaseFeedbackUrl, string feedbackUrl) =>
        string.IsNullOrWhiteSpace(releaseFeedbackUrl) ? feedbackUrl : releaseFeedbackUrl;

    /// <summary>Where the browser remembers that the line was dismissed: per version and user.</summary>
    public static string FeedbackStorageKey(string toolkitVersion, int userId) => $"cstk-feedback-{toolkitVersion}-{userId}";

    internal static string VersionOf(Assembly assembly)
    {
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? string.Empty;
        int metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }
}
