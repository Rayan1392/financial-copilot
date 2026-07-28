using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

/// <summary>
/// Backfills <c>CompanyId</c> on CyclicalWaves <c>FinancialStatements</c> and <c>MonthlyReports</c>
/// rows that were ingested before spec 067 wired company resolution into the normalizers (TASK-007).
///
/// Algorithm:
/// <list type="number">
///   <item>Queries null-CompanyId rows in batches of 500, joining to ExternalCompanyId to get the ticker.</item>
///   <item>For each row: calls <see cref="ICompanyResolverService.ResolveBySymbolAsync"/> on the
///     <c>ExternalCompanyId</c> value (which is the CyclicalWaves ticker for rows processed before 067).</item>
///   <item>Sets <c>CompanyId</c> when resolved; leaves null and records unresolved ticker otherwise.</item>
///   <item>Saves after each batch. Safe to re-run: already-resolved rows are skipped by the WHERE clause.</item>
/// </list>
/// </summary>
public sealed class BackfillCyclicalWavesCompanyIdService(
    FinancialIngestionDbContext dbContext,
    ICompanyResolverService companyResolver,
    ILogger<BackfillCyclicalWavesCompanyIdService> logger) : IBackfillCyclicalWavesCompanyIdService
{
    private const int BatchSize = 500;
    private const string CyclicalWavesProvider = "CyclicalWaves";

    public async Task<BackfillCompanyIdResult> RunAsync(CancellationToken cancellationToken)
    {
        var fsResolved = 0;
        var fsUnresolved = new HashSet<string>(StringComparer.Ordinal);
        var mrResolved = 0;
        var mrUnresolved = new HashSet<string>(StringComparer.Ordinal);

        // --- FinancialStatements ---
        while (true)
        {
            var batch = await dbContext.FinancialStatements
                .Where(r => r.ProviderName == CyclicalWavesProvider && r.CompanyId == null)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0) break;

            foreach (var row in batch)
            {
                var resolved = await companyResolver.ResolveBySymbolAsync(row.ExternalCompanyId, cancellationToken);
                if (resolved is not null)
                {
                    row.CompanyId = resolved.Id;
                    fsResolved++;
                }
                else
                {
                    fsUnresolved.Add(row.ExternalCompanyId);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // --- MonthlyReports ---
        while (true)
        {
            var batch = await dbContext.MonthlyReports
                .Where(r => r.ProviderName == CyclicalWavesProvider && r.CompanyId == null)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0) break;

            foreach (var row in batch)
            {
                var resolved = await companyResolver.ResolveBySymbolAsync(row.ExternalCompanyId, cancellationToken);
                if (resolved is not null)
                {
                    row.CompanyId = resolved.Id;
                    mrResolved++;
                }
                else
                {
                    mrUnresolved.Add(row.ExternalCompanyId);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        foreach (var ticker in fsUnresolved.Concat(mrUnresolved).Distinct(StringComparer.Ordinal))
        {
            logger.LogWarning(
                "[CyclicalWaves] Backfill: CompanyId unresolved for ticker={Ticker}", ticker);
        }

        var totalResolved = fsResolved + mrResolved;
        var totalUnresolved = fsUnresolved.Count + mrUnresolved.Count;

        logger.LogInformation(
            "BackfillCyclicalWavesCompanyId complete: statements_resolved={StatementsResolved} " +
            "reports_resolved={ReportsResolved} unresolved_tickers={UnresolvedTickers}.",
            fsResolved, mrResolved, totalUnresolved);

        return new BackfillCompanyIdResult(totalResolved, totalUnresolved);
    }
}
