using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Application.Authentication;

namespace FinancialCopilot.Application.FinancialData.Insights;

public sealed record InsightDetectionContext(
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset SinceUtc,
    IReadOnlyCollection<string>? ExternalCompanyIds = null,
    int? Take = null);

public sealed record InsightFeedQuery(
    string? Symbol = null,
    string? IndustryCode = null,
    InsightType? InsightType = null,
    InsightSeverity? Severity = null,
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null,
    bool IncludeExpired = false,
    int Skip = 0,
    int Take = 20);

public sealed record InsightFeedResponse(
    int TotalCount,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<InsightFeedItem> Items);

public sealed record InsightFeedItem(
    Guid Id,
    string ExternalCompanyId,
    string Symbol,
    string? IndustryCode,
    InsightType InsightType,
    InsightSeverity Severity,
    decimal ImportanceScore,
    decimal ConfidenceScore,
    string Title,
    string Summary,
    string Reason,
    IReadOnlyList<InsightEvidenceItem> Evidence,
    string SourceProviderName,
    InsightSourceEntityType SourceEntityType,
    string? SourceEntityId,
    string? SourcePeriod,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string DeduplicationKey,
    IReadOnlyList<InsightAction> SuggestedActions);

public sealed record GetMyFollowedSymbolInsightsQuery(
    CurrentActor Actor,
    InsightType? InsightType = null,
    InsightSeverity? Severity = null,
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null,
    bool IncludeExpired = false,
    bool IncludeDismissed = false,
    int Skip = 0,
    int Take = 20);

public sealed record FollowedSymbolInsightFeedQuery(
    InsightUserActor Actor,
    IReadOnlyCollection<string> ExternalCompanyIds,
    InsightType? InsightType = null,
    InsightSeverity? Severity = null,
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null,
    bool IncludeExpired = false,
    bool IncludeDismissed = false,
    int Skip = 0,
    int Take = 20);

public sealed record FollowedSymbolInsightFeedResponse(
    int TotalCount,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<FollowedSymbolInsightFeedItem> Items,
    FollowedSymbolInsightEmptyState? EmptyState);

public sealed record FollowedSymbolInsightFeedItem(
    InsightFeedItem Insight,
    bool Seen,
    bool Dismissed,
    DateTimeOffset? SeenAtUtc,
    DateTimeOffset? DismissedAtUtc,
    IReadOnlyList<InsightActionDto> Actions);

public sealed record FollowedSymbolInsightEmptyState(
    string Reason,
    string Message,
    IReadOnlyList<InsightActionDto> SuggestedActions);

public sealed record InsightActionDto(
    string Kind,
    string Label,
    string? Target);

public sealed record UserInsightStateDto(
    Guid InsightEventId,
    bool Seen,
    bool Dismissed,
    DateTimeOffset? SeenAtUtc,
    DateTimeOffset? DismissedAtUtc);

public sealed record MarkUserInsightSeenCommand(CurrentActor Actor, Guid InsightEventId);

public sealed record DismissUserInsightCommand(CurrentActor Actor, Guid InsightEventId);

public sealed record ExplainInsightQuery(CurrentActor Actor, Guid InsightEventId);

public sealed record GenerateMarketInsightsRequest(int LookbackDays = 7);

public sealed record GenerateMarketInsightsResult(
    int DetectorsRun,
    int EventsDetected,
    int EventsPersisted,
    DateTimeOffset GeneratedAtUtc);

public interface IInsightEventRepository
{
    Task<int> UpsertAsync(IReadOnlyCollection<InsightEvent> events, CancellationToken cancellationToken = default);

    Task<InsightFeedResponse> QueryAsync(InsightFeedQuery query, CancellationToken cancellationToken = default);

    Task<InsightFeedItem?> FindAsync(Guid insightEventId, CancellationToken cancellationToken = default);
}

public interface IFollowedSymbolInsightFeedRepository
{
    Task<FollowedSymbolInsightFeedResponse> QueryAsync(
        FollowedSymbolInsightFeedQuery query,
        CancellationToken cancellationToken = default);
}

public interface IUserInsightStateRepository
{
    Task<UserInsightState?> FindAsync(
        InsightUserActor actor,
        Guid insightEventId,
        CancellationToken cancellationToken = default);

    Task<UserInsightState> MarkSeenAsync(
        InsightUserActor actor,
        Guid insightEventId,
        DateTimeOffset seenAtUtc,
        CancellationToken cancellationToken = default);

    Task<UserInsightState> DismissAsync(
        InsightUserActor actor,
        Guid insightEventId,
        DateTimeOffset dismissedAtUtc,
        CancellationToken cancellationToken = default);
}

public interface IInsightDetector
{
    string DetectorName { get; }

    Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default);
}

public interface IInsightDeduplicationPolicy
{
    string BuildKey(
        InsightType insightType,
        string externalCompanyId,
        string sourceProviderName,
        InsightSourceEntityType sourceEntityType,
        string? sourceEntityId,
        string? sourcePeriod);
}

public interface IGenerateMarketInsightsUseCase
{
    Task<GenerateMarketInsightsResult> ExecuteAsync(
        GenerateMarketInsightsRequest request,
        CancellationToken cancellationToken = default);
}

public interface IGenerateMarketMicrostructureInsightsUseCase
{
    Task<GenerateMarketInsightsResult> ExecuteAsync(
        GenerateMarketInsightsRequest request,
        CancellationToken cancellationToken = default);
}

public interface IGetMarketInsightFeedUseCase
{
    Task<InsightFeedResponse> ExecuteAsync(
        InsightFeedQuery query,
        CancellationToken cancellationToken = default);
}

public interface IGetMyFollowedSymbolInsightsUseCase
{
    Task<FollowedSymbolInsightFeedResponse> ExecuteAsync(
        GetMyFollowedSymbolInsightsQuery query,
        CancellationToken cancellationToken = default);
}

public interface IMarkUserInsightSeenUseCase
{
    Task<UserInsightStateDto> ExecuteAsync(
        MarkUserInsightSeenCommand command,
        CancellationToken cancellationToken = default);
}

public interface IDismissUserInsightUseCase
{
    Task<UserInsightStateDto> ExecuteAsync(
        DismissUserInsightCommand command,
        CancellationToken cancellationToken = default);
}

public interface IExplainInsightUseCase
{
    Task<string> ExecuteAsync(
        ExplainInsightQuery query,
        CancellationToken cancellationToken = default);
}
