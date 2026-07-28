namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Enqueues monthly production/sales ingestion requests for a single company across an explicit
/// Shamsi date range. Intended for targeted re-ingestion after a data-integrity fix — use the
/// full monthly-backfill endpoint to ingest all eligible companies.
/// </summary>
public interface ISingleCompanyMonthlyIngestionService
{
    Task<SingleCompanyMonthlyIngestionResult> EnqueueAsync(
        SingleCompanyMonthlyIngestionRequest request,
        CancellationToken cancellationToken);
}

public sealed record SingleCompanyMonthlyIngestionRequest(
    int ExternalCompanyId,
    int FromShamsiYear,
    int FromShamsiMonth,
    int ToShamsiYear,
    int ToShamsiMonth,
    string RequestedBy);

public sealed record SingleCompanyMonthlyIngestionResult(
    string Outcome,
    int ExternalCompanyId,
    int MonthsInRange,
    int RequestsEnqueued,
    string FirstMonth,
    string LastMonth,
    string RequestedBy);
