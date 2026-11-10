using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal interface ICanonicalMonthlyCompanyResolver
{
    Task<CompanyResolutionOutcome> ResolveAsync(string symbol, CancellationToken cancellationToken);
}

/// <summary>
/// Finds the company whose monthly data the V2 path reads (Feature 158): an exact symbol match in the eligible
/// universe first, so a non-eligible or other-provider row with the same symbol cannot shadow it, then the
/// provider-scoped resolution for everything else.
/// </summary>
internal sealed class CanonicalMonthlyCompanyResolver(
    FinancialIngestionDbContext dbContext,
    ICompanyResolutionOutcomeService resolutionOutcomes) : ICanonicalMonthlyCompanyResolver
{
    private const string ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName;

    private static readonly Func<NormalizedCompanyRow, string?>[] SymbolColumnsByPriority =
    [
        row => row.Ticker,
        row => row.TseSymbol,
        row => row.CompanySymbol
    ];

    public async Task<CompanyResolutionOutcome> ResolveAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalized = PersianSymbolNormalizer.Normalize(symbol);
        if (normalized.Length > 0)
        {
            var eligible = await NoavaranCompanyScope.EligibleCompanies(dbContext, ProviderName).ToListAsync(cancellationToken);
            foreach (var column in SymbolColumnsByPriority)
            {
                var matches = eligible.Where(row => Matches(column(row), normalized)).Take(2).ToArray();
                if (matches.Length > 1)
                    return new(CompanyResolutionStatus.Ambiguous, Reason: "company_identifier_ambiguous");
                if (matches.Length == 1)
                    return new(CompanyResolutionStatus.Resolved, ToResolved(matches[0]));
            }
        }

        return await resolutionOutcomes.ResolveAsync(symbol, ProviderName, cancellationToken);
    }

    private static bool Matches(string? value, string normalized) =>
        value is not null && string.Equals(PersianSymbolNormalizer.Normalize(value), normalized, StringComparison.OrdinalIgnoreCase);

    private static ResolvedCompany ToResolved(NormalizedCompanyRow row) =>
        new(row.Id, row.ExternalCompanyId, row.Ticker, row.EnTicker, row.InstrumentCode, row.SymbolIsin, row.CompanyIsin,
            row.TseSymbol, row.CompanySymbol, row.Name);
}
