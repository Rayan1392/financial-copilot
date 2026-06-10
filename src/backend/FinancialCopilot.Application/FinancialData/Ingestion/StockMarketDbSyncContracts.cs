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

/// <summary>
/// Thrown when a fact-dataset page references instruments missing from the instrument
/// dimension (registered at the source after the last Instruments sync). The page is not
/// persisted; callers should synchronize <see cref="StockMarketDataset.Instruments"/> and retry.
/// </summary>
public sealed class StockMarketUnresolvedInstrumentException(
    StockMarketDataset dataset,
    int unresolvedCount) : InvalidOperationException(
        $"{dataset} contained {unresolvedCount} unresolved instrument references. " +
        "Synchronize the instrument dimension before retrying this page.")
{
    public StockMarketDataset Dataset { get; } = dataset;
    public int UnresolvedCount { get; } = unresolvedCount;
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
