using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundPortfolioAuditEvent(
    string EventType,
    string? ActorId,
    Guid? RunId,
    Guid? ReportId,
    Guid? ReviewId,
    string CorrelationId,
    string? Summary);

public static class FundPortfolioAuditEventTypes
{
    public const string Ingest = "ingest";
    public const string Duplicate = "duplicate";
    public const string CorrectedRevision = "corrected-revision";
    public const string Failure = "failure";
    public const string Supersession = "supersession";
}

public static class FundPortfolioAuditPolicy
{
    public static FundPortfolioAuditEvent Ingested(Guid reportId, Guid correlationId, int sourceRevision, FundPortfolioParseStatus status) =>
        new(FundPortfolioAuditEventTypes.Ingest, null, null, reportId, null, correlationId.ToString("N"),
            $"Fund portfolio workbook ingested. SourceRevision={sourceRevision} ParseStatus={status}.");

    public static FundPortfolioAuditEvent Duplicate(Guid reportId, Guid correlationId, int sourceRevision) =>
        new(FundPortfolioAuditEventTypes.Duplicate, null, null, reportId, null, correlationId.ToString("N"),
            $"Duplicate fund portfolio workbook ignored. ExistingSourceRevision={sourceRevision}.");

    public static FundPortfolioAuditEvent CorrectedRevision(Guid reportId, Guid correlationId, int sourceRevision, Guid? supersedesReportId) =>
        new(FundPortfolioAuditEventTypes.CorrectedRevision, null, null, reportId, null, correlationId.ToString("N"),
            $"Corrected fund portfolio source revision ingested. SourceRevision={sourceRevision} SupersedesReportId={supersedesReportId?.ToString() ?? "none"}.");

    public static FundPortfolioAuditEvent Superseded(Guid reportId, Guid correlationId, int sourceRevision, Guid supersededReportId) =>
        new(FundPortfolioAuditEventTypes.Supersession, null, null, reportId, null, correlationId.ToString("N"),
            $"Fund portfolio report superseded by corrected source revision. NewSourceRevision={sourceRevision} SupersededReportId={supersededReportId}.");

    public static FundPortfolioAuditEvent Failure(Guid? reportId, Guid correlationId, string errorType) =>
        new(FundPortfolioAuditEventTypes.Failure, null, null, reportId, null, correlationId.ToString("N"),
            $"Fund portfolio workbook ingestion failed. ErrorType={errorType}.");
}

public interface IFundPortfolioAuditSink
{
    Task WriteAsync(FundPortfolioAuditEvent auditEvent, CancellationToken cancellationToken);
}

public interface IFundPortfolioOperationalTelemetry
{
    void RecordDiscovery(int count);
    void RecordUpload(long bytes);
    void RecordDownload(long bytes, double latencyMilliseconds);
    void RecordRetry();
    void RecordReview(int count);
    void RecordFinalStatus(FundPortfolioImportRunStatus status);
    void RecordQueueLag(TimeSpan lag);
    void RecordOutcome(FundPortfolioImportItemStatus status);
}
