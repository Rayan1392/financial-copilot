namespace FinancialCopilot.API.Contracts;

public sealed record AlertHistoryResponse(
    IReadOnlyCollection<UserAlertRecordResponse> Items,
    string? NextCursor,
    int PageSize,
    bool HasMore,
    string RetentionPolicy);

public sealed record UserAlertRecordResponse(
    Guid Id,
    string SymbolKey,
    string EventType,
    string Category,
    string Severity,
    string DeliveryStatus,
    string DeliveryReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? DismissedAtUtc,
    DateTimeOffset? MutedAtUtc,
    string WhyText,
    string EvidenceHash,
    string CorrelationId);

public sealed record UserAlertDetailResponse(
    UserAlertRecordResponse Record,
    Guid? SourceEventId,
    Guid? AlertRuleId,
    Guid NotificationIntentId,
    string? EvidenceReference,
    string EvidenceSnapshotJson,
    string DetectorVersion,
    int? RuleVersion,
    int? PreferenceVersion,
    string PolicyVersion,
    IReadOnlyCollection<AlertDeliveryTimelineResponse> DeliveryTimeline,
    IReadOnlyCollection<AlertReactionResponse> Reactions,
    IReadOnlyCollection<AlertSimilarEventResponse> SimilarEvents,
    string RetentionPolicy);

public sealed record AlertWhyResponse(
    Guid AlertId,
    string WhyText,
    string EvidenceHash,
    string EvidenceSnapshotJson,
    string Methodology);

public sealed record AlertDeliveryTimelineResponse(
    DateTimeOffset OccurredAtUtc,
    string Status,
    string Reason,
    int? AttemptNumber,
    string? ProviderMessageId,
    string? ErrorCode);

public sealed record AlertReactionResponse(
    string HorizonCode,
    string Status,
    string CalculationVersion,
    decimal? AnchorPrice,
    DateTimeOffset? AnchorAtUtc,
    decimal? ReactionPercent,
    string Reason,
    DateTimeOffset? CalculatedAtUtc);

public sealed record AlertSimilarEventResponse(
    Guid AlertId,
    string SymbolKey,
    string EventType,
    string Category,
    DateTimeOffset CreatedAtUtc,
    string Methodology);

public sealed record AlertFeedbackRequest(string Feedback);
public sealed record AlertMuteRequest(string Scope, bool Confirmed);
public sealed record AlertReactionRefreshRequest(string? HorizonCode = null);
