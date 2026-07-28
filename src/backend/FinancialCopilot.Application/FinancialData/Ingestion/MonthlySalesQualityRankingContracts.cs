namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum MonthlySalesQualityDirection
{
    Top,
    Bottom
}

public enum MonthlySalesQualityScope
{
    Market,
    Industry,
    Symbols
}

public sealed record MonthlySalesQualityRankingQuery(
    int? ReportYear = null,
    byte? ReportMonth = null,
    Guid? IndustryId = null,
    Guid? IndustryGroupId = null,
    IReadOnlyList<string>? Symbols = null,
    MonthlySalesQualityScope Scope = MonthlySalesQualityScope.Market,
    MonthlySalesQualityDirection Direction = MonthlySalesQualityDirection.Top,
    int Limit = 10,
    decimal? MinimumSalesAmount = null,
    bool IncludeExplanation = true,
    bool IncludeDimensionScores = true,
    bool OnlyEligibleRows = true,
    string? IndustryTitle = null,
    string? IndustryGroupTitle = null);

public sealed record MonthlySalesQualityRankingResponse(
    int ReportYear,
    byte ReportMonth,
    MonthlySalesQualityScope Scope,
    MonthlySalesQualityDirection Direction,
    int TotalEligibleCompanies,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<MonthlySalesQualityRankingItem> Items);

public sealed record MonthlySalesQualityRankingItem(
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
    MonthlySalesQualityDimensionScores? DimensionScores,
    IReadOnlyList<string> PositiveDrivers,
    IReadOnlyList<string> NegativeDrivers,
    MonthlySalesQualityDataCoverage DataCoverage,
    string SourceProviderName,
    DateTimeOffset CalculatedAtUtc);

public sealed record MonthlySalesQualityDimensionScores(
    decimal? SalesGrowthVs12M,
    decimal? QuantityGrowthQuality,
    decimal? RateGrowthQuality,
    decimal? ProductMixStrength,
    decimal? PersistenceTrend,
    decimal? IndustryRelativeStrength);

public sealed record MonthlySalesQualityDataCoverage(
    int HistoryMonths,
    bool HasProductLineItems,
    bool HasProductMix,
    int IndustryPeerCount);

public sealed record MonthlySalesQualityScoreInput(
    decimal MonthlySalesAmount,
    decimal? Avg12MonthSalesAmount,
    decimal? PreviousMonthSalesAmount,
    decimal? SameMonthPreviousYearSalesAmount,
    decimal? MonthlySalesQuantity,
    decimal? PreviousMonthSalesQuantity,
    decimal? MonthlyProductionQuantity,
    decimal? PreviousMonthProductionQuantity,
    decimal? MonthlyAverageSalesRate,
    decimal? PreviousMonthAverageSalesRate,
    IReadOnlyList<MonthlySalesQualityProductMixInput> ProductMixRows,
    IReadOnlyList<decimal> LastThreeMonthlySalesAmounts,
    decimal? IndustryPercentile,
    int IndustryPeerCount,
    int HistoryMonths,
    bool HasProductLineItems);

public sealed record MonthlySalesQualityProductMixInput(
    string ProductName,
    decimal SalesAmount,
    decimal RevenueSharePercentage,
    int ProductRank,
    bool IsDominantProduct,
    decimal? ProductionQuantity,
    decimal? SalesQuantity,
    decimal? SalesRate);

public sealed record MonthlySalesQualityScoreResult(
    decimal QualityScore,
    string QualityLabel,
    decimal ConfidenceScore,
    MonthlySalesQualityDimensionScores DimensionScores,
    IReadOnlyList<string> PositiveDrivers,
    IReadOnlyList<string> NegativeDrivers,
    MonthlySalesQualityDataCoverage DataCoverage,
    decimal? SalesVsAvg12MPercent,
    decimal? SalesMonthOverMonthPercent,
    decimal? SalesYearOverYearPercent);

public interface IMonthlySalesQualityScoreCalculator
{
    MonthlySalesQualityScoreResult Calculate(MonthlySalesQualityScoreInput input);
}

public sealed record MonthlySalesQualityRankingSnapshotUpsertRow(
    Guid Id,
    string ExternalCompanyId,
    string CompanySymbol,
    string? CompanyName,
    Guid? IndustryId,
    string? IndustryTitle,
    Guid? IndustryGroupId,
    string? IndustryGroupTitle,
    int ReportYear,
    byte ReportMonth,
    decimal MonthlySalesAmount,
    decimal? Avg12MonthSalesAmount,
    decimal? SalesVsAvg12MPercent,
    decimal? SalesMonthOverMonthPercent,
    decimal? SalesYearOverYearPercent,
    decimal QualityScore,
    string QualityLabel,
    decimal ConfidenceScore,
    int RankMarket,
    int? RankIndustry,
    string DimensionScoresJson,
    string PositiveDriversJson,
    string NegativeDriversJson,
    string DataCoverageJson,
    string SourceProviderName,
    DateTimeOffset CalculatedAtUtc,
    bool IsEligible);

public interface IMonthlySalesQualityRankingRepository
{
    Task<(int ReportYear, byte ReportMonth)?> GetLatestAvailablePeriodAsync(CancellationToken ct = default);

    Task<MonthlySalesQualityRankingResponse> GetRankingAsync(
        MonthlySalesQualityRankingQuery query,
        CancellationToken ct = default);

    Task DeletePeriodSnapshotsAsync(int reportYear, byte reportMonth, CancellationToken ct = default);

    Task UpsertSnapshotsAsync(
        IReadOnlyList<MonthlySalesQualityRankingSnapshotUpsertRow> rows,
        CancellationToken ct = default);
}

public sealed record RecalculateMonthlySalesQualityRankingRequest(
    int? ReportYear = null,
    byte? ReportMonth = null);

public sealed record RecalculateMonthlySalesQualityRankingResult(
    int ReportYear,
    byte ReportMonth,
    int EligibleCompanies,
    int SkippedCompanies,
    DateTimeOffset CalculatedAtUtc);

public interface IRecalculateMonthlySalesQualityRankingUseCase
{
    Task<RecalculateMonthlySalesQualityRankingResult> ExecuteAsync(
        RecalculateMonthlySalesQualityRankingRequest request,
        CancellationToken ct = default);
}

public interface IMonthlySalesQualityRankingQueryUseCase
{
    Task<MonthlySalesQualityRankingResponse> ExecuteAsync(
        MonthlySalesQualityRankingQuery query,
        CancellationToken ct = default);
}
