using CMS.Core;

namespace XperienceCommunity.ContentSyncToolkit.Http;

/// <summary>
/// The target's Event log entry for a toolkit request it refused: why, from where, and how many more since
/// the previous entry. At most one entry per reason every <see cref="ContentSyncRefusedRequestLog.Period"/>, so a misconfigured source,
/// or someone sending wrong secrets, can't flood the log. See docs/specs/event-log-diagnostics.md.
/// </summary>
internal interface IContentSyncRefusedRequestLog
{
    public void Refused(ContentSyncTargetSecretCheck reason, string? remoteAddress);
}

internal sealed class ContentSyncRefusedRequestLog(IEventLogService eventLogService, TimeProvider timeProvider) : IContentSyncRefusedRequestLog
{
    internal static readonly TimeSpan Period = TimeSpan.FromMinutes(10);

    private sealed class State
    {
        public DateTimeOffset? LastLogged;
        public int Unlogged;
    }

    private readonly Dictionary<ContentSyncTargetSecretCheck, State> states = [];
    private readonly object gate = new();

    public void Refused(ContentSyncTargetSecretCheck reason, string? remoteAddress)
    {
        var now = timeProvider.GetUtcNow();
        int alsoRefused;
        lock (gate)
        {
            if (!states.TryGetValue(reason, out var state))
            {
                states[reason] = state = new State();
            }

            if (state.LastLogged is { } last && now - last < Period)
            {
                state.Unlogged++;
                return;
            }

            alsoRefused = state.Unlogged;
            state.Unlogged = 0;
            state.LastLogged = now;
        }

        string text = $"A Content Sync Toolkit request from {(string.IsNullOrEmpty(remoteAddress) ? "an unknown address" : remoteAddress)} " +
            $"was refused: {Why(reason)}";
        if (alsoRefused > 0)
        {
            text += $" {alsoRefused} more {(alsoRefused == 1 ? "request was" : "requests were")} refused for the same reason since the previous entry.";
        }

        eventLogService.LogWarning(ContentSyncTargetHealth.EventSource, "REQUESTREJECTED", text);
    }

    private static string Why(ContentSyncTargetSecretCheck reason) => reason switch
    {
        ContentSyncTargetSecretCheck.TargetRoleOff =>
            "Content Sync's target role isn't enabled on this instance (ContentSynchronization:Target:Enabled).",
        ContentSyncTargetSecretCheck.NoSecretConfigured =>
            "this instance has no Content Sync target secret configured (ContentSynchronization:Target:Secret).",
        ContentSyncTargetSecretCheck.WrongSecret =>
            "its Content Sync secret doesn't match this instance's. Use the same secret on both instances.",
        ContentSyncTargetSecretCheck.Valid or _ => "it didn't pass the Content Sync secret check.",
    };
}
