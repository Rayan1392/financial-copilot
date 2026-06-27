namespace FinancialCopilot.API.Contracts;

public sealed record MonthlySalesQualityRankingHttpResponse(
    int ReportYear,
    byte ReportMonth,
    string Scope,
    string Direction,
    int TotalEligibleCompanies,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<MonthlySalesQualityRankingItemHttpResponse> Items);

public sealed record MonthlySalesQualityRankingItemHttpResponse(
    int Rank,
    string Symbol,
    string? CompanyName,
    string? IndustryTitle,
    decimal QualityScore,
    string QualityLabel,
    decimal ConfidenceScore,
    decimal MonthlySalesAmount,
    decimal? Avg12MonthSalesAmount,
    decimal? SalesVsAvg12MPercent,
    decimal? SalesMonthOverMonthPercent,
    decimal? SalesYearOverYearPercent,
    MonthlySalesQualityDimensionScoresHttpResponse? DimensionScores,
    IReadOnlyList<string> PositiveDrivers,
    IReadOnlyList<string> NegativeDrivers,
    MonthlySalesQualityDataCoverageHttpResponse DataCoverage,
    string SourceProviderName,
    DateTimeOffset CalculatedAtUtc);

public sealed record MonthlySalesQualityDimensionScoresHttpResponse(
    decimal? SalesGrowthVs12M,
    decimal? QuantityGrowthQuality,
    decimal? RateGrowthQuality,
    decimal? ProductMixStrength,
    decimal? PersistenceTrend,
    decimal? IndustryRelativeStrength);

public sealed record MonthlySalesQualityDataCoverageHttpResponse(
    int HistoryMonths,
    bool HasProductLineItems,
    bool HasProductMix,
    int IndustryPeerCount);

public sealed record RecalculateMonthlySalesQualityRankingHttpRequest(
    int? ReportYear = null,
    byte? ReportMonth = null);

public sealed record RecalculateMonthlySalesQualityRankingHttpResponse(
    int ReportYear,
    byte ReportMonth,
    int EligibleCompanies,
    int SkippedCompanies,
    DateTimeOffset CalculatedAtUtc);
