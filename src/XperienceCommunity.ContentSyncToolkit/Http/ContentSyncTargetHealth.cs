using System.Collections.Concurrent;

using CMS.Core;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>How a request to the target failed; see <see cref="ContentInventoryFetchStatus"/>.</summary>
internal enum ContentSyncTargetFailure
{
    Unreachable,
    Rejected,
    Error,
}

/// <summary>
/// Writes the source's Event log entries about its Content Sync target: when the lists stop getting an
/// answer, why, and when they get one again, and when the target runs an older toolkit. Entries are
/// written when something changes, never on every request. See docs/specs/event-log-diagnostics.md.
/// </summary>
internal interface IContentSyncTargetHealth
{
    /// <summary>A list request (pages or content hub items) failed.</summary>
    public void ListFailed(Uri target, ContentSyncTargetFailure failure, string detail);

    /// <summary>A list request got the target's data, sent with <paramref name="schemaVersion"/>.</summary>
    public void ListAnswered(Uri target, int schemaVersion);

    /// <summary>
    /// An endpoint added after the first release answered 404. Only meaningful while the lists get an
    /// answer: then the target is older, not refusing.
    /// </summary>
    public void FeatureMissing(Uri target, string feature);
}

internal sealed class ContentSyncTargetHealth(IEventLogService eventLogService) : IContentSyncTargetHealth
{
    internal const string EventSource = "ContentSyncToolkit";

    private sealed class State
    {
        public ContentSyncTargetFailure? Failing;
        public string? FailingStatus;
        public bool ListsAnswered;
        public bool OlderLogged;
        public HashSet<string> MissingLogged { get; } = new(StringComparer.Ordinal);
    }

    // In memory: a restart starts fresh, so a problem that survives it is logged again.
    private readonly ConcurrentDictionary<string, State> states = new(StringComparer.OrdinalIgnoreCase);

    public void ListFailed(Uri target, ContentSyncTargetFailure failure, string detail)
    {
        var state = StateOf(target);
        lock (state)
        {
            state.ListsAnswered = false;

            // A different status is a different problem; a connection error's text varies from one attempt to the next.
            string? status = failure == ContentSyncTargetFailure.Rejected ? detail : null;
            if (state.Failing == failure && state.FailingStatus == status)
            {
                return;
            }

            state.Failing = failure;
            state.FailingStatus = status;
        }

        eventLogService.LogWarning(EventSource, FailureCode(failure), FailureText(Name(target), failure, detail));
    }

    public void ListAnswered(Uri target, int schemaVersion)
    {
        var state = StateOf(target);
        bool recovered, older;
        lock (state)
        {
            recovered = state.Failing is not null;
            state.Failing = null;
            state.FailingStatus = null;
            state.ListsAnswered = true;
            older = schemaVersion < ContentSyncToolkitConstants.SchemaVersion && !state.OlderLogged;
            state.OlderLogged |= older;
        }

        if (recovered)
        {
            eventLogService.LogInformation(EventSource, "TARGETRECOVERED", $"The Content Sync target {Name(target)} answers again.");
        }

        if (older)
        {
            eventLogService.LogInformation(EventSource, "TARGETOLDER",
                $"The Content Sync target {Name(target)} runs an older version of the Content Sync Toolkit (data version {schemaVersion}; " +
                $"this instance uses {ContentSyncToolkitConstants.SchemaVersion}). The lists still work, but some details need the same " +
                "version on both instances. Update the toolkit on the target to the same version as this instance.");
        }
    }

    public void FeatureMissing(Uri target, string feature)
    {
        var state = StateOf(target);
        lock (state)
        {
            // While the lists fail too, the 404 is the target refusing everything: TARGETREJECTED covers it.
            if (!state.ListsAnswered || !state.MissingLogged.Add(feature))
            {
                return;
            }
        }

        eventLogService.LogInformation(EventSource, "TARGETOLDER",
            $"The Content Sync target {Name(target)} doesn't support {feature} (it answered 404 Not Found), so it most likely runs an older " +
            "version of the Content Sync Toolkit. The lists still work without it. Update the toolkit on the target to the same version as this instance.");
    }

    private State StateOf(Uri target) => states.GetOrAdd(Name(target), _ => new State());

    // The configured address, which administrators know; never a query string.
    private static string Name(Uri target) => target.GetLeftPart(UriPartial.Path).TrimEnd('/');

    internal static string FailureCode(ContentSyncTargetFailure failure) => failure switch
    {
        ContentSyncTargetFailure.Unreachable => "TARGETUNREACHABLE",
        ContentSyncTargetFailure.Rejected => "TARGETREJECTED",
        ContentSyncTargetFailure.Error or _ => "TARGETERROR",
    };

    private static string FailureText(string target, ContentSyncTargetFailure failure, string detail) => failure switch
    {
        ContentSyncTargetFailure.Unreachable =>
            $"The Content Sync target {target} didn't answer: {detail.TrimEnd('.')}. Check that it's running and that this instance can reach it. " +
            "Content sync status can't be shown until it answers.",
        ContentSyncTargetFailure.Rejected when detail.StartsWith("404", StringComparison.Ordinal) =>
            $"The Content Sync target {target} answered {detail}. Check that both instances use the same Content Sync secret, that Content Sync's " +
            "target role is enabled there, and that the target has the Content Sync Toolkit installed. If it's the secret or the target role, the " +
            "target's Event log says which.",
        ContentSyncTargetFailure.Rejected =>
            $"The Content Sync target {target} answered {detail}. The target's Event log may say why. Content sync status can't be shown until it answers.",
        ContentSyncTargetFailure.Error or _ =>
            $"The Content Sync target {target} answered, but not with the Content Sync Toolkit's data. Check that the target URL points to the " +
            "Xperience application, not to a proxy or another site.",
    };
}
