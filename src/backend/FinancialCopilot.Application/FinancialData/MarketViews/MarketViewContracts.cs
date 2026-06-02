using FinancialCopilot.Application.Authentication;

namespace FinancialCopilot.Application.FinancialData.MarketViews;

public sealed record WatchlistQuote(
    string Symbol,
    decimal? LatestPrice,
    decimal? ChangePercent,
    DateTimeOffset? AsOf,
    string? SourceKind,
    bool IsStale);

public sealed record WatchlistView(
    IReadOnlyCollection<WatchlistQuote> Symbols,
    DateTimeOffset? AsOf);

public sealed record MarketIndexObservation(
    string Symbol,
    string Name,
    decimal? Value,
    decimal? ChangePercent,
    DateTimeOffset AsOf,
    string SourceKind);

public sealed record MarketMover(
    string Symbol,
    string Name,
    decimal LatestPrice,
    decimal ChangePercent,
    DateTimeOffset AsOf,
    bool IsStale);

public sealed record MarketIndustryTrend(string Name, decimal ChangePercent);

public sealed record MarketSummary(
    IReadOnlyCollection<MarketIndexObservation> Indices,
    IReadOnlyCollection<MarketMover> TopGainers,
    IReadOnlyCollection<MarketMover> TopLosers,
    DateTimeOffset? AsOf,
    decimal? RealMoneyFlow,
    IReadOnlyCollection<MarketIndustryTrend>? TrendingIndustries,
    string? Insight);

public interface IWatchlistService
{
    Task<WatchlistView> GetAsync(CurrentActor actor, CancellationToken cancellationToken);

    Task<WatchlistView> UpdateAsync(
        CurrentActor actor,
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken);
}

public interface IMarketSummaryService
{
    Task<MarketSummary> GetAsync(CancellationToken cancellationToken);
}

public interface IMarketViewCache
{
    Task<MarketSummary?> GetSummaryAsync(CancellationToken cancellationToken);

    Task SetSummaryAsync(MarketSummary summary, CancellationToken cancellationToken);

    Task InvalidateAsync(CancellationToken cancellationToken);
}
