using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundSectorHoldingFact(
    Guid Id,
    string? ExternalCompanyId,
    string SecurityName,
    string? IndustryCode,
    string? IndustryName,
    decimal? MarketValue,
    decimal? WeightPercentage,
    FundSecurityResolutionStatus ResolutionStatus);

public sealed record FundAssetAllocationFact(
    FundAssetClass AssetClass,
    decimal? MarketValue,
    decimal? WeightPercentage,
    bool HasSourceFormulaError);

public sealed record FundSectorStrategyInput(
    IReadOnlyCollection<FundSectorHoldingFact> CurrentHoldings,
    IReadOnlyCollection<FundSectorHoldingFact> PreviousHoldings,
    IReadOnlyCollection<FundAssetAllocationFact> CurrentAllocation,
    IReadOnlyCollection<FundAssetAllocationFact> PreviousAllocation,
    bool IssueRedemptionDataAvailable,
    FundStrategyPostureRules PostureRules);

public sealed record FundStrategyPostureRules
{
    public FundStrategyPostureRules(decimal minimumMeaningfulChangePercentagePoints, string version)
    {
        if (minimumMeaningfulChangePercentagePoints < 0m)
            throw new ArgumentOutOfRangeException(nameof(minimumMeaningfulChangePercentagePoints));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Posture rules version is required.", nameof(version));
        MinimumMeaningfulChangePercentagePoints = minimumMeaningfulChangePercentagePoints;
        Version = version.Trim();
    }

    public decimal MinimumMeaningfulChangePercentagePoints { get; }
    public string Version { get; }
}

public sealed record FundSectorExposure(
    string IndustryCode,
    string IndustryName,
    decimal WeightPercentage,
    decimal? MarketValue,
    int SecurityCount,
    int UnresolvedSecurityCount);

public sealed record FundSectorRotation(
    string IndustryCode,
    string IndustryName,
    decimal? CurrentWeightPercentage,
    decimal? PreviousWeightPercentage,
    decimal? ChangePercentagePoints,
    int CurrentSecurityCount,
    int PreviousSecurityCount);

public sealed record FundAssetAllocationChange(
    FundAssetClass AssetClass,
    decimal? CurrentWeightPercentage,
    decimal? PreviousWeightPercentage,
    decimal? ChangePercentagePoints,
    bool HasSourceFormulaError);

public sealed record FundSectorStrategyAnalytics(
    IReadOnlyList<FundSectorExposure> CurrentSectorExposure,
    IReadOnlyList<FundSectorRotation> SectorRotation,
    IReadOnlyList<FundAssetAllocationChange> AllocationChanges,
    decimal SectorResolutionConfidence,
    FundPortfolioRiskPosture RiskPosture,
    bool IssueRedemptionDataAvailable,
    string PostureRulesVersion,
    string EvidenceDefinition);

public interface IFundSectorStrategyAnalyticsCalculator
{
    FundSectorStrategyAnalytics Calculate(FundSectorStrategyInput input);
}

public sealed class FundSectorStrategyAnalyticsCalculator : IFundSectorStrategyAnalyticsCalculator
{
    public FundSectorStrategyAnalytics Calculate(FundSectorStrategyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var current = AggregateSectors(input.CurrentHoldings);
        var previous = AggregateSectors(input.PreviousHoldings);
        var currentByCode = current.ToDictionary(exposure => exposure.IndustryCode, StringComparer.Ordinal);
        var previousByCode = previous.ToDictionary(exposure => exposure.IndustryCode, StringComparer.Ordinal);
        var rotations = currentByCode.Keys.Concat(previousByCode.Keys).Distinct(StringComparer.Ordinal)
            .Select(code =>
            {
                currentByCode.TryGetValue(code, out var currentExposure);
                previousByCode.TryGetValue(code, out var previousExposure);
                return new FundSectorRotation(
                    code,
                    currentExposure?.IndustryName ?? previousExposure!.IndustryName,
                    currentExposure?.WeightPercentage ?? (previousExposure is null ? null : 0m),
                    previousExposure?.WeightPercentage ?? (currentExposure is null ? null : 0m),
                    (currentExposure?.WeightPercentage ?? (previousExposure is null ? null : 0m)) -
                        (previousExposure?.WeightPercentage ?? (currentExposure is null ? null : 0m)),
                    currentExposure?.SecurityCount ?? 0,
                    previousExposure?.SecurityCount ?? 0);
            })
            .OrderByDescending(rotation => rotation.ChangePercentagePoints)
            .ThenBy(rotation => rotation.IndustryCode, StringComparer.Ordinal)
            .ToArray();

        var allocation = AllocationChanges(input.CurrentAllocation, input.PreviousAllocation);
        return new(
            current,
            rotations,
            allocation,
            input.CurrentHoldings.Count == 0
                ? 0m
                : input.CurrentHoldings.Count(holding =>
                    !string.IsNullOrWhiteSpace(holding.IndustryCode) &&
                    holding.ResolutionStatus == FundSecurityResolutionStatus.Resolved) / (decimal)input.CurrentHoldings.Count,
            DeterminePosture(allocation, input.PreviousAllocation.Count > 0, input.PostureRules),
            input.IssueRedemptionDataAvailable,
            input.PostureRules.Version,
            "Descriptive allocation changes only; a cash decrease alone is not treated as bullish when issue/redemption data is unavailable.");
    }

