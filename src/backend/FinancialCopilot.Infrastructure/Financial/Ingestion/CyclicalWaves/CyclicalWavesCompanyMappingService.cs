using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

/// <summary>
/// Enriches <c>Companies</c> rows with <c>Ticker</c> (Persian symbol) and <c>EnTicker</c>
/// (SymbolIsin / English ISIN) sourced from the NADPCO company catalog (spec 067 TASK-005).
///
/// Match order (per company in the NADPCO catalog):
/// <list type="number">
///   <item>Primary: <c>Companies.SymbolIsin = nadpco.TseSIsinCode</c> (normalized, case-insensitive).</item>
///   <item>Fallback: <c>Companies.Ticker = PersianSymbolNormalizer.Normalize(nadpco.CoSymbol)</c>.</item>
/// </list>
/// On match: sets <c>Ticker</c> and <c>EnTicker</c> only if they are currently null (idempotent;
/// a confirmed non-null value from a previous run is never overwritten by a weaker match).
/// </summary>
public sealed class CyclicalWavesCompanyMappingService(
    FinancialIngestionDbContext dbContext,
    ILogger<CyclicalWavesCompanyMappingService> logger) : ICyclicalWavesCompanyMappingService
{
    public async Task<CompanyMappingResult> SyncMappingAsync(CancellationToken cancellationToken)
    {
        // Load eligible NADPCO companies (equity listings on the 3 main markets only).
        var nadpcoCompanies = await NoavaranCompanyScope
            .EligibleCompanies(dbContext, NadpcoApiCompanyNormalizer.NadpcoApiProviderName)
            .Select(c => new
            {
                c.SymbolIsin,    // TseSIsinCode — primary match key
                c.CompanySymbol, // CoSymbol — Persian ticker
                c.CompanyIsin,   // TseCIsinCode — company ISIN (secondary key)
                c.EnTicker,      // TseSIsinCode / English ISIN already populated
                c.Ticker,        // Persian ticker already populated
            })
            .ToListAsync(cancellationToken);

        // Load Companies rows from non-NADPCO providers that need enrichment.
        // NADPCO rows are the source — we enrich rows from other providers (e.g. CodalDb) that share
        // the same instrument but lack Ticker/EnTicker. Exclude the NADPCO source rows themselves.
        var allCompanies = await dbContext.Companies
            .Where(c => c.ProviderName != NadpcoApiCompanyNormalizer.NadpcoApiProviderName)
            .ToListAsync(cancellationToken);

        // Index by normalized SymbolIsin and normalized Ticker. Use GroupBy + First to tolerate
        // duplicate normalized keys (multiple providers can share the same SymbolIsin value).
        var bySymbolIsin = allCompanies
            .Where(c => c.SymbolIsin is not null)
            .GroupBy(c => PersianSymbolNormalizer.Normalize(c.SymbolIsin!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var byTicker = allCompanies
            .Where(c => c.Ticker is not null)
            .GroupBy(c => PersianSymbolNormalizer.Normalize(c.Ticker!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int matched = 0, updated = 0, unmatched = 0;

        foreach (var nadpco in nadpcoCompanies)
        {
            var normalizedSymbolIsin = PersianSymbolNormalizer.Normalize(nadpco.SymbolIsin);
            var normalizedTicker = PersianSymbolNormalizer.Normalize(nadpco.CompanySymbol);

            // Step 1: primary match by SymbolIsin.
            var company = normalizedSymbolIsin.Length > 0 && bySymbolIsin.TryGetValue(normalizedSymbolIsin, out var byIsin)
                ? byIsin
                : null;

            // Step 2: fallback by Persian Ticker.
            if (company is null && normalizedTicker.Length > 0 && byTicker.TryGetValue(normalizedTicker, out var byTickerMatch))
            {
                company = byTickerMatch;
            }

            if (company is null)
            {
                unmatched++;
                continue;
            }

            matched++;
            bool dirty = false;

            // Only populate null columns — never overwrite confirmed values.
            if (company.Ticker is null && normalizedTicker.Length > 0)
            {
                company.Ticker = PersianSymbolNormalizer.Normalize(nadpco.CompanySymbol);
                dirty = true;
            }

            if (company.EnTicker is null && normalizedSymbolIsin.Length > 0)
            {
                company.EnTicker = nadpco.SymbolIsin?.Trim();
                dirty = true;
            }

            if (dirty)
            {
                updated++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "CyclicalWavesCompanyMappingService completed: matched={Matched} updated={Updated} skipped={Skipped} unmatched={Unmatched}.",
            matched,
            updated,
            matched - updated,
            unmatched);

        return new CompanyMappingResult(matched, updated, matched - updated, unmatched);
    }
}
