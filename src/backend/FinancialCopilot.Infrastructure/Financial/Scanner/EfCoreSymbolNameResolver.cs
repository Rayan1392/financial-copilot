using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public sealed class EfCoreSymbolNameResolver(
    FinancialIngestionDbContext dbContext,
    ILogger<EfCoreSymbolNameResolver> logger) : ISymbolNameResolver
{
    public async Task<SymbolCode?> ResolveAsync(string rawName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;

        var normalized = rawName.Trim();
        var variants = BuildLookupVariants(normalized);
        var lowerVariants = variants.Select(v => v.ToLowerInvariant()).ToList();

        var byCode = await dbContext.Symbols.AsNoTracking()
            .Where(s => lowerVariants.Contains(s.SymbolCode.ToLower()))
            .Select(s => s.SymbolCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (byCode.Count == 1) return new SymbolCode(byCode[0]);
        if (byCode.Count > 1)
        {
            logger.LogWarning(
                "Symbol name '{RawName}' matched multiple SymbolCode rows: {Matches}",
                rawName,
                string.Join(", ", byCode));
            return null;
        }

        var exactCompanies = await dbContext.Companies.AsNoTracking()
            .Where(c =>
                lowerVariants.Contains(c.ExternalCompanyId.ToLower()) ||
                (c.TseSymbol != null && lowerVariants.Contains(c.TseSymbol.ToLower())) ||
                (c.CompanySymbol != null && lowerVariants.Contains(c.CompanySymbol.ToLower())) ||
                (c.CompanySymbolEnglish != null && lowerVariants.Contains(c.CompanySymbolEnglish.ToLower())) ||
                (c.CompanySymbolPinglish != null && lowerVariants.Contains(c.CompanySymbolPinglish.ToLower())) ||
                (c.CompanyCode != null && lowerVariants.Contains(c.CompanyCode.ToLower())) ||
                (c.InstrumentCode != null && lowerVariants.Contains(c.InstrumentCode.ToLower())) ||
                (c.SymbolIsin != null && lowerVariants.Contains(c.SymbolIsin.ToLower())) ||
                (c.CompanyIsin != null && lowerVariants.Contains(c.CompanyIsin.ToLower())) ||
                lowerVariants.Contains(c.Name.ToLower()))
            .ToListAsync(cancellationToken);

        var exact = await ResolveSingleCompanyAsync(rawName, exactCompanies, "exact company identifier", cancellationToken);
        if (exact is not null) return exact;
        if (exactCompanies.Count > 1) return null;

        var candidateCompanies = await dbContext.Companies.AsNoTracking()
            .ToListAsync(cancellationToken);
        var byName = candidateCompanies
            .Where(c =>
                lowerVariants.Any(term =>
                    c.Name.ToLowerInvariant().Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return await ResolveSingleCompanyAsync(rawName, byName, "company name", cancellationToken);
    }

    private static IReadOnlyCollection<string> BuildLookupVariants(string value)
    {
        var trimmed = value.Trim();
        var persian = trimmed
            .Replace('ك', 'ک')
            .Replace('ي', 'ی');
        var arabic = trimmed
            .Replace('ک', 'ك')
            .Replace('ی', 'ي');

        return new[] { trimmed, persian, arabic }
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<SymbolCode?> ResolveSingleCompanyAsync(
        string rawName,
        IReadOnlyCollection<NormalizedCompanyRow> companies,
        string basis,
        CancellationToken cancellationToken)
    {
        if (companies.Count == 0) return null;
        if (companies.Count > 1)
        {
            logger.LogWarning(
                "Symbol name '{RawName}' matched multiple companies by {Basis}: {Matches}",
                rawName,
                basis,
                string.Join(", ", companies.Take(5).Select(c => c.Name)));
            return null;
        }

        var company = companies.Single();
        var symbols = await dbContext.Symbols.AsNoTracking()
            .Where(s => s.CompanyId == company.Id)
            .ToListAsync(cancellationToken);

        var preferred = symbols
            .OrderByDescending(s => string.Equals(s.SymbolCode, company.TseSymbol, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(s => string.Equals(s.SymbolCode, company.CompanySymbol, StringComparison.OrdinalIgnoreCase))
            .ThenBy(s => s.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.SymbolCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (preferred is not null)
            return new SymbolCode(preferred.SymbolCode);

        var identifiers = new[]
            {
                company.TseSymbol,
                company.CompanySymbol,
                company.CompanySymbolEnglish,
                company.CompanySymbolPinglish,
                company.SymbolIsin,
                company.CompanyIsin,
                company.InstrumentCode,
                company.ExternalCompanyId
            }
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var upperIdentifiers = identifiers.Select(value => value!.ToUpperInvariant()).ToList();

        var fallbackSymbols = await dbContext.Symbols.AsNoTracking()
            .Where(s => upperIdentifiers.Contains(s.SymbolCode.ToUpper()))
            .ToListAsync(cancellationToken);

        preferred = fallbackSymbols
            .OrderByDescending(s => string.Equals(s.SymbolCode, company.TseSymbol, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(s => string.Equals(s.SymbolCode, company.CompanySymbol, StringComparison.OrdinalIgnoreCase))
            .ThenBy(s => s.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.SymbolCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return preferred is null ? null : new SymbolCode(preferred.SymbolCode);
    }
}
