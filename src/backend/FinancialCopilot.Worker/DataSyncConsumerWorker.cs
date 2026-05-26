using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Messaging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class DataSyncConsumerWorker(
    IServiceScopeFactory scopeFactory,
    IDataSyncRequestConsumer consumer,
    IOptions<RabbitMqDataSyncOptions> options,
    ILogger<DataSyncConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("RabbitMQ data synchronization consumer is disabled by configuration.");
            return;
        }

        await consumer.ConsumeAsync(
            async (request, cancellationToken) =>
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IFinancialDataSyncProcessor>();
                var result = await processor.ProcessAsync(request, cancellationToken);

                logger.LogInformation(
                    "Data synchronization request {RequestId} completed with status {Status} and {ProcessedRecords} records.",
                    request.RequestId,
                    result.Run.Status,
                    result.Run.ProcessedRecords);
            },
            stoppingToken);
    }
}
