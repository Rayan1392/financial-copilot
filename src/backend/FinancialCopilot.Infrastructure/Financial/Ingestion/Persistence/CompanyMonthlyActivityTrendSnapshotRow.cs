namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class CompanyMonthlyActivityTrendSnapshotRow
{
    public Guid Id { get; set; }

    public string ExternalCompanyId { get; set; } = string.Empty;

    public string? CompanySymbol { get; set; }

    public string? CompanyName { get; set; }

    public int? IndustryId { get; set; }

    public string? IndustryTitle { get; set; }

    public int? CategoryId { get; set; }

    public string? CategoryTitle { get; set; }

    public int ReportYear { get; set; }

    public byte ReportMonth { get; set; }

    public string? FiscalEndDate { get; set; }

    public int? FiscalYear { get; set; }

    public int? FiscalMonthIndex { get; set; }

    public string? FiscalMonthNameFa { get; set; }

    public int? CalendarYear { get; set; }

    public int? CalendarMonth { get; set; }

    // Monthly totals from outputTypeId = 0
    public decimal MonthlySalesAmount { get; set; }

    public decimal? MonthlyProductionQuantity { get; set; }

    public decimal? MonthlySalesQuantity { get; set; }

    public decimal? MonthlyAverageSalesRate { get; set; }

    public bool HasMixedProductUnits { get; set; }

    public string? ProductUnitSummary { get; set; }

    // Same month previous fiscal year (outputTypeId = 0 from prior year)
    public decimal? SameMonthPreviousYearSalesAmount { get; set; }

    public decimal? SameMonthPreviousYearProductionQuantity { get; set; }

    public decimal? SameMonthPreviousYearSalesQuantity { get; set; }

    // Trailing 12-month average
    public decimal? Average12MonthSalesAmount { get; set; }

    public int Average12MonthPeriodCount { get; set; }

    // YTD from outputTypeId = 1
    public decimal? YtdSalesAmount { get; set; }

    public decimal? YtdProductionQuantity { get; set; }

    public decimal? YtdSalesQuantity { get; set; }

    // YTD to previous month from outputTypeId = 4
    public decimal? YtdPreviousMonthSalesAmount { get; set; }

    // Growth percentages
    public decimal? SalesAmountMomGrowthPercent { get; set; }

    public decimal? SalesAmountYoYGrowthPercent { get; set; }

    public decimal? ProductionQuantityYoYGrowthPercent { get; set; }

    public decimal? SalesQuantityYoYGrowthPercent { get; set; }

    // Source output type provenance
    public int? CurrentMonthOutputType { get; set; }

    public int? YtdOutputType { get; set; }

    public int? YtdPreviousMonthOutputType { get; set; }

    // Provenance
    public string SourceProviderName { get; set; } = string.Empty;

    public string? SourceReportId { get; set; }

    public string? SourceRawPayloadId { get; set; }

    // Completeness flags
    public bool IsComparablePreviousYearAvailable { get; set; }

    public bool IsAverage12MonthComplete { get; set; }

    public decimal DataCompletenessScore { get; set; }

    public DateTimeOffset CalculatedAtUtc { get; set; }
}
