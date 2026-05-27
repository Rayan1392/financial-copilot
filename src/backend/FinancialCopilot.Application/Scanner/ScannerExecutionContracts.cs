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
    DateTimeOffset? SourceTimestamp);

public sealed record ScannerTableRow(
    string SymbolCode,
    string? CompanyName,
    IReadOnlyDictionary<string, ScannerTableCell> Cells,
    double Score,
    IReadOnlyCollection<string> MatchedConditionMetrics);

public sealed record ScannerExecutionFacts(
    DateTimeOffset ExecutedAt,
    TimeSpan Duration,
    int TotalSymbolsEvaluated,
    int MatchingSymbolCount,
    bool FromCache);

public sealed record ScannerTableResult(
    Guid PlanId,
    IReadOnlyCollection<ScannerTableColumn> Columns,
    IReadOnlyCollection<ScannerTableRow> Rows,
    ScannerExecutionFacts ExecutionFacts,
    IReadOnlyCollection<string> MissingDataWarnings);

public sealed record ScannerExecutionRequest(
    ScannerQueryPlan Plan,
    DateOnly AsOf,
    int MaxRows = 50);

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
