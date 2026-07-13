using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class TelegramMembershipRevalidationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramMembershipRevalidationOptions> options,
    ILogger<TelegramMembershipRevalidationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var leaseOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.Value;
            var delay = TimeSpan.FromSeconds(Math.Max(1, settings.CadenceSeconds));

            try
            {
                if (settings.Enabled)
                {
                    using var scope = scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<TelegramMembershipRevalidationProcessor>();
                    var processed = await processor.ProcessDueAsync(leaseOwner, stoppingToken);
                    if (processed > 0)
                    {
                        logger.LogInformation("Telegram membership revalidation processed {Count} due rows.", processed);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Telegram membership revalidation worker tick failed.");
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
