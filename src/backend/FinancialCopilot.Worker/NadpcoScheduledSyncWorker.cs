using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class NadpcoScheduledSyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NadpcoScheduledSyncOptions> options,
    ILogger<NadpcoScheduledSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.Value;
            var delay = TimeSpan.FromSeconds(Math.Max(1, settings.CadenceSeconds));

            try
            {
                if (settings.Enabled)
                {
                    using var scope = scopeFactory.CreateScope();
                    var coordinator = scope.ServiceProvider.GetRequiredService<INadpcoScheduledSyncCoordinator>();
                    var status = await coordinator.GetStatusAsync(recentRunLimit: 1, stoppingToken);
                    var now = DateTimeOffset.UtcNow;
                    var missed = status.NextDueAt is not null && status.NextDueAt < now.Subtract(delay);
                    var trigger = missed
                        ? NadpcoScheduledSyncTriggerSource.MissedRecovery
                        : NadpcoScheduledSyncTriggerSource.Automatic;

                    if (status.NextDueAt is null || status.NextDueAt <= now || trigger == NadpcoScheduledSyncTriggerSource.MissedRecovery)
                    {
                        var run = await coordinator.RunAsync(
                            new NadpcoScheduledSyncRunRequest(trigger),
                            stoppingToken);
                        logger.LogInformation(
                            "NADPCO scheduled sync completed runId={RunId} status={Status}.",
                            run.RunId,
                            run.Status);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "NADPCO scheduled sync worker tick failed.");
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
