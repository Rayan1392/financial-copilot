using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

/// <summary>Configuration-gated scheduler. It does not contain provider or persistence logic.</summary>
public sealed class CyclicalWavesPsVisualizationSyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CyclicalWavesPsSyncOptions> options,
    ILogger<CyclicalWavesPsVisualizationSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("CyclicalWaves P/S visualization synchronization is disabled.");
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.SnapshotCadenceMinutes));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ICyclicalWavesPsVisualizationSyncService>();
                await service.SyncAsync(new PsVisualizationSyncRequest(CorrelationId: $"worker-{Guid.NewGuid():N}"), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "CyclicalWaves P/S visualization synchronization failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
