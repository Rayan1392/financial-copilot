using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Caching.Memory;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Short-lived cache for paged disclosure results. The generation key lets successful
/// normalized-data syncs invalidate every filter/page without enumerating cache entries.
/// </summary>
public sealed class CompanyDisclosureFeedCache(IMemoryCache memoryCache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(3);
    private long _generation;

    public async Task<CompanyDisclosureFeedPage> GetOrCreateAsync(
        CompanyDisclosureFeedQuery query,
        Func<CancellationToken, Task<CompanyDisclosureFeedPage>> factory,
        CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _generation);
        var key = $"company-disclosure-feed:{generation}:{BuildQueryKey(query)}";
        if (memoryCache.TryGetValue(key, out CompanyDisclosureFeedPage? cached) && cached is not null)
            return cached;

        var result = await factory(cancellationToken);
        // A sync may have invalidated while the database request was in flight. Do not
        // publish that potentially stale result into the current generation.
        if (generation == Volatile.Read(ref _generation))
            memoryCache.Set(key, result, CacheDuration);

        return result;
    }

    public void Invalidate() => Interlocked.Increment(ref _generation);

    private static string BuildQueryKey(CompanyDisclosureFeedQuery query)
    {
        var canonical = query with
        {
            Types = query.Types?.Distinct().Order().ToArray(),
            ProviderNames = query.ProviderNames?.Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            SymbolOrCompany = string.IsNullOrWhiteSpace(query.SymbolOrCompany)
                ? null
                : query.SymbolOrCompany.Trim().ToUpperInvariant(),
            ReceivedFrom = query.ReceivedFrom?.ToUniversalTime(),
            ReceivedTo = query.ReceivedTo?.ToUniversalTime()
        };

        return JsonSerializer.Serialize(canonical);
    }
}
