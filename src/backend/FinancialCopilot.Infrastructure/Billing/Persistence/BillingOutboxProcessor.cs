using FinancialCopilot.Billing.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class BillingOutboxProcessor(
    BillingDbContext dbContext,
    IBillingOutboxDispatcher dispatcher,
    TimeProvider timeProvider) : IBillingOutboxProcessor
{
    public async Task<int> ProcessPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var pending = await dbContext.OutboxMessages
            .Where(row => row.ProcessedAt == null)
            .OrderBy(row => row.OccurredAt)
            .ThenBy(row => row.Id)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);
        var completedCount = 0;

        foreach (var row in pending)
        {
            var attemptedAt = timeProvider.GetUtcNow();

            try
            {
                await dispatcher.DispatchAsync(Map(row), cancellationToken);
                row.ProcessedAt = attemptedAt;
                row.LastError = null;
                completedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                row.LastError = LimitError(exception.Message);
            }

            row.AttemptCount++;
            row.LastAttemptAt = attemptedAt;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return completedCount;
    }

    private static string LimitError(string message) =>
        message.Length <= 1000 ? message : message[..1000];

    private static BillingOutboxMessage Map(BillingOutboxMessageRow row) =>
        new(
            row.Id,
            row.AggregateType,
            row.AggregateId,
            row.EventType,
            row.IdempotencyKey,
            row.Payload,
            row.OccurredAt,
            row.AttemptCount);
}
