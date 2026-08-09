using System.Text.Json;
using System.Security.Cryptography;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundSignalGenerationRules
{
    public FundSignalGenerationRules(
        decimal minimumActivityAmount,
        decimal minimumWeightChangePercentagePoints,
        decimal minimumSectorChangePercentagePoints,
        decimal minimumConcentrationIncreasePercentagePoints,
        decimal minimumHedgeCoverageChangePercentagePoints,
        decimal minimumUnrealizedIncomeConcentrationPercentage,
        decimal minimumValuationAdjustmentExposurePercentage,
        string version)
    {
        if (minimumActivityAmount < 0m || minimumWeightChangePercentagePoints < 0m ||
            minimumSectorChangePercentagePoints < 0m || minimumConcentrationIncreasePercentagePoints < 0m ||
            minimumHedgeCoverageChangePercentagePoints < 0m || minimumUnrealizedIncomeConcentrationPercentage < 0m ||
            minimumValuationAdjustmentExposurePercentage < 0m)
            throw new ArgumentOutOfRangeException(nameof(minimumActivityAmount));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Signal rules version is required.", nameof(version));
        MinimumActivityAmount = minimumActivityAmount;
        MinimumWeightChangePercentagePoints = minimumWeightChangePercentagePoints;
        MinimumSectorChangePercentagePoints = minimumSectorChangePercentagePoints;
        MinimumConcentrationIncreasePercentagePoints = minimumConcentrationIncreasePercentagePoints;
        MinimumHedgeCoverageChangePercentagePoints = minimumHedgeCoverageChangePercentagePoints;
        MinimumUnrealizedIncomeConcentrationPercentage = minimumUnrealizedIncomeConcentrationPercentage;
        MinimumValuationAdjustmentExposurePercentage = minimumValuationAdjustmentExposurePercentage;
        Version = version.Trim();
    }

    public static FundSignalGenerationRules Default { get; } = new(0m, 1m, 1m, 1m, 5m, 25m, 5m, "fund-portfolio-signals-v1");
    public decimal MinimumActivityAmount { get; }
    public decimal MinimumWeightChangePercentagePoints { get; }
    public decimal MinimumSectorChangePercentagePoints { get; }
    public decimal MinimumConcentrationIncreasePercentagePoints { get; }
    public decimal MinimumHedgeCoverageChangePercentagePoints { get; }
    public decimal MinimumUnrealizedIncomeConcentrationPercentage { get; }
    public decimal MinimumValuationAdjustmentExposurePercentage { get; }
    public string Version { get; }
}

public sealed record FundPortfolioSignalGenerationInput(
    Guid FundId,
    Guid ReportId,
    Guid SnapshotId,
    FundPortfolioAnalyticsSnapshot CurrentSnapshot,
    FundPortfolioAnalyticsSnapshot? PreviousSnapshot,
    FundHoldingsActivityAnalytics? HoldingsActivity,
    FundSectorStrategyAnalytics? SectorStrategy,
    FundTurnoverLiquidityAnalytics? TurnoverLiquidity,
    FundTurnoverLiquidityAnalytics? PreviousTurnoverLiquidity,
    FundDerivativeIncomeValuationAnalytics? DerivativeIncomeValuation,
    FundDerivativeIncomeValuationAnalytics? PreviousDerivativeIncomeValuation,
    FundSignalGenerationRules Rules);

public interface IFundPortfolioSignalGenerator
{
    IReadOnlyList<FundPortfolioSignal> Generate(FundPortfolioSignalGenerationInput input);
}

