using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundSectorStrategyOptions
{
    public const string SectionName = "FundPortfolio:Analytics:Strategy";
    public decimal MinimumMeaningfulChangePercentagePoints { get; set; } = 2m;
    public string Version { get; set; } = "fund-strategy-posture-v1";
    public bool IssueRedemptionDataAvailable { get; set; }
}

public sealed class FundSectorStrategyAnalyticsService(
    IFundSectorStrategyFactReader factReader,
    IFundSectorStrategyAnalyticsCalculator calculator,
    IOptions<FundSectorStrategyOptions> options)
{
    public async Task<FundSectorStrategyAnalytics> CalculateAsync(
        Guid currentReportId,
        Guid? previousReportId,
        CancellationToken cancellationToken)
    {
        var facts = await factReader.ReadAsync(currentReportId, previousReportId, cancellationToken);
        var rules = new FundStrategyPostureRules(
            options.Value.MinimumMeaningfulChangePercentagePoints,
            options.Value.Version);
        return calculator.Calculate(new FundSectorStrategyInput(
            facts.CurrentHoldings,
            facts.PreviousHoldings,
            facts.CurrentAllocation,
            facts.PreviousAllocation,
            options.Value.IssueRedemptionDataAvailable,
            rules));
    }
}
