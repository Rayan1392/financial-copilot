using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Feature 126's admission boundary. The view is queried directly and only its
/// SymbolIsin projection is materialized; canonical tables may enrich later,
/// but cannot change this list.
/// </summary>
public sealed class NoavaranEligibleCompanyUniverseReader(FinancialIngestionDbContext db)
    : IEligibleUniverseReader
{
    public async Task<IReadOnlyList<RelativeValuationEligibleSymbol>> ReadAsync(
        CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQueryRaw<EligibleRow>("SELECT \"Id\" AS \"CompanyId\", \"SymbolIsin\" AS \"SymbolIsin\" FROM \"NoavaranEligibleCompanies\"")
            .ToListAsync(cancellationToken);

        return rows.Select(row => new RelativeValuationEligibleSymbol(row.SymbolIsin, row.CompanyId)).ToArray();
    }

    private sealed class EligibleRow
    {
        public Guid? CompanyId { get; set; }
        public string? SymbolIsin { get; set; }
    }
}
