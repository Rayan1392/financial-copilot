using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

/// <summary>Reads the tickers of <c>NoavaranEligibleCompanies</c>, cached briefly because the view changes rarely.</summary>
public sealed class NoavaranEligibleSymbolResolver(
    FinancialIngestionDbContext dbContext,
    IMemoryCache cache,
    ILogger<NoavaranEligibleSymbolResolver> logger) : IEligibleSymbolResolver
{
    private const string CacheKey = "feature156:eligible-symbols";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    public async Task<EligibleSymbol?> ResolveAsync(string compactKey, CancellationToken cancellationToken)
    {
        try
        {
            var symbols = await cache.GetOrCreateAsync(CacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
                return LoadAsync(cancellationToken);
            });
            return symbols!.GetValueOrDefault(compactKey);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Eligible symbol lookup failed; the message continues on the normal route.");
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, EligibleSymbol>> LoadAsync(CancellationToken cancellationToken)
    {
        var companies = await (
            from company in dbContext.NoavaranEligibleCompanies.AsNoTracking()
            join market in dbContext.Markets.AsNoTracking() on company.MarketId equals (Guid?)market.Id into markets
            from market in markets.DefaultIfEmpty()
            join industryGroup in dbContext.IndustryGroups.AsNoTracking() on company.GroupId equals (Guid?)industryGroup.Id into groups
            from industryGroup in groups.DefaultIfEmpty()
            select new
            {
                company.Id,
                company.CompanySymbol,
                company.TseSymbol,
                company.Name,
                MarketName = market == null ? null : market.Name,
                GroupName = industryGroup == null ? null : industryGroup.Name
            }).ToArrayAsync(cancellationToken);

        // A key shared by two companies is ambiguous, so it never opens a card.
        return companies
            .SelectMany(company => new[] { company.CompanySymbol, company.TseSymbol }
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(SymbolSnapshotIntentRules.CompactKey)
                .Where(key => key.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Select(key => (Key: key, Company: company)))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var company = group.Single().Company;
                    var symbol = string.IsNullOrWhiteSpace(company.CompanySymbol) ? company.TseSymbol! : company.CompanySymbol;
                    return new EligibleSymbol(symbol.Trim(), company.Name, company.MarketName, company.GroupName);
                },
                StringComparer.Ordinal);
    }
}
