using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed record FundHoldingsActivityFacts(
    IReadOnlyCollection<FundHoldingFact> EndingHoldings,
    IReadOnlyCollection<FundActivityFact> Activities);

public interface IFundHoldingsActivityFactReader
{
    Task<FundHoldingsActivityFacts> ReadAsync(Guid reportId, CancellationToken cancellationToken);
}

public sealed class EfCoreFundHoldingsActivityFactReader(
    FinancialProviderDbContext dbContext) : IFundHoldingsActivityFactReader
{
    public async Task<FundHoldingsActivityFacts> ReadAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var holdings = await dbContext.FundEquityPositionSnapshots.AsNoTracking()
            .Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod && row.PositionState == FundPositionState.Ending)
            .Select(row => new FundHoldingFact(row.Id, row.ExternalCompanyId, row.NormalizedSecurityName, row.MarketOrNetSaleValue, row.WeightOfTotalAssetsPercentage, null, row.ResolutionStatus))
            .ToArrayAsync(cancellationToken);
        var activities = await dbContext.FundEquityPeriodActivities.AsNoTracking()
            .Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod)
            .Select(row => new FundActivityFact(row.Id, row.ExternalCompanyId, row.NormalizedSecurityName, row.PurchaseCostAmount, row.SaleProceedsAmount, row.PurchasedQuantity, row.SoldQuantity, null, row.ActivityClassification, row.ReconciliationStatus))
            .ToArrayAsync(cancellationToken);
        return new(holdings, activities);
    }
}