    private static IReadOnlyList<FundSectorExposure> AggregateSectors(IReadOnlyCollection<FundSectorHoldingFact> holdings)
    {
        var valid = holdings.Where(holding => holding.WeightPercentage is >= 0m || holding.MarketValue is >= 0m).ToArray();
        var weightTotal = valid.Where(holding => holding.WeightPercentage is >= 0m).Sum(holding => holding.WeightPercentage!.Value);
        var valueTotal = valid.Where(holding => holding.MarketValue is >= 0m).Sum(holding => holding.MarketValue!.Value);
        var bySector = valid.GroupBy(holding =>
        {
            var resolved = holding.ResolutionStatus == FundSecurityResolutionStatus.Resolved && !string.IsNullOrWhiteSpace(holding.IndustryCode);
            return resolved ? holding.IndustryCode!.Trim() : "UNKNOWN";
        }, StringComparer.Ordinal);
        return bySector.Select(group =>
        {
            var representative = group.OrderBy(holding => holding.IndustryName, StringComparer.Ordinal).ThenBy(holding => holding.Id).First();
            var weight = weightTotal > 0m
                ? group.Sum(holding => holding.WeightPercentage ?? 0m) / weightTotal * 100m
                : valueTotal > 0m ? group.Sum(holding => holding.MarketValue ?? 0m) / valueTotal * 100m : 0m;
            return new FundSectorExposure(
                group.Key,
                group.Key == "UNKNOWN" ? "Unknown" : representative.IndustryName ?? group.Key,
                weight,
                group.Any(holding => holding.MarketValue.HasValue) ? group.Sum(holding => holding.MarketValue ?? 0m) : null,
                group.Count(),
                group.Count(holding => holding.ResolutionStatus != FundSecurityResolutionStatus.Resolved || string.IsNullOrWhiteSpace(holding.IndustryCode)));
        }).OrderByDescending(exposure => exposure.WeightPercentage).ThenBy(exposure => exposure.IndustryCode, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<FundAssetAllocationChange> AllocationChanges(
        IReadOnlyCollection<FundAssetAllocationFact> current,
        IReadOnlyCollection<FundAssetAllocationFact> previous)
    {
        var currentWeights = NormalizeAllocation(current);
        var previousWeights = NormalizeAllocation(previous);
        return currentWeights.Keys.Concat(previousWeights.Keys).Distinct()
            .Select(assetClass => new FundAssetAllocationChange(
                assetClass,
                currentWeights.GetValueOrDefault(assetClass),
                previousWeights.GetValueOrDefault(assetClass),
                currentWeights.GetValueOrDefault(assetClass) - previousWeights.GetValueOrDefault(assetClass),
                current.Where(fact => fact.AssetClass == assetClass).Any(fact => fact.HasSourceFormulaError) ||
                    previous.Where(fact => fact.AssetClass == assetClass).Any(fact => fact.HasSourceFormulaError)))
            .OrderByDescending(change => change.ChangePercentagePoints)
            .ThenBy(change => change.AssetClass)
            .ToArray();
    }

    private static IReadOnlyDictionary<FundAssetClass, decimal> NormalizeAllocation(IReadOnlyCollection<FundAssetAllocationFact> facts)
    {
        var valid = facts.Where(fact => !fact.HasSourceFormulaError && (fact.WeightPercentage is >= 0m || fact.MarketValue is >= 0m)).ToArray();
        var weightTotal = valid.Where(fact => fact.WeightPercentage is >= 0m).Sum(fact => fact.WeightPercentage!.Value);
        var valueTotal = valid.Where(fact => fact.MarketValue is >= 0m).Sum(fact => fact.MarketValue!.Value);
        return valid.GroupBy(fact => fact.AssetClass).ToDictionary(
            group => group.Key,
            group => weightTotal > 0m
                ? group.Sum(fact => fact.WeightPercentage ?? 0m) / weightTotal * 100m
                : valueTotal > 0m ? group.Sum(fact => fact.MarketValue ?? 0m) / valueTotal * 100m : 0m);
    }

    private static FundPortfolioRiskPosture DeterminePosture(
        IReadOnlyCollection<FundAssetAllocationChange> changes,
        bool hasPrevious,
        FundStrategyPostureRules rules)
    {
        if (!hasPrevious || changes.Count == 0) return FundPortfolioRiskPosture.Unknown;
        var equity = Change(changes, FundAssetClass.EquityAndRights);
        var commodity = Change(changes, FundAssetClass.CommodityCertificates);
        var derivatives = Change(changes, FundAssetClass.Derivatives);
        var cashAndDeposits = Change(changes, FundAssetClass.CashAndOther) + Change(changes, FundAssetClass.BankDeposits);
        var riskOn = equity >= rules.MinimumMeaningfulChangePercentagePoints ||
            commodity >= rules.MinimumMeaningfulChangePercentagePoints ||
            derivatives >= rules.MinimumMeaningfulChangePercentagePoints;
        var defensive = equity <= -rules.MinimumMeaningfulChangePercentagePoints ||
            cashAndDeposits >= rules.MinimumMeaningfulChangePercentagePoints;
        return riskOn == defensive
            ? FundPortfolioRiskPosture.Stable
            : riskOn ? FundPortfolioRiskPosture.MoreRiskOn : FundPortfolioRiskPosture.MoreDefensive;
    }

    private static decimal Change(IEnumerable<FundAssetAllocationChange> changes, FundAssetClass assetClass) =>
        changes.SingleOrDefault(change => change.AssetClass == assetClass)?.ChangePercentagePoints ?? 0m;
}
