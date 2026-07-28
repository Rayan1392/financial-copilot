using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Infrastructure.Financial.Features.Messaging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class FeatureComputationConsumerWorker(
    IServiceScopeFactory scopeFactory,
    IFeatureRecalculationConsumer consumer,
    IOptions<RabbitMqFeatureOptions> options,
    ILogger<FeatureComputationConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("RabbitMQ feature computation consumer is disabled by configuration.");
            return;
        }

        await consumer.ConsumeAsync(
            async (request, cancellationToken) =>
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IFeatureComputationProcessor>();
                await processor.ProcessAsync(request, cancellationToken);
                logger.LogInformation(
                    "Feature computation request {JobId} for {FeatureCode} completed.",
                    request.JobId,
                    request.FeatureCode.Value);
            },
            stoppingToken);
    }
}
