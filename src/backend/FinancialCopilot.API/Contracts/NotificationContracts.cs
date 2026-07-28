namespace FinancialCopilot.API.Contracts;

public sealed record NotificationCategoryPreferenceRequest(
    string EventType,
    bool Enabled,
    string? MinimumSeverity = null,
    int? CooldownMinutes = null);

public sealed record NotificationSymbolPreferenceRequest(
    string ExternalCompanyId,
    bool Muted);

public sealed record UpdateNotificationPreferencesRequest(
    int ExpectedVersion,
    string TimeZoneId,
    string DeliveryMode,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    string MinimumSeverity,
    int DailyCap,
    TimeOnly DigestTime,
    int CooldownMinutes,
    IReadOnlyCollection<NotificationCategoryPreferenceRequest>? Categories = null,
    IReadOnlyCollection<NotificationSymbolPreferenceRequest>? Symbols = null,
    string? CorrelationId = null);

public sealed record NotificationCategoryPreferenceResponse(
    string EventType,
    bool Enabled,
    string? MinimumSeverity,
    int? CooldownMinutes);

public sealed record NotificationSymbolPreferenceResponse(
    string ExternalCompanyId,
    bool Muted);

public sealed record NotificationPreferencesResponse(
    Guid Id,
    string TimeZoneId,
    string DeliveryMode,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    string MinimumSeverity,
    int DailyCap,
    TimeOnly DigestTime,
    int CooldownMinutes,
    int Version,
    IReadOnlyCollection<NotificationCategoryPreferenceResponse> Categories,
    IReadOnlyCollection<NotificationSymbolPreferenceResponse> Symbols,
    string PolicyVersion,
    string EffectivePolicyExplanation,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationHistoryItemResponse(
    Guid Id,
    string EventType,
    string EntityKey,
    string Severity,
    string Status,
    string SuppressionReason,
    string? EvidenceReference,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    string? LastErrorCode,
    int AttemptCount,
    string CorrelationId);

public sealed record NotificationHistoryResponse(
    IReadOnlyCollection<NotificationHistoryItemResponse> Items,
    int Offset,
    int PageSize,
    bool HasMore);
