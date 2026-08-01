using System.Text.Json;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundIncomeQualitySectionNormalizer(
    FinancialProviderDbContext providerDb,
    FinancialIngestionDbContext catalogDb,
    IFundPortfolioValueNormalizer valueNormalizer,
    ILogger<FundIncomeQualitySectionNormalizer> logger) : IFundIncomeQualitySectionNormalizer
{
    private static readonly string[] OwnedIssueCodes =
    [
        "INCOME_HEADER_LAYOUT_MISMATCH", "INCOME_SOURCE_FORMULA_ERROR", "INCOME_INVALID_PERCENTAGE", "INCOME_DETAIL_SUMMARY_MISMATCH",
        "INCOME_INVALID_DIVIDEND_DATE", "INCOME_UNRESOLVED_SECURITY", "INCOME_AMBIGUOUS_SECURITY", "INCOME_UNRESOLVED_BANK", "INCOME_UNRESOLVED_COMMODITY", "VALUATION_INVALID_PERCENTAGE",
        "VALUATION_MISSING_REASON", "VALUATION_UNRESOLVED_SECURITY", "VALUATION_MATERIAL_ADJUSTMENT", "VALUATION_SUMMARY_UNAVAILABLE"
    ];

    public async Task NormalizeAsync(FundPortfolioWorkbookEnvelope envelope, CancellationToken cancellationToken)
    {
        var report = await providerDb.FundPortfolioReports.AsNoTracking().Where(x => x.Id == envelope.ReportId)
            .Select(x => new { x.FundId, x.PeriodEndDate, x.SourceRevision, x.ImportedAtUtc }).SingleAsync(cancellationToken);
        await RemoveExistingAsync(envelope.ReportId, cancellationToken);

        var summaries = new List<FundInvestmentIncomeSummaryRow>();
        var attributions = new List<FundSecurityIncomeAttributionRow>();
        var dividends = new List<FundDividendIncomeDetailRow>();
        var commodities = new List<FundCommodityIncomeDetailRow>();
        var deposits = new List<FundDepositIncomeDetailRow>();
        var adjustments = new List<FundValuationAdjustmentRow>();
        var issues = new List<FundPortfolioExtractionIssueRow>();

        foreach (var sheet in envelope.Sheets)
        {
            switch (sheet.LogicalSheetType)
            {
                case FundWorkbookLogicalSheetType.InvestmentIncomeSummary:
                case FundWorkbookLogicalSheetType.CommodityIncomeSummary:
                case FundWorkbookLogicalSheetType.DepositIncomeSummary:
                case FundWorkbookLogicalSheetType.OtherIncomeDetail:
                    foreach (var mapped in FundIncomeQualitySheetMapping.ParseIncomeSummaries(sheet, valueNormalizer))
                        summaries.Add(ToSummary(envelope, report, sheet, mapped));
                    break;
                case FundWorkbookLogicalSheetType.EquityUnrealizedIncomeDetail:
                case FundWorkbookLogicalSheetType.EquityRealizedIncomeDetail:
                case FundWorkbookLogicalSheetType.EquityIncomeSummary:
                    if (sheet.LogicalSheetType == FundWorkbookLogicalSheetType.EquityIncomeSummary)
                        foreach (var mapped in FundIncomeQualitySheetMapping.ParseIncomeSummaries(sheet, valueNormalizer)) summaries.Add(ToSummary(envelope, report, sheet, mapped));
                    AddSecurityAttributions(envelope, report, sheet, attributions, issues);
                    break;
                case FundWorkbookLogicalSheetType.DividendIncomeDetail:
                    await AddDividendsAsync(envelope, report, sheet, dividends, issues, cancellationToken);
                    break;
                case FundWorkbookLogicalSheetType.CommodityUnrealizedIncomeDetail:
                case FundWorkbookLogicalSheetType.CommodityRealizedIncomeDetail:
                    foreach (var mapped in FundIncomeQualitySheetMapping.ParseCommodityIncome(sheet, valueNormalizer))
                    {
                        var resolution = await ResolveInstrumentAsync(mapped.RawName, cancellationToken);
                        var commodity = FundCommodityCatalog.Resolve(valueNormalizer.NormalizeText(mapped.RawName));
                        var status = commodity.Type == FundCommodityType.Unknown ? FundIncomeResolutionStatus.Unresolved : resolution.Status == FundIncomeResolutionStatus.Ambiguous ? FundIncomeResolutionStatus.Ambiguous : FundIncomeResolutionStatus.Resolved;
                        commodities.Add(new() { Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = mapped.PeriodContext, RawInstrumentName = mapped.RawName, UnrealizedIncome = mapped.Unrealized, RealizedIncome = mapped.Realized, TotalIncome = mapped.Total, ResolutionStatus = status, SourceLogicalRow = mapped.Row, SourceSheetId = sheet.SheetId, SourceAddress = mapped.SourceAddress, SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc, ParserProfileVersion = envelope.ParserProfileVersion, SourceEvidenceJson = mapped.Evidence });
                        if (commodity.Type == FundCommodityType.Unknown) issues.Add(Issue(envelope, sheet, mapped.Row, mapped.RawName, "INCOME_UNRESOLVED_COMMODITY", FundExtractionIssueSeverity.Warning, "Commodity identity could not be resolved through the governed Feature 103 commodity catalog."));
                        else AddResolutionIssueIfNeeded(envelope, sheet, mapped.Row, mapped.RawName, resolution, "INCOME_UNRESOLVED_SECURITY", issues);
                    }
                    break;
                case FundWorkbookLogicalSheetType.DepositIncomeDetail:
                    foreach (var mapped in FundIncomeQualitySheetMapping.ParseDepositIncome(sheet, valueNormalizer))
                    {
                        var bankCode = FundBankCatalog.Resolve(valueNormalizer.NormalizeText(mapped.RawName));
                        deposits.Add(new() { Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = mapped.PeriodContext, RawBankName = mapped.RawName, GrossIncome = mapped.Gross, DiscountCost = mapped.Discount, NetIncome = mapped.Net, ResolutionStatus = bankCode is null ? FundIncomeResolutionStatus.Unresolved : FundIncomeResolutionStatus.Resolved, SourceLogicalRow = mapped.Row, SourceSheetId = sheet.SheetId, SourceAddress = mapped.SourceAddress, SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc, ParserProfileVersion = envelope.ParserProfileVersion, SourceEvidenceJson = JsonSerializer.Serialize(new { mapped.Evidence, bankCode }) });
                        if (bankCode is null) issues.Add(Issue(envelope, sheet, mapped.Row, mapped.RawName, "INCOME_UNRESOLVED_BANK", FundExtractionIssueSeverity.Warning, "Bank identity could not be resolved through the governed Feature 103 bank catalog."));
                    }
                    break;
                case FundWorkbookLogicalSheetType.ValuationAdjustments:
                    foreach (var mapped in FundIncomeQualitySheetMapping.ParseValuationAdjustments(sheet, valueNormalizer))
                    {
                        var resolution = await ResolveInstrumentAsync(mapped.RawName, cancellationToken);
                        var calculated = FundIncomeQualityMethodology.CalculateAdjustmentPercentage(mapped.ClosingPrice, mapped.AdjustedPrice);
                var material = Math.Abs(calculated ?? mapped.SourcePercentage ?? 0m) >= 5m;
                        adjustments.Add(new() { Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = mapped.PeriodContext, RawSecurityName = mapped.RawName, TradingInstrumentId = resolution.InstrumentId, Quantity = mapped.Quantity, ClosingPrice = mapped.ClosingPrice, AdjustedPrice = mapped.AdjustedPrice, SourceAdjustmentPercentage = mapped.SourcePercentage, CalculatedAdjustmentPercentage = calculated, AdjustedValue = mapped.AdjustedValue, Reason = mapped.Reason, ResolutionStatus = resolution.Status, IsMaterial = material, SourceLogicalRow = mapped.Row, SourceSheetId = sheet.SheetId, SourceAddress = mapped.SourceAddress, SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc, ParserProfileVersion = envelope.ParserProfileVersion, SourceEvidenceJson = mapped.Evidence });
                        AddResolutionIssueIfNeeded(envelope, sheet, mapped.Row, mapped.RawName, resolution, "VALUATION_UNRESOLVED_SECURITY", issues);
                        if (mapped.SourcePercentage is not null && calculated is not null && Math.Abs(mapped.SourcePercentage.Value - calculated.Value) > 0.01m) issues.Add(Issue(envelope, sheet, mapped.Row, mapped.RawName, "VALUATION_INVALID_PERCENTAGE", FundExtractionIssueSeverity.Warning, $"Source adjustment percentage differs from calculated percentage; source={mapped.SourcePercentage}, calculated={calculated}."));
                        if (material && string.IsNullOrWhiteSpace(mapped.Reason)) issues.Add(Issue(envelope, sheet, mapped.Row, mapped.RawName, "VALUATION_MISSING_REASON", FundExtractionIssueSeverity.Warning, "Material valuation adjustment has no disclosed reason."));
                        if (material) issues.Add(Issue(envelope, sheet, mapped.Row, mapped.RawName, "VALUATION_MATERIAL_ADJUSTMENT", FundExtractionIssueSeverity.Info, "Valuation adjustment exceeded the governed materiality threshold."));
                    }
                    break;
            }
        }

        foreach (var summary in summaries.Where(x => x.HasSourceFormulaError))
        {
            issues.Add(Issue(envelope, summary.SourceSheetId, summary.SourceLogicalRow, summary.RawCategory, "INCOME_SOURCE_FORMULA_ERROR", FundExtractionIssueSeverity.Warning, "Source income amount or percentage contained an Excel formula error and was preserved as missing."));
            if (summary.Amount.HasValue && summary.SourcePercentageOfTotalIncome is null)
                issues.Add(Issue(envelope, summary.SourceSheetId, summary.SourceLogicalRow, summary.RawCategory, "INCOME_INVALID_PERCENTAGE", FundExtractionIssueSeverity.Warning, "Source income percentage was invalid; a calculated percentage is available only from valid persisted amounts."));
        }
        ReconcileDetails(envelope, summaries, attributions, dividends, commodities, deposits, issues);
        foreach (var group in summaries.Where(x => x.Amount.HasValue && !x.IsSourceTotal).GroupBy(x => x.PeriodContext))
        {
            var totalIncome = group.Sum(x => x.Amount!.Value);
            foreach (var row in group) row.CalculatedPercentageOfTotalIncome = FundIncomeQualityMethodology.CalculateShare(row.Amount, totalIncome);
        }
        var totalAssets = await providerDb.FundAssetAllocationSnapshots.AsNoTracking().Where(x => x.ReportId == envelope.ReportId && x.IsSectionTotal && !x.HasSourceFormulaError).Select(x => x.MarketOrNetSaleValue).FirstOrDefaultAsync(cancellationToken);
        var adjustedValue = adjustments.Where(x => x.AdjustedValue.HasValue).Sum(x => x.AdjustedValue!.Value);
        var exposure = FundIncomeQualityMethodology.CalculateShare(adjustedValue, totalAssets);
        var materialIssueCount = issues.Count(x => x.IssueCode is "INCOME_DETAIL_SUMMARY_MISMATCH" or "VALUATION_INVALID_PERCENTAGE" or "VALUATION_MISSING_REASON");
        var unresolvedAdjustments = adjustments.Count(x => x.ResolutionStatus != FundIncomeResolutionStatus.Resolved);
        var sourceErrors = summaries.Any(x => x.HasSourceFormulaError);
        var snapshot = new FundPortfolioValuationQualitySnapshotRow { Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, AdjustedSecurityCount = adjustments.Count, AdjustedValueAmount = adjustments.Any(x => x.AdjustedValue.HasValue) ? adjustedValue : null, AdjustedValueExposurePercentage = exposure, MaterialReconciliationIssueCount = materialIssueCount, QualityStatus = FundIncomeQualityMethodology.ClassifyValuationQuality(totalAssets.HasValue, materialIssueCount, unresolvedAdjustments, exposure, sourceErrors), QualityScore = CalculateScore(totalAssets.HasValue, materialIssueCount, unresolvedAdjustments, sourceErrors), CalculationVersion = FundIncomeQualityMethodology.CalculationVersion, EvidenceJson = JsonSerializer.Serialize(new { totalAssets, adjustedValue, exposure, sourceErrors }), SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc };

        providerDb.FundInvestmentIncomeSummaries.AddRange(summaries); providerDb.FundSecurityIncomeAttributions.AddRange(attributions); providerDb.FundDividendIncomeDetails.AddRange(dividends); providerDb.FundCommodityIncomeDetails.AddRange(commodities); providerDb.FundDepositIncomeDetails.AddRange(deposits); providerDb.FundValuationAdjustments.AddRange(adjustments); providerDb.FundPortfolioValuationQualitySnapshots.Add(snapshot); providerDb.FundPortfolioExtractionIssues.AddRange(issues);
        await providerDb.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Fund income quality normalized. ReportId={ReportId} Summaries={Summaries} Attributions={Attributions} Dividends={Dividends} CommodityDetails={CommodityDetails} DepositDetails={DepositDetails} Adjustments={Adjustments} Issues={Issues} QualityStatus={QualityStatus}", envelope.ReportId, summaries.Count, attributions.Count, dividends.Count, commodities.Count, deposits.Count, adjustments.Count, issues.Count, snapshot.QualityStatus);
    }

    private async Task AddDividendsAsync(FundPortfolioWorkbookEnvelope envelope, dynamic report, FundWorkbookSheetEnvelope sheet, List<FundDividendIncomeDetailRow> target, List<FundPortfolioExtractionIssueRow> issues, CancellationToken cancellationToken)
    {
        foreach (var mapped in FundIncomeQualitySheetMapping.ParseDividends(sheet, valueNormalizer))
        {
            var resolution = await ResolveInstrumentAsync(mapped.RawName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(mapped.JalaliDate) && mapped.Date is null) issues.Add(Issue(envelope, sheet, mapped.Row, mapped.RawName, "INCOME_INVALID_DIVIDEND_DATE", FundExtractionIssueSeverity.Warning, "Disclosed Jalali meeting date is invalid."));
            var net = mapped.Net ?? (mapped.Gross.HasValue && mapped.Discount.HasValue ? mapped.Gross.Value - mapped.Discount.Value : null);
            target.Add(new() { Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = mapped.PeriodContext, RawSecurityName = mapped.RawName, ExternalCompanyId = resolution.ExternalCompanyId, MeetingDateJalali = mapped.JalaliDate, MeetingDate = mapped.Date, EntitledQuantity = mapped.Quantity, DividendPerShare = mapped.Dps, GrossDividendIncome = mapped.Gross, DiscountCost = mapped.Discount, NetDividendIncome = net, ResolutionStatus = resolution.Status, SourceLogicalRow = mapped.Row, SourceSheetId = sheet.SheetId, SourceAddress = mapped.SourceAddress, SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc, ParserProfileVersion = envelope.ParserProfileVersion, SourceEvidenceJson = mapped.Evidence });
            AddResolutionIssueIfNeeded(envelope, sheet, mapped.Row, mapped.RawName, resolution, "INCOME_UNRESOLVED_SECURITY", issues);
        }
    }

    private void AddSecurityAttributions(FundPortfolioWorkbookEnvelope envelope, dynamic report, FundWorkbookSheetEnvelope sheet, List<FundSecurityIncomeAttributionRow> target, List<FundPortfolioExtractionIssueRow> issues)
    {
        foreach (var mapped in FundIncomeQualitySheetMapping.ParseSecurityIncome(sheet, valueNormalizer))
        {
            var category = sheet.LogicalSheetType == FundWorkbookLogicalSheetType.EquityUnrealizedIncomeDetail ? FundIncomeCategory.EquityUnrealized : sheet.LogicalSheetType == FundWorkbookLogicalSheetType.EquityRealizedIncomeDetail ? FundIncomeCategory.EquityRealized : FundIncomeCategory.EquityDividend;
            var resolution = ResolveInstrumentAsync(mapped.RawName, CancellationToken.None).GetAwaiter().GetResult();
            target.Add(new() { Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = mapped.PeriodContext, RawSecurityName = mapped.RawName, ExternalCompanyId = resolution.ExternalCompanyId, TradingInstrumentId = resolution.InstrumentId, DividendIncome = category == FundIncomeCategory.EquityDividend ? mapped.Dividend ?? mapped.Total : null, UnrealizedPriceChangeIncome = category == FundIncomeCategory.EquityUnrealized ? mapped.Unrealized ?? mapped.Total : null, RealizedSaleIncome = category == FundIncomeCategory.EquityRealized ? mapped.Realized ?? mapped.Total : null, TotalIncome = mapped.Total, ResolutionStatus = resolution.Status, ReconciliationStatus = FundIncomeReconciliationStatus.UnknownInputs, SourceLogicalRow = mapped.Row, SourceSheetId = sheet.SheetId, SourceAddress = mapped.SourceAddress, SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc, ParserProfileVersion = envelope.ParserProfileVersion, SourceEvidenceJson = mapped.Evidence });
            AddResolutionIssueIfNeeded(envelope, sheet, mapped.Row, mapped.RawName, resolution, "INCOME_UNRESOLVED_SECURITY", issues);
        }
    }

    private static FundInvestmentIncomeSummaryRow ToSummary(FundPortfolioWorkbookEnvelope envelope, dynamic report, FundWorkbookSheetEnvelope sheet, IncomeSummaryMappedRow mapped) => new()
    {
        Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = mapped.PeriodContext, IncomeCategory = mapped.Category, RawCategory = mapped.RawCategory, Amount = mapped.Amount, SourcePercentageOfTotalIncome = mapped.SourceIncomePercentage, CalculatedPercentageOfTotalIncome = null, PercentageOfTotalAssets = mapped.AssetPercentage, CumulativeAmount = mapped.CumulativeAmount, HasSourceFormulaError = mapped.HasFormulaError, IsSourceTotal = mapped.IsSourceTotal, ReconciliationStatus = FundIncomeReconciliationStatus.UnknownInputs, SourceLogicalRow = mapped.Row, SourceSheetId = sheet.SheetId, SourceAddress = mapped.SourceAddress, SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc, ParserProfileVersion = envelope.ParserProfileVersion, CalculationVersion = FundIncomeQualityMethodology.CalculationVersion, SourceEvidenceJson = mapped.Evidence
    };

    private void ReconcileDetails(FundPortfolioWorkbookEnvelope envelope, List<FundInvestmentIncomeSummaryRow> summaries, List<FundSecurityIncomeAttributionRow> attributions, List<FundDividendIncomeDetailRow> dividends, List<FundCommodityIncomeDetailRow> commodities, List<FundDepositIncomeDetailRow> deposits, List<FundPortfolioExtractionIssueRow> issues)
    {
        var totals = new Dictionary<FundIncomeCategory, decimal>();
        Add(totals, FundIncomeCategory.EquityDividend, dividends.Sum(x => x.NetDividendIncome ?? 0m)); Add(totals, FundIncomeCategory.EquityUnrealized, attributions.Sum(x => x.UnrealizedPriceChangeIncome ?? 0m)); Add(totals, FundIncomeCategory.EquityRealized, attributions.Sum(x => x.RealizedSaleIncome ?? 0m)); Add(totals, FundIncomeCategory.CommodityUnrealized, commodities.Sum(x => x.UnrealizedIncome ?? 0m)); Add(totals, FundIncomeCategory.CommodityRealized, commodities.Sum(x => x.RealizedIncome ?? 0m)); Add(totals, FundIncomeCategory.DepositInterest, deposits.Sum(x => x.NetIncome ?? 0m));
        foreach (var group in summaries.Where(x => x.Amount.HasValue).GroupBy(x => x.IncomeCategory))
            if (totals.TryGetValue(group.Key, out var detail) && Math.Abs(group.Sum(x => x.Amount!.Value) - detail) > FundIncomeQualityMethodology.ReconciliationTolerance)
                issues.Add(Issue(envelope, group.First().SourceSheetId, group.First().SourceLogicalRow, group.Key.ToString(), "INCOME_DETAIL_SUMMARY_MISMATCH", FundExtractionIssueSeverity.Warning, $"Summary/detail difference for {group.Key}: {group.Sum(x => x.Amount!.Value) - detail}."));
        static void Add(Dictionary<FundIncomeCategory, decimal> values, FundIncomeCategory key, decimal amount) { if (amount != 0m) values[key] = amount; }
    }

    private async Task RemoveExistingAsync(Guid reportId, CancellationToken cancellationToken)
    {
        providerDb.FundInvestmentIncomeSummaries.RemoveRange(await providerDb.FundInvestmentIncomeSummaries.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken)); providerDb.FundSecurityIncomeAttributions.RemoveRange(await providerDb.FundSecurityIncomeAttributions.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken)); providerDb.FundDividendIncomeDetails.RemoveRange(await providerDb.FundDividendIncomeDetails.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken)); providerDb.FundCommodityIncomeDetails.RemoveRange(await providerDb.FundCommodityIncomeDetails.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken)); providerDb.FundDepositIncomeDetails.RemoveRange(await providerDb.FundDepositIncomeDetails.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken)); providerDb.FundValuationAdjustments.RemoveRange(await providerDb.FundValuationAdjustments.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken)); providerDb.FundPortfolioValuationQualitySnapshots.RemoveRange(await providerDb.FundPortfolioValuationQualitySnapshots.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken)); providerDb.FundPortfolioExtractionIssues.RemoveRange(await providerDb.FundPortfolioExtractionIssues.Where(x => x.ReportId == reportId && OwnedIssueCodes.Contains(x.IssueCode)).ToListAsync(cancellationToken));
        await providerDb.SaveChangesAsync(cancellationToken);
    }

    private async Task<Resolution> ResolveInstrumentAsync(string rawName, CancellationToken cancellationToken)
    {
        var normalized = FundEquitySheetMapping.NormalizeSecurityName(rawName);
        var candidates = await catalogDb.TradingInstruments.AsNoTracking().Where(x => x.Symbol == normalized || x.Name == normalized || x.Symbol == rawName || x.Name == rawName).Select(x => new { x.Id, x.NormalizedCompanyId }).Take(3).ToListAsync(cancellationToken);
        if (candidates.Count == 1)
        {
            var companyId = candidates[0].NormalizedCompanyId;
            var company = companyId is Guid id ? await catalogDb.Companies.AsNoTracking().Where(x => x.Id == id).Select(x => x.ExternalCompanyId).SingleOrDefaultAsync(cancellationToken) : null;
            return new(FundIncomeResolutionStatus.Resolved, candidates[0].Id, company);
        }
        return candidates.Count > 1 ? new(FundIncomeResolutionStatus.Ambiguous, null, null) : new(FundIncomeResolutionStatus.Unresolved, null, null);
    }

    private sealed record Resolution(FundIncomeResolutionStatus Status, Guid? InstrumentId, string? ExternalCompanyId);
    private static decimal? CalculateScore(bool hasAssets, int materialIssues, int unresolved, bool sourceErrors) => !hasAssets ? null : Math.Max(0m, 100m - materialIssues * 15m - unresolved * 10m - (sourceErrors ? 20m : 0m));
    private static void AddResolutionIssueIfNeeded(FundPortfolioWorkbookEnvelope envelope, FundWorkbookSheetEnvelope sheet, int row, string raw, Resolution resolution, string code, List<FundPortfolioExtractionIssueRow> issues)
    { if (resolution.Status == FundIncomeResolutionStatus.Unresolved) issues.Add(Issue(envelope, sheet, row, raw, code, FundExtractionIssueSeverity.Warning, "Security identity could not be resolved through the canonical catalog.")); else if (resolution.Status == FundIncomeResolutionStatus.Ambiguous) issues.Add(Issue(envelope, sheet, row, raw, "INCOME_AMBIGUOUS_SECURITY", FundExtractionIssueSeverity.Warning, "Security identity matched multiple canonical instruments.")); }
    private static FundPortfolioExtractionIssueRow Issue(FundPortfolioWorkbookEnvelope envelope, FundWorkbookSheetEnvelope sheet, int row, string? raw, string code, FundExtractionIssueSeverity severity, string message) => Issue(envelope, sheet.SheetId, row, raw, code, severity, message);
    private static FundPortfolioExtractionIssueRow Issue(FundPortfolioWorkbookEnvelope envelope, Guid sheetId, int row, string? raw, string code, FundExtractionIssueSeverity severity, string message) => new() { Id = Guid.NewGuid(), ReportId = envelope.ReportId, SheetId = sheetId, Severity = severity, IssueCode = code, SourceAddress = $"row:{row}", RawValue = raw, Message = message, ParserProfileVersion = envelope.ParserProfileVersion, CreatedAtUtc = DateTimeOffset.UtcNow };
}
