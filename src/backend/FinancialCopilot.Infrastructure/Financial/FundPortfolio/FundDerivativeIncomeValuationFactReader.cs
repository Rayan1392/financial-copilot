using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed record FundDerivativeIncomeValuationFacts(
    IReadOnlyCollection<FundDerivativePosition> Derivatives,
    IReadOnlyCollection<FundEquityPositionSnapshot> EndingEquityHoldings,
    IReadOnlyCollection<FundInvestmentIncomeSummary> IncomeSummaries,
    IReadOnlyCollection<FundSecurityIncomeAttribution> SecurityIncomeAttributions,
    IReadOnlyCollection<FundValuationAdjustment> ValuationAdjustments,
    FundPortfolioValuationQualitySnapshot? ValuationQuality,
    int SourceErrorCount);

public interface IFundDerivativeIncomeValuationFactReader
{
    Task<FundDerivativeIncomeValuationFacts> ReadAsync(Guid reportId, CancellationToken cancellationToken);
}

public sealed class EfCoreFundDerivativeIncomeValuationFactReader(FinancialProviderDbContext db) : IFundDerivativeIncomeValuationFactReader
{
    public async Task<FundDerivativeIncomeValuationFacts> ReadAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var derivatives = await db.FundDerivativePositions.AsNoTracking().Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod)
            .Select(row => new FundDerivativePosition(row.Id, row.ReportId, row.FundId, row.PeriodContext, row.PeriodEndDate, row.DerivativeType, row.OptionType, row.PositionSide, row.TradingInstrumentId, row.UnderlyingExternalCompanyId, row.UnderlyingTradingInstrumentId, row.RawInstrumentName, row.NormalizedInstrumentName, row.RawUnderlyingName, row.ContractQuantity, row.ContractMultiplier, row.UnderlyingCoverageQuantity, row.StrikePrice, row.ExpiryOrExerciseJalali, row.ExpiryOrExerciseDate, row.EffectiveReturnPercentage, row.CostAmount, row.MarketValue, row.WeightOfTotalAssetsPercentage, row.ResolutionStatus, row.HedgeCoverageStatus, row.HedgeCoverageCalculationVersion, row.HedgeCoverageEvidenceJson, row.SourceLogicalRow, row.SourceSheetId, row.SourceAddress, row.SourceRevision, row.ImportedAtUtc, row.ParserProfileVersion, row.SourceEvidenceJson)).ToArrayAsync(cancellationToken);
        var holdings = await db.FundEquityPositionSnapshots.AsNoTracking().Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod && row.PositionState == FundPositionState.Ending)
            .Select(row => new FundEquityPositionSnapshot(row.Id, row.ReportId, row.FundId, row.PeriodContext, row.PeriodEndDate, row.PositionState, row.SecurityType, row.ExternalCompanyId, row.TradingInstrumentId, row.RawSecurityName, row.NormalizedSecurityName, row.Quantity, row.UnitMarketPrice, row.CostAmount, row.MarketOrNetSaleValue, row.WeightOfTotalAssetsPercentage, row.ResolutionStatus, row.SourceLogicalRow, row.SourceSheetId, row.SourceAddress, row.SourceRevision, row.ImportedAtUtc, row.ParserProfileVersion, row.MonetaryUnit, row.PercentageScale, row.SourceEvidenceJson)).ToArrayAsync(cancellationToken);
        var summaries = await db.FundInvestmentIncomeSummaries.AsNoTracking().Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod)
            .Select(row => new FundInvestmentIncomeSummary(row.ReportId, row.FundId, row.PeriodContext, row.IncomeCategory, row.Amount, row.SourcePercentageOfTotalIncome, row.CalculatedPercentageOfTotalIncome, row.PercentageOfTotalAssets, row.CumulativeAmount, row.HasSourceFormulaError, row.ReconciliationStatus, row.SourceEvidenceJson, row.CalculationVersion)).ToArrayAsync(cancellationToken);
        var attributions = await db.FundSecurityIncomeAttributions.AsNoTracking().Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod)
            .Select(row => new FundSecurityIncomeAttribution(row.ReportId, row.FundId, row.PeriodContext, row.RawSecurityName, row.ExternalCompanyId, row.TradingInstrumentId, row.DividendIncome, row.UnrealizedPriceChangeIncome, row.RealizedSaleIncome, row.TotalIncome, row.ResolutionStatus, row.ReconciliationStatus, row.SourceEvidenceJson)).ToArrayAsync(cancellationToken);
        var adjustments = await db.FundValuationAdjustments.AsNoTracking().Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod)
            .Select(row => new FundValuationAdjustment(row.ReportId, row.FundId, row.PeriodContext, row.RawSecurityName, row.TradingInstrumentId, row.Quantity, row.ClosingPrice, row.AdjustedPrice, row.SourceAdjustmentPercentage, row.CalculatedAdjustmentPercentage, row.AdjustedValue, row.Reason, row.ResolutionStatus, row.IsMaterial, row.SourceEvidenceJson)).ToArrayAsync(cancellationToken);
        var quality = await db.FundPortfolioValuationQualitySnapshots.AsNoTracking().Where(row => row.ReportId == reportId)
            .Select(row => new FundPortfolioValuationQualitySnapshot(row.ReportId, row.FundId, row.AdjustedSecurityCount, row.AdjustedValueAmount, row.AdjustedValueExposurePercentage, row.MaterialReconciliationIssueCount, row.QualityStatus, row.QualityScore, row.CalculationVersion, row.EvidenceJson)).SingleOrDefaultAsync(cancellationToken);
        var sourceErrors = summaries.Count(summary => summary.HasSourceFormulaError);
        return new(derivatives, holdings, summaries, attributions, adjustments, quality, sourceErrors);
    }
}
