using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundPortfolioMaterialityThresholds
{
    public FundPortfolioMaterialityThresholds(
        decimal absoluteAmount,
        decimal assetWeightChangePercentagePoints,
        decimal percentile,
        string version)
    {
        if (absoluteAmount < 0m) throw new ArgumentOutOfRangeException(nameof(absoluteAmount));
        if (assetWeightChangePercentagePoints < 0m) throw new ArgumentOutOfRangeException(nameof(assetWeightChangePercentagePoints));
        if (percentile is < 0m or > 1m) throw new ArgumentOutOfRangeException(nameof(percentile));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Materiality version is required.", nameof(version));
        AbsoluteAmount = absoluteAmount;
        AssetWeightChangePercentagePoints = assetWeightChangePercentagePoints;
        Percentile = percentile;
        Version = version.Trim();
    }

    public decimal AbsoluteAmount { get; }
    public decimal AssetWeightChangePercentagePoints { get; }
    public decimal Percentile { get; }
    public string Version { get; }
}

public static class FundPortfolioMaterialityPolicy
{
    public const string DefaultVersion = "fund-portfolio-materiality-v1";

    public static FundPortfolioMaterialityThresholds Default { get; } =
        new(0m, 1m, 0.9m, DefaultVersion);
}

public sealed record FundHoldingFact(
    Guid Id,
    string? ExternalCompanyId,
    string SecurityName,
    decimal? MarketValue,
    decimal? WeightOfTotalAssetsPercentage,
    decimal? WeightChangePercentagePoints,
    FundSecurityResolutionStatus ResolutionStatus);

public sealed record FundActivityFact(
    Guid Id,
    string? ExternalCompanyId,
    string SecurityName,
    decimal? PurchaseAmount,
    decimal? SaleProceeds,
    decimal? PurchasedQuantity,
    decimal? SoldQuantity,
    decimal? PortfolioWeightImpactPercentagePoints,
    FundEquityActivityClassification Classification,
    FundEquityReconciliationStatus ReconciliationStatus);

public sealed record FundHoldingsActivityInput(
    IReadOnlyCollection<FundHoldingFact> EndingHoldings,
    IReadOnlyCollection<FundActivityFact> Activities,
    FundPortfolioMaterialityThresholds MaterialityThresholds);

public sealed record FundHoldingRanking(
    Guid Id,
    string Subject,
    string? ExternalCompanyId,
    decimal? MarketValue,
    decimal? WeightOfTotalAssetsPercentage,
    decimal? WeightChangePercentagePoints,
    bool IsMaterial,
    FundSecurityResolutionStatus ResolutionStatus);

public sealed record FundActivityRanking(
    Guid Id,
    string Subject,
    string? ExternalCompanyId,
    decimal? PurchaseAmount,
    decimal? SaleProceeds,
    decimal? PurchasedQuantity,
    decimal? SoldQuantity,
    decimal? PortfolioWeightImpactPercentagePoints,
    FundEquityActivityClassification Classification,
    FundEquityReconciliationStatus ReconciliationStatus)
{
    public decimal? DisclosedAmount => PurchaseAmount ?? SaleProceeds;
    public decimal? QuantityChange => PurchasedQuantity.HasValue || SoldQuantity.HasValue
        ? PurchasedQuantity - SoldQuantity
        : null;
}

public sealed record FundHoldingsActivityAnalytics(
    IReadOnlyList<FundHoldingRanking> TopHoldings,
    decimal? Top5Concentration,
    decimal? Top10Concentration,
    decimal? HerfindahlIndex,
    int MaterialPositionCount,
    IReadOnlyList<FundActivityRanking> PurchasesByAmount,
    IReadOnlyList<FundActivityRanking> SalesByAmount,
    IReadOnlyList<FundActivityRanking> PurchasesByQuantity,
    IReadOnlyList<FundActivityRanking> SalesByQuantity,
    IReadOnlyList<FundActivityRanking> PurchasesByWeightImpact,
    IReadOnlyList<FundActivityRanking> SalesByWeightImpact,
    IReadOnlyList<FundActivityRanking> NewPositions,
    IReadOnlyList<FundActivityRanking> FullExits,
    IReadOnlyList<FundActivityRanking> Increases,
    IReadOnlyList<FundActivityRanking> Reductions,
    decimal? PurchaseAmount,
    decimal? SaleAmount,
    decimal? NetEquityDeploymentAmount,
    string NetEquityDeploymentDefinition,
    string MaterialityVersion);

public interface IFundHoldingsActivityAnalyticsCalculator
{
    FundHoldingsActivityAnalytics Calculate(FundHoldingsActivityInput input);
}

