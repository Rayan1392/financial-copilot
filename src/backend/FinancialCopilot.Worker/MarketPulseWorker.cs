using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Infrastructure.Financial.MarketViews;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class MarketPulseWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MarketViewOptions> options,
    ILogger<MarketPulseWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PulseEnabled) return;
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.PulseCadenceMinutes));
        using var timer = new PeriodicTimer(interval);
        do
        {
            foreach (var segment in options.Value.PulseSegments.Append("all").Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!await CaptureWithRetryAsync(segment, stoppingToken)) return;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<bool> CaptureWithRetryAsync(string segment, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var generator = scope.ServiceProvider.GetRequiredService<IMarketPulseSnapshotGenerator>();
                var snapshot = await generator.CaptureAsync(segment, cancellationToken);
                if (snapshot.SessionState == MarketPulseSessionState.Closed && !snapshot.IsFinal)
                    logger.LogWarning(
                        "Market pulse final snapshot is not ready for {TradingDate}/{Segment}; sourceWatermark={Watermark}.",
                        snapshot.TradingDate, snapshot.Segment, snapshot.SourceWatermarkUtc);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception) when (attempt < 3)
            {
                logger.LogWarning(
                    exception,
                    "Market pulse capture attempt {Attempt}/3 failed for segment {Segment}; retrying.",
                    attempt, segment);
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Market pulse capture exhausted retries for segment {Segment}; the slot remains uncommitted and will be retried by the next cadence.",
                    segment);
            }
        }
        return true;
    }
}
