using System.Diagnostics;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class CyclicalWavesRelativeValuationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RelativeValuationIngestionOptions> options,
    Feature126WorkerHealth health,
    ILogger<CyclicalWavesRelativeValuationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        health.Configure(options.Value.ConfigurationRevision,
            !string.IsNullOrWhiteSpace(options.Value.ConfigurationRevision) &&
            !string.IsNullOrWhiteSpace(options.Value.DeploymentIdentifier));
        while (!stoppingToken.IsCancellationRequested)
        {
            var cadence = TimeSpan.FromMinutes(Math.Max(1, options.Value.DailyCadenceMinutes));
            try
            {
                await ExecuteTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Feature126TelemetryUnavailableException ex)
            {
                health.MarkTelemetryUnavailable();
                logger.LogError(ex, "Feature 126 durable telemetry is unavailable; ingestion stopped.");
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("lease was lost", StringComparison.OrdinalIgnoreCase)) health.MarkLeaseLost();
                logger.LogError(ex, "Feature 126 daily ingestion tick failed.");
            }
            try { await Task.Delay(cadence, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
        health.MarkStopping();
    }

    public async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;
        if (!options.Value.Enabled)
        {
            health.MarkDisabled();
            return;
        }

        health.MarkRunAttempt();
        using var scope = scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IFeature126RelativeValuationPipeline>();
        var providerStarted = Stopwatch.GetTimestamp();
        var result = await pipeline.RunAsync(null, cancellationToken);
        health.RecordProviderLatency(Stopwatch.GetElapsedTime(providerStarted).TotalMilliseconds);
        health.RecordRun(result);
    }
}
