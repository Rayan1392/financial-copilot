namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class MonthlySalesQualityRankingSnapshotRow
{
    public Guid Id { get; set; }

    public string ExternalCompanyId { get; set; } = string.Empty;

    public string CompanySymbol { get; set; } = string.Empty;

    public string? CompanyName { get; set; }

    public Guid? IndustryId { get; set; }

    public string? IndustryTitle { get; set; }

    public Guid? IndustryGroupId { get; set; }

    public string? IndustryGroupTitle { get; set; }

    public int ReportYear { get; set; }

    public byte ReportMonth { get; set; }

    public decimal MonthlySalesAmount { get; set; }

    public decimal? Avg12MonthSalesAmount { get; set; }

    public decimal? SalesVsAvg12MPercent { get; set; }

    public decimal? SalesMonthOverMonthPercent { get; set; }

    public decimal? SalesYearOverYearPercent { get; set; }

    public decimal QualityScore { get; set; }

    public string QualityLabel { get; set; } = string.Empty;

    public decimal ConfidenceScore { get; set; }

    public int RankMarket { get; set; }

    public int? RankIndustry { get; set; }

    public string DimensionScoresJson { get; set; } = "{}";

    public string PositiveDriversJson { get; set; } = "[]";

    public string NegativeDriversJson { get; set; } = "[]";

    public string DataCoverageJson { get; set; } = "{}";

    public string SourceProviderName { get; set; } = string.Empty;

    public DateTimeOffset CalculatedAtUtc { get; set; }

    public bool IsEligible { get; set; }
}
