using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

/// <summary>
/// Feature 148 CyclicalWaves v2 synchronization. Always hosted; <c>IngestionEnabled</c> gates all work and
/// <c>DailySyncEnabled</c> gates the schedule. Runs a startup catch-up when the last scheduled Full run was
/// missed, a Full run at every <c>DailySyncCronUtc</c> occurrence (default 23:30 UTC), and Admin-requested
/// runs from the queue. The Chat provider setting has no effect here.
/// </summary>
public sealed class CyclicalWavesV2SyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CyclicalWavesV2Options> options,
    TimeProvider timeProvider,
    ILogger<CyclicalWavesV2SyncWorker> logger) : BackgroundService
{
    private readonly string _lockOwner = $"worker:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.IngestionEnabled)
        {
            logger.LogInformation("CyclicalWaves v2 ingestion is disabled.");
            return;
        }

        var schedule = CyclicalWavesUtcCronSchedule.Parse(settings.DailySyncCronUtc);
        var pollInterval = TimeSpan.FromSeconds(settings.RequestedRunPollSeconds);
        DateTimeOffset? nextScheduled = null;

        if (settings.DailySyncEnabled)
        {
            await RunStartupCatchUpAsync(schedule, stoppingToken);
            nextScheduled = schedule.GetNextOccurrence(timeProvider.GetUtcNow());
            logger.LogInformation("CyclicalWaves v2 daily sync scheduled. NextRunUtc={NextRunUtc}", nextScheduled);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessRequestedRunsAsync(stoppingToken);

            var now = timeProvider.GetUtcNow();
            if (nextScheduled is { } due && now >= due)
            {
                await RunAsync(CyclicalWavesV2SyncTrigger.Scheduled, stoppingToken);
                nextScheduled = schedule.GetNextOccurrence(timeProvider.GetUtcNow());
                logger.LogInformation("CyclicalWaves v2 daily sync scheduled. NextRunUtc={NextRunUtc}", nextScheduled);
                continue;
            }

            var wait = nextScheduled is { } next && next - now < pollInterval ? next - now : pollInterval;
            await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, timeProvider, stoppingToken);
        }
    }

    /// <summary>Runs a Full sync once when no Full run has completed since the previous scheduled occurrence.</summary>
    internal async Task RunStartupCatchUpAsync(CyclicalWavesUtcCronSchedule schedule, CancellationToken stoppingToken)
    {
        var now = timeProvider.GetUtcNow();
        var previousOccurrence = PreviousOccurrence(schedule, now);
        DateTimeOffset? lastFull;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            lastFull = await scope.ServiceProvider.GetRequiredService<ICyclicalWavesV2SyncRunStore>()
                .GetLastCompletedFullRunStartAsync(stoppingToken);
        }

        if (previousOccurrence is not null && (lastFull is null || lastFull < previousOccurrence))
        {
            logger.LogInformation(
                "CyclicalWaves v2 catch-up: the last completed Full run ({LastFullRunUtc}) is older than the scheduled run at {ScheduledUtc}.",
                lastFull,
                previousOccurrence);
            await RunAsync(CyclicalWavesV2SyncTrigger.StartupCatchUp, stoppingToken);
        }
    }

    internal async Task ProcessRequestedRunsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var runs = scope.ServiceProvider.GetRequiredService<ICyclicalWavesV2SyncRunStore>();
            var now = timeProvider.GetUtcNow();
            var expired = await runs.ExpireStaleLeasesAsync(now, stoppingToken);
            if (expired > 0)
            {
                logger.LogWarning("CyclicalWaves v2 marked {Count} run(s) failed because their lease expired.", expired);
            }

            var claimed = await runs.ClaimNextRequestedAsync(_lockOwner, now, now.AddMinutes(options.Value.LeaseMinutes), stoppingToken);
            if (claimed is null)
            {
                return;
            }

            logger.LogInformation(
                "CyclicalWaves v2 requested run started. RunId={RunId} Scope={Scope} RequestedBy={RequestedBy}",
                claimed.RunId,
                claimed.Request.Scope,
                claimed.Request.RequestedBy);
            var outcome = await scope.ServiceProvider.GetRequiredService<ICyclicalWavesV2SyncCoordinator>()
                .RunClaimedAsync(claimed, stoppingToken);
            LogOutcome(outcome);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "CyclicalWaves v2 requested-run processing failed.");
        }
    }

    private async Task RunAsync(CyclicalWavesV2SyncTrigger trigger, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<ICyclicalWavesV2SyncCoordinator>().RunAsync(
                new CyclicalWavesV2SyncRequest(CyclicalWavesV2SyncScope.Full, null, null, trigger, "system"),
                _lockOwner,
                stoppingToken);
            LogOutcome(outcome);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "CyclicalWaves v2 {Trigger} run failed.", trigger);
        }
    }

    private void LogOutcome(CyclicalWavesV2SyncOutcome outcome) =>
        logger.LogInformation(
            "CyclicalWaves v2 run finished. RunId={RunId} Status={Status} Total={Total} Updated={Updated} Unchanged={Unchanged} Failed={Failed}",
            outcome.RunId,
            outcome.Status,
            outcome.Counts.TotalItems,
            outcome.Counts.UpdatedItems,
            outcome.Counts.UnchangedItems,
            outcome.Counts.FailedItems);

    /// <summary>The latest occurrence at or before <paramref name="now"/>, searching back up to eight days.</summary>
    internal static DateTimeOffset? PreviousOccurrence(CyclicalWavesUtcCronSchedule schedule, DateTimeOffset now)
    {
        DateTimeOffset? previous = null;
        var cursor = now.AddDays(-8);
        while (true)
        {
            var next = schedule.GetNextOccurrence(cursor);
            if (next > now)
            {
                return previous;
            }

            previous = next;
            cursor = next;
        }
    }
}
