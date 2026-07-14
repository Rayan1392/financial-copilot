using FinancialCopilot.Domain.Financial.Insights;

namespace FinancialCopilot.Application.Notifications;

public enum NotificationChannel
{
    Telegram
}

public enum NotificationIntentStatus
{
    Pending,
    Suppressed
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
    string CorrelationId);

public sealed record NotificationIntentDto(
    Guid Id,
    NotificationActor Actor,
    NotificationChannel Channel,
    string EventType,
    string EntityKey,
    string DeduplicationKey,
    InsightSeverity Severity,
    NotificationIntentStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset? ExpiresAtUtc);

public interface INotificationIntentPublisher
{
    Task<NotificationIntentDto> EnqueueAsync(
        NotificationIntentRequest request,
        CancellationToken cancellationToken);
}
