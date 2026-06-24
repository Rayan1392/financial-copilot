namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// DataAdmin-only backfill that rebuilds trend snapshots from already-persisted Noavaran
/// monthly activity data. Reuses the existing calculator so historical and live paths match.
/// </summary>
public interface ICompanyMonthlyActivityTrendSnapshotBackfillService
{
    Task<CompanyMonthlyActivityTrendSnapshotBackfillResult> RunAsync(
        CompanyMonthlyActivityTrendSnapshotBackfillRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Date range, company scope, and rebuild flag are now read from TrendSnapshotBackfillOptions
/// (appsettings "TrendSnapshotBackfill" section). Only the audit identity is passed at call time.
/// </summary>
public sealed record CompanyMonthlyActivityTrendSnapshotBackfillRequest(string RequestedBy);

public sealed record CompanyMonthlyActivityTrendSnapshotBackfillResult(
    string Outcome,
    string RequestedBy,
    int CompaniesConsidered,
    int CompanyMonthsDiscovered,
    int CompanyMonthsProcessed,
    int CompanyMonthsSkipped,
    int CompanyMonthsFailed,
    string Duration);
