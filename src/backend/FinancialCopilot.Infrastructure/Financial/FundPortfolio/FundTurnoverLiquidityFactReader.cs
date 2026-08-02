using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed record FundTurnoverLiquidityFacts(
    IReadOnlyCollection<FundActivityFact> Activities,
    IReadOnlyCollection<FundLiquidityPositionFact> EquityPositions,
    IReadOnlyCollection<FundMarketVolumeFact> MarketVolumes,
    IReadOnlyCollection<FundDepositBufferFact> CurrentDeposits,
    IReadOnlyCollection<FundDepositBufferFact> PreviousDeposits,
    decimal? AverageDisclosedPortfolioMarketValue);

public interface IFundTurnoverLiquidityFactReader
{
    Task<FundTurnoverLiquidityFacts> ReadAsync(Guid currentReportId, Guid? previousReportId, CancellationToken cancellationToken);
}

public sealed class EfCoreFundTurnoverLiquidityFactReader(
    FinancialProviderDbContext providerDb,
    FinancialIngestionDbContext ingestionDb) : IFundTurnoverLiquidityFactReader
{
    public async Task<FundTurnoverLiquidityFacts> ReadAsync(Guid currentReportId, Guid? previousReportId, CancellationToken cancellationToken)
    {
        var currentReport = await providerDb.FundPortfolioReports.AsNoTracking()
            .Where(report => report.Id == currentReportId)
            .Select(report => new { report.PeriodEndDate })
            .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException($"Fund report '{currentReportId}' was not found.");
        var activities = await providerDb.FundEquityPeriodActivities.AsNoTracking()
            .Where(row => row.ReportId == currentReportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod)
            .Select(row => new FundActivityFact(row.Id, row.ExternalCompanyId, row.NormalizedSecurityName, row.PurchaseCostAmount, row.SaleProceedsAmount, row.PurchasedQuantity, row.SoldQuantity, null, row.ActivityClassification, row.ReconciliationStatus))
            .ToArrayAsync(cancellationToken);
        var positions = await providerDb.FundEquityPositionSnapshots.AsNoTracking()
            .Where(row => row.ReportId == currentReportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod && row.PositionState == FundPositionState.Ending)
            .Select(row => new FundLiquidityPositionFact(row.Id, row.NormalizedSecurityName, row.TradingInstrumentId, row.ExternalCompanyId, row.Quantity, row.MarketOrNetSaleValue, row.ResolutionStatus == FundSecurityResolutionStatus.Resolved))
            .ToArrayAsync(cancellationToken);
        var currentDeposits = await ReadDepositsAsync(currentReportId, cancellationToken);
        var previousDeposits = previousReportId is { } previousId ? await ReadDepositsAsync(previousId, cancellationToken) : [];
        var instrumentIds = positions.Where(position => position.TradingInstrumentId.HasValue).Select(position => position.TradingInstrumentId!.Value).Distinct().ToArray();
        var from = currentReport.PeriodEndDate?.AddDays(-30);
        var to = currentReport.PeriodEndDate;
        var volumes = from.HasValue && to.HasValue
            ? await ingestionDb.DailyInstrumentTrades.AsNoTracking()
                .Where(trade => instrumentIds.Contains(trade.TradingInstrumentId) && trade.TradingDate >= from && trade.TradingDate <= to)
                .GroupBy(trade => trade.TradingInstrumentId)
                .Select(group => new FundMarketVolumeFact(group.Key, group.Average(trade => trade.Volume), false))
                .ToArrayAsync(cancellationToken)
            : [];
        var averagePortfolioValue = await providerDb.FundEquitySectionTotals.AsNoTracking()
            .Where(row => row.ReportId == currentReportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod)
            .Select(row => row.MarketOrNetSaleValue)
            .Where(value => value.HasValue)
            .Select(value => (decimal?)value)
            .AverageAsync(cancellationToken);
        return new(activities, positions, volumes, currentDeposits, previousDeposits, averagePortfolioValue);
    }

    private async Task<IReadOnlyCollection<FundDepositBufferFact>> ReadDepositsAsync(Guid reportId, CancellationToken cancellationToken) =>
        await providerDb.FundBankDepositPositions.AsNoTracking()
            .Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod && !row.IsSectionTotal)
            .Select(row => new FundDepositBufferFact(row.EndingBalance, row.WeightOfTotalAssetsPercentage))
            .ToArrayAsync(cancellationToken);
}
