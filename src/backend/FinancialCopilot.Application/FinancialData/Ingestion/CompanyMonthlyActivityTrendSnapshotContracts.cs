namespace FinancialCopilot.Application.FinancialData.Ingestion;

// ---------------------------------------------------------------------------
// Upsert DTO (Application boundary — no ORM types)
// ---------------------------------------------------------------------------

public sealed record CompanyMonthlyActivityTrendSnapshotUpsertRow(
    Guid Id,
    string ExternalCompanyId,
    string? CompanySymbol,
    string? CompanyName,
    int? IndustryId,
    string? IndustryTitle,
    int? CategoryId,
    string? CategoryTitle,
    int ReportYear,
    byte ReportMonth,
    string? FiscalEndDate,
    int? FiscalYear,
    int? FiscalMonthIndex,
    string? FiscalMonthNameFa,
    int? CalendarYear,
    int? CalendarMonth,
    decimal MonthlySalesAmount,
    decimal? MonthlyProductionQuantity,
    decimal? MonthlySalesQuantity,
    decimal? MonthlyAverageSalesRate,
    bool HasMixedProductUnits,
    string? ProductUnitSummary,
    decimal? SameMonthPreviousYearSalesAmount,
    decimal? SameMonthPreviousYearProductionQuantity,
    decimal? SameMonthPreviousYearSalesQuantity,
    decimal? Average12MonthSalesAmount,
    int Average12MonthPeriodCount,
    decimal? YtdSalesAmount,
    decimal? YtdProductionQuantity,
    decimal? YtdSalesQuantity,
    decimal? YtdPreviousMonthSalesAmount,
    decimal? SalesAmountMomGrowthPercent,
    decimal? SalesAmountYoYGrowthPercent,
    decimal? ProductionQuantityYoYGrowthPercent,
    decimal? SalesQuantityYoYGrowthPercent,
    int? CurrentMonthOutputType,
    int? YtdOutputType,
    int? YtdPreviousMonthOutputType,
    string SourceProviderName,
    string? SourceReportId,
    string? SourceRawPayloadId,
    bool IsComparablePreviousYearAvailable,
    bool IsAverage12MonthComplete,
    decimal DataCompletenessScore,
    DateTimeOffset CalculatedAtUtc);

// ---------------------------------------------------------------------------
// Query response DTOs
// ---------------------------------------------------------------------------

public sealed record CompanyMonthlyActivityTrendSnapshot(
    string ExternalCompanyId,
    string? CompanySymbol,
    string? CompanyName,
    int ReportYear,
    byte ReportMonth,
    string? FiscalEndDate,
    int? FiscalYear,
    int? FiscalMonthIndex,
    string? FiscalMonthNameFa,
    decimal MonthlySalesAmount,
    decimal? MonthlyProductionQuantity,
    decimal? MonthlySalesQuantity,
    decimal? MonthlyAverageSalesRate,
    bool HasMixedProductUnits,
    string? ProductUnitSummary,
    decimal? SameMonthPreviousYearSalesAmount,
    decimal? SameMonthPreviousYearProductionQuantity,
    decimal? SameMonthPreviousYearSalesQuantity,
    decimal? Average12MonthSalesAmount,
    int Average12MonthPeriodCount,
    decimal? YtdSalesAmount,
    decimal? YtdPreviousMonthSalesAmount,
    decimal? SalesAmountMomGrowthPercent,
    decimal? SalesAmountYoYGrowthPercent,
    decimal? ProductionQuantityYoYGrowthPercent,
    decimal? SalesQuantityYoYGrowthPercent,
    string SourceProviderName,
    bool IsComparablePreviousYearAvailable,
    bool IsAverage12MonthComplete,
    decimal DataCompletenessScore,
    DateTimeOffset CalculatedAtUtc);

// ---------------------------------------------------------------------------
// Repository interface (Application owns it; Infrastructure implements)
// ---------------------------------------------------------------------------

public interface ICompanyMonthlyActivityTrendSnapshotRepository
{
    /// <summary>Upserts (replaces) the snapshot for one company/month atomically.</summary>
    Task UpsertAsync(
        CompanyMonthlyActivityTrendSnapshotUpsertRow row,
        CancellationToken ct = default);

    /// <summary>Returns snapshots for a company within a date range (inclusive).</summary>
    Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetCompanyTrendAsync(
        string externalCompanyId,
        int fromYear,
        int fromMonth,
        int toYear,
        int toMonth,
        CancellationToken ct = default);

    /// <summary>Returns the most recent snapshot for a company.</summary>
    Task<CompanyMonthlyActivityTrendSnapshot?> GetLatestAsync(
        string externalCompanyId,
        CancellationToken ct = default);

    /// <summary>Returns the latest N available snapshots for a company, newest first.</summary>
    Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetLatestAvailablePeriodsAsync(
        string externalCompanyId,
        int count,
        CancellationToken ct = default);

    /// <summary>Returns snapshots needed for the annual comparison chart centered on a reporting period.</summary>
    Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetAnnualComparisonBaseAsync(
        string externalCompanyId,
        int latestReportYear,
        int latestReportMonth,
        CancellationToken ct = default);
}

// ---------------------------------------------------------------------------
// Calculator interface
// ---------------------------------------------------------------------------

public interface ICompanyMonthlyActivityTrendSnapshotCalculator
{
    /// <summary>
    /// Calculates and persists the trend snapshot for one company/month using
    /// Noavaran Amin monthly report rows already stored in MonthlyReports and
    /// MonthlyReportLineItems.
    /// </summary>
    /// <summary>
    /// bourseSymbol, companyName, fiscalEndDate are optional hints from the caller (e.g. from the
    /// ingestion payload). IndustryId/Title and CategoryId/Title are always resolved from the
    /// authoritative Companies → Industries / IndustryGroups join inside the calculator.
    /// </summary>
    Task RecalculateAsync(
        string externalCompanyId,
        int jalaliYear,
        byte jalaliMonth,
        string? bourseSymbol,
        string? companyName,
        string? fiscalEndDate,
        CancellationToken ct = default);

    /// <summary>
    /// Recalculates trend snapshots for a contiguous range of months for one company.
    /// Used by the backfill coordinator.
    /// </summary>
    Task RecalculateRangeAsync(
        string externalCompanyId,
        int fromYear,
        int fromMonth,
        int toYear,
        int toMonth,
        CancellationToken ct = default);
}
