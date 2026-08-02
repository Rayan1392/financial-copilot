using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.UnitTests;

public sealed class FundPortfolioSignalGenerationTests
{
    [Fact]
    public void Generator_EmitsGovernedActivitySignalsWithIndependentScores()
    {
        var fund = Guid.NewGuid();
        var report = Guid.NewGuid();
        var input = Input(fund, report, Snapshot(fund, report, 10m), holdings: Activities());

        var signals = new FundPortfolioSignalGenerator().Generate(input);

        Assert.Contains(signals, signal => signal.SignalType == FundPortfolioSignalType.NewPosition);
        Assert.Contains(signals, signal => signal.SignalType == FundPortfolioSignalType.FullExit);
        Assert.Contains(signals, signal => signal.SignalType == FundPortfolioSignalType.MaterialPositionIncrease);
        Assert.Contains(signals, signal => signal.SignalType == FundPortfolioSignalType.MaterialPositionReduction);
        Assert.Contains(signals, signal => signal.SignalType == FundPortfolioSignalType.TopPurchase);
        Assert.Contains(signals, signal => signal.SignalType == FundPortfolioSignalType.TopSale);
        var topPurchase = signals.Single(signal => signal.SignalType == FundPortfolioSignalType.TopPurchase);
        Assert.NotEqual(topPurchase.Magnitude, topPurchase.ImportanceScore);
        Assert.InRange(topPurchase.ConfidenceScore, 0m, 1m);
        Assert.Contains("threshold", topPurchase.EvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain(signals, signal => signal.Title.Contains("buy", StringComparison.OrdinalIgnoreCase) || signal.Reason.Contains("sell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generator_UsesStableDeduplicationAndEvidenceForBaselineSignals()
    {
        var fund = Guid.NewGuid();
        var report = Guid.NewGuid();
        var current = Snapshot(fund, report, 20m);
        var previous = Snapshot(fund, Guid.NewGuid(), 10m);
        var first = new FundPortfolioSignalGenerator().Generate(Input(fund, report, current, previous, holdings: null));
        var second = new FundPortfolioSignalGenerator().Generate(Input(fund, report, current, previous, holdings: null));

        var firstSignal = Assert.Single(first);
        var secondSignal = Assert.Single(second);
        Assert.Equal(FundPortfolioSignalType.ConcentrationIncrease, firstSignal.SignalType);
        Assert.Equal(firstSignal.Id, secondSignal.Id);
        Assert.Equal(firstSignal.DeduplicationKey, secondSignal.DeduplicationKey);
        Assert.Equal(firstSignal.EvidenceJson, secondSignal.EvidenceJson);
        Assert.Contains("baseline", firstSignal.EvidenceJson, StringComparison.Ordinal);
        Assert.Contains("fund-portfolio-signals-v1", firstSignal.EvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsLiquidityAndValuationSignalsOnlyWhenGovernedInputsSupportThem()
    {
        var fund = Guid.NewGuid();
        var report = Guid.NewGuid();
        var snapshot = Snapshot(fund, report, 10m);
        var previousLiquidity = Liquidity(FundPortfolioLiquidityRiskStatus.Available, 100m, 0m);
        var currentLiquidity = Liquidity(FundPortfolioLiquidityRiskStatus.Unavailable, 0m, 10m);
        var income = new FundIncomeValuationAnalytics(
            [], null, null, null, null, [], [],
            new FundUnrealizedIncomeConcentration(100m, 80m, 80m, 1m, 1),
            1, 1, 100m, 10m, new Dictionary<string, int> { ["manual"] = 1 },
            FundPortfolioValuationQualityStatus.Limited, 0.6m, 0, 0, "test-v1");
        var analytics = new FundDerivativeIncomeValuationAnalytics(
            new FundProtectivePutCoverageSummary(0, 0, 0, 0, 0, 0, null, null, null, null, null, []), income);
        var input = Input(fund, report, snapshot, previous: null, holdings: null, liquidity: currentLiquidity, previousLiquidity: previousLiquidity, income: analytics);

        var signals = new FundPortfolioSignalGenerator().Generate(input);

        Assert.Contains(signals, signal => signal.SignalType == FundPortfolioSignalType.LiquidityRiskIncrease);
        Assert.Contains(signals, signal => signal.SignalType == FundPortfolioSignalType.UnrealizedIncomeConcentration);
        Assert.Contains(signals, signal => signal.SignalType == FundPortfolioSignalType.MaterialValuationAdjustment);
    }

    private static FundPortfolioSignalGenerationInput Input(
        Guid fund,
        Guid report,
        FundPortfolioAnalyticsSnapshot current,
        FundPortfolioAnalyticsSnapshot? previous = null,
        FundHoldingsActivityAnalytics? holdings = null,
        FundTurnoverLiquidityAnalytics? liquidity = null,
        FundTurnoverLiquidityAnalytics? previousLiquidity = null,
        FundDerivativeIncomeValuationAnalytics? income = null) =>
        new(fund, report, current.Id, current, previous, holdings, null, liquidity, previousLiquidity, income, null, FundSignalGenerationRules.Default);

    private static FundHoldingsActivityAnalytics Activities()
    {
        var newPosition = Activity("NEW", 100m, null, FundEquityActivityClassification.NewPosition);
        var exit = Activity("EXIT", null, 90m, FundEquityActivityClassification.FullExit);
        var increase = Activity("INC", 80m, null, FundEquityActivityClassification.Increased, 2m);
        var reduction = Activity("RED", null, 70m, FundEquityActivityClassification.Reduced, -2m);
        var purchases = new[] { newPosition, increase };
        var sales = new[] { exit, reduction };
        return new([], 0m, 0m, 0m, 0, purchases, sales, [], [], [], [], [newPosition], [exit], [increase], [reduction], 180m, 160m, 20m, "test", "test-v1");
    }

    private static FundActivityRanking Activity(string subject, decimal? purchase, decimal? sale, FundEquityActivityClassification classification, decimal? weight = null) =>
        new(Guid.NewGuid(), subject, subject, purchase, sale, null, null, weight, classification, FundEquityReconciliationStatus.Reconciled);

    private static FundPortfolioAnalyticsSnapshot Snapshot(Guid fund, Guid report, decimal top5) =>
        new(Guid.NewGuid(), fund, report, new DateOnly(2026, 8, 1), null, 50m, 20m, 10m, 5m, top5, 30m, 0.2m, null, null, null, null, 0, 0, FundPortfolioRiskPosture.Stable, FundPortfolioLiquidityRiskStatus.Available, FundPortfolioValuationQualityStatus.High, new(true, true, true, true, true, true), 0.9m, "test-v1", "{}");

    private static FundTurnoverLiquidityAnalytics Liquidity(FundPortfolioLiquidityRiskStatus status, decimal coverage, decimal depositChange) =>
        new(null, null, null, null, null, null, FundTurnoverDenominatorPolicy.AverageDisclosedPortfolioMarketValue, "test", 100m, 90m, depositChange, 20m, 10m, depositChange, [], null, coverage, status, "test", "test-v1");
}
