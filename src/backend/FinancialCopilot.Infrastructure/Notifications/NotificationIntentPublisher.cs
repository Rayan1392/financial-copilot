using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Notifications;
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
        Validate(request);
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
            Status = NotificationIntentState.Pending.ToString(),
            Category = Normalize(request.Category) ?? request.EventType.Trim(),
            PayloadJson = request.PayloadJson,
            SourceEventId = request.SourceEventId,
            EvidenceReference = Normalize(request.EvidenceReference),
            CooldownKey = Normalize(request.CooldownKey) ?? $"{request.EventType.Trim()}:{request.EntityKey.Trim()}",
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAtUtc = now,
            NotBeforeUtc = request.NotBeforeUtc,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CorrelationId = request.CorrelationId
        };
        dbContext.NotificationIntents.Add(row);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Map(row);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(row).State = EntityState.Detached;
            var concurrent = await dbContext.NotificationIntents.SingleOrDefaultAsync(candidate =>
                candidate.TenantId == request.Actor.TenantId &&
                candidate.ActorId == request.Actor.ActorId &&
                candidate.ActorType == request.Actor.ActorType &&
                candidate.Channel == request.Channel.ToString() &&
                candidate.DeduplicationKey == request.DeduplicationKey,
                cancellationToken);
            if (concurrent is null) throw;
            return Map(concurrent);
        }
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
            Enum.Parse<NotificationIntentState>(row.Status),
            row.CreatedAtUtc,
            row.NotBeforeUtc,
            row.ExpiresAtUtc,
            row.DecisionReason,
            row.DeliveredAtUtc);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Validate(NotificationIntentRequest request)
    {
        _ = new NotificationOwner(request.Actor.TenantId, request.Actor.ActorId, request.Actor.ActorType);
        if (string.IsNullOrWhiteSpace(request.EventType) || request.EventType.Trim().Length > 128)
            throw new NotificationValidationException("A bounded event type is required.");
        if (string.IsNullOrWhiteSpace(request.EntityKey) || request.EntityKey.Trim().Length > 256)
            throw new NotificationValidationException("A bounded entity key is required.");
        if (string.IsNullOrWhiteSpace(request.DeduplicationKey) || request.DeduplicationKey.Trim().Length > 512)
            throw new NotificationValidationException("A bounded producer deduplication key is required.");
        if (string.IsNullOrWhiteSpace(request.PayloadJson))
            throw new NotificationValidationException("An immutable notification payload is required.");
        if (request.ExpiresAtUtc is not null && request.ExpiresAtUtc <= request.NotBeforeUtc)
            throw new NotificationValidationException("Notification expiry must be later than its earliest delivery time.");
    }
}
