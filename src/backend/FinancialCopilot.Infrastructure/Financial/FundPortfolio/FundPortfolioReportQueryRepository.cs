using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class EfCoreFundPortfolioReportQueryRepository(FinancialProviderDbContext dbContext) : IFundPortfolioReportQueryRepository
{
    public async Task<FundPortfolioReportPage> ListAsync(FundPortfolioReportQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page); var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var source = dbContext.FundPortfolioReports.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.ProviderName)) source = source.Where(x => x.ProviderName == query.ProviderName);
        if (query.ParseStatus is not null) source = source.Where(x => x.ParseStatus == query.ParseStatus);
        if (query.PeriodEndFrom is not null) source = source.Where(x => x.PeriodEndDate >= query.PeriodEndFrom);
        if (query.PeriodEndTo is not null) source = source.Where(x => x.PeriodEndDate <= query.PeriodEndTo);
        var total = await source.CountAsync(cancellationToken);
        var reports = await source.OrderByDescending(x => x.ImportedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.FundId, x.ProviderName, x.OriginalFileName, x.ParseStatus, x.SourceRevision, x.PeriodEndDate, x.ImportedAtUtc }).ToListAsync(cancellationToken);
        var ids = reports.Select(x => x.Id).ToArray();
        var stats = await dbContext.FundPortfolioReportSheets.AsNoTracking().Where(x => ids.Contains(x.ReportId)).GroupBy(x => x.ReportId).Select(g => new { ReportId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.ReportId, cancellationToken);
        var issues = await dbContext.FundPortfolioExtractionIssues.AsNoTracking().Where(x => ids.Contains(x.ReportId)).GroupBy(x => x.ReportId).Select(g => new { ReportId = g.Key, Count = g.Count(), Errors = g.Count(x => x.Severity == Domain.Financial.FundPortfolio.FundExtractionIssueSeverity.Error), Reconciliation = g.Any(x => x.IssueCode == "RECONCILIATION_FAILURE" || x.IssueCode == "RECONCILIATION_MISMATCH") }).ToDictionaryAsync(x => x.ReportId, cancellationToken);
        var items = reports.Select(x => new FundPortfolioReportListItem(x.Id, x.FundId, x.ProviderName, x.OriginalFileName, x.ParseStatus, x.SourceRevision, x.PeriodEndDate, x.ImportedAtUtc, stats.GetValueOrDefault(x.Id)?.Count ?? 0, issues.GetValueOrDefault(x.Id)?.Count ?? 0, issues.GetValueOrDefault(x.Id)?.Errors ?? 0, issues.GetValueOrDefault(x.Id)?.Reconciliation ?? false)).ToList();
        return new(items, page, pageSize, total);
    }

    public async Task<FundPortfolioReportDetail?> GetDetailAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var report = await dbContext.FundPortfolioReports.AsNoTracking().Where(x => x.Id == reportId).Select(x => new { x.Id, x.FundId, x.ParseStatus, x.SourceRevision, x.ProviderName, x.FileSha256, x.ImportedAtUtc, x.OriginalFileName, x.ParserProfileVersion, x.RawFileSizeBytes, x.CorrelationId, x.SourceObjectId }).SingleOrDefaultAsync(cancellationToken);
        if (report is null) return null;
        var status = new FundPortfolioReportStatusResult(report.Id, report.FundId, report.ParseStatus, report.SourceRevision, report.ProviderName, report.FileSha256, report.ImportedAtUtc, report.CorrelationId, report.SourceObjectId);
        var sheets = await dbContext.FundPortfolioReportSheets.AsNoTracking().Where(x => x.ReportId == reportId).OrderBy(x => x.SheetIndex).Select(x => new FundPortfolioReportSheetInventoryItem(x.OriginalSheetName, x.NormalizedSheetName, x.LogicalSheetType, x.SheetIndex, x.UsedRange, x.ClassificationConfidence)).ToListAsync(cancellationToken);
        var normalizedSections = sheets.Count(x => x.LogicalSheetType != Domain.Financial.FundPortfolio.FundWorkbookLogicalSheetType.Unclassified);
        var reconciliation = await dbContext.FundPortfolioExtractionIssues.AsNoTracking().AnyAsync(x => x.ReportId == reportId && (x.IssueCode == "RECONCILIATION_FAILURE" || x.IssueCode == "RECONCILIATION_MISMATCH"), cancellationToken);
        var timeline = await dbContext.FundPortfolioReportStatusHistory.AsNoTracking().Where(x => x.ReportId == reportId).OrderBy(x => x.CreatedAtUtc).Select(x => new FundPortfolioReportStatusTimelineItem(x.EventType, x.Status, x.CreatedAtUtc, x.CorrelationId, x.Details)).ToListAsync(cancellationToken);
        return new(status, report.OriginalFileName, report.ParserProfileVersion, report.RawFileSizeBytes, sheets, normalizedSections, reconciliation, timeline);
    }
}
