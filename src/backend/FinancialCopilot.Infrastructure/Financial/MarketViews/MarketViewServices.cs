using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.MarketViews;

public sealed class MarketViewOptions
{
    public const string SectionName = "MarketViews";

    public int DefaultWatchlistSymbolLimit { get; init; } = 20;

    public int StaleAfterMinutes { get; init; } = 15;

    public int SummaryCacheSeconds { get; init; } = 30;

    public int TopMoverCount { get; init; } = 3;
}

public sealed class MemoryMarketViewCache(
    IMemoryCache cache,
    IOptions<MarketViewOptions> options) : IMarketViewCache
{
    private const string SummaryKey = "market-view:summary:v1";
    private readonly MarketViewOptions _options = options.Value;

    public Task<MarketSummary?> GetSummaryAsync(CancellationToken cancellationToken) =>
        Task.FromResult(cache.TryGetValue(SummaryKey, out MarketSummary? summary) ? summary : null);

    public Task SetSummaryAsync(MarketSummary summary, CancellationToken cancellationToken)
    {
        cache.Set(SummaryKey, summary, TimeSpan.FromSeconds(_options.SummaryCacheSeconds));
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(CancellationToken cancellationToken)
    {
        cache.Remove(SummaryKey);
        return Task.CompletedTask;
    }
}

public sealed class WatchlistService(
    FinancialIngestionDbContext dbContext,
    IBillableAccountResolver accountResolver,
    IPlanCapabilityService planCapabilities,
    IOptions<MarketViewOptions> options,
    TimeProvider timeProvider) : IWatchlistService
{
    private const string WatchlistCapability = "Watchlist.Symbols";
    private readonly MarketViewOptions _options = options.Value;

    public async Task<WatchlistView> GetAsync(CurrentActor actor, CancellationToken cancellationToken)
    {
        var symbols = await dbContext.WatchlistSymbols
            .AsNoTracking()
            .Where(row =>
                row.TenantId == actor.TenantId &&
                row.ActorId == actor.ActorId &&
                row.ActorType == actor.ActorType.ToString())
            .OrderBy(row => row.Position)
            .Select(row => row.Symbol)
            .ToArrayAsync(cancellationToken);
        return await BuildViewAsync(symbols, cancellationToken);
    }

    public async Task<WatchlistView> UpdateAsync(
        CurrentActor actor,
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken)
    {
        var normalized = symbols
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Where(symbol => symbol.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Any(symbol => symbol.Length > 64))
        {
            throw new ArgumentException("Watchlist symbols must not exceed 64 characters.", nameof(symbols));
        }

        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(
                actor.ActorId,
                actor.TenantId,
                actor.UserId,
                actor.ApiClientId,
                ExternalUserId: null),
            cancellationToken);
        var limit = await planCapabilities.GetLimitAsync(account, WatchlistCapability, cancellationToken)
            ?? _options.DefaultWatchlistSymbolLimit;
        if (normalized.Length > limit)
        {
            throw new ArgumentException($"The active subscription plan allows at most {limit} watchlist symbols.", nameof(symbols));
        }

        // Spec 068: Symbols table removed. Fall back to Companies rows for symbol validation.
        var knownSymbols = await dbContext.TradingInstruments
            .AsNoTracking()
            .Where(row => row.IsActive && normalized.Contains(row.Symbol))
            .Select(row => row.Symbol)
            .Union(dbContext.Companies.AsNoTracking()
                .Where(row => row.TseSymbol != null && normalized.Contains(row.TseSymbol))
                .Select(row => row.TseSymbol!))
            .ToArrayAsync(cancellationToken);
        var known = knownSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = normalized.Where(symbol => !known.Contains(symbol)).ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException($"Unknown watchlist symbols: {string.Join(", ", unknown)}.", nameof(symbols));
        }

        var existing = await dbContext.WatchlistSymbols
            .Where(row =>
                row.TenantId == actor.TenantId &&
                row.ActorId == actor.ActorId &&
                row.ActorType == actor.ActorType.ToString())
            .ToListAsync(cancellationToken);
        dbContext.WatchlistSymbols.RemoveRange(existing);
        dbContext.WatchlistSymbols.AddRange(normalized.Select((symbol, index) => new WatchlistSymbolRow
        {
            Id = Guid.NewGuid(),
            TenantId = actor.TenantId,
            ActorId = actor.ActorId,
            ActorType = actor.ActorType.ToString(),
            Symbol = symbol,
            Position = index,
            CreatedAt = timeProvider.GetUtcNow()
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await BuildViewAsync(normalized, cancellationToken);
    }

    private async Task<WatchlistView> BuildViewAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
        {
            return new WatchlistView([], null);
        }

        var rows = await (
            from instrument in dbContext.TradingInstruments.AsNoTracking()
            where symbols.Contains(instrument.Symbol)
            join quote in dbContext.LatestMarketQuotes.AsNoTracking()
                on instrument.Id equals quote.TradingInstrumentId into quoteRows
            from quote in quoteRows.DefaultIfEmpty()
            select new { instrument.Symbol, Quote = quote })
            .ToListAsync(cancellationToken);
        var quotes = rows
            .GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Where(item => item.Quote is not null)
                    .OrderByDescending(item => item.Quote!.AsOf)
                    .Select(item => item.Quote)
                    .FirstOrDefault(),
                StringComparer.OrdinalIgnoreCase);
        var staleBefore = timeProvider.GetUtcNow().AddMinutes(-_options.StaleAfterMinutes);
        var result = symbols.Select(symbol =>
        {
            quotes.TryGetValue(symbol, out var quote);
            return new WatchlistQuote(
                symbol,
                quote?.LatestPrice,
                quote?.PriceChangePercentage,
                quote?.AsOf,
                quote?.SourceKind,
                quote is not null && quote.AsOf < staleBefore);
        }).ToArray();
        var timestamps = result.Select(item => item.AsOf).OfType<DateTimeOffset>().ToArray();
        return new WatchlistView(result, timestamps.Length == 0 ? null : timestamps.Max());
    }
}

