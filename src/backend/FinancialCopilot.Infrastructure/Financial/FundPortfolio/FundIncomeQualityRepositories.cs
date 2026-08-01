using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class EfCoreFundIncomeQualityRepository(FinancialProviderDbContext db) : IFundIncomeQualityRepository
{
    public async Task<IReadOnlyList<FundInvestmentIncomeSummary>> QueryIncomeSummariesAsync(FundIncomeQualityQuery query, CancellationToken cancellationToken) =>
        await db.FundInvestmentIncomeSummaries.AsNoTracking().Where(x => (!query.ReportId.HasValue || x.ReportId == query.ReportId) && (!query.FundId.HasValue || x.FundId == query.FundId) && (!query.PeriodContext.HasValue || x.PeriodContext == query.PeriodContext)).OrderBy(x => x.PeriodContext).ThenBy(x => x.IncomeCategory).Select(x => new FundInvestmentIncomeSummary(x.ReportId, x.FundId, x.PeriodContext, x.IncomeCategory, x.Amount, x.SourcePercentageOfTotalIncome, x.CalculatedPercentageOfTotalIncome, x.PercentageOfTotalAssets, x.CumulativeAmount, x.HasSourceFormulaError, x.ReconciliationStatus, x.SourceEvidenceJson, x.CalculationVersion)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FundSecurityIncomeAttribution>> QuerySecurityAttributionsAsync(FundIncomeQualityQuery query, CancellationToken cancellationToken) =>
        await db.FundSecurityIncomeAttributions.AsNoTracking().Where(x => (!query.ReportId.HasValue || x.ReportId == query.ReportId) && (!query.FundId.HasValue || x.FundId == query.FundId) && (!query.PeriodContext.HasValue || x.PeriodContext == query.PeriodContext)).Select(x => new FundSecurityIncomeAttribution(x.ReportId, x.FundId, x.PeriodContext, x.RawSecurityName, x.ExternalCompanyId, x.TradingInstrumentId, x.DividendIncome, x.UnrealizedPriceChangeIncome, x.RealizedSaleIncome, x.TotalIncome, x.ResolutionStatus, x.ReconciliationStatus, x.SourceEvidenceJson)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FundDividendIncomeDetail>> QueryDividendDetailsAsync(FundIncomeQualityQuery query, CancellationToken cancellationToken) =>
        await db.FundDividendIncomeDetails.AsNoTracking().Where(x => (!query.ReportId.HasValue || x.ReportId == query.ReportId) && (!query.FundId.HasValue || x.FundId == query.FundId) && (!query.PeriodContext.HasValue || x.PeriodContext == query.PeriodContext)).Select(x => new FundDividendIncomeDetail(x.ReportId, x.FundId, x.PeriodContext, x.RawSecurityName, x.ExternalCompanyId, x.MeetingDate, x.MeetingDateJalali, x.EntitledQuantity, x.DividendPerShare, x.GrossDividendIncome, x.DiscountCost, x.NetDividendIncome, x.ResolutionStatus, x.SourceEvidenceJson)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FundValuationAdjustment>> QueryValuationAdjustmentsAsync(FundIncomeQualityQuery query, CancellationToken cancellationToken) =>
        await db.FundValuationAdjustments.AsNoTracking().Where(x => (!query.ReportId.HasValue || x.ReportId == query.ReportId) && (!query.FundId.HasValue || x.FundId == query.FundId) && (!query.PeriodContext.HasValue || x.PeriodContext == query.PeriodContext)).Select(x => new FundValuationAdjustment(x.ReportId, x.FundId, x.PeriodContext, x.RawSecurityName, x.TradingInstrumentId, x.Quantity, x.ClosingPrice, x.AdjustedPrice, x.SourceAdjustmentPercentage, x.CalculatedAdjustmentPercentage, x.AdjustedValue, x.Reason, x.ResolutionStatus, x.IsMaterial, x.SourceEvidenceJson)).ToListAsync(cancellationToken);

    public async Task<FundPortfolioValuationQualitySnapshot?> GetValuationQualityAsync(Guid reportId, CancellationToken cancellationToken) =>
        await db.FundPortfolioValuationQualitySnapshots.AsNoTracking().Where(x => x.ReportId == reportId).Select(x => new FundPortfolioValuationQualitySnapshot(x.ReportId, x.FundId, x.AdjustedSecurityCount, x.AdjustedValueAmount, x.AdjustedValueExposurePercentage, x.MaterialReconciliationIssueCount, x.QualityStatus, x.QualityScore, x.CalculationVersion, x.EvidenceJson)).SingleOrDefaultAsync(cancellationToken);
}
