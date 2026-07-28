using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Notifications;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Notifications;

public sealed class NotificationOperations(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider) : INotificationOperations
{
    public async Task<IReadOnlyCollection<NotificationDeadLetterDto>> GetDeadLettersAsync(
        int maximumCount,
        CancellationToken cancellationToken) =>
        await dbContext.NotificationIntents.AsNoTracking()
            .Where(row => row.Status == NotificationIntentState.DeadLettered.ToString())
            .OrderByDescending(row => row.DeadLetteredAtUtc).Take(Math.Clamp(maximumCount, 1, 100))
            .Select(row => new NotificationDeadLetterDto(row.Id, row.EventType, row.EntityKey,
                row.AttemptCount, row.LastErrorCode, row.DeadLetteredAtUtc,
                row.CorrelationId ?? row.Id.ToString("N"))).ToArrayAsync(cancellationToken);

    public async Task RetryDeadLetterAsync(
        Guid notificationIntentId,
        Guid operatorActorId,
        Guid operatorTenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.NotificationIntents.SingleOrDefaultAsync(
            item => item.Id == notificationIntentId, cancellationToken)
            ?? throw new NotificationValidationException("Notification intent was not found.");
        var current = Enum.Parse<NotificationIntentState>(row.Status);
        NotificationIntentLifecycle.EnsureManualRetry(current);
        var now = timeProvider.GetUtcNow();
        row.Status = NotificationIntentState.Pending.ToString();
        row.NotBeforeUtc = now;
        row.NextAttemptAtUtc = null;
        row.LeaseToken = null;
        row.LeaseExpiresAtUtc = null;
        row.DeadLetteredAtUtc = null;
        row.DecisionReason = NotificationSuppressionReason.None.ToString();
        row.DecisionExplanation = "A DataAdmin operator requested a manual retry.";
        row.ConcurrencyToken = Guid.NewGuid();
        dbContext.NotificationOperationAudits.Add(new NotificationOperationAuditRow
        {
            Id = Guid.NewGuid(), NotificationIntentId = row.Id,
            OperatorActorId = operatorActorId, OperatorTenantId = operatorTenantId,
            Action = "ManualRetry", Detail = $"Previous error code: {row.LastErrorCode ?? "Unavailable"}.",
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? row.Id.ToString("N") : correlationId.Trim()[..Math.Min(correlationId.Trim().Length, 128)],
            OccurredAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
