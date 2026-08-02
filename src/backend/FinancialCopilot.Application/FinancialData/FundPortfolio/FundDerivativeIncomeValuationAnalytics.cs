using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundProtectivePutCoverage(
    Guid PositionId,
    string InstrumentName,
    string? UnderlyingExternalCompanyId,
    FundHedgeCoverageStatus Status,
    decimal? UnderlyingQuantity,
    decimal? CoveredQuantity,
    decimal? CoveragePercentage);

public sealed record FundProtectivePutCoverageSummary(
    int ProtectivePutCount,
    int CoveredCount,
    int PartiallyCoveredCount,
    int OverCoveredCount,
    int UncoveredCount,
    int UnknownCount,
    decimal? UnderlyingQuantity,
    decimal? CoveredQuantity,
    decimal? CoveragePercentage,
    decimal? UncoveredQuantity,
    decimal? UnknownUnderlyingExposure,
    IReadOnlyList<FundProtectivePutCoverage> Positions);

public sealed record FundIncomeComposition(
    FundIncomeCategory Category,
    decimal Amount,
    decimal? PercentageOfKnownIncome,
    int ReconciledSourceCount,
    int ExcludedSourceCount);

public sealed record FundIncomeContributor(
    string SecurityName,
    string? ExternalCompanyId,
    decimal Amount,
    decimal? PercentageOfAbsoluteIncome);

public sealed record FundUnrealizedIncomeConcentration(
    decimal? TotalAbsoluteUnrealizedIncome,
    decimal? LargestAbsoluteUnrealizedIncome,
    decimal? LargestContributorPercentage,
    decimal? HerfindahlIndex,
    int ResolvedContributorCount);

public sealed record FundIncomeValuationAnalytics(
    IReadOnlyList<FundIncomeComposition> IncomeComposition,
    decimal? DividendIncome,
    decimal? RealizedIncome,
    decimal? UnrealizedIncome,
    decimal? KnownIncome,
    IReadOnlyList<FundIncomeContributor> TopContributors,
    IReadOnlyList<FundIncomeContributor> TopDetractors,
    FundUnrealizedIncomeConcentration UnrealizedConcentration,
    int ValuationAdjustmentCount,
    int MaterialValuationAdjustmentCount,
    decimal? ValuationAdjustmentExposureAmount,
    decimal? ValuationAdjustmentExposurePercentage,
    IReadOnlyDictionary<string, int> ValuationAdjustmentReasons,
    FundPortfolioValuationQualityStatus ValuationQualityStatus,
    decimal ConfidenceScore,
    int UnreconciledInputCount,
    int SourceErrorCount,
    string CalculationVersion);

public sealed record FundDerivativeIncomeValuationInput(
    IReadOnlyCollection<FundDerivativePosition> Derivatives,
    IReadOnlyCollection<FundEquityPositionSnapshot> EndingEquityHoldings,
    IReadOnlyCollection<FundInvestmentIncomeSummary> IncomeSummaries,
    IReadOnlyCollection<FundSecurityIncomeAttribution> SecurityIncomeAttributions,
    IReadOnlyCollection<FundValuationAdjustment> ValuationAdjustments,
    FundPortfolioValuationQualitySnapshot? ValuationQuality,
    int SourceErrorCount,
    string CalculationVersion);

public sealed record FundDerivativeIncomeValuationAnalytics(
    FundProtectivePutCoverageSummary ProtectivePutCoverage,
    FundIncomeValuationAnalytics IncomeAndValuation);

public interface IFundDerivativeIncomeValuationAnalyticsCalculator
{
    FundDerivativeIncomeValuationAnalytics Calculate(FundDerivativeIncomeValuationInput input);
}

