using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

/// <summary>
/// Feature 148 CyclicalWaves v2 synchronization. Always hosted; <c>IngestionEnabled</c> gates all work.
///
/// Full runs — gated by <c>DailySyncEnabled</c>: a startup catch-up when the last scheduled Full run was
/// missed, and a Full run at every <c>DailySyncCronUtc</c> occurrence (default 02:30 and 15:30 UTC, which is
/// 06:00 and 19:00 Tehran).
///
/// Market-hours runs — gated by <c>IntradaySyncEnabled</c>: gauge details and then equilibrium ranges at
/// every slot of <see cref="CyclicalWavesV2IntradaySchedule"/> (default Saturday to Wednesday, 09:00 to
/// 12:30 Tehran, every 30 minutes). A slot that is a whole interval late is dropped; slots are never caught
/// up after a restart.
///
/// Queued runs — Admin requests and CodalMirror announcement refreshes, drained on every poll.
///
/// All of these run one after another in this loop, so they never compete for the run lease with each
/// other. The Chat provider setting has no effect here.
/// </summary>
public sealed class CyclicalWavesV2SyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CyclicalWavesV2Options> options,
    TimeProvider timeProvider,
    ILogger<CyclicalWavesV2SyncWorker> logger) : BackgroundService
{
    /// <summary>Upper bound of queued runs handled in one poll, so a long queue cannot starve the schedules.</summary>
    internal const int MaximumQueuedRunsPerPoll = 50;

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

        var intraday = settings.IntradaySyncEnabled
            ? CyclicalWavesV2IntradaySchedule.Create(
                settings.IntradayWindowStartTehran, settings.IntradayWindowEndTehran, settings.IntradayIntervalMinutes)
            : null;
        DateTimeOffset? nextIntraday = intraday?.GetNextOccurrence(timeProvider.GetUtcNow());
        if (intraday is not null)
        {
            logger.LogInformation("CyclicalWaves v2 market-hours sync scheduled. NextSlotUtc={NextSlotUtc}", nextIntraday);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessRequestedRunsAsync(stoppingToken);

            var now = timeProvider.GetUtcNow();
            if (nextScheduled is { } due && now >= due)
            {
                await RunAsync(CyclicalWavesV2SyncScope.Full, CyclicalWavesV2SyncTrigger.Scheduled, stoppingToken);
                nextScheduled = schedule.GetNextOccurrence(timeProvider.GetUtcNow());
                logger.LogInformation("CyclicalWaves v2 daily sync scheduled. NextRunUtc={NextRunUtc}", nextScheduled);
                continue;
            }

            if (intraday is not null && nextIntraday is { } slot && now >= slot)
            {
                await RunIntradaySlotAsync(intraday, slot, stoppingToken);
                nextIntraday = intraday.GetNextOccurrence(timeProvider.GetUtcNow());
                logger.LogInformation("CyclicalWaves v2 market-hours sync scheduled. NextSlotUtc={NextSlotUtc}", nextIntraday);
                continue;
            }

            var wait = pollInterval;
            if (nextScheduled is { } nextFull && nextFull - now < wait) wait = nextFull - now;
            if (nextIntraday is { } nextSlot && nextSlot - now < wait) wait = nextSlot - now;
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
            await RunAsync(CyclicalWavesV2SyncScope.Full, CyclicalWavesV2SyncTrigger.StartupCatchUp, stoppingToken);
        }
    }

    /// <summary>
    /// One market-hours slot: gauge details, then equilibrium ranges. The second run starts whatever the first
    /// one's outcome. A slot that is a whole interval late (a long Full run or queue was in the way) is dropped,
    /// because the next slot is already due.
    /// </summary>
    internal async Task RunIntradaySlotAsync(
        CyclicalWavesV2IntradaySchedule schedule,
        DateTimeOffset slotUtc,
        CancellationToken stoppingToken)
    {
        var late = timeProvider.GetUtcNow() - slotUtc;
        if (late >= schedule.Interval)
        {
            logger.LogWarning(
                "CyclicalWaves v2 market-hours slot dropped. SlotUtc={SlotUtc} LateMinutes={LateMinutes}",
                slotUtc,
                (int)late.TotalMinutes);
            return;
        }

        logger.LogInformation("CyclicalWaves v2 market-hours slot started. SlotUtc={SlotUtc}", slotUtc);
        await RunAsync(CyclicalWavesV2SyncScope.GaugeDetails, CyclicalWavesV2SyncTrigger.Scheduled, stoppingToken);
        await RunAsync(CyclicalWavesV2SyncScope.EquilibriumDetails, CyclicalWavesV2SyncTrigger.Scheduled, stoppingToken);
    }

    /// <summary>Runs the queued runs that are due, oldest first, up to <see cref="MaximumQueuedRunsPerPoll"/>.</summary>
    internal async Task ProcessRequestedRunsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var expired = await scope.ServiceProvider.GetRequiredService<ICyclicalWavesV2SyncRunStore>()
                    .ExpireStaleLeasesAsync(timeProvider.GetUtcNow(), stoppingToken);
                if (expired > 0)
                {
                    logger.LogWarning("CyclicalWaves v2 marked {Count} run(s) failed because their lease expired.", expired);
                }
            }

            for (var handled = 0; handled < MaximumQueuedRunsPerPoll; handled++)
            {
                stoppingToken.ThrowIfCancellationRequested();
                await using var scope = scopeFactory.CreateAsyncScope();
                var runs = scope.ServiceProvider.GetRequiredService<ICyclicalWavesV2SyncRunStore>();
                var now = timeProvider.GetUtcNow();
                var claimed = await runs.ClaimNextRequestedAsync(_lockOwner, now, now.AddMinutes(options.Value.LeaseMinutes), stoppingToken);
                if (claimed is null)
                {
                    return;
                }

                logger.LogInformation(
                    "CyclicalWaves v2 requested run started. RunId={RunId} Scope={Scope} Ticker={Ticker} Trigger={Trigger} RequestedBy={RequestedBy}",
                    claimed.RunId,
                    claimed.Request.Scope,
                    claimed.Request.Ticker,
                    claimed.Request.Trigger,
                    claimed.Request.RequestedBy);
                var outcome = await scope.ServiceProvider.GetRequiredService<ICyclicalWavesV2SyncCoordinator>()
                    .RunClaimedAsync(claimed, stoppingToken);
                LogOutcome(outcome);
            }
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

    private async Task RunAsync(CyclicalWavesV2SyncScope scope, CyclicalWavesV2SyncTrigger trigger, CancellationToken stoppingToken)
    {
        try
        {
            await using var serviceScope = scopeFactory.CreateAsyncScope();
            var outcome = await serviceScope.ServiceProvider.GetRequiredService<ICyclicalWavesV2SyncCoordinator>().RunAsync(
                new CyclicalWavesV2SyncRequest(scope, null, null, trigger, "system"),
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
            logger.LogError(exception, "CyclicalWaves v2 {Trigger} {Scope} run failed.", trigger, scope);
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