public sealed class FundHoldingsActivityAnalyticsCalculator : IFundHoldingsActivityAnalyticsCalculator
{
    public FundHoldingsActivityAnalytics Calculate(FundHoldingsActivityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var holdings = input.EndingHoldings
            .Where(holding => holding.MarketValue is >= 0m || holding.WeightOfTotalAssetsPercentage is >= 0m)
            .Select(holding => new { Fact = holding, Subject = Subject(holding.ExternalCompanyId, holding.SecurityName) })
            .OrderByDescending(item => item.Fact.WeightOfTotalAssetsPercentage ?? item.Fact.MarketValue)
            .ThenBy(item => item.Subject, StringComparer.Ordinal)
            .ThenBy(item => item.Fact.Id)
            .ToArray();
        var shares = ConcentrationShares(holdings.Select(item => item.Fact).ToArray());
        var values = holdings.Select(item => item.Fact.MarketValue).Where(value => value.HasValue).Select(value => value!.Value).OrderBy(value => value).ToArray();
        var percentileCutoff = values.Length == 0 ? (decimal?)null : Quantile(values, input.MaterialityThresholds.Percentile);
        var rankedHoldings = holdings.Select(item => new FundHoldingRanking(
            item.Fact.Id,
            item.Subject,
            item.Fact.ExternalCompanyId,
            item.Fact.MarketValue,
            item.Fact.WeightOfTotalAssetsPercentage,
            item.Fact.WeightChangePercentagePoints,
            IsMaterial(item.Fact, percentileCutoff, input.MaterialityThresholds),
            item.Fact.ResolutionStatus)).ToArray();
        var activities = input.Activities.Select(activity => ToRanking(activity)).ToArray();

        return new(
            rankedHoldings,
            shares is null ? null : shares.OrderByDescending(value => value).Take(5).Sum(),
            shares is null ? null : shares.OrderByDescending(value => value).Take(10).Sum(),
            shares is null ? null : shares.Sum(value => value * value),
            rankedHoldings.Count(holding => holding.IsMaterial),
            Rank(activities, activity => activity.PurchaseAmount),
            Rank(activities, activity => activity.SaleProceeds),
            Rank(activities, activity => activity.PurchasedQuantity),
            Rank(activities, activity => activity.SoldQuantity),
            Rank(activities, activity => activity.PortfolioWeightImpactPercentagePoints,
                activity => activity.Classification is FundEquityActivityClassification.NewPosition or FundEquityActivityClassification.Increased),
            Rank(activities, activity => activity.PortfolioWeightImpactPercentagePoints,
                activity => activity.Classification is FundEquityActivityClassification.FullExit or FundEquityActivityClassification.Reduced),
            activities.Where(activity => activity.Classification == FundEquityActivityClassification.NewPosition).ToArray(),
            activities.Where(activity => activity.Classification == FundEquityActivityClassification.FullExit).ToArray(),
            activities.Where(activity => activity.Classification == FundEquityActivityClassification.Increased).ToArray(),
            activities.Where(activity => activity.Classification == FundEquityActivityClassification.Reduced).ToArray(),
            SumKnown(input.Activities.Select(activity => activity.PurchaseAmount)),
            SumKnown(input.Activities.Select(activity => activity.SaleProceeds)),
            NetDeployment(input.Activities),
            "disclosed purchases minus disclosed sale proceeds; not fund net cash flow",
            input.MaterialityThresholds.Version);
    }

    private static decimal[]? ConcentrationShares(IReadOnlyCollection<FundHoldingFact> holdings)
    {
        var weights = holdings.Where(holding => holding.WeightOfTotalAssetsPercentage is >= 0m)
            .Select(holding => holding.WeightOfTotalAssetsPercentage!.Value).ToArray();
        if (weights.Length > 0 && weights.Sum() > 0m)
            return weights.Select(weight => weight / weights.Sum()).ToArray();
        var values = holdings.Where(holding => holding.MarketValue is >= 0m)
            .Select(holding => holding.MarketValue!.Value).ToArray();
        var total = values.Sum();
        return values.Length == 0 || total <= 0m ? null : values.Select(value => value / total).ToArray();
    }

    private static bool IsMaterial(FundHoldingFact holding, decimal? percentileCutoff, FundPortfolioMaterialityThresholds thresholds) =>
        (holding.MarketValue is >= 0m && holding.MarketValue >= thresholds.AbsoluteAmount) ||
        (holding.WeightChangePercentagePoints.HasValue && Math.Abs(holding.WeightChangePercentagePoints.Value) >= thresholds.AssetWeightChangePercentagePoints) ||
        (percentileCutoff.HasValue && holding.MarketValue.HasValue && holding.MarketValue.Value >= percentileCutoff.Value);

    private static FundActivityRanking ToRanking(FundActivityFact activity) =>
        new(activity.Id, Subject(activity.ExternalCompanyId, activity.SecurityName), activity.ExternalCompanyId,
            activity.PurchaseAmount, activity.SaleProceeds, activity.PurchasedQuantity, activity.SoldQuantity,
            activity.PortfolioWeightImpactPercentagePoints,
            activity.Classification,
            activity.ReconciliationStatus);

    private static IReadOnlyList<FundActivityRanking> Rank(
        IEnumerable<FundActivityRanking> activities,
        Func<FundActivityRanking, decimal?> key,
        Func<FundActivityRanking, bool>? include = null) => activities
        .Where(activity => key(activity) is > 0m && (include?.Invoke(activity) ?? true))
        .OrderByDescending(key)
        .ThenBy(activity => activity.Subject, StringComparer.Ordinal)
        .ThenBy(activity => activity.Id)
        .ToArray();

    private static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    private static decimal? NetDeployment(IEnumerable<FundActivityFact> activities)
    {
        var purchases = SumKnown(activities.Select(activity => activity.PurchaseAmount));
        var sales = SumKnown(activities.Select(activity => activity.SaleProceeds));
        return purchases.HasValue && sales.HasValue ? purchases.Value - sales.Value : null;
    }

    private static decimal Quantile(IReadOnlyList<decimal> values, decimal percentile)
    {
        var index = (int)Math.Ceiling((values.Count - 1) * percentile);
        return values[Math.Clamp(index, 0, values.Count - 1)];
    }

    private static string Subject(string? externalCompanyId, string securityName) =>
        string.IsNullOrWhiteSpace(externalCompanyId) ? $"unresolved:{securityName.Trim()}" : externalCompanyId.Trim();
}
