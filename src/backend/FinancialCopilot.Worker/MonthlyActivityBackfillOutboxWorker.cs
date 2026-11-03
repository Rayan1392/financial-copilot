using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class MonthlyActivityBackfillOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MonthlyActivityBackfillOutboxOptions> options,
    ILogger<MonthlyActivityBackfillOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var relayed = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var relay = scope.ServiceProvider.GetRequiredService<IMonthlyActivityBackfillOutboxRelay>();
                await relay.ReconcileActiveBatchesAsync(stoppingToken);
                relayed = await relay.RelayPendingAsync(options.Value.BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Monthly-activity outbox worker cycle failed.");
            }

            if (relayed == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken);
            }
        }
    }
}
