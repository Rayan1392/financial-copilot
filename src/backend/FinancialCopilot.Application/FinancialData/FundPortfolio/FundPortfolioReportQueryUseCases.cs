namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed class QueryFundPortfolioReportsUseCase(IFundPortfolioReportQueryRepository repository) : IQueryFundPortfolioReportsUseCase
{
    public Task<FundPortfolioReportPage> ListAsync(FundPortfolioReportQuery query, CancellationToken cancellationToken) => repository.ListAsync(query, cancellationToken);
    public Task<FundPortfolioReportDetail?> GetDetailAsync(Guid reportId, CancellationToken cancellationToken) => repository.GetDetailAsync(reportId, cancellationToken);
}
