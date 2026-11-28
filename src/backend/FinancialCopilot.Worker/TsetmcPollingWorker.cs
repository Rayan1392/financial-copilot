using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Options;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class TsetmcPollingOptions
{
    public const string SectionName = "TsetmcPolling";

    public bool Enabled { get; init; }

    /// <summary>How often the intraday loop wakes to check whether it should fire (seconds).</summary>
    public int IntradayTickSeconds { get; init; } = 30;

    /// <summary>Minimum gap between consecutive intraday syncs (seconds). Default 60s.</summary>
    public int IntradayIntervalSeconds { get; init; } = 60;

    /// <summary>Feature 167: poll option contract terms and open interest. Off by default.</summary>
    public bool OptionContractsEnabled { get; init; }

    /// <summary>Minimum gap between option contract syncs inside the intraday window (seconds).</summary>
    public int OptionContractsIntervalSeconds { get; init; } = 300;

    /// <summary>Feature 167: poll the option order book. Off by default.</summary>
    public bool OptionOrderBookEnabled { get; init; }

    /// <summary>Minimum gap between option order-book syncs inside the intraday window (seconds).</summary>
    public int OptionOrderBookIntervalSeconds { get; init; } = 60;

    public const int MinimumIntervalSeconds = 10;

    public bool HasValidIntervals =>
        IntradayTickSeconds > 0 && IntradayIntervalSeconds > 0 &&
        OptionContractsIntervalSeconds >= MinimumIntervalSeconds &&
        OptionOrderBookIntervalSeconds >= MinimumIntervalSeconds;
}

