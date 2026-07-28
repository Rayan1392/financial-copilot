using System.Globalization;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Authoritative eligibility scope for per-company Noavaran current-API data requests
/// (statements, fundamental indexes, monthly activity, catch-ups, backfills): only equity listings
/// (<c>PrecedencyRight = 0</c>, i.e. no حق تقدم rights) on the three primary markets —
/// بورس, فرابورس, and بازار پایه. The company-catalog sync itself stays unscoped (it is the
/// catalog source); every other vendor call enumerates companies through this scope so the vendor
/// is never queried for funds, rights, or off-market listings.
/// The <c>NoavaranEligibleCompanies</c> PostgreSQL view mirrors this filter for operators.
/// </summary>
public static class NoavaranCompanyScope
{
    /// <summary>بورس (TSE main market).</summary>
    public static readonly Guid BourseMarketId = Guid.Parse("037c69ad-f519-419f-ae62-59003b6b2428");

    /// <summary>فرابورس (IFB).</summary>
    public static readonly Guid FaraBourseMarketId = Guid.Parse("a3ccb30a-caed-4f26-a84a-ac0eb8c78c76");

    /// <summary>بازار پایه (IFB base market).</summary>
    public static readonly Guid BaseMarketId = Guid.Parse("86c05022-632c-44cd-96c9-5c4f58c51ef5");

    public static readonly IReadOnlyList<Guid> EligibleMarketIds =
        [BourseMarketId, FaraBourseMarketId, BaseMarketId];

    /// <summary>Eligible companies for per-company vendor requests, for the given provider.</summary>
    public static IQueryable<NormalizedCompanyRow> EligibleCompanies(
        FinancialIngestionDbContext dbContext,
        string providerName) =>
        dbContext.Companies.AsNoTracking()
            .Where(row => row.ProviderName == providerName &&
                row.PrecedencyRight == 0 &&
                row.MarketId != null &&
                EligibleMarketIds.Contains(row.MarketId.Value));

    /// <summary>Distinct numeric vendor company ids (coID) of the eligible companies, ascending.</summary>
    public static async Task<IReadOnlyList<int>> EligibleCompanyIdsAsync(
        FinancialIngestionDbContext dbContext,
        string providerName,
        CancellationToken cancellationToken)
    {
        var ids = await EligibleCompanies(dbContext, providerName)
            .Select(row => row.ExternalCompanyId)
            .ToListAsync(cancellationToken);

        return ids
            .Select(id => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (int?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
    }
}
