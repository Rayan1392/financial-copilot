using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class IndustryRelativeValuationCalculationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IndustryRelativeValuationOptions> options,
    TimeProvider timeProvider,
    ILogger<IndustryRelativeValuationCalculationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Industry-relative valuation calculation is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await ExecuteTickAsync(stoppingToken);
            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(options.Value.DailyCadenceMinutes),
                    timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !options.Value.Enabled)
            return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var orchestration = scope.ServiceProvider
                .GetRequiredService<IIndustryRelativeValuationOrchestrationService>();
            var now = timeProvider.GetUtcNow();
            await orchestration.RunAsync($"industry-relative-valuation-{now:yyyyMMddHHmmss}", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Industry-relative valuation calculation tick failed.");
        }
    }
}
