namespace FinancialCopilot.Application.FinancialData.Ingestion;

// ---------------------------------------------------------------------------
// Query input
// ---------------------------------------------------------------------------

public enum MonthlyActivityTrendMeasure
{
    SalesAmount,
    ProductionQuantity,
    SalesQuantity
}

public sealed record MonthlyActivityTrendQuery(
    string UserMessage,
    string? SymbolOrCompanyName,
    int? LatestReportYear = null,
    int? LatestReportMonth = null,
    MonthlyActivityTrendMeasure Measure = MonthlyActivityTrendMeasure.SalesAmount,
    bool IncludeChartPayload = true);

// ---------------------------------------------------------------------------
// Chart point — one fiscal month across both years
// ---------------------------------------------------------------------------

public sealed record MonthlyActivityTrendChartPoint(
    int FiscalMonthIndex,
    string FiscalMonthNameFa,
    int? PreviousFiscalYear,
    decimal? PreviousFiscalYearSalesAmount,
    int? CurrentFiscalYear,
    decimal? CurrentFiscalYearSalesAmount,
    decimal? Average12MonthSalesAmount,
    bool IsCurrentYearReported,
    bool IsPreviousYearReported);

// ---------------------------------------------------------------------------
// Insight — a single deterministic observation derived from persisted values
// ---------------------------------------------------------------------------

public enum MonthlyActivityTrendInsightKind
{
    YoYGrowth,
    VsAverage12Month,
    YtdProgress,
    MissingData,
    DataQuality
}

public sealed record MonthlyActivityTrendInsight(
    MonthlyActivityTrendInsightKind Kind,
    string TextFa);

// ---------------------------------------------------------------------------
// Missing data point — flagged period with no persisted data
// ---------------------------------------------------------------------------

public sealed record MonthlyActivityTrendMissingDataPoint(
    int Year,
    int Month,
    string ReasonFa);

// ---------------------------------------------------------------------------
// Response
// ---------------------------------------------------------------------------

public sealed record MonthlyActivityTrendResponse(
    string CompanySymbol,
    string? CompanyName,
    int LatestReportYear,
    int LatestReportMonth,
    string UnitLabelFa,
    decimal? LatestMonthlySalesAmount,
    decimal? SameMonthPreviousYearSalesAmount,
    decimal? Average12MonthSalesAmount,
    decimal? SalesAmountYoYGrowthPercent,
    decimal? SalesVsAverage12MonthPercent,
    decimal? YtdSalesAmount,
    decimal? YtdPreviousMonthSalesAmount,
    IReadOnlyList<MonthlyActivityTrendChartPoint> ChartPoints,
    IReadOnlyList<MonthlyActivityTrendInsight> Insights,
    IReadOnlyList<MonthlyActivityTrendMissingDataPoint> MissingDataPoints,
    string SourceProviderName,
    DateTimeOffset CalculatedAtUtc);

// ---------------------------------------------------------------------------
// Use-case interface (Application owns it; Infrastructure implements)
// ---------------------------------------------------------------------------

public interface IMonthlyActivityTrendQueryUseCase
{
    Task<MonthlyActivityTrendResponse?> ExecuteAsync(
        MonthlyActivityTrendQuery query,
        CancellationToken ct = default);
}
