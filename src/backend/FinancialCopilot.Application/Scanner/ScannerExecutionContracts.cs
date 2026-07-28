using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.ValueObjects;

namespace FinancialCopilot.Application.Scanner;

public enum ScannerColumnType
{
    Symbol,
    CompanyName,
    LatestPrice,
    DailyChangePercent,
    MarketCap,
    Metric
}

public enum CellFreshnessStatus
{
    Live,
    PreviousTradingDay,
    Persisted,
    Missing
}

public sealed record ScannerTableColumn(
    string Identifier,
    string DisplayName,
    ScannerColumnType ColumnType,
    string? MetricCode = null);

public sealed record ScannerTableCell(
    decimal? Value,
    string? FormattedValue,
    CellFreshnessStatus FreshnessStatus,
    DateTimeOffset? SourceTimestamp,
    DateOnly? TradingDate = null,
    string? TradingDatePersian = null,
    string? SourceLabel = null);

public sealed record ScannerTableRow(
    string SymbolCode,
    string? CompanyName,
    IReadOnlyDictionary<string, ScannerTableCell> Cells,
    double Score,
    IReadOnlyCollection<string> MatchedConditionMetrics,
    /// <summary>
    /// Physical source that owns this symbol's company row (e.g. <c>NoavaranArchiveSql</c>). Optional;
    /// surfaced so explainable answers can cite archive provenance for historical rows (spec 052 AC #10).
    /// </summary>
    string? SourceProvider = null,
    string? ExternalCompanyId = null);

public sealed record ScannerExecutionFacts(
    DateTimeOffset ExecutedAt,
    TimeSpan Duration,
    int TotalSymbolsEvaluated,
    int MatchingSymbolCount,
    bool FromCache,
    int Page = 1,
    int PageSize = 20,
    int TotalPages = 1);

public sealed record ScannerTableResult(
    Guid PlanId,
    IReadOnlyCollection<ScannerTableColumn> Columns,
    IReadOnlyCollection<ScannerTableRow> Rows,
    ScannerExecutionFacts ExecutionFacts,
    IReadOnlyCollection<string> MissingDataWarnings);

public sealed record ScannerExecutionRequest(
    ScannerQueryPlan Plan,
    DateOnly AsOf,
    int Page = 1,
    int PageSize = 20,
    // Optional execution-time context used only for missing-answer feedback collection (spec 028).
    // Null values disable feedback for this call; supplied by the AI facade orchestrator.
    string? ActorId = null,
    string? QueryText = null,
    ScannerUniverseScope? Universe = null);

public sealed record ScannerUniverseScope(
    string? IndustryCode = null,
    string? InstrumentClass = null,
    int MaximumSymbols = 5_000);

public interface IScannerExecutionService
{
    Task<ScannerTableResult> ExecuteAsync(
        ScannerExecutionRequest request,
        CancellationToken cancellationToken);
}

public interface IScannerResultColumnPolicy
{
    IReadOnlyCollection<ScannerTableColumn> BuildColumns(ScannerQueryPlan plan);
}

public interface IScannerResultRanker
{
    IReadOnlyCollection<ScannerTableRow> Rank(
        IReadOnlyCollection<ScannerTableRow> rows,
        ScannerQueryPlan plan);
}

public interface IMarketQuoteResolver
{
    Task<BatchMarketQuoteResult> ResolveAsync(
        IReadOnlyCollection<SymbolCode> symbols,
        CancellationToken cancellationToken);
}
