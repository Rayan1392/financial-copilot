using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundNonEquityQuery(
    Guid? FundId = null,
    Guid? ReportId = null,
    DateOnly? PeriodEndDate = null,
    FundWorkbookPeriodContext? PeriodContext = null,
    FundNonEquityResolutionStatus? ResolutionStatus = null);

public interface IFundPortfolioNonEquitySectionNormalizer : IFundPortfolioSectionNormalizer
{
}

public interface IFundNonEquityAssetRepository
{
    Task<IReadOnlyList<FundAssetAllocationSnapshot>> QueryAllocationsAsync(FundNonEquityQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<FundCommodityCertificatePosition>> QueryCommodityCertificatesAsync(FundNonEquityQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<FundBankDepositPosition>> QueryBankDepositsAsync(FundNonEquityQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<FundDerivativePosition>> QueryDerivativesAsync(FundNonEquityQuery query, CancellationToken cancellationToken);
    Task<int> CountUnresolvedAsync(Guid reportId, CancellationToken cancellationToken);
}

public sealed class FundNonEquityNormalizationOptions
{
    public const string SectionName = "FundPortfolio:NonEquityNormalization";
    public decimal QuantityTolerance { get; init; } = 0.0001m;
    public decimal AbsoluteValueTolerance { get; init; } = 1m;
    public decimal PercentagePointTolerance { get; init; } = 0.01m;
}

public interface IFundNonEquityNormalizationTelemetry
{
    void Record(
        Guid reportId,
        int allocationCount,
        int commodityCount,
        int depositCount,
        int derivativeCount,
        int unresolvedCount,
        int depositEquationFailureCount,
        int totalDifferenceCount,
        int resolvedUnderlyingCount,
        int coverageAvailableCount,
        TimeSpan duration);
}

public static class FundNonEquityReconciliationPolicy
{
    public static decimal? MovementDifference(decimal? beginning, decimal? increase, decimal? decrease, decimal? ending) =>
        beginning.HasValue && increase.HasValue && decrease.HasValue && ending.HasValue
            ? ending.Value - (beginning.Value + increase.Value - decrease.Value)
            : null;

    public static FundNonEquityReconciliationStatus ReconcileMovement(
        decimal? beginning,
        decimal? increase,
        decimal? decrease,
        decimal? ending,
        decimal tolerance)
    {
        var difference = MovementDifference(beginning, increase, decrease, ending);
        return difference.HasValue
            ? Math.Abs(difference.Value) <= tolerance
                ? FundNonEquityReconciliationStatus.Reconciled
                : FundNonEquityReconciliationStatus.Unreconciled
            : FundNonEquityReconciliationStatus.UnknownInputs;
    }
}

public static class FundHedgeCoveragePolicy
{
    public const string CalculationVersion = "protective-put-coverage-v1";

    public static FundHedgeCoverageStatus Classify(
        FundDerivativeType derivativeType,
        decimal? coveredQuantity,
        decimal? matchingUnderlyingEndingQuantity,
        decimal tolerance = 0.0001m)
    {
        if (derivativeType != FundDerivativeType.ProtectivePut) return FundHedgeCoverageStatus.NotApplicable;
        if (!coveredQuantity.HasValue) return FundHedgeCoverageStatus.UnknownInputs;
        if (!matchingUnderlyingEndingQuantity.HasValue || matchingUnderlyingEndingQuantity.Value <= tolerance)
            return FundHedgeCoverageStatus.NoMatchingHolding;
        var difference = coveredQuantity.Value - matchingUnderlyingEndingQuantity.Value;
        if (Math.Abs(difference) <= tolerance) return FundHedgeCoverageStatus.Covered;
        return difference < 0 ? FundHedgeCoverageStatus.PartiallyCovered : FundHedgeCoverageStatus.OverCovered;
    }
}

public static class FundBankCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["بانک ملی"] = "BANK_MELLI",
        ["ملی"] = "BANK_MELLI",
        ["بانک ملت"] = "BANK_MELLAT",
        ["ملت"] = "BANK_MELLAT",
        ["بانک تجارت"] = "BANK_TEJARAT",
        ["تجارت"] = "BANK_TEJARAT",
        ["بانک صادرات"] = "BANK_SADERAT",
        ["صادرات"] = "BANK_SADERAT",
        ["بانک پاسارگاد"] = "BANK_PASARGAD",
        ["پاسارگاد"] = "BANK_PASARGAD",
        ["بانک سامان"] = "BANK_SAMAN",
        ["سامان"] = "BANK_SAMAN",
        ["بانک پارسیان"] = "BANK_PARSIAN",
        ["پارسیان"] = "BANK_PARSIAN",
        ["بانک اقتصاد نوین"] = "BANK_EGHTESAD_NOVIN",
        ["اقتصاد نوین"] = "BANK_EGHTESAD_NOVIN"
    };

    public static string? Resolve(string normalizedName) => Aliases.TryGetValue(normalizedName, out var code) ? code : null;
}

public static class FundCommodityCatalog
{
    public static (FundCommodityType Type, string? Code) Resolve(string normalizedName)
    {
        if (Contains(normalizedName, "طلا", "سکه", "شمش", "gold")) return (FundCommodityType.GoldBullion, "GOLD_BULLION");
        if (Contains(normalizedName, "مس", "کاتد", "copper", "cathode")) return (FundCommodityType.CopperCathode, "COPPER_CATHODE");
        if (Contains(normalizedName, "میلگرد", "rebar")) return (FundCommodityType.Rebar, "REBAR");
        if (Contains(normalizedName, "گواهی سپرده", "کالا", "commodity")) return (FundCommodityType.OtherCommodity, "OTHER_COMMODITY");
        return (FundCommodityType.Unknown, null);
    }

    private static bool Contains(string value, params string[] terms) => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
