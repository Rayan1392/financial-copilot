using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Resolves a company from a ticker / symbol string using a normalized, multi-step lookup
/// against the <c>Companies</c> table (spec 067).
///
/// Resolution order (each step normalizes the input with <see cref="PersianSymbolNormalizer"/>
/// before comparison):
/// <list type="number">
///   <item>Exact match on <c>Companies.Ticker</c> (case-sensitive after normalize).</item>
///   <item>Exact match on <c>Companies.TseSymbol</c> (case-insensitive).</item>
///   <item>Exact match on <c>Companies.EnTicker</c> / <c>SymbolIsin</c> / <c>CompanyIsin</c> (case-insensitive).</item>
///   <item>Exact match on <c>Companies.InstrumentCode</c> (case-insensitive).</item>
///   <item>Exact match on <c>Companies.CompanySymbol</c> or <c>Companies.Name</c> (case-insensitive).</item>
/// </list>
/// Returns <c>null</c> (never throws) when no match is found.
/// </summary>
public sealed class CompanyResolverService(
    FinancialIngestionDbContext dbContext,
    ILogger<CompanyResolverService> logger) : ICompanyResolverService
{
    public async Task<ResolvedCompany?> ResolveBySymbolAsync(string symbol, CancellationToken ct = default)
    {
        var normalized = PersianSymbolNormalizer.Normalize(symbol);
        if (normalized.Length == 0)
        {
            return null;
        }

        // Load all candidate companies in one round-trip. The Companies table is small enough
        // for this to be cheaper than N separate indexed queries per resolution step.
        var candidates = await dbContext.Companies.AsNoTracking().ToListAsync(ct);

        // Step 1: Ticker exact match (Persian, case-sensitive after normalize).
        var match = candidates.FirstOrDefault(c =>
            c.Ticker is not null &&
            PersianSymbolNormalizer.Normalize(c.Ticker) == normalized);

        // Step 2: TseSymbol (also a Persian ticker variant).
        match ??= candidates.FirstOrDefault(c =>
            c.TseSymbol is not null &&
            string.Equals(PersianSymbolNormalizer.Normalize(c.TseSymbol), normalized, StringComparison.OrdinalIgnoreCase));

        // Step 3: EnTicker / SymbolIsin / CompanyIsin (English identifiers, case-insensitive).
        match ??= candidates.FirstOrDefault(c =>
            (c.EnTicker is not null && string.Equals(PersianSymbolNormalizer.Normalize(c.EnTicker), normalized, StringComparison.OrdinalIgnoreCase)) ||
            (c.SymbolIsin is not null && string.Equals(PersianSymbolNormalizer.Normalize(c.SymbolIsin), normalized, StringComparison.OrdinalIgnoreCase)) ||
            (c.CompanyIsin is not null && string.Equals(PersianSymbolNormalizer.Normalize(c.CompanyIsin), normalized, StringComparison.OrdinalIgnoreCase)));

        // Step 4: InstrumentCode (TSETMC 12-digit code).
        match ??= candidates.FirstOrDefault(c =>
            c.InstrumentCode is not null &&
            string.Equals(PersianSymbolNormalizer.Normalize(c.InstrumentCode), normalized, StringComparison.OrdinalIgnoreCase));

        // Step 5: CompanySymbol or exact Name fallback.
        match ??= candidates.FirstOrDefault(c =>
            (c.CompanySymbol is not null && string.Equals(PersianSymbolNormalizer.Normalize(c.CompanySymbol), normalized, StringComparison.OrdinalIgnoreCase)) ||
            (c.Name is not null && string.Equals(PersianSymbolNormalizer.Normalize(c.Name), normalized, StringComparison.OrdinalIgnoreCase)));

        // Step 6: unambiguous company-name fragment fallback, e.g. "چادرملو" ->
        // "معدنی و صنعتی چادرملو". Ambiguous fragments deliberately return null.
        match ??= ResolveByCompanyNameFragment(candidates, normalized);

        if (match is null)
        {
            logger.LogDebug(
                "CompanyResolverService: no company found for symbol '{Symbol}' (normalized: '{Normalized}').",
                symbol,
                normalized);
            return null;
        }

        return new ResolvedCompany(
            match.Id,
            match.ExternalCompanyId,
            match.Ticker,
            match.EnTicker,
            match.InstrumentCode,
            match.SymbolIsin,
            match.CompanyIsin,
            match.TseSymbol,
            match.CompanySymbol);
    }

    private static NormalizedCompanyRow? ResolveByCompanyNameFragment(
        IReadOnlyCollection<NormalizedCompanyRow> candidates,
        string normalized)
    {
        if (normalized.Length < 3)
            return null;

        var matches = candidates
            .Where(c => c.Name is not null)
            .Select(c => new
            {
                Company = c,
                NormalizedName = PersianSymbolNormalizer.Normalize(c.Name!)
            })
            .Where(c => c.NormalizedName.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0].Company : null;
    }
}
