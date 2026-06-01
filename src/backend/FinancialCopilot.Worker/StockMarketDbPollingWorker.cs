using FinancialCopilot.Application.FinancialData.Ingestion;
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
}

public sealed class StockMarketDbPollingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<StockMarketDbPollingOptions> options,
    TimeProvider timeProvider,
    ILogger<StockMarketDbPollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;

        var next = new Dictionary<StockMarketDataset, DateTimeOffset>
        {
            [StockMarketDataset.Instruments] = DateTimeOffset.MinValue,
            [StockMarketDataset.IntradayTrades] = DateTimeOffset.MinValue,
            [StockMarketDataset.IntradayIndices] = DateTimeOffset.MinValue,
            [StockMarketDataset.DailyTrades] = DateTimeOffset.MinValue
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
                    using var scope = scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IStockMarketDbSyncService>();
                    var result = await service.SynchronizeAsync(dataset, fullReload: false, stoppingToken);
                    logger.LogInformation(
                        "StockMarketDb poll {Dataset} persisted {Persisted}/{Read} rows.",
                        dataset, result.RowsPersisted, result.RowsRead);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "StockMarketDb poll {Dataset} failed.", dataset);
                }
                next[dataset] = now.AddSeconds(IntervalSeconds(dataset));
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
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
