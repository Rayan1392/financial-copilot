using FinancialCopilot.Application.FinancialData.Radar;
using FinancialCopilot.Infrastructure.Financial.Radar;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class RadarEvaluationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RadarOptions> options,
    ILogger<RadarEvaluationWorker> logger) : BackgroundService
{
    private readonly RadarOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Personal market radar evaluation is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.EvaluationCadenceSeconds));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IRadarEvaluationProcessor>();
                var result = await processor.EvaluateAsync(_options.BatchSize, stoppingToken);
                if (result.Matched > 0 || result.Failed > 0)
                    logger.LogInformation(
                        "Radar batch considered {Profiles} profiles and {Events} events; matched {Matched}, suppressed {Suppressed}, handed off {Intents}, failed {Failed}.",
                        result.ProfilesConsidered, result.EventsConsidered, result.Matched, result.Suppressed,
                        result.NotificationIntents, result.Failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Personal market radar evaluation batch failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
