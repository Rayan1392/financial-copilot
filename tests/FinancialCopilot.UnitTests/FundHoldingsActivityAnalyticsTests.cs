using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.UnitTests;

public sealed class FundHoldingsActivityAnalyticsTests
{
    [Fact]
    public void Calculator_ComputesConcentrationAndStableTopHoldingOrder()
    {
        var input = new FundHoldingsActivityInput(
            [
                Holding("B", 60m, 60m),
                Holding("A", 40m, 40m),
                Holding("C", 0m, 0m)
            ],
            [],
            new FundPortfolioMaterialityThresholds(50m, 100m, 1m, "test-v1"));

        var result = new FundHoldingsActivityAnalyticsCalculator().Calculate(input);

        Assert.Equal(["B", "A", "C"], result.TopHoldings.Select(holding => holding.Subject));
        Assert.Equal(1m, result.Top5Concentration);
        Assert.Equal(0.52m, result.HerfindahlIndex);
        Assert.Equal(1, result.MaterialPositionCount);
    }

    [Fact]
    public void Calculator_SeparatesActivityRankingsAndUsesDisclosedDeploymentDefinition()
    {
        var input = new FundHoldingsActivityInput(
            [],
            [
                Activity("BUY", 100m, null, 10m, null, FundEquityActivityClassification.Increased),
                Activity("SELL", null, 30m, null, 3m, FundEquityActivityClassification.Reduced)
            ],
            new FundPortfolioMaterialityThresholds(0m, 0m, 0.9m, "test-v1"));

        var result = new FundHoldingsActivityAnalyticsCalculator().Calculate(input);

        Assert.Equal(100m, result.PurchaseAmount);
        Assert.Equal(30m, result.SaleAmount);
        Assert.Equal(70m, result.NetEquityDeploymentAmount);
        Assert.Contains("not fund net cash flow", result.NetEquityDeploymentDefinition, StringComparison.Ordinal);
        Assert.Equal("BUY", result.PurchasesByAmount.Single().Subject);
        Assert.Equal("SELL", result.SalesByAmount.Single().Subject);
        Assert.Equal("BUY", result.PurchasesByQuantity.Single().Subject);
        Assert.Equal("SELL", result.SalesByQuantity.Single().Subject);
    }

    [Fact]
    public void Calculator_UsesFeature102ClassificationAndDoesNotInferPurchaseFromPrice()
    {
        var input = new FundHoldingsActivityInput(
            [Holding("UNCHANGED", 200m, 100m)],
            [],
            new FundPortfolioMaterialityThresholds(0m, 100m, 0.9m, "test-v1"));

        var result = new FundHoldingsActivityAnalyticsCalculator().Calculate(input);

        Assert.Empty(result.NewPositions);
        Assert.Empty(result.FullExits);
        Assert.Null(result.PurchaseAmount);
        Assert.Null(result.NetEquityDeploymentAmount);
    }

    private static FundHoldingFact Holding(string subject, decimal marketValue, decimal weight) =>
        new(Guid.NewGuid(), subject, subject, marketValue, weight, null, FundSecurityResolutionStatus.Resolved);

    private static FundActivityFact Activity(string subject, decimal? purchase, decimal? sale, decimal? purchasedQuantity, decimal? soldQuantity, FundEquityActivityClassification classification) =>
        new(Guid.NewGuid(), subject, subject, purchase, sale, purchasedQuantity, soldQuantity, null, classification, FundEquityReconciliationStatus.Reconciled);
}
