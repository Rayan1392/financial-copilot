using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Domain.Financial.Features;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public enum FundPortfolioAnalyticsRecalculationReason
{
    NormalizedSectionsCompleted,
    MappingChanged,
    MarketDataChanged,
    CalculationVersionChanged,
    Manual
}

public sealed record FundPortfolioAnalyticsRecalculationRequest(
    Guid FundId,
    Guid ReportId,
    DateOnly PeriodEndDate,
    FundPortfolioAnalyticsRecalculationReason Reason,
    string InputFingerprint,
    string CalculationVersion);

public sealed record FundPortfolioAnalyticsRecalculationResult(
    bool Scheduled,
    string IdempotencyKey,
    FeatureComputationJob? Job,
    string? SkipReason = null);

public interface IFundPortfolioAnalyticsRecalculationCoordinator
{
    Task<FundPortfolioAnalyticsRecalculationResult> RequestAsync(
        FundPortfolioAnalyticsRecalculationRequest request,
        CancellationToken cancellationToken);
}
