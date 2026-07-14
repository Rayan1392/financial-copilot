using FinancialCopilot.Application.FinancialData.Insights;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class MarketMicrostructureDetectionWorkerOptions
{
    public const string SectionName = "MarketMicrostructureDetection";
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    public int LookbackDays { get; set; } = 2;
    public int RetryCount { get; set; } = 3;
}

/// <summary>
/// Scheduled trigger for the idempotent InsightEvent pipeline. The use case owns detector
/// isolation and deduplication; this host owns cadence and bounded transient retries only.
/// </summary>
public sealed class MarketMicrostructureDetectionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MarketMicrostructureDetectionWorkerOptions> options,
    ILogger<MarketMicrostructureDetectionWorker> logger) : BackgroundService
{
    private readonly MarketMicrostructureDetectionWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Market microstructure detection is disabled.");
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
                var useCase = scope.ServiceProvider.GetRequiredService<IGenerateMarketMicrostructureInsightsUseCase>();
                var result = await useCase.ExecuteAsync(new GenerateMarketInsightsRequest(_options.LookbackDays), cancellationToken);
                if (result.EventsDetected > 0)
                    logger.LogInformation(
                        "Scheduled insight detection ran {DetectorCount} detectors, detected {DetectedCount}, persisted {PersistedCount}.",
                        result.DetectorsRun, result.EventsDetected, result.EventsPersisted);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (attempt < _options.RetryCount)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, 1 << attempt));
                logger.LogWarning(exception,
                    "Scheduled market microstructure detection attempt {Attempt}/{RetryCount} failed; retrying in {DelaySeconds} seconds.",
                    attempt, _options.RetryCount, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Scheduled market microstructure detection exhausted {RetryCount} attempts; the next cadence will retry the idempotent batch.",
                    _options.RetryCount);
            }
        }
    }
}
