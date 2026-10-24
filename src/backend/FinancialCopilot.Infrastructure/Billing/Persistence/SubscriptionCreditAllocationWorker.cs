using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

/// <summary>
/// Closes due 30-day subscription credit periods and creates only the current cycle's allowance.
/// Database idempotency keys and wallet revisions fence overlapping worker instances.
/// </summary>
public sealed class SubscriptionCreditAllocationWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<SubscriptionCreditAllocationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 250;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var lifecycle = scope.ServiceProvider.GetRequiredService<SubscriptionCreditLifecycleService>();
                var count = await lifecycle.ProcessDuePeriodsAsync(
                    BatchSize,
                    timeProvider.GetUtcNow(),
                    stoppingToken);
                if (count > 0)
                {
                    logger.LogInformation("Processed {PeriodCount} due SapioAI subscription credit periods.", count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "SapioAI subscription credit period processing failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
