using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public enum FundTurnoverDenominatorPolicy
{
    AverageDisclosedPortfolioMarketValue
}

public enum FundLiquidityAvailability
{
    Available,
    UnresolvedSecurity,
    MissingMarketVolume,
    Suspended,
    ZeroVolume,
    MissingQuantity
}

public sealed record FundTurnoverLiquidityRules
{
    public FundTurnoverLiquidityRules(
        decimal participationRate,
        FundTurnoverDenominatorPolicy denominatorPolicy,
        string version)
    {
        if (participationRate is <= 0m or > 1m) throw new ArgumentOutOfRangeException(nameof(participationRate));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Liquidity rules version is required.", nameof(version));
        ParticipationRate = participationRate;
        DenominatorPolicy = denominatorPolicy;
        Version = version.Trim();
    }

    public decimal ParticipationRate { get; }
    public FundTurnoverDenominatorPolicy DenominatorPolicy { get; }
    public string Version { get; }
}

public sealed record FundLiquidityPositionFact(
    Guid Id,
    string Subject,
    Guid? TradingInstrumentId,
    string? ExternalCompanyId,
    decimal? EndingQuantity,
    decimal? MarketValue,
    bool IsSecurityResolved);

public sealed record FundMarketVolumeFact(
    Guid? TradingInstrumentId,
    decimal? AverageDailyVolume,
    bool IsSuspended);

public sealed record FundDepositBufferFact(
    decimal? EndingBalance,
    decimal? WeightPercentage);

public sealed record FundTurnoverLiquidityInput(
    IReadOnlyCollection<FundActivityFact> Activities,
    IReadOnlyCollection<FundLiquidityPositionFact> EquityPositions,
    IReadOnlyCollection<FundMarketVolumeFact> MarketVolumes,
    IReadOnlyCollection<FundDepositBufferFact> CurrentDeposits,
    IReadOnlyCollection<FundDepositBufferFact> PreviousDeposits,
    decimal? AverageDisclosedPortfolioMarketValue,
    FundTurnoverLiquidityRules Rules);

public sealed record FundLiquidityPositionResult(
    Guid PositionId,
    string Subject,
    decimal? MarketValue,
    decimal? LiquidationDays,
    FundLiquidityAvailability Availability);

public sealed record FundTurnoverLiquidityAnalytics(
    decimal? PurchaseAmount,
    decimal? SaleAmount,
    decimal? GrossTurnoverAmount,
    decimal? NetEquityDeploymentAmount,
    decimal? TurnoverRatio,
    decimal? TurnoverDenominatorAmount,
    FundTurnoverDenominatorPolicy TurnoverDenominatorPolicy,
    string TurnoverDenominatorDefinition,
    decimal? CurrentDepositBufferAmount,
    decimal? PreviousDepositBufferAmount,
    decimal? DepositBufferChangeAmount,
    decimal? CurrentDepositBufferWeightPercentage,
    decimal? PreviousDepositBufferWeightPercentage,
    decimal? DepositBufferChangePercentagePoints,
    IReadOnlyList<FundLiquidityPositionResult> LiquidityPositions,
    decimal? WeightedLiquidationDays,
    decimal LiquidityCoveragePercentage,
    FundPortfolioLiquidityRiskStatus LiquidityRiskStatus,
    string MissingDataCoverage,
    string RulesVersion);

public interface IFundTurnoverLiquidityAnalyticsCalculator
{
    FundTurnoverLiquidityAnalytics Calculate(FundTurnoverLiquidityInput input);
}

