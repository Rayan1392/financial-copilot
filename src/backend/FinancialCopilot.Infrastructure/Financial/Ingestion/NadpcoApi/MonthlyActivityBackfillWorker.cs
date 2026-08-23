using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class MonthlyActivityBackfillWorker(
    MonthlyActivityBackfillQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<MonthlyActivityBackfillWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            MonthlyActivityBackfillRequest request;
            try
            {
                request = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var coordinator = scope.ServiceProvider
                    .GetRequiredService<IMonthlyActivityBackfillCoordinator>();
                await coordinator.StartAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Monthly-activity backfill background execution failed.");
            }
            finally
            {
                queue.MarkFinished();
            }
        }
    }
}
