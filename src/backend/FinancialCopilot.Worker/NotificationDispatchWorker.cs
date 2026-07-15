using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class NotificationDispatchWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationDispatcherOptions> options,
    ILogger<NotificationDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Notification dispatcher is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                var result = await dispatcher.DispatchDueAsync(settings.BatchSize, stoppingToken);
                if (result.Claimed > 0)
                    logger.LogInformation(
                        "Notification dispatch claimed {Claimed}: delivered {Delivered}, deferred {Deferred}, batched {Batched}, suppressed {Suppressed}, retried {Retried}, dead-lettered {DeadLettered}, failed {Failed}.",
                        result.Claimed, result.Delivered, result.Deferred, result.Batched,
                        result.Suppressed, result.Retried, result.DeadLettered, result.Failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification dispatch iteration failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