public sealed class FundTurnoverLiquidityAnalyticsCalculator : IFundTurnoverLiquidityAnalyticsCalculator
{
    public FundTurnoverLiquidityAnalytics Calculate(FundTurnoverLiquidityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var purchase = SumKnown(input.Activities.Select(activity => activity.PurchaseAmount));
        var sale = SumKnown(input.Activities.Select(activity => activity.SaleProceeds));
        var gross = purchase.HasValue && sale.HasValue ? purchase + sale : null;
        var net = purchase.HasValue && sale.HasValue ? purchase - sale : null;
        var denominator = input.AverageDisclosedPortfolioMarketValue is > 0m
            ? input.AverageDisclosedPortfolioMarketValue
            : null;
        var ratio = denominator.HasValue && gross.HasValue ? gross / denominator : null;
        var currentDepositAmount = SumKnown(input.CurrentDeposits.Select(deposit => deposit.EndingBalance));
        var previousDepositAmount = SumKnown(input.PreviousDeposits.Select(deposit => deposit.EndingBalance));
        var currentDepositWeight = SumKnown(input.CurrentDeposits.Select(deposit => deposit.WeightPercentage));
        var previousDepositWeight = SumKnown(input.PreviousDeposits.Select(deposit => deposit.WeightPercentage));
        var liquidity = input.EquityPositions.Select(position => CalculateLiquidity(position, input.MarketVolumes, input.Rules.ParticipationRate)).ToArray();
        var totalMarketValue = SumKnown(input.EquityPositions.Select(position => position.MarketValue));
        var availableMarketValue = SumKnown(liquidity.Where(result => result.Availability == FundLiquidityAvailability.Available).Select(result => result.MarketValue));
        var coverage = totalMarketValue is > 0m && availableMarketValue.HasValue ? availableMarketValue.Value / totalMarketValue.Value * 100m : 0m;
        var weightedDays = WeightedLiquidationDays(liquidity, totalMarketValue);
        var status = liquidity.Length == 0 || availableMarketValue is null
            ? FundPortfolioLiquidityRiskStatus.Unavailable
            : coverage >= 99.999m ? FundPortfolioLiquidityRiskStatus.Available : FundPortfolioLiquidityRiskStatus.Partial;

        return new(
            purchase,
            sale,
            gross,
            net,
            ratio,
            denominator,
            input.Rules.DenominatorPolicy,
            "average disclosed portfolio market value where available; gross turnover is purchases plus sales",
            currentDepositAmount,
            previousDepositAmount,
            Difference(currentDepositAmount, previousDepositAmount),
            currentDepositWeight,
            previousDepositWeight,
            Difference(currentDepositWeight, previousDepositWeight),
            liquidity,
            weightedDays,
            coverage,
            status,
            BuildMissingDataCoverage(liquidity, denominator, currentDepositAmount, previousDepositAmount),
            input.Rules.Version);
    }

    private static FundLiquidityPositionResult CalculateLiquidity(
        FundLiquidityPositionFact position,
        IReadOnlyCollection<FundMarketVolumeFact> volumes,
        decimal participationRate)
    {
        if (!position.IsSecurityResolved || position.TradingInstrumentId is null)
            return new(position.Id, position.Subject, position.MarketValue, null, FundLiquidityAvailability.UnresolvedSecurity);
        if (position.EndingQuantity is not > 0m)
            return new(position.Id, position.Subject, position.MarketValue, null, FundLiquidityAvailability.MissingQuantity);
        var volume = volumes.FirstOrDefault(candidate => candidate.TradingInstrumentId == position.TradingInstrumentId);
        if (volume is null || volume.AverageDailyVolume is null)
            return new(position.Id, position.Subject, position.MarketValue, null, FundLiquidityAvailability.MissingMarketVolume);
        if (volume.IsSuspended)
            return new(position.Id, position.Subject, position.MarketValue, null, FundLiquidityAvailability.Suspended);
        if (volume.AverageDailyVolume <= 0m)
            return new(position.Id, position.Subject, position.MarketValue, null, FundLiquidityAvailability.ZeroVolume);
        return new(position.Id, position.Subject, position.MarketValue, position.EndingQuantity.Value / (volume.AverageDailyVolume.Value * participationRate), FundLiquidityAvailability.Available);
    }

    private static decimal? WeightedLiquidationDays(IReadOnlyCollection<FundLiquidityPositionResult> results, decimal? totalMarketValue)
    {
        if (totalMarketValue is not > 0m) return null;
        var weighted = results.Where(result => result.Availability == FundLiquidityAvailability.Available && result.LiquidationDays.HasValue && result.MarketValue is >= 0m)
            .Sum(result => result.LiquidationDays!.Value * result.MarketValue!.Value);
        return weighted / totalMarketValue.Value;
    }

    private static decimal? Difference(decimal? current, decimal? previous) =>
        current.HasValue && previous.HasValue ? current.Value - previous.Value : null;

    private static string BuildMissingDataCoverage(
        IReadOnlyCollection<FundLiquidityPositionResult> liquidity,
        decimal? denominator,
        decimal? currentDepositAmount,
        decimal? previousDepositAmount)
    {
        var available = liquidity.Count(result => result.Availability == FundLiquidityAvailability.Available);
        var unavailable = liquidity.Count - available;
        var denominatorState = denominator.HasValue ? "available" : "missing or non-positive";
        var currentDepositState = currentDepositAmount.HasValue ? "available" : "missing";
        var previousDepositState = previousDepositAmount.HasValue ? "available" : "missing";
        return $"liquidity market-volume coverage: {available}/{liquidity.Count} positions; " +
               $"unavailable positions: {unavailable}; turnover denominator: {denominatorState}; " +
               $"deposit buffer current: {currentDepositState}, previous: {previousDepositState}";
    }

    private static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }
}
