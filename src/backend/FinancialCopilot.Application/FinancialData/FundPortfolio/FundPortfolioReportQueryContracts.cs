using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundPortfolioReportQuery(
    int Page = 1,
    int PageSize = 50,
    string? ProviderName = null,
    FundPortfolioParseStatus? ParseStatus = null,
    DateOnly? PeriodEndFrom = null,
    DateOnly? PeriodEndTo = null);

public sealed record FundPortfolioReportListItem(
    Guid ReportId,
    Guid FundId,
    string ProviderName,
    string OriginalFileName,
    FundPortfolioParseStatus ParseStatus,
    int SourceRevision,
    DateOnly? PeriodEndDate,
    DateTimeOffset ImportedAtUtc,
    int SheetCount,
    int IssueCount,
    int ErrorCount,
    bool HasReconciliationIssues);

public sealed record FundPortfolioReportPage(IReadOnlyList<FundPortfolioReportListItem> Items, int Page, int PageSize, int TotalCount);

public sealed record FundPortfolioReportDetail(
    FundPortfolioReportStatusResult Status,
    string OriginalFileName,
    string ParserProfileVersion,
    long RawFileSizeBytes,
    IReadOnlyList<FundPortfolioReportSheetInventoryItem> Sheets,
    int NormalizedSectionCount,
    bool HasReconciliationIssues,
    IReadOnlyList<FundPortfolioReportStatusTimelineItem> StatusTimeline);

public sealed record FundPortfolioReportStatusTimelineItem(string EventType, FundPortfolioParseStatus Status, DateTimeOffset CreatedAtUtc, string? CorrelationId, string? Details);

public sealed record FundPortfolioReportSheetInventoryItem(
    string OriginalSheetName,
    string NormalizedSheetName,
    FundWorkbookLogicalSheetType LogicalSheetType,
    int SheetIndex,
    string? UsedRange,
    decimal ClassificationConfidence);

public interface IFundPortfolioReportQueryRepository
{
    Task<FundPortfolioReportPage> ListAsync(FundPortfolioReportQuery query, CancellationToken cancellationToken);
    Task<FundPortfolioReportDetail?> GetDetailAsync(Guid reportId, CancellationToken cancellationToken);
}

public interface IQueryFundPortfolioReportsUseCase
{
    Task<FundPortfolioReportPage> ListAsync(FundPortfolioReportQuery query, CancellationToken cancellationToken);
    Task<FundPortfolioReportDetail?> GetDetailAsync(Guid reportId, CancellationToken cancellationToken);
}
