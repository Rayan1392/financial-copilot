using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundTurnoverLiquidityOptions
{
    public const string SectionName = "FundPortfolio:Analytics:Liquidity";
    public decimal ParticipationRate { get; set; } = 0.1m;
    public FundTurnoverDenominatorPolicy DenominatorPolicy { get; set; } = FundTurnoverDenominatorPolicy.AverageDisclosedPortfolioMarketValue;
    public string Version { get; set; } = "fund-turnover-liquidity-v1";
    public int VolumeLookbackDays { get; set; } = 30;
}

public sealed class FundTurnoverLiquidityAnalyticsService(
    IFundTurnoverLiquidityFactReader factReader,
    IFundTurnoverLiquidityAnalyticsCalculator calculator,
    IOptions<FundTurnoverLiquidityOptions> options)
{
    public async Task<FundTurnoverLiquidityAnalytics> CalculateAsync(Guid currentReportId, Guid? previousReportId, CancellationToken cancellationToken)
    {
        var facts = await factReader.ReadAsync(currentReportId, previousReportId, cancellationToken);
        var settings = options.Value;
        return calculator.Calculate(new FundTurnoverLiquidityInput(
            facts.Activities,
            facts.EquityPositions,
            facts.MarketVolumes,
            facts.CurrentDeposits,
            facts.PreviousDeposits,
            facts.AverageDisclosedPortfolioMarketValue,
            new FundTurnoverLiquidityRules(settings.ParticipationRate, settings.DenominatorPolicy, settings.Version)));
    }
}
