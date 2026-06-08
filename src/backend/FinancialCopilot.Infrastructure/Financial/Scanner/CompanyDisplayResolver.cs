using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

internal sealed record CompanyDisplayLookup(
    IReadOnlyDictionary<Guid, NormalizedCompanyRow> ByCompanyId,
    IReadOnlyDictionary<string, NormalizedCompanyRow> BySymbolIdentifier);

internal static class CompanyDisplayResolver
{
    public static async Task<CompanyDisplayLookup> BuildLookupAsync(
        FinancialIngestionDbContext dbContext,
        IReadOnlyCollection<NormalizedSymbolRow> symbols,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
        {
            return new CompanyDisplayLookup(
                new Dictionary<Guid, NormalizedCompanyRow>(),
                new Dictionary<string, NormalizedCompanyRow>(StringComparer.OrdinalIgnoreCase));
        }

        var companyIds = symbols.Select(s => s.CompanyId).Distinct().ToList();
        var symbolCodes = symbols
            .Select(s => s.SymbolCode.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var upperSymbolCodes = symbolCodes.Select(s => s.ToUpperInvariant()).ToList();

        var companies = await dbContext.Companies.AsNoTracking()
            .Where(c =>
                companyIds.Contains(c.Id) ||
                (c.TseSymbol != null && upperSymbolCodes.Contains(c.TseSymbol.ToUpper())) ||
                (c.CompanySymbol != null && upperSymbolCodes.Contains(c.CompanySymbol.ToUpper())) ||
                (c.CompanySymbolEnglish != null && upperSymbolCodes.Contains(c.CompanySymbolEnglish.ToUpper())) ||
                (c.CompanySymbolPinglish != null && upperSymbolCodes.Contains(c.CompanySymbolPinglish.ToUpper())) ||
                (c.SymbolIsin != null && upperSymbolCodes.Contains(c.SymbolIsin.ToUpper())) ||
                (c.CompanyIsin != null && upperSymbolCodes.Contains(c.CompanyIsin.ToUpper())) ||
                (c.InstrumentCode != null && upperSymbolCodes.Contains(c.InstrumentCode.ToUpper())) ||
                upperSymbolCodes.Contains(c.ExternalCompanyId.ToUpper()))
            .ToListAsync(cancellationToken);

        var byCompanyId = companies
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var bySymbolIdentifier = new Dictionary<string, NormalizedCompanyRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var company in companies.OrderBy(ProviderPreference).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            AddIdentifier(bySymbolIdentifier, company, company.TseSymbol);
            AddIdentifier(bySymbolIdentifier, company, company.CompanySymbol);
            AddIdentifier(bySymbolIdentifier, company, company.CompanySymbolEnglish);
            AddIdentifier(bySymbolIdentifier, company, company.CompanySymbolPinglish);
            AddIdentifier(bySymbolIdentifier, company, company.SymbolIsin);
            AddIdentifier(bySymbolIdentifier, company, company.CompanyIsin);
            AddIdentifier(bySymbolIdentifier, company, company.InstrumentCode);
            AddIdentifier(bySymbolIdentifier, company, company.ExternalCompanyId);
        }

        return new CompanyDisplayLookup(byCompanyId, bySymbolIdentifier);
    }

    public static NormalizedCompanyRow? ResolveCompany(
        NormalizedSymbolRow symbol,
        CompanyDisplayLookup lookup)
    {
        if (lookup.ByCompanyId.TryGetValue(symbol.CompanyId, out var byId))
            return byId;

        return lookup.BySymbolIdentifier.TryGetValue(symbol.SymbolCode, out var bySymbol)
            ? bySymbol
            : null;
    }

    public static string GetDisplaySymbol(
        NormalizedCompanyRow? company,
        NormalizedSymbolRow symbol) =>
        FirstNonBlank(company?.TseSymbol, company?.CompanySymbol, symbol.SymbolCode) ?? symbol.SymbolCode;

    public static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static void AddIdentifier(
        IDictionary<string, NormalizedCompanyRow> companiesByIdentifier,
        NormalizedCompanyRow company,
        string? identifier)
    {
        var normalized = identifier?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
            companiesByIdentifier.TryAdd(normalized, company);
    }

    // Prefer the Noavaran current API catalog over the frozen archive for display (spec 051).
    private static int ProviderPreference(NormalizedCompanyRow company) =>
        company.ProviderName switch
        {
            ProviderSources.NoavaranCurrentApiName => 0,
            ProviderSources.NoavaranArchiveSqlName => 1,
            _ => 2
        };
}