public sealed class FundDerivativeIncomeValuationAnalyticsCalculator : IFundDerivativeIncomeValuationAnalyticsCalculator
{
    public FundDerivativeIncomeValuationAnalytics Calculate(FundDerivativeIncomeValuationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.CalculationVersion))
            throw new ArgumentException("Calculation version is required.", nameof(input));

        var protectivePuts = input.Derivatives
            .Where(derivative => derivative.DerivativeType == FundDerivativeType.ProtectivePut)
            .Select(derivative => CalculatePutCoverage(derivative, input.EndingEquityHoldings))
            .OrderByDescending(position => position.Status)
            .ThenBy(position => position.InstrumentName, StringComparer.Ordinal)
            .ToArray();
        var knownUnderlying = Sum(protectivePuts.Select(position => position.UnderlyingQuantity));
        var covered = Sum(protectivePuts.Select(position => position.CoveredQuantity));
        var unknownExposure = Sum(protectivePuts
            .Where(position => position.Status == FundHedgeCoverageStatus.UnknownInputs)
            .Select(position => position.UnderlyingQuantity));
        var coverage = knownUnderlying is > 0m && covered.HasValue
            ? (decimal?)(Math.Min(covered.Value, knownUnderlying.Value) / knownUnderlying.Value * 100m)
            : null;
        var protectiveSummary = new FundProtectivePutCoverageSummary(
            protectivePuts.Length,
            protectivePuts.Count(position => position.Status == FundHedgeCoverageStatus.Covered),
            protectivePuts.Count(position => position.Status == FundHedgeCoverageStatus.PartiallyCovered),
            protectivePuts.Count(position => position.Status == FundHedgeCoverageStatus.OverCovered),
            protectivePuts.Count(position => position.Status == FundHedgeCoverageStatus.NoMatchingHolding),
            protectivePuts.Count(position => position.Status == FundHedgeCoverageStatus.UnknownInputs),
            knownUnderlying,
            covered,
            coverage,
            Sum(protectivePuts.Where(position => position.Status == FundHedgeCoverageStatus.NoMatchingHolding)
                .Select(position => position.UnderlyingQuantity)),
            unknownExposure,
            protectivePuts);

        var reconciledSummaries = input.IncomeSummaries
            .Where(summary => summary.ReconciliationStatus == FundIncomeReconciliationStatus.Reconciled && summary.Amount.HasValue)
            .ToArray();
        var knownIncome = Sum(reconciledSummaries.Select(summary => summary.Amount));
        var compositions = input.IncomeSummaries
            .GroupBy(summary => summary.IncomeCategory)
            .Select(group => new FundIncomeComposition(
                group.Key,
                group.Where(summary => summary.ReconciliationStatus == FundIncomeReconciliationStatus.Reconciled)
                    .Select(summary => summary.Amount).Where(amount => amount.HasValue).Sum(amount => amount!.Value),
                null,
                group.Count(summary => summary.ReconciliationStatus == FundIncomeReconciliationStatus.Reconciled && summary.Amount.HasValue),
                group.Count(summary => summary.ReconciliationStatus != FundIncomeReconciliationStatus.Reconciled || !summary.Amount.HasValue)))
            .Where(composition => composition.ReconciledSourceCount > 0)
            .ToArray();
        compositions = compositions.Select(composition => composition with
        {
            PercentageOfKnownIncome = knownIncome is { } total && total != 0m
                ? composition.Amount / total * 100m
                : null
        }).OrderByDescending(composition => Math.Abs(composition.Amount)).ThenBy(composition => composition.Category).ToArray();

        var resolvedAttributions = input.SecurityIncomeAttributions
            .Where(attribution => attribution.ResolutionStatus == FundIncomeResolutionStatus.Resolved &&
                attribution.ReconciliationStatus == FundIncomeReconciliationStatus.Reconciled && attribution.TotalIncome.HasValue)
            .Select(attribution => new FundIncomeContributor(attribution.RawSecurityName, attribution.ExternalCompanyId, attribution.TotalIncome!.Value, null))
            .OrderByDescending(contributor => contributor.Amount).ThenBy(contributor => contributor.SecurityName, StringComparer.Ordinal)
            .ToArray();
        var absoluteIncome = resolvedAttributions.Sum(contributor => Math.Abs(contributor.Amount));
        var contributors = resolvedAttributions.Select(contributor => contributor with
        {
            PercentageOfAbsoluteIncome = absoluteIncome > 0m ? Math.Abs(contributor.Amount) / absoluteIncome * 100m : null
        }).ToArray();
        var unrealized = input.SecurityIncomeAttributions
            .Where(attribution => attribution.ResolutionStatus == FundIncomeResolutionStatus.Resolved &&
                attribution.ReconciliationStatus == FundIncomeReconciliationStatus.Reconciled && attribution.UnrealizedPriceChangeIncome.HasValue)
            .Select(attribution => attribution.UnrealizedPriceChangeIncome!.Value)
            .ToArray();
        var absoluteUnrealized = unrealized.Sum(Math.Abs);
        var unrealizedShares = input.SecurityIncomeAttributions
            .Where(attribution => attribution.ResolutionStatus == FundIncomeResolutionStatus.Resolved &&
                attribution.ReconciliationStatus == FundIncomeReconciliationStatus.Reconciled && attribution.UnrealizedPriceChangeIncome.HasValue)
            .Select(attribution => Math.Abs(attribution.UnrealizedPriceChangeIncome!.Value) / (absoluteUnrealized == 0m ? 1m : absoluteUnrealized))
            .ToArray();
        var incomeAnalytics = new FundIncomeValuationAnalytics(
            compositions,
            AmountFor(compositions, FundIncomeCategory.EquityDividend),
            AddAmounts(AmountFor(compositions, FundIncomeCategory.EquityRealized), AmountFor(compositions, FundIncomeCategory.CommodityRealized)),
            AddAmounts(AmountFor(compositions, FundIncomeCategory.EquityUnrealized), AmountFor(compositions, FundIncomeCategory.CommodityUnrealized)),
            knownIncome,
            contributors.Where(contributor => contributor.Amount > 0m).Take(5).ToArray(),
            contributors.Where(contributor => contributor.Amount < 0m).OrderBy(contributor => contributor.Amount).Take(5).ToArray(),
            new FundUnrealizedIncomeConcentration(
                absoluteUnrealized > 0m ? absoluteUnrealized : null,
                unrealized.Length == 0 ? null : unrealized.Max(value => Math.Abs(value)),
                absoluteUnrealized > 0m ? unrealized.Max(value => Math.Abs(value)) / absoluteUnrealized * 100m : null,
                unrealizedShares.Length == 0 ? null : unrealizedShares.Sum(share => share * share),
                unrealized.Length),
            input.ValuationAdjustments.Count,
            input.ValuationAdjustments.Count(adjustment => adjustment.IsMaterial),
            Sum(input.ValuationAdjustments.Select(adjustment => adjustment.AdjustedValue)),
            input.ValuationQuality?.AdjustedValueExposurePercentage,
            input.ValuationAdjustments.Where(adjustment => !string.IsNullOrWhiteSpace(adjustment.Reason))
                .GroupBy(adjustment => adjustment.Reason!.Trim(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            input.ValuationQuality?.QualityStatus ?? FundPortfolioValuationQualityStatus.Unknown,
            CalculateConfidence(input, protectivePuts.Length, resolvedAttributions.Length),
            input.IncomeSummaries.Count(summary => summary.ReconciliationStatus != FundIncomeReconciliationStatus.Reconciled) +
                input.SecurityIncomeAttributions.Count(attribution => attribution.ReconciliationStatus != FundIncomeReconciliationStatus.Reconciled) +
                input.ValuationAdjustments.Count(adjustment => adjustment.ResolutionStatus != FundIncomeResolutionStatus.Resolved),
            input.SourceErrorCount,
            input.CalculationVersion);
        return new(protectiveSummary, incomeAnalytics);
    }

    private static FundProtectivePutCoverage CalculatePutCoverage(
        FundDerivativePosition derivative,
        IReadOnlyCollection<FundEquityPositionSnapshot> holdings)
    {
        var holding = holdings.FirstOrDefault(candidate =>
            derivative.UnderlyingTradingInstrumentId.HasValue && candidate.TradingInstrumentId == derivative.UnderlyingTradingInstrumentId ||
            derivative.UnderlyingExternalCompanyId != null && candidate.ExternalCompanyId == derivative.UnderlyingExternalCompanyId);
        var underlying = holding?.Quantity;
        var status = derivative.ResolutionStatus != FundNonEquityResolutionStatus.Resolved ||
            (!derivative.UnderlyingTradingInstrumentId.HasValue && string.IsNullOrWhiteSpace(derivative.UnderlyingExternalCompanyId))
            ? FundHedgeCoverageStatus.UnknownInputs
            : holding is null ? FundHedgeCoverageStatus.NoMatchingHolding : derivative.HedgeCoverageStatus;
        var covered = derivative.UnderlyingCoverageQuantity;
        var percentage = underlying is > 0m && covered.HasValue ? (decimal?)(covered.Value / underlying.Value * 100m) : null;
        return new(derivative.Id, derivative.NormalizedInstrumentName, derivative.UnderlyingExternalCompanyId, status, underlying, covered, percentage);
    }

    private static decimal? AmountFor(IEnumerable<FundIncomeComposition> compositions, FundIncomeCategory category) =>
        compositions.SingleOrDefault(composition => composition.Category == category)?.Amount;

    private static decimal? AddAmounts(decimal? first, decimal? second) =>
        first.HasValue || second.HasValue ? (first ?? 0m) + (second ?? 0m) : null;

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    private static decimal CalculateConfidence(FundDerivativeIncomeValuationInput input, int protectivePutCount, int resolvedAttributionCount)
    {
        var score = 1m;
        if (input.SourceErrorCount > 0) score -= 0.2m;
        if (input.IncomeSummaries.Any(summary => summary.ReconciliationStatus != FundIncomeReconciliationStatus.Reconciled) ||
            input.SecurityIncomeAttributions.Any(attribution => attribution.ReconciliationStatus != FundIncomeReconciliationStatus.Reconciled) ||
            input.ValuationAdjustments.Any(adjustment => adjustment.ResolutionStatus != FundIncomeResolutionStatus.Resolved)) score -= 0.15m;
        if (input.IncomeSummaries.Count == 0 || resolvedAttributionCount == 0) score -= 0.15m;
        if (protectivePutCount > 0 && input.Derivatives.Any(derivative => derivative.HedgeCoverageStatus == FundHedgeCoverageStatus.UnknownInputs)) score -= 0.15m;
        if (input.ValuationQuality is null) score -= 0.2m;
        else if (input.ValuationQuality.QualityStatus is FundPortfolioValuationQualityStatus.Limited or FundPortfolioValuationQualityStatus.InsufficientEvidence) score -= 0.2m;
        return Math.Clamp(score, 0m, 1m);
    }
}
