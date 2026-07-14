using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Notifications;

public sealed class EfCoreNotificationIntentPublisher(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider) : INotificationIntentPublisher
{
    public async Task<NotificationIntentDto> EnqueueAsync(
        NotificationIntentRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.NotificationIntents
            .SingleOrDefaultAsync(row =>
                row.TenantId == request.Actor.TenantId &&
                row.ActorId == request.Actor.ActorId &&
                row.ActorType == request.Actor.ActorType &&
                row.Channel == request.Channel.ToString() &&
                row.DeduplicationKey == request.DeduplicationKey,
                cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        var now = timeProvider.GetUtcNow();
        var row = new NotificationIntentRow
        {
            Id = Guid.NewGuid(),
            TenantId = request.Actor.TenantId,
            ActorId = request.Actor.ActorId,
            ActorType = request.Actor.ActorType,
            Channel = request.Channel.ToString(),
            EventType = request.EventType,
            EntityKey = request.EntityKey,
            DeduplicationKey = request.DeduplicationKey,
            Severity = request.Severity.ToString(),
            Status = NotificationIntentStatus.Pending.ToString(),
            PayloadJson = request.PayloadJson,
            CreatedAtUtc = now,
            NotBeforeUtc = request.NotBeforeUtc,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CorrelationId = request.CorrelationId
        };
        dbContext.NotificationIntents.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    private static NotificationIntentDto Map(NotificationIntentRow row) =>
        new(
            row.Id,
            new NotificationActor(row.TenantId, row.ActorId, row.ActorType),
            Enum.Parse<NotificationChannel>(row.Channel),
            row.EventType,
            row.EntityKey,
            row.DeduplicationKey,
            Enum.Parse<InsightSeverity>(row.Severity),
            Enum.Parse<NotificationIntentStatus>(row.Status),
            row.CreatedAtUtc,
            row.NotBeforeUtc,
            row.ExpiresAtUtc);
}
