using System.Text.Json;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

internal static class BillingOutboxWriter
{
    public static void Add<TPayload>(
        BillingDbContext dbContext,
        string aggregateType,
        Guid aggregateId,
        string eventType,
        string idempotencyKey,
        TPayload payload,
        DateTimeOffset occurredAt)
    {
        dbContext.OutboxMessages.Add(new BillingOutboxMessageRow
        {
            Id = Guid.NewGuid(),
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = eventType,
            IdempotencyKey = idempotencyKey,
            Payload = JsonSerializer.Serialize(payload),
            OccurredAt = occurredAt
        });
    }
}
