using System.Text;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class EfCoreFundPortfolioIntelligenceDetailRepository(
    FinancialProviderDbContext providerDb,
    FinancialIngestionDbContext ingestionDb) : IFundPortfolioIntelligenceDetailRepository
{
    public async Task<FundPortfolioIntelligenceDetailPage> QueryAsync(FundPortfolioIntelligenceDetailQuery query, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var snapshot = await providerDb.FundPortfolioAnalyticsSnapshots.AsNoTracking()
            .Where(row => row.FundId == query.FundId && (!query.PeriodEndDate.HasValue || row.PeriodEndDate == query.PeriodEndDate))
            .OrderByDescending(row => row.PeriodEndDate).ThenByDescending(row => row.CalculationVersion)
            .Select(row => new { row.ReportId, row.PeriodEndDate, row.EvidenceJson })
            .FirstOrDefaultAsync(cancellationToken);
        if (snapshot is null) return new(query.Section, query.PeriodEndDate, [], null, false);

        var items = query.Section switch
        {
            FundPortfolioIntelligenceSection.Holdings => await ReadHoldingsAsync(query, snapshot.ReportId, cancellationToken),
            FundPortfolioIntelligenceSection.Activity => await ReadActivityAsync(query, snapshot.ReportId, cancellationToken),
            FundPortfolioIntelligenceSection.Allocation => await ReadAllocationAsync(query, snapshot.ReportId, cancellationToken),
            FundPortfolioIntelligenceSection.Sectors => await ReadSectorsAsync(query, snapshot.ReportId, cancellationToken),
            FundPortfolioIntelligenceSection.IncomeAttribution => await ReadIncomeAsync(query, snapshot.ReportId, cancellationToken),
            FundPortfolioIntelligenceSection.Risk => await ReadRiskAsync(query, snapshot.ReportId, snapshot.EvidenceJson, cancellationToken),
            FundPortfolioIntelligenceSection.SourceEvidence => await ReadSourceEvidenceAsync(query, snapshot.ReportId, snapshot.EvidenceJson, cancellationToken),
            _ => []
        };
        return Page(query, snapshot.PeriodEndDate, items, pageSize);
    }

    private async Task<IReadOnlyList<FundPortfolioIntelligenceDetailItem>> ReadHoldingsAsync(FundPortfolioIntelligenceDetailQuery query, Guid reportId, CancellationToken cancellationToken) =>
        await providerDb.FundEquityPositionSnapshots.AsNoTracking()
            .Where(row => row.ReportId == reportId && row.PositionState == FundPositionState.Ending && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod)
            .Select(row => new FundPortfolioIntelligenceDetailItem(row.Id, row.NormalizedSecurityName, row.ExternalCompanyId, "Holding", row.MarketOrNetSaleValue, row.WeightOfTotalAssetsPercentage, null, null, row.ResolutionStatus.ToString(), row.SourceRevision, row.ImportedAtUtc, row.SourceEvidenceJson))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<FundPortfolioIntelligenceDetailItem>> ReadActivityAsync(FundPortfolioIntelligenceDetailQuery query, Guid reportId, CancellationToken cancellationToken) =>
        await providerDb.FundEquityPeriodActivities.AsNoTracking()
            .Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod)
            .Select(row => new FundPortfolioIntelligenceDetailItem(row.Id, row.NormalizedSecurityName, row.ExternalCompanyId, row.ActivityClassification.ToString(), row.PurchaseCostAmount ?? row.SaleProceedsAmount, null, row.QuantityReconciliationDifference, row.ReconciliationStatus.ToString(), null, row.SourceRevision, row.ImportedAtUtc, row.SourceEvidenceJson))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<FundPortfolioIntelligenceDetailItem>> ReadAllocationAsync(FundPortfolioIntelligenceDetailQuery query, Guid reportId, CancellationToken cancellationToken) =>
        await providerDb.FundAssetAllocationSnapshots.AsNoTracking()
            .Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod && !row.IsSectionTotal)
            .Select(row => new FundPortfolioIntelligenceDetailItem(row.Id, row.NormalizedAssetClassCode, null, row.AssetClass.ToString(), row.MarketOrNetSaleValue, row.WeightOfTotalAssetsPercentage, null, null, null, row.SourceRevision, row.ImportedAtUtc, row.SourceEvidenceJson))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<FundPortfolioIntelligenceDetailItem>> ReadIncomeAsync(FundPortfolioIntelligenceDetailQuery query, Guid reportId, CancellationToken cancellationToken) =>
        await providerDb.FundSecurityIncomeAttributions.AsNoTracking()
            .Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod)
            .Select(row => new FundPortfolioIntelligenceDetailItem(row.Id, row.RawSecurityName, row.ExternalCompanyId, "SecurityIncome", row.TotalIncome, null, null, row.ReconciliationStatus.ToString(), row.ResolutionStatus.ToString(), row.SourceRevision, row.ImportedAtUtc, row.SourceEvidenceJson))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<FundPortfolioIntelligenceDetailItem>> ReadSectorsAsync(FundPortfolioIntelligenceDetailQuery query, Guid reportId, CancellationToken cancellationToken)
    {
        var report = await providerDb.FundPortfolioReports.AsNoTracking().Where(row => row.Id == reportId).Select(row => row.ProviderName).SingleOrDefaultAsync(cancellationToken);
        var rows = await providerDb.FundEquityPositionSnapshots.AsNoTracking()
            .Where(row => row.ReportId == reportId && row.PositionState == FundPositionState.Ending && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod)
            .Select(row => new { row.Id, row.NormalizedSecurityName, row.ExternalCompanyId, row.MarketOrNetSaleValue, row.WeightOfTotalAssetsPercentage, row.ResolutionStatus, row.SourceRevision, row.ImportedAtUtc, row.SourceEvidenceJson })
            .ToListAsync(cancellationToken);
        var externalIds = rows.Where(row => row.ExternalCompanyId != null).Select(row => row.ExternalCompanyId!).Distinct().ToArray();
        var companies = await ingestionDb.Companies.AsNoTracking().Where(row => row.ProviderName == report && externalIds.Contains(row.ExternalCompanyId)).Select(row => new { row.ExternalCompanyId, row.IndustryId }).ToArrayAsync(cancellationToken);
        var industryIds = companies.Where(row => row.IndustryId.HasValue).Select(row => row.IndustryId!.Value).Distinct().ToArray();
        var industries = await ingestionDb.Industries.AsNoTracking().Where(row => industryIds.Contains(row.Id)).Select(row => new { row.Id, row.ExternalId, row.Name }).ToDictionaryAsync(row => row.Id, cancellationToken);
        var map = companies.ToDictionary(row => row.ExternalCompanyId, row => row.IndustryId.HasValue && industries.TryGetValue(row.IndustryId.Value, out var industry) ? (industry.ExternalId, industry.Name) : (null, "Unknown"));
        return rows.Select(row =>
        {
            var sector = row.ExternalCompanyId != null && map.TryGetValue(row.ExternalCompanyId, out var value) ? value : ("UNKNOWN", "Unknown");
            return new FundPortfolioIntelligenceDetailItem(row.Id, sector.Item2, row.ExternalCompanyId, sector.Item1, row.MarketOrNetSaleValue, row.WeightOfTotalAssetsPercentage, null, null, row.ResolutionStatus.ToString(), row.SourceRevision, row.ImportedAtUtc, row.SourceEvidenceJson);
        }).ToArray();
    }

    private async Task<IReadOnlyList<FundPortfolioIntelligenceDetailItem>> ReadRiskAsync(FundPortfolioIntelligenceDetailQuery query, Guid reportId, string evidence, CancellationToken cancellationToken)
    {
        var row = await (from candidate in providerDb.FundPortfolioAnalyticsSnapshots.AsNoTracking()
                         join report in providerDb.FundPortfolioReports.AsNoTracking() on candidate.ReportId equals report.Id
                         where candidate.ReportId == reportId
                         select new FundPortfolioIntelligenceDetailItem(candidate.Id, "portfolio-risk", null, candidate.RiskPosture.ToString(), candidate.ConfidenceScore, null, null, null, candidate.LiquidityRiskStatus.ToString(), report.SourceRevision, report.ImportedAtUtc, evidence)).SingleOrDefaultAsync(cancellationToken);
        return row is null ? [] : [row];
    }

    private async Task<IReadOnlyList<FundPortfolioIntelligenceDetailItem>> ReadSourceEvidenceAsync(FundPortfolioIntelligenceDetailQuery query, Guid reportId, string evidence, CancellationToken cancellationToken)
    {
        var row = await (from candidate in providerDb.FundPortfolioAnalyticsSnapshots.AsNoTracking()
                         join report in providerDb.FundPortfolioReports.AsNoTracking() on candidate.ReportId equals report.Id
                         where candidate.ReportId == reportId
                         select new FundPortfolioIntelligenceDetailItem(candidate.Id, "portfolio-analytics", null, candidate.CalculationVersion, null, null, null, null, null, report.SourceRevision, report.ImportedAtUtc, candidate.EvidenceJson)).SingleOrDefaultAsync(cancellationToken);
        return row is null ? [] : [row];
    }

    private static FundPortfolioIntelligenceDetailPage Page(FundPortfolioIntelligenceDetailQuery query, DateOnly? periodEndDate, IReadOnlyList<FundPortfolioIntelligenceDetailItem> unsorted, int pageSize)
    {
        var cursor = Decode(query.Cursor);
        var ordered = unsorted.OrderBy(item => item.Subject, StringComparer.Ordinal).ThenBy(item => item.Id).Where(item => cursor is null || item.Subject.CompareTo(cursor.Value.Subject) > 0 || item.Subject == cursor.Value.Subject && item.Id.CompareTo(cursor.Value.Id) > 0).ToArray();
        var hasMore = ordered.Length > pageSize;
        var items = ordered.Take(pageSize).ToArray();
        return new(query.Section, periodEndDate, items, hasMore ? Encode(items[^1].Subject, items[^1].Id) : null, hasMore);
    }

    private static string Encode(string subject, Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{subject}\u001f{id:N}"));
    private static (string Subject, Guid Id)? Decode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split('\u001f');
            return parts.Length == 2 && Guid.TryParse(parts[1], out var id) ? (parts[0], id) : null;
        }
        catch (FormatException) { return null; }
    }
}
