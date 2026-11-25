using System.Text;

using CMS.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Sync;

/// <summary>
/// Follows the syncs started from the status list until they complete or fail: reads their progress
/// every few seconds, writes the audit entry when they end, and clears the cached copy of the target's
/// lists so the next load shows the new statuses without Refresh. See docs/specs/sync-actions.md,
/// While a sync runs.
/// </summary>
internal sealed class ContentSyncProgressService(
    ContentSyncTracker tracker,
    IServiceScopeFactory scopeFactory,
    IContentInventoryCache inventoryCache,
    IEventLogService eventLogService,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan pollInterval = TimeSpan.FromSeconds(3);

    // Kentico can leave a sync waiting for a target that never picks it up; stop following it then.
    private static readonly TimeSpan giveUpAfter = TimeSpan.FromHours(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (tracker.Running.Count > 0)
            {
                try
                {
                    await PollAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    eventLogService.LogException(ContentSyncActionService.EventSource, "SYNCPROGRESS", ex);
                }
            }

            await Task.Delay(pollInterval, timeProvider, stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        // The engine wraps Kentico's scoped services, so each round gets its own scope.
        await using var scope = scopeFactory.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IContentSyncEngine>();
        bool anyFinished = false;

        foreach (var sync in tracker.Running)
        {
            // A scheduled sync has nothing to report until its time.
            if (sync.ScheduledRestore is { } when && when.ToUniversalTime() > timeProvider.GetUtcNow().UtcDateTime)
            {
                continue;
            }

            var progress = timeProvider.GetUtcNow() - sync.StartedAt > giveUpAfter
                ? new ContentSyncProgress(ContentSyncProgressState.TimedOut, "No answer", "The target didn't restore it in time.", [])
                : await engine.GetProgressAsync(sync.SyncGuid, cancellationToken);
            tracker.Update(sync.SyncGuid, progress);

            if (progress.IsFinished)
            {
                anyFinished = true;
                LogFinished(sync, progress);
            }
        }

        if (anyFinished)
        {
            inventoryCache.Clear();
        }
    }

    private void LogFinished(ContentSyncTrackedSync sync, ContentSyncProgress progress)
    {
        var text = new StringBuilder()
            .AppendLine($"Sync {sync.SyncGuid}, started from the Content Sync Toolkit's status list by {sync.UserName}, {(progress.Failed ? "failed" : "completed")}.")
            .AppendLine($"Status: {progress.State}. {progress.Title} {progress.Description}".TrimEnd());
        foreach (string error in progress.Errors)
        {
            text.AppendLine(error);
        }

        if (progress.Failed)
        {
            eventLogService.LogError(ContentSyncActionService.EventSource, "SYNCFAILED", text.ToString());
        }
        else
        {
            eventLogService.LogInformation(ContentSyncActionService.EventSource, "SYNCCOMPLETED", text.ToString());
        }
    }
}
