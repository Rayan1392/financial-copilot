using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class BillingMaintenanceOptions
{
    public const string SectionName = "BillingMaintenance";

    public int IntervalSeconds { get; init; } = 60;

    public int BatchSize { get; init; } = 100;
}

public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    IOptions<BillingMaintenanceOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var maintenance = scope.ServiceProvider.GetRequiredService<IBillingMaintenanceService>();
                var result = await maintenance.ProcessAsync(settings.BatchSize, stoppingToken);

                if (result.ExpiredReservationCount > 0 || result.DispatchedOutboxMessageCount > 0)
                {
                    logger.LogInformation(
                        "Billing maintenance expired {ExpiredReservationCount} reservations and dispatched {DispatchedOutboxMessageCount} outbox messages.",
                        result.ExpiredReservationCount,
                        result.DispatchedOutboxMessageCount);
                }

                if (!result.OutboxDispatcherConfigured)
                {
                    logger.LogDebug(
                        "Billing outbox dispatch is inactive because no transport dispatcher is configured.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Billing maintenance processing failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