/// <summary>
/// Polls the TSETMC web service on the Iranian equity market schedule:
///
/// Intraday trades + intraday indices
///   — Saturday through Wednesday, 09:00–12:30 IRST, every <see cref="TsetmcPollingOptions.IntradayIntervalSeconds"/> seconds.
///
/// Daily trades + daily indices (end-of-day snapshots)
///   — Saturday through Tuesday: fired once at 17:00 IRST.
///   — Wednesday: fired once at 20:00 IRST.
///
/// Option contracts (terms, open interest) and option order book — Feature 167
///   — inside the intraday window, each on its own interval and only when its switch is on.
///   — contracts once more after the end-of-day instruments sync, so new listings get their terms.
///   Each runs in its own scope; a failure is logged and does not delay trades, indices or the other.
///
/// IRST = Iran Standard Time = UTC+03:30.
/// All schedule evaluation is done in IRST so Daylight Saving does not apply
/// (Iran does not observe DST since 2005).
/// </summary>
public sealed class TsetmcPollingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TsetmcPollingOptions> pollingOptions,
    TimeProvider timeProvider,
    ILogger<TsetmcPollingWorker> logger) : BackgroundService
{
    // Iran Standard Time offset — fixed, no DST.
    private static readonly TimeSpan IrstOffset = TimeSpan.FromHours(3.5);
    private static readonly TimeOnly IntradayOpen = new(9, 0);
    private static readonly TimeOnly IntradayClose = new(12, 30);
    private static readonly TimeOnly EodWeekday = new(17, 0);   // Sat–Tue
    private static readonly TimeOnly EodWednesday = new(20, 0); // Wed

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!pollingOptions.Value.Enabled) return;

        DateTimeOffset nextIntraday = DateTimeOffset.MinValue;
        DateTimeOffset nextOptionContracts = DateTimeOffset.MinValue;
        DateTimeOffset nextOptionOrderBook = DateTimeOffset.MinValue;
        DateOnly lastEodDate = DateOnly.MinValue;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(
            Math.Max(10, pollingOptions.Value.IntradayTickSeconds)));

        do
        {
            var utcNow = timeProvider.GetUtcNow();
            var irstNow = utcNow.ToOffset(IrstOffset);
            var today = DateOnly.FromDateTime(irstNow.DateTime);
            var timeNow = TimeOnly.FromDateTime(irstNow.DateTime);
            var dow = irstNow.DayOfWeek; // Saturday=6, Sunday=0..Friday=5 in .NET DayOfWeek

            // ── Intraday: Sat(6)–Wed(3), 09:00–12:30 ──────────────────────────
            if (IsIranTradingDay(dow) && timeNow >= IntradayOpen && timeNow <= IntradayClose)
            {
                if (nextIntraday <= utcNow)
                {
                    await RunIntradayAsync(stoppingToken);
                    nextIntraday = timeProvider.GetUtcNow().AddSeconds(
                        Math.Max(10, pollingOptions.Value.IntradayIntervalSeconds));
                }

                if (pollingOptions.Value.OptionContractsEnabled && nextOptionContracts <= utcNow)
                {
                    await RunOptionContractsAsync(stoppingToken);
                    nextOptionContracts = timeProvider.GetUtcNow().AddSeconds(Math.Max(
                        TsetmcPollingOptions.MinimumIntervalSeconds, pollingOptions.Value.OptionContractsIntervalSeconds));
                }

                if (pollingOptions.Value.OptionOrderBookEnabled && nextOptionOrderBook <= utcNow)
                {
                    await RunOptionDatasetAsync("OptionOrderBook", svc => svc.SynchronizeOrderBookAsync(stoppingToken), stoppingToken);
                    nextOptionOrderBook = timeProvider.GetUtcNow().AddSeconds(Math.Max(
                        TsetmcPollingOptions.MinimumIntervalSeconds, pollingOptions.Value.OptionOrderBookIntervalSeconds));
                }
            }
            else
            {
                // Reset so the next session starts immediately when the window opens.
                nextIntraday = DateTimeOffset.MinValue;
                nextOptionContracts = DateTimeOffset.MinValue;
                nextOptionOrderBook = DateTimeOffset.MinValue;
            }

            // ── End-of-day: once per calendar day, after the designated cut-off ─
            if (IsEodWindow(dow, timeNow) && lastEodDate != today)
            {
                await RunEodAsync(stoppingToken);
                if (pollingOptions.Value.OptionContractsEnabled)
                    await RunOptionContractsAsync(stoppingToken);
                lastEodDate = today;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true for the five trading days of the Iranian week: Sat(6)–Wed(3).
    /// Thu(4) and Fri(5) are the weekend.
    /// </summary>
    private static bool IsIranTradingDay(DayOfWeek dow) =>
        dow is DayOfWeek.Saturday or DayOfWeek.Sunday or DayOfWeek.Monday
               or DayOfWeek.Tuesday or DayOfWeek.Wednesday;

    /// <summary>
    /// End-of-day window: at or after 17:00 on Sat–Tue, at or after 20:00 on Wed.
    /// </summary>
    private static bool IsEodWindow(DayOfWeek dow, TimeOnly time) =>
        dow switch
        {
            DayOfWeek.Saturday or DayOfWeek.Sunday or DayOfWeek.Monday or DayOfWeek.Tuesday
                => time >= EodWeekday,
            DayOfWeek.Wednesday => time >= EodWednesday,
            _ => false
        };

    private async Task RunIntradayAsync(CancellationToken ct)
    {
        await RunDatasetAsync("IntradayTrades", async svc =>
        {
            if (!svc.IsOperational) return;
            var r = await svc.SynchronizeIntradayTradesAsync(ct);
            logger.LogInformation(
                "TSETMC intraday trades synced: {Fetched} fetched, {Persisted} persisted in {Duration}.",
                r.RowsFetched, r.RowsPersisted, r.Duration);
        }, ct);

        await RunDatasetAsync("IntradayIndices", async svc =>
        {
            if (!svc.IsOperational) return;
            var r = await svc.SynchronizeIntradayIndicesAsync(ct);
            logger.LogInformation(
                "TSETMC intraday indices synced: {Fetched} fetched, {Persisted} persisted in {Duration}.",
                r.RowsFetched, r.RowsPersisted, r.Duration);
        }, ct);
    }

    private async Task RunEodAsync(CancellationToken ct)
    {
        await RunDatasetAsync("Instruments", async svc =>
        {
            if (!svc.IsOperational) return;
            var r = await svc.SynchronizeInstrumentsAsync(ct);
            if (r.InstrumentCoverage is { IsComplete: false } coverage)
            {
                logger.LogWarning(
                    "TSETMC instruments sync incomplete: {Fetched} fetched, {Persisted} persisted, {SuccessfulFlows} successful flows, failed flows {FailedFlows}, {RejectedInstruments} rejected.",
                    r.RowsFetched, r.RowsPersisted, coverage.SuccessfulFlows,
                    string.Join(", ", coverage.FailedFlows), coverage.RejectedInstruments);
                return;
            }
            logger.LogInformation(
                "TSETMC instruments synced: {Fetched} fetched, {Persisted} persisted in {Duration}.",
                r.RowsFetched, r.RowsPersisted, r.Duration);
        }, ct);

        await RunDatasetAsync("DailyTrades", async svc =>
        {
            if (!svc.IsOperational) return;
            var r = await svc.SynchronizeDailyTradesAsync(ct);
            logger.LogInformation(
                "TSETMC daily trades synced: {Fetched} fetched, {Persisted} persisted in {Duration}.",
                r.RowsFetched, r.RowsPersisted, r.Duration);
        }, ct);

        await RunDatasetAsync("DailyIndices", async svc =>
        {
            if (!svc.IsOperational) return;
            var r = await svc.SynchronizeDailyIndicesAsync(ct);
            logger.LogInformation(
                "TSETMC daily indices synced: {Fetched} fetched, {Persisted} persisted in {Duration}.",
                r.RowsFetched, r.RowsPersisted, r.Duration);
        }, ct);
    }

    private Task RunOptionContractsAsync(CancellationToken ct) =>
        RunOptionDatasetAsync("OptionContracts", svc => svc.SynchronizeContractsAsync(ct), ct);

    /// <summary>
    /// Runs one option dataset in its own scope. The sync service logs its own counts; a failure
    /// (including the provider's 403 page) is logged here and swallowed so the loop keeps going.
    /// </summary>
    private async Task RunOptionDatasetAsync(
        string dataset,
        Func<IOptionMarketSyncService, Task> action,
        CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IOptionMarketSyncService>();
            if (!svc.IsOperational) return;
            await action(svc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TSETMC polling {Dataset} failed.", dataset);
        }
    }

    private async Task RunDatasetAsync(
        string dataset,
        Func<ITsetmcDirectFeedSyncService, Task> action,
        CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ITsetmcDirectFeedSyncService>();
            await action(svc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TSETMC polling {Dataset} failed.", dataset);
        }
    }
}