public sealed class MarketSummaryService(
    FinancialIngestionDbContext dbContext,
    IMarketViewCache cache,
    IOptions<MarketViewOptions> options,
    TimeProvider timeProvider) : IMarketSummaryService
{
    private readonly MarketViewOptions _options = options.Value;

    public async Task<MarketSummary> GetAsync(CancellationToken cancellationToken)
    {
        var cached = await cache.GetSummaryAsync(cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // The summary shows exactly the six governed named indices (شاخص کل, کل فرابورس, ...)
        // owned by StockMarketNamedIndices, in catalog order — not whatever index instruments
        // happen to exist in the dimension.
        var namedRefs = StockMarketNamedIndices.InstrumentRefs.ToArray();
        var indexInstruments = await dbContext.TradingInstruments.AsNoTracking()
            .Where(instrument => namedRefs.Contains(instrument.ExternalInstrumentId))
            .ToListAsync(cancellationToken);
        var indices = new List<MarketIndexObservation>(StockMarketNamedIndices.All.Count);
        foreach (var named in StockMarketNamedIndices.All)
        {
            var instrument = indexInstruments
                .FirstOrDefault(row => row.ExternalInstrumentId == named.InstrumentRef);
            if (instrument is null) continue;
            var snapshot = await dbContext.DailyIndexSnapshots.AsNoTracking()
                .Where(row => row.TradingInstrumentId == instrument.Id)
                .OrderByDescending(row => row.ObservedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (snapshot is null) continue;
            indices.Add(new MarketIndexObservation(
                instrument.Symbol,
                named.PersianName,
                snapshot.Value,
                snapshot.ChangePercent,
                snapshot.ObservedAt,
                snapshot.SourceKind));
        }
        var quoteRows = await (
            from quote in dbContext.LatestMarketQuotes.AsNoTracking()
            join instrument in dbContext.TradingInstruments.AsNoTracking()
                on quote.TradingInstrumentId equals instrument.Id
            where instrument.IsActive
            select new { quote, instrument })
            .ToListAsync(cancellationToken);
        var staleBefore = timeProvider.GetUtcNow().AddMinutes(-_options.StaleAfterMinutes);
        var movers = quoteRows
            .GroupBy(row => row.instrument.Id)
            .Select(group => group.OrderByDescending(row => row.quote.AsOf).First())
            .Select(row => new MarketMover(
                row.instrument.Symbol,
                row.instrument.Name,
                row.quote.LatestPrice,
                row.quote.PriceChangePercentage,
                row.quote.AsOf,
                row.quote.AsOf < staleBefore))
            .ToArray();
        var timestamps = indices.Select(row => row.AsOf).Concat(movers.Select(row => row.AsOf)).ToArray();
        var summary = new MarketSummary(
            indices,
            movers.Where(row => row.ChangePercent > 0).OrderByDescending(row => row.ChangePercent).Take(_options.TopMoverCount).ToArray(),
            movers.Where(row => row.ChangePercent < 0).OrderBy(row => row.ChangePercent).Take(_options.TopMoverCount).ToArray(),
            timestamps.Length == 0 ? null : timestamps.Max(),
            RealMoneyFlow: null,
            TrendingIndustries: null,
            Insight: null);
        await cache.SetSummaryAsync(summary, cancellationToken);
        return summary;
    }
}
