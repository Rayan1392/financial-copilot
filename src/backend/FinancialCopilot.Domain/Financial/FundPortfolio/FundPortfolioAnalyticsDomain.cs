namespace FinancialCopilot.Domain.Financial.FundPortfolio;

public enum FundPortfolioRiskPosture
{
    MoreRiskOn,
    Stable,
    MoreDefensive,
    Unknown
}

public enum FundPortfolioLiquidityRiskStatus
{
    Available,
    Partial,
    Unavailable
}

public enum FundPortfolioCompletenessDimension
{
    Equity,
    Allocation,
    NonEquity,
    Income,
    MarketLiquidity,
    ValuationQuality
}

public enum FundPortfolioSignalType
{
    NewPosition,
    FullExit,
    MaterialPositionIncrease,
    MaterialPositionReduction,
    TopPurchase,
    TopSale,
    SectorAllocationIncrease,
    SectorAllocationDecrease,
    EquityExposureIncrease,
    CashBufferIncrease,
    CommodityExposureIncrease,
    DerivativeHedgeCoverageChange,
    ConcentrationIncrease,
    LiquidityRiskIncrease,
    UnrealizedIncomeConcentration,
    MaterialValuationAdjustment
}

public sealed record FundPortfolioInputCompleteness(
    bool Equity,
    bool Allocation,
    bool NonEquity,
    bool Income,
    bool MarketLiquidity,
    bool ValuationQuality)
{
    public bool IsComplete(FundPortfolioCompletenessDimension dimension) => dimension switch
    {
        FundPortfolioCompletenessDimension.Equity => Equity,
        FundPortfolioCompletenessDimension.Allocation => Allocation,
        FundPortfolioCompletenessDimension.NonEquity => NonEquity,
        FundPortfolioCompletenessDimension.Income => Income,
        FundPortfolioCompletenessDimension.MarketLiquidity => MarketLiquidity,
        FundPortfolioCompletenessDimension.ValuationQuality => ValuationQuality,
        _ => false
    };

    public int CompletedDimensions =>
        new[] { Equity, Allocation, NonEquity, Income, MarketLiquidity, ValuationQuality }.Count(value => value);

    public decimal Score => CompletedDimensions / 6m;
}

public sealed record FundPortfolioAnalyticsSnapshot(
    Guid Id,
    Guid FundId,
    Guid ReportId,
    DateOnly PeriodEndDate,
    Guid? PreviousComparableReportId,
    decimal? EquityWeight,
    decimal? DepositWeight,
    decimal? CommodityWeight,
    decimal? DerivativeWeight,
    decimal? Top5Concentration,
    decimal? Top10Concentration,
    decimal? HerfindahlIndex,
    decimal? PurchaseAmount,
    decimal? SaleAmount,
    decimal? NetEquityDeploymentAmount,
    decimal? TurnoverRatio,
    int NewPositionCount,
    int FullExitCount,
    FundPortfolioRiskPosture RiskPosture,
    FundPortfolioLiquidityRiskStatus LiquidityRiskStatus,
    FundPortfolioValuationQualityStatus ValuationQualityStatus,
    FundPortfolioInputCompleteness InputCompleteness,
    decimal ConfidenceScore,
    string CalculationVersion,
    string EvidenceJson)
{
    public FundPortfolioAnalyticsSnapshot WithConfidence(decimal confidence) =>
        this with { ConfidenceScore = Math.Clamp(confidence, 0m, 1m) };
}

public sealed record FundPortfolioSignal(
    Guid Id,
    Guid SnapshotId,
    FundPortfolioSignalType SignalType,
    string? ExternalCompanyId,
    string? IndustryCode,
    decimal? Magnitude,
    decimal ImportanceScore,
    decimal ConfidenceScore,
    string Title,
    string Reason,
    string EvidenceJson,
    string DeduplicationKey);

public static class FundPortfolioAnalyticsCalculationPolicy
{
    public const string FeatureCode = "FUND_PORTFOLIO_ANALYTICS";
    public const string CalculationVersion = "fund-portfolio-analytics-v1";
    public const string SelectionPolicyVersion = "comparable-report-selection-v1";
    public const string InputSchemaVersion = "fund-portfolio-inputs-v1";

    public static string SignalDeduplicationKey(
        Guid fundId,
        Guid reportId,
        FundPortfolioSignalType signalType,
        string? subject) =>
        $"{fundId:N}|{reportId:N}|{signalType}|{subject?.Trim() ?? string.Empty}|{CalculationVersion}";
}

public static class FundPortfolioAnalyticsOrdering
{
    public static IEnumerable<T> OrderDeterministically<T>(
        IEnumerable<T> values,
        Func<T, decimal?> primaryDescending,
        Func<T, string?> subjectAscending,
        Func<T, Guid> idAscending) =>
        values
            .OrderByDescending(primaryDescending)
            .ThenBy(subjectAscending, StringComparer.Ordinal)
            .ThenBy(idAscending);
}
