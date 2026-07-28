using FinancialCopilot.Billing.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed record BillingMaintenanceResult(
    int ExpiredReservationCount,
    int DispatchedOutboxMessageCount,
    bool OutboxDispatcherConfigured);

public interface IBillingMaintenanceService
{
    Task<BillingMaintenanceResult> ProcessAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

public sealed class BillingMaintenanceService(
    ICreditReservationService reservations,
    IServiceProvider services) : IBillingMaintenanceService
{
    public async Task<BillingMaintenanceResult> ProcessAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var expired = await reservations.ExpireAbandonedAsync(maximumCount, cancellationToken);
        var dispatcherConfigured = services.GetService<IBillingOutboxDispatcher>() is not null;

        if (!dispatcherConfigured)
        {
            return new BillingMaintenanceResult(expired, 0, OutboxDispatcherConfigured: false);
        }

        var outbox = services.GetRequiredService<IBillingOutboxProcessor>();
        var dispatched = await outbox.ProcessPendingAsync(maximumCount, cancellationToken);

        return new BillingMaintenanceResult(expired, dispatched, OutboxDispatcherConfigured: true);
    }
}
