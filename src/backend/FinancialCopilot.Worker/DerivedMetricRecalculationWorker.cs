using FinancialCopilot.Application.FinancialData.Metrics;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class DerivedMetricRecalculationOptions
{
    public const string SectionName = "DerivedMetricRecalculation";

    public int IntervalSeconds { get; init; } = 60;

    public int BatchSize { get; init; } = 100;
}

/// <summary>
/// Background worker that drains the <c>MetricRecalculationRequests</c> outbox at a configurable
/// interval. Mirrors the Billing <c>Worker</c>/<c>BillingOutboxProcessor</c>/<c>PeriodicTimer</c>
/// pattern. Provider-agnostic — benefits every ingestion source.
/// </summary>
public sealed class DerivedMetricRecalculationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DerivedMetricRecalculationOptions> options,
    ILogger<DerivedMetricRecalculationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IMetricRecalculationProcessor>();
                var result = await processor.ProcessPendingAsync(settings.BatchSize, stoppingToken);

                if (result.ProcessedRequestCount > 0)
                {
                    logger.LogInformation(
                        "Derived-metric recalculation drained {Processed} requests ({Completed} ok, {Failed} failed); recomputed {Metrics} metrics.",
                        result.ProcessedRequestCount,
                        result.CompletedRequestCount,
                        result.FailedRequestCount,
                        result.MetricsRecomputed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Derived-metric recalculation drain failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
