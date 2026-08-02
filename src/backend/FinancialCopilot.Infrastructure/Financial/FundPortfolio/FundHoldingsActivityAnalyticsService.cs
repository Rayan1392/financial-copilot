using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundPortfolioMaterialityOptions
{
    public const string SectionName = "FundPortfolio:Analytics:Materiality";

    public decimal AbsoluteAmount { get; set; }
    public decimal AssetWeightChangePercentagePoints { get; set; } = 1m;
    public decimal Percentile { get; set; } = 0.9m;
    public string Version { get; set; } = FundPortfolioMaterialityPolicy.DefaultVersion;

    public FundPortfolioMaterialityThresholds ToThresholds() =>
        new(AbsoluteAmount, AssetWeightChangePercentagePoints, Percentile, Version);
}

public sealed class FundHoldingsActivityAnalyticsService(
    IFundHoldingsActivityFactReader factReader,
    IFundHoldingsActivityAnalyticsCalculator calculator,
    IOptions<FundPortfolioMaterialityOptions> options)
{
    public async Task<FundHoldingsActivityAnalytics> CalculateAsync(
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var facts = await factReader.ReadAsync(reportId, cancellationToken);
        return calculator.Calculate(new FundHoldingsActivityInput(
            facts.EndingHoldings,
            facts.Activities,
            options.Value.ToThresholds()));
    }
}
