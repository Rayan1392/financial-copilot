using FinancialCopilot.Application.FinancialData.MarketReports;
using FinancialCopilot.Infrastructure.Financial.MarketReports;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class MarketReportWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MarketReportOptions> options,
    ILogger<MarketReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ScheduledGenerationEnabled) return;
        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(Math.Max(1, options.Value.ScheduleCadenceMinutes)));
        do
        {
            await GenerateWithRetryAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task GenerateWithRetryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= Math.Max(1, options.Value.MaximumAttempts); attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scheduler = scope.ServiceProvider.GetRequiredService<IMarketReportScheduler>();
                var generated = await scheduler.GenerateDueAsync(cancellationToken);
                logger.LogInformation("Market-report scheduler evaluated {Generated} eligible segments.", generated);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (attempt < options.Value.MaximumAttempts)
            {
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning(exception,
                    "Market-report schedule attempt {Attempt}/{MaximumAttempts} failed; retrying after {Backoff}.",
                    attempt, options.Value.MaximumAttempts, backoff);
                await Task.Delay(backoff, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Market-report schedule exhausted retries. Durable pending reports remain available for the next cadence.");
            }
        }
    }
}
