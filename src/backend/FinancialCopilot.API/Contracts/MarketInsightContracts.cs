namespace FinancialCopilot.API.Contracts;

public sealed record InsightFeedHttpResponse(
    int TotalCount,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<InsightFeedItemHttpResponse> Items);

public sealed record FollowedSymbolInsightFeedHttpResponse(
    int TotalCount,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<FollowedSymbolInsightFeedItemHttpResponse> Items,
    FollowedSymbolInsightEmptyStateHttpResponse? EmptyState);

public sealed record FollowedSymbolInsightFeedItemHttpResponse(
    InsightFeedItemHttpResponse Insight,
    bool Seen,
    bool Dismissed,
    DateTimeOffset? SeenAtUtc,
    DateTimeOffset? DismissedAtUtc,
    IReadOnlyList<InsightActionHttpResponse> Actions);

public sealed record FollowedSymbolInsightEmptyStateHttpResponse(
    string Reason,
    string Message,
    IReadOnlyList<InsightActionHttpResponse> SuggestedActions);

public sealed record InsightActionHttpResponse(
    string Kind,
    string Label,
    string? Target);

public sealed record UserInsightStateHttpResponse(
    Guid InsightEventId,
    bool Seen,
    bool Dismissed,
    DateTimeOffset? SeenAtUtc,
    DateTimeOffset? DismissedAtUtc);

public sealed record InsightFeedItemHttpResponse(
    Guid Id,
    string ExternalCompanyId,
    string Symbol,
    string? IndustryCode,
    string InsightType,
    string Severity,
    decimal ImportanceScore,
    decimal ConfidenceScore,
    string Title,
    string Summary,
    string Reason,
    IReadOnlyList<InsightEvidenceItemHttpResponse> Evidence,
    string SourceProviderName,
    string SourceEntityType,
    string? SourceEntityId,
    string? SourcePeriod,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    IReadOnlyList<string> SuggestedActions);

public sealed record InsightEvidenceItemHttpResponse(
    string Label,
    string Value,
    string SourceProvider,
    string? SourcePeriod,
    DateTimeOffset? LastSyncedAtUtc);

public sealed record GenerateMarketInsightsHttpRequest(int LookbackDays = 7);

public sealed record GenerateMarketInsightsHttpResponse(
    int DetectorsRun,
    int EventsDetected,
    int EventsPersisted,
    DateTimeOffset GeneratedAtUtc);
