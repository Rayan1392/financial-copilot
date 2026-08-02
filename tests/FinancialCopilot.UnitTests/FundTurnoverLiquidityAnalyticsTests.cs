using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.UnitTests;

public sealed class FundTurnoverLiquidityAnalyticsTests
{
    [Fact]
    public void Calculator_SeparatesTurnoverComponentsAndDeclaresDenominator()
    {
        var result = Calculate(
            [Activity(100m, null), Activity(null, 30m)],
            [Position("A", Guid.NewGuid(), 100m, 1000m)],
            [new FundMarketVolumeFact(Guid.NewGuid(), 100m, false)],
            [new FundDepositBufferFact(20m, 20m)],
            [new FundDepositBufferFact(10m, 10m)],
            500m);

        Assert.Equal(100m, result.PurchaseAmount);
        Assert.Equal(30m, result.SaleAmount);
        Assert.Equal(130m, result.GrossTurnoverAmount);
        Assert.Equal(70m, result.NetEquityDeploymentAmount);
        Assert.Equal(0.26m, result.TurnoverRatio);
        Assert.Equal(500m, result.TurnoverDenominatorAmount);
        Assert.Contains("average disclosed portfolio market value", result.TurnoverDenominatorDefinition, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculator_ComputesDepositBufferChangeAndLiquidationDaysWithParticipationRate()
    {
        var instrument = Guid.NewGuid();
        var result = Calculate([], [Position("A", instrument, 100m, 1000m)], [new FundMarketVolumeFact(instrument, 50m, false)], [new FundDepositBufferFact(80m, 20m)], [new FundDepositBufferFact(60m, 15m)], 1000m);

        Assert.Equal(20m, result.DepositBufferChangeAmount);
        Assert.Equal(5m, result.DepositBufferChangePercentagePoints);
        Assert.Equal(20m, result.LiquidityPositions.Single().LiquidationDays);
        Assert.Equal(FundPortfolioLiquidityRiskStatus.Available, result.LiquidityRiskStatus);
        Assert.Equal(100m, result.LiquidityCoveragePercentage);
    }

    [Fact]
    public void Calculator_ReportsPartialCoverageForMissingSuspendedOrZeroVolumeWithoutDivisionByZero()
    {
        var resolved = Guid.NewGuid();
        var suspended = Guid.NewGuid();
        var result = Calculate([], [Position("A", resolved, 100m, 100m), Position("B", suspended, 100m, 100m), Position("C", Guid.NewGuid(), 100m, 100m)], [new FundMarketVolumeFact(resolved, 100m, false), new FundMarketVolumeFact(suspended, 100m, true)], [], [], 300m);

        Assert.Equal(FundPortfolioLiquidityRiskStatus.Partial, result.LiquidityRiskStatus);
        Assert.Equal(33.33333333333333333333333333m, result.LiquidityCoveragePercentage);
        Assert.Contains(result.LiquidityPositions, position => position.Availability == FundLiquidityAvailability.Suspended);
        Assert.Contains(result.LiquidityPositions, position => position.Availability == FundLiquidityAvailability.MissingMarketVolume);
    }

    private static FundTurnoverLiquidityAnalytics Calculate(
        IReadOnlyCollection<FundActivityFact> activities,
        IReadOnlyCollection<FundLiquidityPositionFact> positions,
        IReadOnlyCollection<FundMarketVolumeFact> volumes,
        IReadOnlyCollection<FundDepositBufferFact> currentDeposits,
        IReadOnlyCollection<FundDepositBufferFact> previousDeposits,
        decimal? averageValue) => new FundTurnoverLiquidityAnalyticsCalculator().Calculate(new FundTurnoverLiquidityInput(activities, positions, volumes, currentDeposits, previousDeposits, averageValue, new FundTurnoverLiquidityRules(0.1m, FundTurnoverDenominatorPolicy.AverageDisclosedPortfolioMarketValue, "test-v1")));

    private static FundActivityFact Activity(decimal? purchase, decimal? sale) => new(Guid.NewGuid(), "A", "A", purchase, sale, null, null, null, FundEquityActivityClassification.Increased, FundEquityReconciliationStatus.Reconciled);
    private static FundLiquidityPositionFact Position(string subject, Guid instrument, decimal quantity, decimal value) => new(Guid.NewGuid(), subject, instrument, subject, quantity, value, true);
}
