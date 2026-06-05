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
        var lower = normalized.ToLowerInvariant();

        var byCode = await dbContext.Symbols.AsNoTracking()
            .Where(s => s.SymbolCode.ToLower() == lower)
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
                c.ExternalCompanyId.ToLower() == lower ||
                (c.TseSymbol != null && c.TseSymbol.ToLower() == lower) ||
                (c.CompanySymbol != null && c.CompanySymbol.ToLower() == lower) ||
                (c.SymbolIsin != null && c.SymbolIsin.ToLower() == lower) ||
                (c.CompanyIsin != null && c.CompanyIsin.ToLower() == lower) ||
                (c.CompanySymbolEnglish != null && c.CompanySymbolEnglish.ToLower() == lower) ||
                c.Name.ToLower() == lower)
            .ToListAsync(cancellationToken);

        var exact = await ResolveSingleCompanyAsync(rawName, exactCompanies, "exact company identifier", cancellationToken);
        if (exact is not null) return exact;
        if (exactCompanies.Count > 1) return null;

        var byName = await dbContext.Companies.AsNoTracking()
            .Where(c => c.Name.ToLower().Contains(lower))
            .ToListAsync(cancellationToken);

        return await ResolveSingleCompanyAsync(rawName, byName, "company name", cancellationToken);
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

        return preferred is null ? null : new SymbolCode(preferred.SymbolCode);
    }
}
