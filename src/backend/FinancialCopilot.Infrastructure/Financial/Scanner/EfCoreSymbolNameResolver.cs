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

        // 1. Case-insensitive exact match on Symbols.SymbolCode
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

        // 2. Case-insensitive match on Companies.ExternalCompanyId
        var byExternalId = await dbContext.Companies.AsNoTracking()
            .Where(c => c.ExternalCompanyId.ToLower() == lower)
            .Join(dbContext.Symbols.AsNoTracking(),
                c => c.Id,
                s => s.CompanyId,
                (c, s) => s.SymbolCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (byExternalId.Count == 1) return new SymbolCode(byExternalId[0]);
        if (byExternalId.Count > 1)
        {
            logger.LogWarning(
                "Symbol name '{RawName}' matched multiple companies by ExternalCompanyId.",
                rawName);
            return null;
        }

        // 3. Case-insensitive substring/trim match on Companies.Name
        var byName = await dbContext.Companies.AsNoTracking()
            .Where(c => c.Name.ToLower().Contains(lower) || c.Name.ToLower() == lower)
            .Join(dbContext.Symbols.AsNoTracking(),
                c => c.Id,
                s => s.CompanyId,
                (c, s) => s.SymbolCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (byName.Count == 1) return new SymbolCode(byName[0]);
        if (byName.Count > 1)
        {
            logger.LogWarning(
                "Symbol name '{RawName}' matched multiple companies by Name (ambiguous): {Matches}",
                rawName,
                string.Join(", ", byName.Take(5)));
            return null;
        }

        return null;
    }
}
