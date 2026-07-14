using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Radar;

namespace FinancialCopilot.Application.FinancialData.Radar;

public sealed record RadarProfileInput(
    IReadOnlyCollection<InsightType> EventTypes,
    InsightSeverity MinimumSeverity,
    decimal MinimumImportance,
    RadarSensitivity Sensitivity,
    RadarDeliveryMode DeliveryMode,
    RadarState State);

public sealed record RadarSymbolOverrideInput(
    RadarState State,
    IReadOnlyCollection<InsightType>? EventTypes,
    InsightSeverity? MinimumSeverity,
    decimal? MinimumImportance,
    RadarSensitivity? Sensitivity);

public sealed record GetMyRadarQuery(CurrentActor Actor);
public sealed record UpdateMyRadarCommand(CurrentActor Actor, int ExpectedVersion, RadarProfileInput Input, string Source = "Api");
public sealed record RemoveMyRadarCommand(CurrentActor Actor, int ExpectedVersion, string Source = "Api");
public sealed record UpsertRadarSymbolOverrideCommand(
    CurrentActor Actor, string ExternalCompanyId, int? ExpectedVersion, RadarSymbolOverrideInput Input, string Source = "Api");
public sealed record RemoveRadarSymbolOverrideCommand(
    CurrentActor Actor, string ExternalCompanyId, int ExpectedVersion, string Source = "Api");
public sealed record SendRadarTestNotificationCommand(CurrentActor Actor, string IdempotencyKey, string CorrelationId);

public sealed record RadarSymbolOverrideDto(
    Guid Id,
    string ExternalCompanyId,
    string Symbol,
    RadarState State,
    IReadOnlyCollection<InsightType>? EventTypes,
    InsightSeverity? MinimumSeverity,
    decimal? MinimumImportance,
    RadarSensitivity? Sensitivity,
    int Version,
    DateTimeOffset UpdatedAtUtc);

public sealed record RadarProfileDto(
    Guid Id,
    RadarState State,
    IReadOnlyCollection<InsightType> EventTypes,
    InsightSeverity MinimumSeverity,
    decimal MinimumImportance,
    RadarSensitivity Sensitivity,
    RadarDeliveryMode DeliveryMode,
    int Version,
    IReadOnlyCollection<RadarSymbolOverrideDto> SymbolOverrides,
    int EvaluationCadenceSeconds,
    DateTimeOffset? LastEvaluatedAtUtc,
    DateTimeOffset? LastSourceFreshnessUtc,
    string FreshnessDisclosure,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record RadarProfileSnapshot(
    RadarProfile Profile,
    IReadOnlyCollection<RadarSymbolOverride> SymbolOverrides,
    DateTimeOffset? LastEvaluatedAtUtc = null,
    DateTimeOffset? LastSourceFreshnessUtc = null);

public sealed record RadarNotificationGateDecision(
    bool Allowed,
    RadarSuppressionReason SuppressionReason,
    DateTimeOffset NotBeforeUtc,
    string PolicyVersion);

public interface IRadarRepository
{
    Task<RadarProfileSnapshot?> FindAsync(RadarActor actor, bool includeRemoved, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<RadarProfileSnapshot>> GetActiveAsync(int maximumCount, CancellationToken cancellationToken);
    Task SaveProfileAsync(RadarProfile profile, string auditAction, string source, CancellationToken cancellationToken);
    Task SaveOverrideAsync(RadarProfile profile, RadarSymbolOverride symbolOverride, string auditAction, string source, CancellationToken cancellationToken);
}

public interface IRadarEntitlementPolicy
{
    Task ValidateManageAsync(CurrentActor actor, int followedSymbolCount, CancellationToken cancellationToken);
    Task<bool> CanEvaluateAsync(RadarActor actor, int followedSymbolCount, CancellationToken cancellationToken);
}

/// <summary>Feature 097 implements global mute, quiet-hours, caps and delivery-channel precedence.</summary>
public interface IRadarNotificationPolicyGate
{
    Task<RadarNotificationGateDecision> EvaluateAsync(
        RadarActor actor,
        InsightSeverity severity,
        RadarDeliveryMode deliveryMode,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IRadarUseCases
{
    Task<RadarProfileDto> GetAsync(GetMyRadarQuery query, CancellationToken cancellationToken);
    Task<RadarProfileDto> UpdateAsync(UpdateMyRadarCommand command, CancellationToken cancellationToken);
    Task RemoveAsync(RemoveMyRadarCommand command, CancellationToken cancellationToken);
    Task<RadarProfileDto> UpsertOverrideAsync(UpsertRadarSymbolOverrideCommand command, CancellationToken cancellationToken);
    Task<RadarProfileDto> RemoveOverrideAsync(RemoveRadarSymbolOverrideCommand command, CancellationToken cancellationToken);
    Task<Guid> SendTestNotificationAsync(SendRadarTestNotificationCommand command, CancellationToken cancellationToken);
}

public interface IRadarEvaluationProcessor
{
    Task<RadarEvaluationBatchResult> EvaluateAsync(int maximumProfiles, CancellationToken cancellationToken);
}

public sealed record RadarEvaluationBatchResult(
    int ProfilesConsidered,
    int EventsConsidered,
    int Matched,
    int Suppressed,
    int NotificationIntents,
    int CompositeMatches,
    int Failed);
