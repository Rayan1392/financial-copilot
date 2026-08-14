using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class CyclicalWavesDataAcquisitionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CyclicalWavesDataAcquisitionOptions> options,
    CyclicalWavesTokenCache tokenCache,
    TimeProvider timeProvider,
    ILogger<CyclicalWavesDataAcquisitionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("CyclicalWaves data acquisition is disabled.");
            return;
        }

        await tokenCache.ValidateAvailabilityAsync(stoppingToken);
        var schedule = CyclicalWavesUtcCronSchedule.Parse(settings.Schedule);
        await RunCycleAsync(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var next = schedule.GetNextOccurrence(now);
            await Task.Delay(next - now, timeProvider, stoppingToken);
            await RunCycleAsync(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime), stoppingToken);
        }
    }

    private async Task RunCycleAsync(DateOnly cycleDateUtc, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "CyclicalWaves data acquisition cycle starting. CycleDateUtc={CycleDateUtc}",
            cycleDateUtc);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ICyclicalWavesDataAcquisitionService>();
            var summary = await service.ExecuteAsync(cycleDateUtc, cancellationToken);

            logger.LogInformation(
                "CyclicalWaves data acquisition cycle completed. CycleDateUtc={CycleDateUtc} " +
                "Changed={Changed} Unchanged={Unchanged} Failed={Failed} Skipped={Skipped}",
                summary.CycleDateUtc,
                summary.Changed,
                summary.Unchanged,
                summary.Failed,
                summary.Skipped);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "CyclicalWaves data acquisition cycle failed. CycleDateUtc={CycleDateUtc}",
                cycleDateUtc);
        }
    }
}
