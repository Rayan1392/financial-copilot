using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundDerivativeIncomeValuationOptions
{
    public const string SectionName = "FundPortfolio:Analytics:IncomeValuation";
    public string Version { get; set; } = "fund-derivative-income-valuation-v1";
}

public sealed class FundDerivativeIncomeValuationAnalyticsService(
    IFundDerivativeIncomeValuationFactReader factReader,
    IFundDerivativeIncomeValuationAnalyticsCalculator calculator,
    IOptions<FundDerivativeIncomeValuationOptions> options)
{
    public async Task<FundDerivativeIncomeValuationAnalytics> CalculateAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var facts = await factReader.ReadAsync(reportId, cancellationToken);
        return calculator.Calculate(new FundDerivativeIncomeValuationInput(facts.Derivatives, facts.EndingEquityHoldings, facts.IncomeSummaries, facts.SecurityIncomeAttributions, facts.ValuationAdjustments, facts.ValuationQuality, facts.SourceErrorCount, options.Value.Version));
    }
}
