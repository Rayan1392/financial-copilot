using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.UnitTests;

public sealed class FundDerivativeIncomeValuationAnalyticsTests
{
    [Fact]
    public void Calculator_SummarizesProtectivePutCoverageAndUnknownExposure()
    {
        var instrument = Guid.NewGuid();
        var result = Calculate(
            [Derivative(FundHedgeCoverageStatus.Covered, instrument, "A", 80m), Derivative(FundHedgeCoverageStatus.UnknownInputs, null, null, null)],
            [Holding(instrument, "A", 100m)], [], [], [], null, 0);

        Assert.Equal(2, result.ProtectivePutCoverage.ProtectivePutCount);
        Assert.Equal(1, result.ProtectivePutCoverage.CoveredCount);
        Assert.Equal(1, result.ProtectivePutCoverage.UnknownCount);
        Assert.Equal(100m, result.ProtectivePutCoverage.UnderlyingQuantity);
        Assert.Equal(80m, result.ProtectivePutCoverage.CoveredQuantity);
        Assert.Equal(80m, result.ProtectivePutCoverage.CoveragePercentage);
    }

    [Fact]
    public void Calculator_ComposesIncomeAndRanksContributorsDetractorsAndUnrealizedConcentration()
    {
        var result = Calculate([], [],
            [Summary(FundIncomeCategory.EquityDividend, 20m), Summary(FundIncomeCategory.EquityUnrealized, 80m), Summary(FundIncomeCategory.EquityRealized, -10m)],
            [Attribution("A", 60m, null, 60m), Attribution("B", null, -20m, -20m)], [], Quality(FundPortfolioValuationQualityStatus.High), 0);

        Assert.Equal(90m, result.IncomeAndValuation.KnownIncome);
        Assert.Equal(20m, result.IncomeAndValuation.DividendIncome);
        Assert.Equal(-10m, result.IncomeAndValuation.RealizedIncome);
        Assert.Equal("A", result.IncomeAndValuation.TopContributors.Single().SecurityName);
        Assert.Equal("B", result.IncomeAndValuation.TopDetractors.Single().SecurityName);
        Assert.Equal(100m, result.IncomeAndValuation.UnrealizedConcentration.LargestContributorPercentage);
        Assert.Equal(FundPortfolioValuationQualityStatus.High, result.IncomeAndValuation.ValuationQualityStatus);
    }

    [Fact]
    public void Calculator_PropagatesReconciliationAndValuationIssuesIntoConfidence()
    {
        var result = Calculate([], [], [Summary(FundIncomeCategory.OtherIncome, 10m, FundIncomeReconciliationStatus.Unreconciled)], [], [Adjustment(true)], Quality(FundPortfolioValuationQualityStatus.Limited), 1);

        Assert.Equal(1, result.IncomeAndValuation.ValuationAdjustmentCount);
        Assert.Equal(1, result.IncomeAndValuation.MaterialValuationAdjustmentCount);
        Assert.Equal(1, result.IncomeAndValuation.UnreconciledInputCount);
        Assert.True(result.IncomeAndValuation.ConfidenceScore < 1m);
    }

    private static FundDerivativeIncomeValuationAnalytics Calculate(
        IReadOnlyCollection<FundDerivativePosition> derivatives,
        IReadOnlyCollection<FundEquityPositionSnapshot> holdings,
        IReadOnlyCollection<FundInvestmentIncomeSummary> summaries,
        IReadOnlyCollection<FundSecurityIncomeAttribution> attributions,
        IReadOnlyCollection<FundValuationAdjustment> adjustments,
        FundPortfolioValuationQualitySnapshot? quality,
        int sourceErrors) => new FundDerivativeIncomeValuationAnalyticsCalculator().Calculate(new FundDerivativeIncomeValuationInput(derivatives, holdings, summaries, attributions, adjustments, quality, sourceErrors, "test-v1"));

    private static FundDerivativePosition Derivative(FundHedgeCoverageStatus status, Guid? instrument, string? company, decimal? covered) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), FundWorkbookPeriodContext.CurrentPeriod, null, FundDerivativeType.ProtectivePut, FundOptionType.Put, FundPositionSide.Long, null, company, instrument, "put", "put", null, 1m, 1m, covered, null, null, null, null, null, null, null, FundNonEquityResolutionStatus.Resolved, status, null, null, 1, Guid.NewGuid(), null, 1, DateTimeOffset.UtcNow, "test", "{}");
    private static FundEquityPositionSnapshot Holding(Guid instrument, string company, decimal quantity) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), FundWorkbookPeriodContext.CurrentPeriod, null, FundPositionState.Ending, FundEquitySecurityType.OrdinaryEquity, company, instrument, company, company, quantity, null, null, 100m, null, FundSecurityResolutionStatus.Resolved, 1, Guid.NewGuid(), null, 1, DateTimeOffset.UtcNow, "test", "IRR", "percentage_points", "{}");
    private static FundInvestmentIncomeSummary Summary(FundIncomeCategory category, decimal amount, FundIncomeReconciliationStatus status = FundIncomeReconciliationStatus.Reconciled) => new(Guid.NewGuid(), Guid.NewGuid(), FundWorkbookPeriodContext.CurrentPeriod, category, amount, null, null, null, null, false, status, "{}", "test");
    private static FundSecurityIncomeAttribution Attribution(string name, decimal? dividend, decimal? unrealized, decimal? total) => new(Guid.NewGuid(), Guid.NewGuid(), FundWorkbookPeriodContext.CurrentPeriod, name, name, null, dividend, unrealized, null, total, FundIncomeResolutionStatus.Resolved, FundIncomeReconciliationStatus.Reconciled, "{}");
    private static FundValuationAdjustment Adjustment(bool material) => new(Guid.NewGuid(), Guid.NewGuid(), FundWorkbookPeriodContext.CurrentPeriod, "A", null, 1m, 10m, 11m, 10m, 10m, 10m, "manual", FundIncomeResolutionStatus.Resolved, material, "{}");
    private static FundPortfolioValuationQualitySnapshot Quality(FundPortfolioValuationQualityStatus status) => new(Guid.NewGuid(), Guid.NewGuid(), 1, 10m, 2m, 0, status, 1m, "test", "{}");
}
