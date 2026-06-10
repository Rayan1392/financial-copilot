using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class StockMarketDbPollingOptions
{
    public const string SectionName = "StockMarketDbPolling";

    public bool Enabled { get; init; }
    public int IntradayTradeIntervalSeconds { get; init; } = 60;
    public int IntradayIndexIntervalSeconds { get; init; } = 300;
    public int DailyTradeIntervalSeconds { get; init; } = 3600;
    public int InstrumentIntervalSeconds { get; init; } = 86400;
    public int RetentionIntervalSeconds { get; init; } = 86400;

    /// <summary>
    /// Upper bound on how many full pages a single poll may drain when the source has a
    /// backlog (continuation cursor pending). Bounds each cycle's duration so one dataset
    /// cannot monopolize the loop; the remainder drains on the next poll.
    /// </summary>
    public int MaxCatchUpPagesPerPoll { get; init; } = 50;
}

public sealed class StockMarketDbPollingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<StockMarketDbPollingOptions> options,
    IOptions<StockMarketDbProviderOptions> providerOptions,
    TimeProvider timeProvider,
    ILogger<StockMarketDbPollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;

        // Insertion order is the per-tick execution priority: intraday quotes and indices
        // feed LatestMarketQuotes for live AI answers, so they always run before the
        // slower-cadence daily/instrument dimensions.
        var next = new Dictionary<StockMarketDataset, DateTimeOffset>
        {
            [StockMarketDataset.IntradayTrades] = DateTimeOffset.MinValue,
            [StockMarketDataset.IntradayIndices] = DateTimeOffset.MinValue,
            [StockMarketDataset.DailyTrades] = DateTimeOffset.MinValue,
            [StockMarketDataset.Instruments] = DateTimeOffset.MinValue
        };
        var nextRetention = DateTimeOffset.MinValue;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            var now = timeProvider.GetUtcNow();
            if (nextRetention <= now)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var retention = scope.ServiceProvider.GetRequiredService<IStockMarketHistoryRetentionService>();
                    var result = await retention.DeleteExpiredAsync(stoppingToken);
                    logger.LogInformation(
                        "StockMarketDb retention deleted {TradeCount} trade and {IndexCount} index snapshots.",
                        result.IntradayTradesDeleted, result.IntradayIndicesDeleted);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "StockMarketDb retention failed.");
                }
                nextRetention = now.AddSeconds(Math.Max(30, options.Value.RetentionIntervalSeconds));
            }
            foreach (var dataset in next.Keys.ToArray())
            {
                if (next[dataset] > now) continue;
                try
                {
                    await DrainAsync(dataset, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (StockMarketUnresolvedInstrumentException exception) when (
                    dataset != StockMarketDataset.Instruments)
                {
                    // A row referenced an instrument registered after the last Instruments sync.
                    // Pull the instrument dimension forward instead of failing until its 24h cadence;
                    // the failed dataset page retries on its own next poll.
                    logger.LogWarning(
                        "StockMarketDb poll {Dataset} found {Count} unresolved instrument references; scheduling an immediate Instruments sync.",
                        dataset, exception.UnresolvedCount);
                    next[StockMarketDataset.Instruments] = DateTimeOffset.MinValue;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "StockMarketDb poll {Dataset} failed.", dataset);
                }
                next[dataset] = timeProvider.GetUtcNow().AddSeconds(IntervalSeconds(dataset));
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Synchronizes one dataset until the source is drained (a short page returns) or the
    /// per-poll page cap is reached. Each page runs in a fresh scope so the DbContext change
    /// tracker stays bounded; sync state (watermark + continuation cursor) is persisted per
    /// page, so an interrupted drain resumes exactly where it stopped.
    /// </summary>
    private async Task DrainAsync(StockMarketDataset dataset, CancellationToken stoppingToken)
    {
        var pageSize = providerOptions.Value.PageSize;
        var maxPages = Math.Max(1, options.Value.MaxCatchUpPagesPerPoll);
        var pages = 0;
        var totalRead = 0;
        var totalPersisted = 0;
        bool fullPage;
        do
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IStockMarketDbSyncService>();
            var result = await service.SynchronizeAsync(dataset, fullReload: false, stoppingToken);
            pages++;
            totalRead += result.RowsRead;
            totalPersisted += result.RowsPersisted;
            fullPage = result.RowsRead >= pageSize;
        }
        while (fullPage && pages < maxPages && !stoppingToken.IsCancellationRequested);

        if (fullPage && pages >= maxPages)
        {
            logger.LogWarning(
                "StockMarketDb poll {Dataset} hit the {MaxPages}-page catch-up cap with backlog remaining; continuing on next poll.",
                dataset, maxPages);
        }
        logger.LogInformation(
            "StockMarketDb poll {Dataset} persisted {Persisted}/{Read} rows across {Pages} page(s).",
            dataset, totalPersisted, totalRead, pages);
    }

    private int IntervalSeconds(StockMarketDataset dataset) =>
        Math.Max(30, dataset switch
        {
            StockMarketDataset.Instruments => options.Value.InstrumentIntervalSeconds,
            StockMarketDataset.IntradayTrades => options.Value.IntradayTradeIntervalSeconds,
            StockMarketDataset.IntradayIndices => options.Value.IntradayIndexIntervalSeconds,
            StockMarketDataset.DailyTrades => options.Value.DailyTradeIntervalSeconds,
            _ => 3600
        });
}
