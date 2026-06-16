using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed record CyclicalWavesCompanyLinkage(
    Guid CompanyId,
    string ExternalCompanyId);

internal static class CyclicalWavesCompanyLinkageResolver
{
    // Spec 051: the authoritative Noavaran Amin company catalog now lands under the current API
    // source name (was "NadpcoApi").
    private const string NadpcoProviderName = ProviderSources.NoavaranCurrentApiName;

    public static async Task<CyclicalWavesCompanyLinkage?> ResolveAsync(
        FinancialIngestionDbContext dbContext,
        string? ticker,
        string? enticker,
        CancellationToken cancellationToken)
    {
        var normalizedTicker = Normalize(ticker);
        var normalizedEnticker = Normalize(enticker);
        if (normalizedTicker is null && normalizedEnticker is null)
        {
            return null;
        }

        var companies = await dbContext.Companies.AsNoTracking()
            .Where(row => row.ProviderName == NadpcoProviderName)
            .ToListAsync(cancellationToken);

        // Spec 068: Symbols table removed. Match solely on Companies fields.
        var company = companies.FirstOrDefault(row =>
            Matches(normalizedTicker, row.CompanySymbol) ||
            Matches(normalizedTicker, row.Name) ||
            Matches(normalizedTicker, row.TseSymbol) ||
            Matches(normalizedEnticker, row.SymbolIsin) ||
            Matches(normalizedEnticker, row.CompanyIsin) ||
            Matches(normalizedEnticker, row.CompanySymbolEnglish));
        if (company is null)
        {
            return null;
        }

        return new CyclicalWavesCompanyLinkage(company.Id, company.ExternalCompanyId);
    }

    private static bool Matches(string? expected, string? actual) =>
        expected is not null &&
        string.Equals(expected, Normalize(actual), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
