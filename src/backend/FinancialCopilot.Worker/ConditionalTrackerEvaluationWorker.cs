using FinancialCopilot.Application.FinancialData.ConditionalTrackers;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class ConditionalTrackerEvaluationOptions
{
    public const string SectionName = "ConditionalTrackerEvaluation";
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 100;
}

public sealed class ConditionalTrackerEvaluationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ConditionalTrackerEvaluationOptions> options,
    ILogger<ConditionalTrackerEvaluationWorker> logger) : BackgroundService
{
    private readonly ConditionalTrackerEvaluationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Conditional tracker evaluation is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervalSeconds));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IConditionalTrackerEvaluationProcessor>();
                var result = await processor.EvaluateDueAsync(_options.BatchSize, stoppingToken);
                if (result.Triggered > 0 || result.Failed > 0)
                    logger.LogInformation(
                        "Conditional tracker batch considered {Considered}, triggered {Triggered}, skipped {Skipped}, failed {Failed}.",
                        result.Considered, result.Triggered, result.Skipped, result.Failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Conditional tracker evaluation batch failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
