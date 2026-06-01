namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum StockMarketDataset
{
    Instruments,
    IntradayTrades,
    DailyTrades,
    IntradayIndices,
    HistoricalDailyIndices
}

public sealed record StockMarketSyncResult(
    StockMarketDataset Dataset,
    int RowsRead,
    int RowsPersisted,
    DateTimeOffset? AdvancedWatermark,
    TimeSpan Duration);

public interface IStockMarketDbSyncService
{
    Task<StockMarketSyncResult> SynchronizeAsync(
        StockMarketDataset dataset,
        bool fullReload,
        CancellationToken cancellationToken);
}

public sealed record StockMarketSyncState(
    StockMarketDataset Dataset,
    DateTimeOffset? Watermark,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunCompletedAt);

public interface IStockMarketDbSyncStateReader
{
    Task<IReadOnlyCollection<StockMarketSyncState>> QueryAsync(CancellationToken cancellationToken);
}

public sealed record StockMarketHistoryRetentionResult(
    int IntradayTradesDeleted,
    int IntradayIndicesDeleted);

public interface IStockMarketHistoryRetentionService
{
    Task<StockMarketHistoryRetentionResult> DeleteExpiredAsync(CancellationToken cancellationToken);
}
