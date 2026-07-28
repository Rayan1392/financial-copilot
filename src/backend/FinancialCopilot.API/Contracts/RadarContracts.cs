namespace FinancialCopilot.API.Contracts;

public sealed record UpdateRadarPreferencesRequest(
    int ExpectedVersion,
    IReadOnlyCollection<string>? EventTypes,
    string? MinimumSeverity,
    decimal MinimumImportance,
    string? Sensitivity,
    string? DeliveryMode,
    string? State);

public sealed record UpdateRadarSymbolOverrideRequest(
    int? ExpectedVersion,
    string? State,
    IReadOnlyCollection<string>? EventTypes,
    string? MinimumSeverity,
    decimal? MinimumImportance,
    string? Sensitivity);

public sealed record RadarTestNotificationRequest(string? IdempotencyKey, string? CorrelationId);

public sealed record RadarStateChangeRequest(int ExpectedVersion);

public sealed record RadarSymbolOverrideResponse(
    Guid Id,
    string ExternalCompanyId,
    string Symbol,
    string State,
    IReadOnlyCollection<string>? EventTypes,
    string? MinimumSeverity,
    decimal? MinimumImportance,
    string? Sensitivity,
    int Version,
    DateTimeOffset UpdatedAtUtc);

public sealed record RadarProfileResponse(
    Guid Id,
    string State,
    IReadOnlyCollection<string> EventTypes,
    string MinimumSeverity,
    decimal MinimumImportance,
    string Sensitivity,
    string DeliveryMode,
    int Version,
    IReadOnlyCollection<RadarSymbolOverrideResponse> SymbolOverrides,
    int EvaluationCadenceSeconds,
    DateTimeOffset? LastEvaluatedAtUtc,
    DateTimeOffset? LastSourceFreshnessUtc,
    string FreshnessDisclosure,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record RadarTestNotificationResponse(Guid NotificationIntentId, bool Informational, bool Billable);
