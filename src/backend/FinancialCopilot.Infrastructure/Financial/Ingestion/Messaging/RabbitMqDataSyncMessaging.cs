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

    /// <summary>
    /// Number of competing data-sync consumers started by a Worker instance.
    /// Keep this bounded to avoid overwhelming the upstream provider or database.
    /// </summary>
    public int ConsumerCount { get; init; } = 4;

    public ushort PrefetchCount { get; init; } = 1;

    public int? ConsumerTimeoutMilliseconds { get; init; }

    /// <summary>
    /// Maximum concurrently leased publisher channels on the process-wide publisher connection.
    /// </summary>
    public int PublisherChannelLimit { get; init; } = 4;

    /// <summary>
    /// Upper bound, per message, for waiting on a publisher channel and its broker confirm.
    /// </summary>
    public int PublishTimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Publishes and consumes data-synchronization requests over two long-lived connections per process:
/// one for publishing and one shared by all competing consumers, each consumer on its own channel.
/// </summary>
public sealed class RabbitMqDataSyncRequestBus :
    IDataSyncRequestPublisher,
    IDataSyncRequestConsumer,
    IAsyncDisposable,
    IDisposable
{
    private const string ConnectionName = "FinancialCopilot.DataSync";

    private readonly IOptions<RabbitMqDataSyncOptions> options;
    private readonly ILogger<RabbitMqDataSyncRequestBus> logger;
    private readonly RabbitMqConnectionPolicy policy;
    private readonly RabbitMqSharedConnection publisherConnection;
    private readonly RabbitMqSharedConnection consumerConnection;
    private readonly RabbitMqPublisherChannelPool publisherChannels;

    public RabbitMqDataSyncRequestBus(
        IOptions<RabbitMqDataSyncOptions> options,
        ILogger<RabbitMqDataSyncRequestBus> logger)
        : this(
            options,
            logger,
            CreateConnectionFactory(options.Value),
            RabbitMqConnectionPolicy.Create(options.Value.PublisherChannelLimit, options.Value.PublishTimeoutSeconds),
            TimeProvider.System)
    {
    }

    internal RabbitMqDataSyncRequestBus(
        IOptions<RabbitMqDataSyncOptions> options,
        ILogger<RabbitMqDataSyncRequestBus> logger,
        IConnectionFactory connectionFactory,
        RabbitMqConnectionPolicy policy,
        TimeProvider timeProvider)
    {
        this.options = options;
        this.logger = logger;
        this.policy = policy;
        publisherConnection = new RabbitMqSharedConnection(
            connectionFactory, $"{ConnectionName}.Publisher", policy, timeProvider, logger);
        consumerConnection = new RabbitMqSharedConnection(
            connectionFactory, $"{ConnectionName}.Consumer", policy, timeProvider, logger);
        publisherChannels = new RabbitMqPublisherChannelPool(
            publisherConnection,
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            policy,
            logger);
    }

    public async Task PublishAsync(DataSyncRequest request, CancellationToken cancellationToken)
    {
        await PublishBatchAsync([request], cancellationToken);
    }

    public async Task PublishBatchAsync(
        IReadOnlyCollection<DataSyncRequest> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return;
        }

        var settings = options.Value;
        EnsureEnabled(settings);
        await publisherChannels.ExecuteAsync(
            requests.Count,
            (channel, token) => PublishOnChannelAsync(channel, settings, requests, token),
            cancellationToken);
    }

    private static async Task PublishOnChannelAsync(
        IChannel channel,
        RabbitMqDataSyncOptions settings,
        IReadOnlyCollection<DataSyncRequest> requests,
        CancellationToken cancellationToken)
    {
        await DeclareQueueAsync(channel, settings.RequestQueue, settings.ConsumerTimeoutMilliseconds, cancellationToken);

        foreach (var request in requests)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = request.RequestId.ToString(),
                CorrelationId = request.IdempotencyKey
            };

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: settings.RequestQueue,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
    }

    public Task ConsumeAsync(
        Func<DataSyncRequest, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        EnsureEnabled(settings);
        return RabbitMqResilientConsumer.RunAsync(
            consumerConnection,
            (channel, token) => StartConsumingAsync(channel, settings, handler, token),
            "data synchronization",
            policy,
            logger,
            cancellationToken);
    }

    private async Task StartConsumingAsync(
        IChannel channel,
        RabbitMqDataSyncOptions settings,
        Func<DataSyncRequest, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
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

            try
            {
                await handler(request, cancellationToken);

                await RabbitMqConsumerAcknowledgement.TryAckAsync(
                    channel,
                    args.DeliveryTag,
                    logger,
                    "data synchronization",
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Data synchronization message processing failed.");
                await RabbitMqConsumerAcknowledgement.TryNackAsync(
                    channel,
                    args.DeliveryTag,
                    logger,
                    "data synchronization",
                    cancellationToken,
                    requeue: true);
            }
        };

        await channel.BasicConsumeAsync(
            queue: settings.RequestQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await publisherChannels.DisposeAsync();
        await publisherConnection.DisposeAsync();
        await consumerConnection.DisposeAsync();
    }

    // A synchronously disposed service provider throws for singletons that are only IAsyncDisposable.
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static void EnsureEnabled(RabbitMqDataSyncOptions settings)
    {
        if (!settings.Enabled)
        {
            throw new InvalidOperationException(
                "RabbitMQ data synchronization transport is disabled by configuration.");
        }
    }

    private static ConnectionFactory CreateConnectionFactory(RabbitMqDataSyncOptions settings) =>
        RabbitMqConnectionPolicy.CreateConnectionFactory(
            settings.HostName,
            settings.Port,
            settings.VirtualHost,
            settings.UserName,
            settings.Password);

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
