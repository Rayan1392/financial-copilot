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
    DateTimeOffset? LastRunCompletedAt,
    string? LogicalVendor = null,
    string? PhysicalSource = null,
    string? SourceMode = null);

public interface IStockMarketDbSyncStateReader
{
    Task<IReadOnlyCollection<StockMarketSyncState>> QueryAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Direct TSETMC web-service ingestion adapter (spec 054, Phase 2).
/// The interface lives in Application so Infrastructure and tests can depend on the abstraction.
/// <see cref="IsOperational"/> returns false until credentials and the endpoint are configured;
/// callers fall back to the StockMarketDB bridge when this is false.
/// </summary>
public interface ITsetmcDirectFeedSyncService
{
    /// <summary>
    /// Returns true only when the adapter is enabled and configured with credentials.
    /// </summary>
    bool IsOperational { get; }

    /// <summary>
    /// Synchronizes instruments (equity dimension) for all configured market flows.
    /// </summary>
    Task<TsetmcSyncResult> SynchronizeInstrumentsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fetches today's intraday trade snapshots for all configured market flows.
    /// </summary>
    Task<TsetmcSyncResult> SynchronizeIntradayTradesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fetches daily historical trade records for the configured date range, day by day.
    /// </summary>
    Task<TsetmcSyncResult> SynchronizeDailyTradesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fetches daily index values for the configured date range.
    /// </summary>
    Task<TsetmcSyncResult> SynchronizeDailyIndicesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fetches today's intraday index snapshots for all configured market flows.
    /// </summary>
    Task<TsetmcSyncResult> SynchronizeIntradayIndicesAsync(CancellationToken cancellationToken);
}

public sealed record TsetmcSyncResult(
    string Dataset,
    int RowsFetched,
    int RowsPersisted,
    TimeSpan Duration);

public sealed record StockMarketHistoryRetentionResult(
    int IntradayTradesDeleted,
    int IntradayIndicesDeleted);

public interface IStockMarketHistoryRetentionService
{
    Task<StockMarketHistoryRetentionResult> DeleteExpiredAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Determines which physical source feeds the latest market quote projection (spec 054, AC #8).
/// Phase 1 always returns <c>StockMarketDb</c>; Phase 4 cutover will flip this to
/// <c>TsetmcWebService</c> once the direct feed is validated and stable.
/// </summary>
public interface IMarketQuoteSourcePriority
{
    /// <summary>
    /// The physical source currently designated as the authoritative market-quote feed.
    /// </summary>
    string PrimarySourceName { get; }
}

/// <summary>Configuration options for <see cref="IMarketQuoteSourcePriority"/>.</summary>
public sealed class MarketQuoteSourcePriorityOptions
{
    public const string SectionName = "MarketQuoteSourcePriority";

    /// <summary>
    /// Stable persisted source name. Defaults to <c>StockMarketDb</c> (bridge phase).
    /// Set to <c>TsetmcWebService</c> for Phase 4 cutover.
    /// </summary>
    public string PrimarySourceName { get; set; } = "StockMarketDb";
}
