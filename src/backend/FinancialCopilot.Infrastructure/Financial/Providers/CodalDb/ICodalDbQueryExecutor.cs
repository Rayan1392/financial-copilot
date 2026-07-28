namespace FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

/// <summary>
/// Read-only query seam over CodalDB. Isolates the SQL/`Microsoft.Data.SqlClient` details from the
/// provider client so the client (payload shaping, checksums, health mapping) is fully unit-testable
/// against a fake, with no live SQL.
/// </summary>
public interface ICodalDbQueryExecutor
{
    Task<IReadOnlyList<CodalDbCompanyRecord>> QueryCompaniesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CodalStatementRow>> QueryStatementsAsync(int companyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CodalMonthlyActivityRow>> QueryMonthlyActivityAsync(
        int companyId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns vendor-precomputed ratio rows for <paramref name="companyId"/>, filtered to the
    /// curated <paramref name="mappedItemIds"/> set. Never a full-table scan.
    /// </summary>
    Task<IReadOnlyList<CodalRatioRow>> QueryFinancialRatiosAsync(
        int companyId,
        IReadOnlyCollection<int> mappedItemIds,
        CancellationToken cancellationToken);

    Task<CodalDbHealthProbe> ProbeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the distinct <c>CompanyId</c> values for which Companies/Statements/MonthlyActivity/
    /// FinancialRatios <c>ModifiedDateTime</c> is strictly greater than <paramref name="since"/>.
    /// When <paramref name="since"/> is null this is a full inventory (used by full-reload mode).
    /// </summary>
    Task<IReadOnlyList<int>> QueryChangedCompanyIdsAsync(
        DateTimeOffset? since,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the maximum <c>ModifiedDateTime</c> across Companies/Statements/MonthlyActivity/
    /// FinancialRatios, or null when every source table is empty. Used by the incremental sync
    /// orchestrator to advance the watermark after a successful run.
    /// </summary>
    Task<DateTimeOffset?> QueryMaxModifiedDateTimeAsync(CancellationToken cancellationToken);
}
