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

    Task<CodalDbHealthProbe> ProbeAsync(CancellationToken cancellationToken);
}
