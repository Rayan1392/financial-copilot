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

        var company = companies.FirstOrDefault(row =>
            Matches(normalizedTicker, row.CompanySymbol) ||
            Matches(normalizedTicker, row.Name) ||
            Matches(normalizedTicker, row.TseSymbol) ||
            Matches(normalizedEnticker, row.SymbolIsin) ||
            Matches(normalizedEnticker, row.CompanyIsin) ||
            Matches(normalizedEnticker, row.CompanySymbolEnglish));
        if (company is not null)
        {
            return new CyclicalWavesCompanyLinkage(company.Id, company.ExternalCompanyId);
        }

        var symbols = await dbContext.Symbols.AsNoTracking()
            .Where(row => row.ProviderName == NadpcoProviderName)
            .ToListAsync(cancellationToken);
        var symbol = symbols.FirstOrDefault(row =>
            Matches(normalizedTicker, row.ExternalSymbolId) ||
            Matches(normalizedTicker, row.SymbolCode) ||
            Matches(normalizedEnticker, row.ExternalSymbolId) ||
            Matches(normalizedEnticker, row.SymbolCode));
        if (symbol is null)
        {
            return null;
        }

        var symbolCompany = companies.SingleOrDefault(row => row.Id == symbol.CompanyId);
        return symbolCompany is null
            ? null
            : new CyclicalWavesCompanyLinkage(symbolCompany.Id, symbolCompany.ExternalCompanyId);
    }

    private static bool Matches(string? expected, string? actual) =>
        expected is not null &&
        string.Equals(expected, Normalize(actual), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
