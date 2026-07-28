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
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("RabbitMQ data synchronization consumer is disabled by configuration.");
            return;
        }

        logger.LogInformation(
            "Starting {ConsumerCount} competing RabbitMQ data synchronization consumers for queue {RequestQueue}.",
            settings.ConsumerCount,
            settings.RequestQueue);

        var consumers = Enumerable.Range(1, settings.ConsumerCount)
            .Select(consumerNumber => ConsumeAsync(consumerNumber, stoppingToken));

        await Task.WhenAll(consumers);
    }

    private Task ConsumeAsync(int consumerNumber, CancellationToken stoppingToken) =>
        consumer.ConsumeAsync(
            async (request, cancellationToken) =>
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IFinancialDataSyncProcessor>();
                var result = await processor.ProcessAsync(request, cancellationToken);

                logger.LogInformation(
                    "Data synchronization consumer {ConsumerNumber} completed request {RequestId} with status {Status} and {ProcessedRecords} records.",
                    consumerNumber,
                    request.RequestId,
                    result.Run.Status,
                    result.Run.ProcessedRecords);
            },
            stoppingToken);
}
