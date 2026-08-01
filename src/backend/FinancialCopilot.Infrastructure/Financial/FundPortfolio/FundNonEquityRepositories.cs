using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class EfCoreFundNonEquityAssetRepository(FinancialProviderDbContext dbContext) : IFundNonEquityAssetRepository
{
    public async Task<IReadOnlyList<FundAssetAllocationSnapshot>> QueryAllocationsAsync(FundNonEquityQuery query, CancellationToken cancellationToken)
    {
        var source = Apply(dbContext.FundAssetAllocationSnapshots.AsNoTracking(), query);
        var rows = await source.OrderByDescending(x => x.PeriodEndDate).ThenBy(x => x.AssetClass).ThenBy(x => x.SourceLogicalRow).ToListAsync(cancellationToken);
        return rows.Select(x => new FundAssetAllocationSnapshot(x.Id, x.ReportId, x.FundId, x.PeriodContext, x.PeriodEndDate, x.AssetClass,
            x.RawAssetClassLabel, x.NormalizedAssetClassCode, x.CostAmount, x.MarketOrNetSaleValue, x.WeightOfTotalAssetsPercentage,
            x.IsSectionTotal, x.HasSourceFormulaError, x.SourceLogicalRow, x.SourceSheetId, x.SourceAddress, x.SourceRevision,
            x.ImportedAtUtc, x.ParserProfileVersion, x.MonetaryUnit, x.PercentageScale, x.SourceEvidenceJson)).ToArray();
    }

    public async Task<IReadOnlyList<FundCommodityCertificatePosition>> QueryCommodityCertificatesAsync(FundNonEquityQuery query, CancellationToken cancellationToken)
    {
        var source = Apply(dbContext.FundCommodityCertificatePositions.AsNoTracking(), query);
        if (query.ResolutionStatus is not null) source = source.Where(x => x.ResolutionStatus == query.ResolutionStatus);
        var rows = await source.OrderByDescending(x => x.PeriodEndDate).ThenBy(x => x.NormalizedInstrumentName).ThenBy(x => x.SourceLogicalRow).ToListAsync(cancellationToken);
        return rows.Select(x => new FundCommodityCertificatePosition(x.Id, x.ReportId, x.FundId, x.PeriodContext, x.PeriodEndDate, x.CommodityType,
            x.CommodityCode, x.ExtractedInstrumentSymbol, x.TradingInstrumentId, x.RawInstrumentName, x.NormalizedInstrumentName,
            x.BeginningQuantity, x.BeginningCostAmount, x.BeginningMarketValue, x.PurchasedQuantity, x.PurchaseCostAmount, x.SoldQuantity,
            x.SaleProceedsAmount, x.EndingQuantity, x.EndingUnitPrice, x.EndingCostAmount, x.EndingMarketValue,
            x.WeightOfTotalAssetsPercentage, x.QuantityReconciliationDifference, x.ReconciliationStatus, x.ResolutionStatus, x.IsSectionTotal,
            x.SourceLogicalRow, x.SourceSheetId, x.SourceAddress, x.SourceRevision, x.ImportedAtUtc, x.ParserProfileVersion, x.SourceEvidenceJson)).ToArray();
    }

    public async Task<IReadOnlyList<FundBankDepositPosition>> QueryBankDepositsAsync(FundNonEquityQuery query, CancellationToken cancellationToken)
    {
        var source = Apply(dbContext.FundBankDepositPositions.AsNoTracking(), query);
        if (query.ResolutionStatus is not null) source = source.Where(x => x.ResolutionStatus == query.ResolutionStatus);
        var rows = await source.OrderByDescending(x => x.PeriodEndDate).ThenBy(x => x.NormalizedBankName).ThenBy(x => x.SourceLogicalRow).ToListAsync(cancellationToken);
        return rows.Select(x => new FundBankDepositPosition(x.Id, x.ReportId, x.FundId, x.PeriodContext, x.PeriodEndDate, x.BankCode,
            x.RawBankName, x.NormalizedBankName, x.BeginningBalance, x.IncreaseAmount, x.DecreaseAmount, x.EndingBalance,
            x.WeightOfTotalAssetsPercentage, x.BalanceReconciliationDifference, x.ReconciliationStatus, x.ResolutionStatus, x.IsSectionTotal,
            x.SourceLogicalRow, x.SourceSheetId, x.SourceAddress, x.SourceRevision, x.ImportedAtUtc, x.ParserProfileVersion, x.SourceEvidenceJson)).ToArray();
    }

    public async Task<IReadOnlyList<FundDerivativePosition>> QueryDerivativesAsync(FundNonEquityQuery query, CancellationToken cancellationToken)
    {
        var source = Apply(dbContext.FundDerivativePositions.AsNoTracking(), query);
        if (query.ResolutionStatus is not null) source = source.Where(x => x.ResolutionStatus == query.ResolutionStatus);
        var rows = await source.OrderByDescending(x => x.PeriodEndDate).ThenBy(x => x.DerivativeType).ThenBy(x => x.ExpiryOrExerciseDate).ThenBy(x => x.SourceLogicalRow).ToListAsync(cancellationToken);
        return rows.Select(x => new FundDerivativePosition(x.Id, x.ReportId, x.FundId, x.PeriodContext, x.PeriodEndDate, x.DerivativeType,
            x.OptionType, x.PositionSide, x.TradingInstrumentId, x.UnderlyingExternalCompanyId, x.UnderlyingTradingInstrumentId,
            x.RawInstrumentName, x.NormalizedInstrumentName, x.RawUnderlyingName, x.ContractQuantity, x.ContractMultiplier,
            x.UnderlyingCoverageQuantity, x.StrikePrice, x.ExpiryOrExerciseJalali, x.ExpiryOrExerciseDate,
            x.EffectiveReturnPercentage, x.CostAmount, x.MarketValue, x.WeightOfTotalAssetsPercentage, x.ResolutionStatus,
            x.HedgeCoverageStatus, x.HedgeCoverageCalculationVersion, x.HedgeCoverageEvidenceJson, x.SourceLogicalRow,
            x.SourceSheetId, x.SourceAddress, x.SourceRevision, x.ImportedAtUtc, x.ParserProfileVersion, x.SourceEvidenceJson)).ToArray();
    }

    public async Task<int> CountUnresolvedAsync(Guid reportId, CancellationToken cancellationToken) =>
        await dbContext.FundCommodityCertificatePositions.CountAsync(x => x.ReportId == reportId && (x.ResolutionStatus == FundNonEquityResolutionStatus.Unresolved || x.ResolutionStatus == FundNonEquityResolutionStatus.Ambiguous), cancellationToken)
        + await dbContext.FundBankDepositPositions.CountAsync(x => x.ReportId == reportId && (x.ResolutionStatus == FundNonEquityResolutionStatus.Unresolved || x.ResolutionStatus == FundNonEquityResolutionStatus.Ambiguous), cancellationToken)
        + await dbContext.FundDerivativePositions.CountAsync(x => x.ReportId == reportId && (x.ResolutionStatus == FundNonEquityResolutionStatus.Unresolved || x.ResolutionStatus == FundNonEquityResolutionStatus.Ambiguous), cancellationToken);

    private static IQueryable<T> Apply<T>(IQueryable<T> source, FundNonEquityQuery query) where T : class
    {
        if (query.FundId is not null) source = source.Where(x => EF.Property<Guid>(x, "FundId") == query.FundId);
        if (query.ReportId is not null) source = source.Where(x => EF.Property<Guid>(x, "ReportId") == query.ReportId);
        if (query.PeriodEndDate is not null) source = source.Where(x => EF.Property<DateOnly?>(x, "PeriodEndDate") == query.PeriodEndDate);
        if (query.PeriodContext is not null) source = source.Where(x => EF.Property<FundWorkbookPeriodContext>(x, "PeriodContext") == query.PeriodContext);
        return source;
    }
}
