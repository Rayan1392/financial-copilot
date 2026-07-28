using FinancialCopilot.Application.FinancialData.Insights;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class MarketInsightGenerationWorkerOptions
{
    public const string SectionName = "MarketInsightGeneration";
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 300;
    public int LookbackDays { get; set; } = 7;
    public int RetryCount { get; set; } = 3;
}

/// <summary>
/// Runs the complete insight detector pipeline so the market and followed-symbol feeds are
/// populated during normal operation, without requiring a data-admin request.
/// </summary>
public sealed class MarketInsightGenerationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MarketInsightGenerationWorkerOptions> options,
    ILogger<MarketInsightGenerationWorker> logger) : BackgroundService
{
    private readonly MarketInsightGenerationWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Market insight generation is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervalSeconds));
        do
        {
            await RunWithRetryAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunWithRetryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _options.RetryCount; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var useCase = scope.ServiceProvider.GetRequiredService<IGenerateMarketInsightsUseCase>();
                var result = await useCase.ExecuteAsync(
                    new GenerateMarketInsightsRequest(_options.LookbackDays),
                    cancellationToken);

                logger.LogInformation(
                    "Scheduled insight generation ran {DetectorCount} detectors, detected {DetectedCount}, persisted {PersistedCount}.",
                    result.DetectorsRun,
                    result.EventsDetected,
                    result.EventsPersisted);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (attempt < _options.RetryCount)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, 1 << attempt));
                logger.LogWarning(
                    exception,
                    "Scheduled insight generation attempt {Attempt}/{RetryCount} failed; retrying in {DelaySeconds} seconds.",
                    attempt,
                    _options.RetryCount,
                    delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Scheduled insight generation exhausted {RetryCount} attempts; the next cadence will retry the idempotent batch.",
                    _options.RetryCount);
            }
        }
    }
}
