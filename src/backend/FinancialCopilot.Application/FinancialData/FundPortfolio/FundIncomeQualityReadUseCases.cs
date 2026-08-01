using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundIncomeQualityOverview(
    IReadOnlyList<FundInvestmentIncomeSummary> Summaries,
    IReadOnlyList<FundSecurityIncomeAttribution> SecurityAttributions,
    IReadOnlyList<FundDividendIncomeDetail> DividendDetails,
    IReadOnlyList<FundValuationAdjustment> ValuationAdjustments,
    FundPortfolioValuationQualitySnapshot? ValuationQuality);

public interface IGetFundIncomeQualityOverviewUseCase
{
    Task<FundIncomeQualityOverview> ExecuteAsync(FundIncomeQualityQuery query, CancellationToken cancellationToken);
}

public sealed class GetFundIncomeQualityOverviewUseCase(IFundIncomeQualityRepository repository) : IGetFundIncomeQualityOverviewUseCase
{
    public async Task<FundIncomeQualityOverview> ExecuteAsync(FundIncomeQualityQuery query, CancellationToken cancellationToken)
    {
        var summaries = await repository.QueryIncomeSummariesAsync(query, cancellationToken);
        var attributions = await repository.QuerySecurityAttributionsAsync(query, cancellationToken);
        var dividends = await repository.QueryDividendDetailsAsync(query, cancellationToken);
        var adjustments = await repository.QueryValuationAdjustmentsAsync(query, cancellationToken);
        var quality = query.ReportId.HasValue ? await repository.GetValuationQualityAsync(query.ReportId.Value, cancellationToken) : null;
        return new(summaries, attributions, dividends, adjustments, quality);
    }
}
