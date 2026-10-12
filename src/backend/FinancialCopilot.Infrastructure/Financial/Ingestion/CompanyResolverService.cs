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
    ILogger<CompanyResolverService> logger) : ICompanyResolverService, ICompanyResolutionOutcomeService
{
    public async Task<CompanyResolutionOutcome> ResolveAsync(
        string? identifier,
        string providerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 128 || identifier.Any(char.IsControl))
            return new(CompanyResolutionStatus.InvalidIdentifier, Reason: "invalid_company_identifier");

        var normalized = PersianSymbolNormalizer.Normalize(identifier);
        if (normalized.Length == 0)
            return new(CompanyResolutionStatus.InvalidIdentifier, Reason: "invalid_company_identifier");
        if (!string.Equals(providerName, "NoavaranCurrentApi", StringComparison.Ordinal))
            return new(CompanyResolutionStatus.UnsupportedProviderCompany, Reason: "unsupported_provider");

        var allCandidates = await dbContext.Companies.AsNoTracking().ToListAsync(cancellationToken);
        var providerCandidates = allCandidates.Where(company =>
            string.Equals(company.ProviderName, providerName, StringComparison.Ordinal)).ToArray();
        var providerResult = ResolveDetailed(providerCandidates, normalized);
        if (providerResult.Status == CompanyResolutionStatus.Resolved)
            return providerResult;
        if (providerResult.Status == CompanyResolutionStatus.Ambiguous)
            return providerResult;

        var otherProviderResult = ResolveDetailed(allCandidates.Where(company =>
            !string.Equals(company.ProviderName, providerName, StringComparison.Ordinal)).ToArray(), normalized);
        return otherProviderResult.Status switch
        {
            CompanyResolutionStatus.Resolved => new(CompanyResolutionStatus.UnsupportedProviderCompany,
                Reason: "company_not_available_from_requested_provider"),
            CompanyResolutionStatus.Ambiguous => otherProviderResult,
            _ => new(CompanyResolutionStatus.NotFound, Reason: "company_not_found")
        };
    }

    private static CompanyResolutionOutcome ResolveDetailed(
        IReadOnlyCollection<NormalizedCompanyRow> candidates,
        string normalized)
    {
        var tiers = new Func<NormalizedCompanyRow, bool>[]
        {
            company => company.Ticker is not null && PersianSymbolNormalizer.Normalize(company.Ticker) == normalized,
            company => company.TseSymbol is not null && PersianSymbolNormalizer.Normalize(company.TseSymbol) == normalized,
            company => (company.EnTicker is not null && string.Equals(PersianSymbolNormalizer.Normalize(company.EnTicker), normalized, StringComparison.OrdinalIgnoreCase)) ||
                       (company.SymbolIsin is not null && string.Equals(PersianSymbolNormalizer.Normalize(company.SymbolIsin), normalized, StringComparison.OrdinalIgnoreCase)) ||
                       (company.CompanyIsin is not null && string.Equals(PersianSymbolNormalizer.Normalize(company.CompanyIsin), normalized, StringComparison.OrdinalIgnoreCase)),
            company => company.InstrumentCode is not null && string.Equals(PersianSymbolNormalizer.Normalize(company.InstrumentCode), normalized, StringComparison.OrdinalIgnoreCase),
            company => (company.CompanySymbol is not null && string.Equals(PersianSymbolNormalizer.Normalize(company.CompanySymbol), normalized, StringComparison.OrdinalIgnoreCase)) ||
                       (company.Name is not null && string.Equals(PersianSymbolNormalizer.Normalize(company.Name), normalized, StringComparison.OrdinalIgnoreCase))
        };

        foreach (var tier in tiers)
        {
            var matches = candidates.Where(tier).DistinctBy(company => company.Id).Take(2).ToArray();
            if (matches.Length > 1)
                return new(CompanyResolutionStatus.Ambiguous, Reason: "company_identifier_ambiguous");
            if (matches.Length == 1)
                return Resolved(matches[0]);
        }

        if (normalized.Length >= 3)
        {
            var fragments = candidates.Where(company => company.Name is not null &&
                    PersianSymbolNormalizer.Normalize(company.Name).Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(company => company.Id).Take(2).ToArray();
            if (fragments.Length > 1)
                return new(CompanyResolutionStatus.Ambiguous, Reason: "company_name_fragment_ambiguous");
            if (fragments.Length == 1)
                return Resolved(fragments[0]);
        }

        return new(CompanyResolutionStatus.NotFound, Reason: "company_not_found");
    }

    private static CompanyResolutionOutcome Resolved(NormalizedCompanyRow company) => new(
        CompanyResolutionStatus.Resolved,
        new ResolvedCompany(company.Id, company.ExternalCompanyId, company.Ticker, company.EnTicker,
            company.InstrumentCode, company.SymbolIsin, company.CompanyIsin, company.TseSymbol, company.CompanySymbol));

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
