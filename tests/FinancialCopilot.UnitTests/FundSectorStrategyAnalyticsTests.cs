using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.UnitTests;

public sealed class FundSectorStrategyAnalyticsTests
{
    [Fact]
    public void Calculator_AggregatesCanonicalSectorsAndPlacesUnresolvedHoldingsInUnknown()
    {
        var input = Input(
            [
                Holding("A", "TECH", "Technology", 60m, FundSecurityResolutionStatus.Resolved),
                Holding("B", "TECH", "Technology", 20m, FundSecurityResolutionStatus.Resolved),
                Holding("C", null, null, 20m, FundSecurityResolutionStatus.Unresolved)
            ],
            [],
            [],
            []);

        var result = new FundSectorStrategyAnalyticsCalculator().Calculate(input);

        Assert.Equal(["TECH", "UNKNOWN"], result.CurrentSectorExposure.Select(exposure => exposure.IndustryCode));
        Assert.Equal(80m, result.CurrentSectorExposure[0].WeightPercentage);
        Assert.Equal(1, result.CurrentSectorExposure[1].UnresolvedSecurityCount);
        Assert.Equal(2m / 3m, result.SectorResolutionConfidence);
    }

    [Fact]
    public void Calculator_ComparesSectorAndAssetAllocationWeightsWithDeterministicDeltas()
    {
        var result = new FundSectorStrategyAnalyticsCalculator().Calculate(Input(
            [Holding("A", "TECH", "Technology", 70m, FundSecurityResolutionStatus.Resolved), Holding("B", "OTHER", "Other", 30m, FundSecurityResolutionStatus.Resolved)],
            [Holding("A", "FIN", "Financial", 70m, FundSecurityResolutionStatus.Resolved), Holding("B", "OTHER", "Other", 30m, FundSecurityResolutionStatus.Resolved)],
            [new FundAssetAllocationFact(FundAssetClass.EquityAndRights, null, 70m, false), new FundAssetAllocationFact(FundAssetClass.BankDeposits, null, 30m, false)],
            [new FundAssetAllocationFact(FundAssetClass.EquityAndRights, null, 50m, false), new FundAssetAllocationFact(FundAssetClass.BankDeposits, null, 50m, false)]));

        var tech = result.SectorRotation.Single(rotation => rotation.IndustryCode == "TECH");
        Assert.Equal(70m, tech.ChangePercentagePoints);
        var equity = result.AllocationChanges.Single(change => change.AssetClass == FundAssetClass.EquityAndRights);
        Assert.Equal(20m, equity.ChangePercentagePoints);
        Assert.Equal(FundPortfolioRiskPosture.MoreRiskOn, result.RiskPosture);
    }

    [Fact]
    public void Calculator_DoesNotCallCashDecreaseBullishWithoutIssuanceRedemptionEvidence()
    {
        var result = new FundSectorStrategyAnalyticsCalculator().Calculate(Input(
            [], [],
            [new FundAssetAllocationFact(FundAssetClass.CashAndOther, null, 20m, false), new FundAssetAllocationFact(FundAssetClass.Unknown, null, 80m, false)],
            [new FundAssetAllocationFact(FundAssetClass.CashAndOther, null, 40m, false), new FundAssetAllocationFact(FundAssetClass.Unknown, null, 60m, false)]));

        Assert.Equal(FundPortfolioRiskPosture.Stable, result.RiskPosture);
        Assert.False(result.IssueRedemptionDataAvailable);
        Assert.Contains("cash decrease alone", result.EvidenceDefinition, StringComparison.Ordinal);
    }

    private static FundSectorStrategyInput Input(
        IReadOnlyCollection<FundSectorHoldingFact> current,
        IReadOnlyCollection<FundSectorHoldingFact> previous,
        IReadOnlyCollection<FundAssetAllocationFact> currentAllocation,
        IReadOnlyCollection<FundAssetAllocationFact> previousAllocation) =>
        new(current, previous, currentAllocation, previousAllocation, false, new FundStrategyPostureRules(2m, "test-v1"));

    private static FundSectorHoldingFact Holding(string id, string? industryCode, string? industryName, decimal weight, FundSecurityResolutionStatus status) =>
        new(Guid.NewGuid(), id, id, industryCode, industryName, null, weight, status);
}
