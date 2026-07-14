using FinancialCopilot.Application.Authentication;
using System.Text.Json.Serialization;

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

[JsonConverter(typeof(JsonStringEnumConverter<MarketPulseSessionState>))]
public enum MarketPulseSessionState
{
    PreOpen,
    Open,
    Intermission,
    Closed,
    Holiday,
    Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter<MarketPulseFactStatus>))]
public enum MarketPulseFactStatus
{
    Available,
    Partial,
    Stale,
    Unavailable
}

public sealed record MarketPulseFact(
    string Code,
    string LabelFa,
    decimal? Value,
    string Unit,
    MarketPulseFactStatus Status,
    string? Reason);

public sealed record MarketPulseBreadth(
    int? Advancing,
    int? Declining,
    int? Unchanged,
    int IncludedInstruments,
    int ExcludedInstruments,
    MarketPulseFactStatus Status,
    string? Reason);

public sealed record MarketPulseIndustryDriver(
    string IndustryCode,
    string IndustryName,
    decimal ChangePercent,
    int InstrumentCount);

public sealed record MarketPulseComparison(
    string Window,
    int RequiredSessions,
    int AvailableSessions,
    decimal? BaselineAverage,
    decimal? ChangePercent,
    MarketPulseFactStatus Status,
    string? Reason);

public sealed record MarketPulseEvidence(
    string Dataset,
    string Provider,
    DateTimeOffset? WatermarkUtc,
    int IncludedRecords,
    int ExcludedRecords,
    string Unit,
    string Cutoff);

public sealed record MarketPulseSnapshot(
    Guid Id,
    DateOnly TradingDate,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset GeneratedAtUtc,
    string Segment,
    MarketPulseSessionState SessionState,
    string CadenceSlot,
    bool IsPartial,
    bool IsFinal,
    int Revision,
    Guid? SupersedesSnapshotId,
    string DefinitionVersion,
    DateTimeOffset? SourceWatermarkUtc,
    IReadOnlyCollection<MarketPulseFact> Facts,
    MarketPulseBreadth Breadth,
    IReadOnlyCollection<MarketPulseIndustryDriver> LeadingIndustries,
    IReadOnlyCollection<MarketPulseIndustryDriver> LaggingIndustries,
    IReadOnlyCollection<MarketPulseComparison> Comparisons,
    IReadOnlyCollection<MarketPulseEvidence> Evidence,
    string Disclaimer);

public sealed record MarketPulseHistoryPage(
    IReadOnlyCollection<MarketPulseSnapshot> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MarketPulseHistoryQuery(
    DateOnly? From,
    DateOnly? To,
    MarketPulseSessionState? SessionState,
    bool? IsFinal,
    string? Segment,
    int Page = 1,
    int PageSize = 20);

public sealed class MarketPulseValidationException(string message) : Exception(message);

public sealed class MarketPulseAccessDeniedException(string message) : Exception(message);

public interface IMarketPulseService
{
    Task<MarketPulseSnapshot> GetLatestAsync(
        CurrentActor actor,
        string? segment,
        CancellationToken cancellationToken);

    Task<MarketPulseHistoryPage> GetHistoryAsync(
        CurrentActor actor,
        MarketPulseHistoryQuery query,
        CancellationToken cancellationToken);
}

public interface IMarketPulseSnapshotGenerator
{
    Task<MarketPulseSnapshot> CaptureAsync(string? segment, CancellationToken cancellationToken);
}

public interface IMarketViewCache
{
    Task<MarketSummary?> GetSummaryAsync(CancellationToken cancellationToken);

    Task SetSummaryAsync(MarketSummary summary, CancellationToken cancellationToken);

    Task InvalidateAsync(CancellationToken cancellationToken);
}
