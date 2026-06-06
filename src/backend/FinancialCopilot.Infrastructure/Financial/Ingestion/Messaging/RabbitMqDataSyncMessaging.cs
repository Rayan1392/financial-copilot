using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Messaging;

public sealed class RabbitMqDataSyncOptions
{
    public const string SectionName = "DataSyncMessaging";

    public bool Enabled { get; init; }

    public string HostName { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string VirtualHost { get; init; } = "/";

    public string UserName { get; init; } = "guest";

    public string Password { get; init; } = "guest";

    public string RequestQueue { get; init; } = "financialcopilot.data-sync.requests";

    public ushort PrefetchCount { get; init; } = 1;

    public int? ConsumerTimeoutMilliseconds { get; init; }
}

public sealed class RabbitMqDataSyncRequestBus(
    IOptions<RabbitMqDataSyncOptions> options,
    ILogger<RabbitMqDataSyncRequestBus> logger) : IDataSyncRequestPublisher, IDataSyncRequestConsumer
{
    public async Task PublishAsync(DataSyncRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        EnsureEnabled(settings);
        await using var connection = await CreateConnectionAsync(settings, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await DeclareQueueAsync(channel, settings.RequestQueue, settings.ConsumerTimeoutMilliseconds, cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: settings.RequestQueue,
            mandatory: false,
            body: body,
            cancellationToken: cancellationToken);
    }

    public async Task ConsumeAsync(
        Func<DataSyncRequest, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        EnsureEnabled(settings);
        await using var connection = await CreateConnectionAsync(settings, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await DeclareQueueAsync(channel, settings.RequestQueue, settings.ConsumerTimeoutMilliseconds, cancellationToken);
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: settings.PrefetchCount,
            global: false,
            cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, args) =>
        {
            DataSyncRequest request;
            try
            {
                request = JsonSerializer.Deserialize<DataSyncRequest>(args.Body.Span, JsonOptions) ??
                    throw new InvalidOperationException("Data synchronization message was empty.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Data synchronization message was malformed.");
                await RabbitMqConsumerAcknowledgement.TryNackAsync(
                    channel,
                    args.DeliveryTag,
                    logger,
                    "data synchronization",
                    cancellationToken);
                return;
            }

            if (!await RabbitMqConsumerAcknowledgement.TryAckAsync(
                    channel,
                    args.DeliveryTag,
                    logger,
                    "data synchronization",
                    cancellationToken))
            {
                return;
            }

            try
            {
                await handler(request, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Data synchronization message processing failed.");
            }
        };

        await channel.BasicConsumeAsync(
            queue: settings.RequestQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private static void EnsureEnabled(RabbitMqDataSyncOptions settings)
    {
        if (!settings.Enabled)
        {
            throw new InvalidOperationException(
                "RabbitMQ data synchronization transport is disabled by configuration.");
        }
    }

    private static Task<IConnection> CreateConnectionAsync(
        RabbitMqDataSyncOptions settings,
        CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            VirtualHost = settings.VirtualHost,
            UserName = settings.UserName,
            Password = settings.Password
        };

        return factory.CreateConnectionAsync("FinancialCopilot.DataSync", cancellationToken);
    }

    private static Task<QueueDeclareOk> DeclareQueueAsync(
        IChannel channel,
        string queue,
        int? consumerTimeoutMilliseconds,
        CancellationToken cancellationToken) =>
        channel.QueueDeclareAsync(
            queue: queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: CreateQueueArguments(consumerTimeoutMilliseconds),
            cancellationToken: cancellationToken);

    private static Dictionary<string, object?>? CreateQueueArguments(int? consumerTimeoutMilliseconds) =>
        consumerTimeoutMilliseconds is null
            ? null
            : new Dictionary<string, object?>
            {
                ["x-consumer-timeout"] = consumerTimeoutMilliseconds.Value
            };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