public sealed class FundPortfolioSignalGenerator : IFundPortfolioSignalGenerator
{
    public IReadOnlyList<FundPortfolioSignal> Generate(FundPortfolioSignalGenerationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var candidates = new List<FundPortfolioSignal>();

        if (input.HoldingsActivity is { } holdings)
        {
            foreach (var activity in holdings.NewPositions)
                candidates.Add(ActivitySignal(input, FundPortfolioSignalType.NewPosition, activity, "Disclosed new position", "A new position was disclosed in the current report.", input.Rules.MinimumActivityAmount));
            foreach (var activity in holdings.FullExits)
                candidates.Add(ActivitySignal(input, FundPortfolioSignalType.FullExit, activity, "Disclosed full exit", "A full exit was disclosed in the current report.", input.Rules.MinimumActivityAmount));
            foreach (var activity in holdings.Increases.Where(activity => IsMaterial(activity, input.Rules)))
                candidates.Add(ActivitySignal(input, FundPortfolioSignalType.MaterialPositionIncrease, activity, "Material position increase", "The disclosed position increase met the governed materiality threshold.", input.Rules.MinimumWeightChangePercentagePoints));
            foreach (var activity in holdings.Reductions.Where(activity => IsMaterial(activity, input.Rules)))
                candidates.Add(ActivitySignal(input, FundPortfolioSignalType.MaterialPositionReduction, activity, "Material position reduction", "The disclosed position reduction met the governed materiality threshold.", input.Rules.MinimumWeightChangePercentagePoints));
            AddTopActivitySignal(input, candidates, holdings.PurchasesByAmount.FirstOrDefault(), FundPortfolioSignalType.TopPurchase, "Top disclosed purchase", "This was the largest disclosed purchase by amount in the current report.");
            AddTopActivitySignal(input, candidates, holdings.SalesByAmount.FirstOrDefault(), FundPortfolioSignalType.TopSale, "Top disclosed sale", "This was the largest disclosed sale by amount in the current report.");
        }

        if (input.SectorStrategy is { } strategy)
        {
            foreach (var rotation in strategy.SectorRotation.Where(rotation => rotation.ChangePercentagePoints is >= 0m && rotation.ChangePercentagePoints >= input.Rules.MinimumSectorChangePercentagePoints))
                candidates.Add(SimpleSignal(input, FundPortfolioSignalType.SectorAllocationIncrease, rotation.IndustryCode, rotation.ChangePercentagePoints, input.Rules.MinimumSectorChangePercentagePoints, rotation.CurrentWeightPercentage, rotation.PreviousWeightPercentage, strategy.SectorResolutionConfidence, $"Sector allocation increased: {rotation.IndustryName}", "The sector's disclosed portfolio allocation increased versus the comparable report."));
            foreach (var rotation in strategy.SectorRotation.Where(rotation => rotation.ChangePercentagePoints is < 0m && Math.Abs(rotation.ChangePercentagePoints.Value) >= input.Rules.MinimumSectorChangePercentagePoints))
                candidates.Add(SimpleSignal(input, FundPortfolioSignalType.SectorAllocationDecrease, rotation.IndustryCode, rotation.ChangePercentagePoints, input.Rules.MinimumSectorChangePercentagePoints, rotation.CurrentWeightPercentage, rotation.PreviousWeightPercentage, strategy.SectorResolutionConfidence, $"Sector allocation decreased: {rotation.IndustryName}", "The sector's disclosed portfolio allocation decreased versus the comparable report."));
            var equityChange = strategy.AllocationChanges.SingleOrDefault(change => change.AssetClass == FundAssetClass.EquityAndRights);
            if (equityChange?.ChangePercentagePoints >= input.Rules.MinimumSectorChangePercentagePoints)
                candidates.Add(SimpleSignal(input, FundPortfolioSignalType.EquityExposureIncrease, nameof(FundAssetClass.EquityAndRights), equityChange.ChangePercentagePoints, input.Rules.MinimumSectorChangePercentagePoints, equityChange.CurrentWeightPercentage, equityChange.PreviousWeightPercentage, strategy.SectorResolutionConfidence, "Equity exposure increased", "The disclosed equity allocation increased versus the comparable report."));
            var commodityChange = strategy.AllocationChanges.SingleOrDefault(change => change.AssetClass == FundAssetClass.CommodityCertificates);
            if (commodityChange?.ChangePercentagePoints >= input.Rules.MinimumSectorChangePercentagePoints)
                candidates.Add(SimpleSignal(input, FundPortfolioSignalType.CommodityExposureIncrease, nameof(FundAssetClass.CommodityCertificates), commodityChange.ChangePercentagePoints, input.Rules.MinimumSectorChangePercentagePoints, commodityChange.CurrentWeightPercentage, commodityChange.PreviousWeightPercentage, strategy.SectorResolutionConfidence, "Commodity exposure increased", "The disclosed commodity allocation increased versus the comparable report."));
        }

        if (input.TurnoverLiquidity is { } liquidity && liquidity.DepositBufferChangePercentagePoints >= input.Rules.MinimumWeightChangePercentagePoints)
            candidates.Add(SimpleSignal(input, FundPortfolioSignalType.CashBufferIncrease, "cash-buffer", liquidity.DepositBufferChangePercentagePoints, input.Rules.MinimumWeightChangePercentagePoints, liquidity.CurrentDepositBufferWeightPercentage, liquidity.PreviousDepositBufferWeightPercentage, LiquidityConfidence(liquidity), "Cash buffer increased", "The disclosed deposit/cash buffer increased versus the comparable report."));

        if (input.DerivativeIncomeValuation is { } derivative && input.PreviousDerivativeIncomeValuation is { } previousDerivative)
        {
            var currentCoverage = derivative.ProtectivePutCoverage.CoveragePercentage;
            var previousCoverage = previousDerivative.ProtectivePutCoverage.CoveragePercentage;
            if (currentCoverage.HasValue && previousCoverage.HasValue && Math.Abs(currentCoverage.Value - previousCoverage.Value) >= input.Rules.MinimumHedgeCoverageChangePercentagePoints)
                candidates.Add(SimpleSignal(input, FundPortfolioSignalType.DerivativeHedgeCoverageChange, "protective-put-coverage", currentCoverage.Value - previousCoverage.Value, input.Rules.MinimumHedgeCoverageChangePercentagePoints, currentCoverage, previousCoverage, derivative.IncomeAndValuation.ConfidenceScore, "Protective-put coverage changed", "Reported protective-put coverage changed versus the comparable report."));
        }

        if (input.PreviousSnapshot is { } previousSnapshot && input.CurrentSnapshot.Top5Concentration is { } currentConcentration && previousSnapshot.Top5Concentration is { } previousConcentration)
        {
            var change = currentConcentration - previousConcentration;
            if (change >= input.Rules.MinimumConcentrationIncreasePercentagePoints)
                candidates.Add(SimpleSignal(input, FundPortfolioSignalType.ConcentrationIncrease, "top5", change, input.Rules.MinimumConcentrationIncreasePercentagePoints, currentConcentration, previousConcentration, input.CurrentSnapshot.ConfidenceScore, "Top-five concentration increased", "The disclosed top-five holding concentration increased versus the comparable report."));
        }

        if (input.TurnoverLiquidity is { } currentLiquidity && input.PreviousTurnoverLiquidity is { } previousLiquidity && currentLiquidity.LiquidityRiskStatus > previousLiquidity.LiquidityRiskStatus)
            candidates.Add(SimpleSignal(input, FundPortfolioSignalType.LiquidityRiskIncrease, "market-liquidity", currentLiquidity.LiquidityCoveragePercentage, 0m, currentLiquidity.LiquidityCoveragePercentage, null, LiquidityConfidence(currentLiquidity), "Liquidity availability decreased", "The current report has a higher governed liquidity-risk status than the comparable report."));

        if (input.DerivativeIncomeValuation is { } incomeValuation)
        {
            var concentration = incomeValuation.IncomeAndValuation.UnrealizedConcentration.LargestContributorPercentage;
            if (concentration >= input.Rules.MinimumUnrealizedIncomeConcentrationPercentage)
                candidates.Add(SimpleSignal(input, FundPortfolioSignalType.UnrealizedIncomeConcentration, "unrealized-income", concentration, input.Rules.MinimumUnrealizedIncomeConcentrationPercentage, concentration, null, incomeValuation.IncomeAndValuation.ConfidenceScore, "Unrealized income concentration identified", "A single resolved security represented at least the governed share of absolute unrealized income."));
            var adjustmentExposure = incomeValuation.IncomeAndValuation.ValuationAdjustmentExposurePercentage;
            if (incomeValuation.IncomeAndValuation.MaterialValuationAdjustmentCount > 0 && adjustmentExposure >= input.Rules.MinimumValuationAdjustmentExposurePercentage)
                candidates.Add(SimpleSignal(input, FundPortfolioSignalType.MaterialValuationAdjustment, "valuation-adjustment", adjustmentExposure, input.Rules.MinimumValuationAdjustmentExposurePercentage, adjustmentExposure, null, incomeValuation.IncomeAndValuation.ConfidenceScore, "Material valuation adjustment reported", "The report contains material valuation adjustments at or above the governed exposure threshold."));
        }

        return candidates
            .Where(signal => !string.IsNullOrWhiteSpace(signal.DeduplicationKey))
            .GroupBy(signal => signal.DeduplicationKey, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(signal => signal.ImportanceScore).ThenBy(signal => signal.Id).First())
            .OrderBy(signal => signal.SignalType)
            .ThenByDescending(signal => signal.ImportanceScore)
            .ThenBy(signal => signal.ExternalCompanyId, StringComparer.Ordinal)
            .ThenBy(signal => signal.IndustryCode, StringComparer.Ordinal)
            .ThenBy(signal => signal.DeduplicationKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static FundPortfolioSignal ActivitySignal(FundPortfolioSignalGenerationInput input, FundPortfolioSignalType type, FundActivityRanking activity, string title, string reason, decimal threshold) =>
        SimpleSignal(input, type, activity.ExternalCompanyId ?? activity.Subject, activity.DisclosedAmount ?? activity.PortfolioWeightImpactPercentagePoints, threshold, activity.DisclosedAmount, null, activity.ReconciliationStatus == FundEquityReconciliationStatus.Reconciled ? 1m : 0.65m, title, reason, activity.ExternalCompanyId, null);

    private static void AddTopActivitySignal(FundPortfolioSignalGenerationInput input, ICollection<FundPortfolioSignal> signals, FundActivityRanking? activity, FundPortfolioSignalType type, string title, string reason)
    {
        if (activity is not null)
            signals.Add(ActivitySignal(input, type, activity, title, reason, input.Rules.MinimumActivityAmount));
    }

    private static FundPortfolioSignal SimpleSignal(FundPortfolioSignalGenerationInput input, FundPortfolioSignalType type, string subject, decimal? magnitude, decimal threshold, decimal? current, decimal? baseline, decimal confidenceFactor, string title, string reason, string? externalCompanyId = null, string? industryCode = null)
    {
        var normalizedSubject = subject.Trim();
        var magnitudeValue = magnitude ?? 0m;
        var importance = threshold > 0m ? Math.Clamp(Math.Abs(magnitudeValue) / (Math.Abs(magnitudeValue) + threshold), 0m, 1m) : Math.Clamp(Math.Abs(magnitudeValue) / 100m, 0m, 1m);
        var confidence = Math.Clamp(input.CurrentSnapshot.ConfidenceScore * Math.Clamp(confidenceFactor, 0m, 1m), 0m, 1m);
        var key = $"{input.FundId:N}|{input.ReportId:N}|{type}|{normalizedSubject}|{input.Rules.Version}";
        var evidence = JsonSerializer.Serialize(new
        {
            calculationVersion = input.Rules.Version,
            fundId = input.FundId,
            reportId = input.ReportId,
            signalType = type.ToString(),
            subject = normalizedSubject,
            magnitude = magnitudeValue,
            threshold,
            current,
            baseline,
            confidenceFactor,
            sourceSnapshotConfidence = input.CurrentSnapshot.ConfidenceScore
        });
        return new(StableGuid(key), input.SnapshotId, type, externalCompanyId, industryCode, magnitude, importance, confidence, title, reason, evidence, key);
    }

    private static Guid StableGuid(string value)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static bool IsMaterial(FundActivityRanking activity, FundSignalGenerationRules rules) =>
        (activity.DisclosedAmount is >= 0m && activity.DisclosedAmount >= rules.MinimumActivityAmount) ||
        (activity.PortfolioWeightImpactPercentagePoints.HasValue && Math.Abs(activity.PortfolioWeightImpactPercentagePoints.Value) >= rules.MinimumWeightChangePercentagePoints);

    private static decimal LiquidityConfidence(FundTurnoverLiquidityAnalytics liquidity) => liquidity.LiquidityRiskStatus switch
    {
        FundPortfolioLiquidityRiskStatus.Available => 1m,
        FundPortfolioLiquidityRiskStatus.Partial => 0.7m,
        _ => 0.4m
    };
}
