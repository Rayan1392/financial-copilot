using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Domain.Notifications;

namespace FinancialCopilot.Application.Notifications;

public enum NotificationChannel
{
    Telegram
}

public sealed record NotificationActor(
    Guid TenantId,
    Guid ActorId,
    string ActorType);

public sealed record NotificationIntentRequest(
    NotificationActor Actor,
    NotificationChannel Channel,
    string EventType,
    string EntityKey,
    string DeduplicationKey,
    InsightSeverity Severity,
    string PayloadJson,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset? ExpiresAtUtc,
    string CorrelationId,
    Guid? SourceEventId = null,
    string? EvidenceReference = null,
    string? Category = null,
    string? CooldownKey = null);

public sealed record NotificationIntentDto(
    Guid Id,
    NotificationActor Actor,
    NotificationChannel Channel,
    string EventType,
    string EntityKey,
    string DeduplicationKey,
    InsightSeverity Severity,
    NotificationIntentState Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? DecisionReason = null,
    DateTimeOffset? DeliveredAtUtc = null);

public interface INotificationIntentPublisher
{
    Task<NotificationIntentDto> EnqueueAsync(
        NotificationIntentRequest request,
        CancellationToken cancellationToken);
}

public sealed record NotificationCategoryPreferenceInput(
    string EventType,
    bool Enabled,
    InsightSeverity? MinimumSeverity = null,
    int? CooldownMinutes = null);

public sealed record NotificationSymbolPreferenceInput(
    string ExternalCompanyId,
    bool Muted);

public sealed record NotificationPreferenceInput(
    string TimeZoneId,
    NotificationDeliveryMode DeliveryMode,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    InsightSeverity MinimumSeverity,
    int DailyCap,
    TimeOnly DigestTime,
    int CooldownMinutes,
    IReadOnlyCollection<NotificationCategoryPreferenceInput> Categories,
    IReadOnlyCollection<NotificationSymbolPreferenceInput> Symbols);

public sealed record NotificationCategoryPreferenceDto(
    string EventType,
    bool Enabled,
    InsightSeverity? MinimumSeverity,
    int? CooldownMinutes);

public sealed record NotificationSymbolPreferenceDto(
    string ExternalCompanyId,
    bool Muted);

public sealed record NotificationPreferenceDto(
    Guid Id,
    string TimeZoneId,
    NotificationDeliveryMode DeliveryMode,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    InsightSeverity MinimumSeverity,
    int DailyCap,
    TimeOnly DigestTime,
    int CooldownMinutes,
    int Version,
    IReadOnlyCollection<NotificationCategoryPreferenceDto> Categories,
    IReadOnlyCollection<NotificationSymbolPreferenceDto> Symbols,
    string PolicyVersion,
    string EffectivePolicyExplanation,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationHistoryItemDto(
    Guid Id,
    string EventType,
    string EntityKey,
    InsightSeverity Severity,
    NotificationIntentState Status,
    NotificationSuppressionReason SuppressionReason,
    string? EvidenceReference,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    string? LastErrorCode,
    int AttemptCount,
    string CorrelationId);

public sealed record NotificationDeadLetterDto(
    Guid Id,
    string EventType,
    string EntityKey,
    int AttemptCount,
    string? LastErrorCode,
    DateTimeOffset? DeadLetteredAtUtc,
    string CorrelationId);

public sealed record NotificationHistoryPage(
    IReadOnlyCollection<NotificationHistoryItemDto> Items,
    int Offset,
    int PageSize,
    bool HasMore);

public sealed record UpdateNotificationPreferenceCommand(
    CurrentActor Actor,
    int ExpectedVersion,
    NotificationPreferenceInput Input,
    string Source,
    string CorrelationId);

public interface INotificationUseCases
{
    Task<NotificationPreferenceDto> GetPreferencesAsync(CurrentActor actor, CancellationToken cancellationToken);
    Task<NotificationPreferenceDto> UpdatePreferencesAsync(
        UpdateNotificationPreferenceCommand command,
        CancellationToken cancellationToken);
    Task<NotificationHistoryPage> GetHistoryAsync(
        CurrentActor actor,
        int offset,
        int pageSize,
        CancellationToken cancellationToken);
}

public interface INotificationEntitlementPolicy
{
    Task ValidateManageAsync(CurrentActor actor, CancellationToken cancellationToken);
    Task<bool> CanDeliverAsync(NotificationActor actor, CancellationToken cancellationToken);
}

public sealed record TelegramNotificationRecipient(long ChatId);

public interface INotificationRecipientResolver
{
    Task<TelegramNotificationRecipient?> ResolveTelegramAsync(
        NotificationActor actor,
        CancellationToken cancellationToken);
}

public enum NotificationTransportOutcome
{
    Delivered,
    RetryableFailure,
    PermanentFailure
}

public sealed record NotificationTransportResult(
    NotificationTransportOutcome Outcome,
    string? ProviderMessageId,
    string? ErrorCode,
    string? RedactedError,
    TimeSpan? RetryAfter = null);

public interface ITelegramNotificationTransport
{
    Task<NotificationTransportResult> SendAsync(
        long chatId,
        string text,
        string deliveryPartIdempotencyKey,
        CancellationToken cancellationToken);
}

public sealed record NotificationDispatchBatchResult(
    int Claimed,
    int Delivered,
    int Deferred,
    int Batched,
    int Suppressed,
    int Expired,
    int Retried,
    int DeadLettered,
    int Failed);

public interface INotificationDispatcher
{
    Task<NotificationDispatchBatchResult> DispatchDueAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

public interface INotificationOperations
{
    Task<IReadOnlyCollection<NotificationDeadLetterDto>> GetDeadLettersAsync(
        int maximumCount,
        CancellationToken cancellationToken);
    Task RetryDeadLetterAsync(
        Guid notificationIntentId,
        Guid operatorActorId,
        Guid operatorTenantId,
        string correlationId,
        CancellationToken cancellationToken);
}
