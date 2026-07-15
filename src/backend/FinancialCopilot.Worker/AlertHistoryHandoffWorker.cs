using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class AlertHistoryHandoffWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AlertHistoryOptions> options,
    ILogger<AlertHistoryHandoffWorker> logger) : BackgroundService
{
    private readonly AlertHistoryOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Alert history handoff worker is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervalSeconds));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IAlertOutcomeHandoffProcessor>();
                var result = await processor.ProcessPendingAsync(_options.HandoffBatchSize, stoppingToken);
                if (result.Created > 0 || result.Duplicates > 0 || result.Failed > 0)
                    logger.LogInformation(
                        "Alert history handoff batch considered {Considered}, created {Created}, duplicates {Duplicates}, failed {Failed}.",
                        result.Considered, result.Created, result.Duplicates, result.Failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Alert history handoff batch failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
