using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public static class FundIncomeQualityMethodology
{
    public const string CalculationVersion = "fund-income-nav-quality-v1";
    public const decimal MaterialAdjustmentExposurePercentage = 5m;
    public const decimal ReconciliationTolerance = 1m;

    public static decimal? CalculateShare(decimal? amount, decimal? denominator) =>
        amount.HasValue && denominator.HasValue && denominator.Value != 0m
            ? amount.Value / denominator.Value * 100m
            : null;

    public static decimal? CalculateAdjustmentPercentage(decimal? closingPrice, decimal? adjustedPrice) =>
        closingPrice.HasValue && adjustedPrice.HasValue && closingPrice.Value != 0m
            ? (adjustedPrice.Value - closingPrice.Value) / closingPrice.Value * 100m
            : null;

    public static FundPortfolioValuationQualityStatus ClassifyValuationQuality(
        bool hasTotalAssets,
        int materialIssueCount,
        int unresolvedAdjustmentCount,
        decimal? adjustedExposurePercentage,
        bool hasSourceErrors) =>
        !hasTotalAssets || hasSourceErrors ? FundPortfolioValuationQualityStatus.InsufficientEvidence :
        materialIssueCount > 0 || unresolvedAdjustmentCount > 0 ? FundPortfolioValuationQualityStatus.Limited :
        adjustedExposurePercentage is null ? FundPortfolioValuationQualityStatus.Moderate :
        adjustedExposurePercentage.Value >= MaterialAdjustmentExposurePercentage ? FundPortfolioValuationQualityStatus.Moderate :
        FundPortfolioValuationQualityStatus.High;
}

public interface IFundIncomeQualitySectionNormalizer : IFundPortfolioSectionNormalizer
{
}

public sealed record FundIncomeQualityQuery(Guid? FundId = null, Guid? ReportId = null, FundWorkbookPeriodContext? PeriodContext = null);

public interface IFundIncomeQualityRepository
{
    Task<IReadOnlyList<FundInvestmentIncomeSummary>> QueryIncomeSummariesAsync(FundIncomeQualityQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<FundSecurityIncomeAttribution>> QuerySecurityAttributionsAsync(FundIncomeQualityQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<FundDividendIncomeDetail>> QueryDividendDetailsAsync(FundIncomeQualityQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<FundValuationAdjustment>> QueryValuationAdjustmentsAsync(FundIncomeQualityQuery query, CancellationToken cancellationToken);
    Task<FundPortfolioValuationQualitySnapshot?> GetValuationQualityAsync(Guid reportId, CancellationToken cancellationToken);
}
