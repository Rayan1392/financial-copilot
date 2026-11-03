using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Infrastructure.Financial.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FinancialCopilot.Infrastructure.Financial.Features.Messaging;

public sealed class RabbitMqFeatureOptions
{
    public const string SectionName = "FeatureMessaging";

    public bool Enabled { get; init; }

    public string HostName { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string VirtualHost { get; init; } = "/";

    public string UserName { get; init; } = "guest";

    public string Password { get; init; } = "guest";

    public string RequestedQueue { get; init; } = "financialcopilot.features.recalculate.requested";

    public string CompletedQueue { get; init; } = "financialcopilot.features.recalculate.completed";

    public string FailedQueue { get; init; } = "financialcopilot.features.recalculate.failed";

    public ushort PrefetchCount { get; init; } = 1;

    public int? ConsumerTimeoutMilliseconds { get; init; }

    /// <summary>
    /// Maximum concurrently leased publisher channels on the process-wide publisher connection.
    /// </summary>
    public int PublisherChannelLimit { get; init; } = 4;

    /// <summary>
    /// Upper bound, per message, for waiting on a publisher channel and completing the publish.
    /// </summary>
    public int PublishTimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Publishes and consumes feature-recalculation messages over two long-lived connections per process:
/// one for publishing and one for the consumer.
/// </summary>
public sealed class RabbitMqFeatureBus :
    IFeatureRecalculationPublisher,
    IFeatureRecalculationConsumer,
    IAsyncDisposable,
    IDisposable
{
    private const string ConnectionName = "FinancialCopilot.Features";

    private readonly IOptions<RabbitMqFeatureOptions> options;
    private readonly ILogger<RabbitMqFeatureBus> logger;
    private readonly RabbitMqConnectionPolicy policy;
    private readonly RabbitMqSharedConnection publisherConnection;
    private readonly RabbitMqSharedConnection consumerConnection;
    private readonly RabbitMqPublisherChannelPool publisherChannels;

    public RabbitMqFeatureBus(
        IOptions<RabbitMqFeatureOptions> options,
        ILogger<RabbitMqFeatureBus> logger)
        : this(
            options,
            logger,
            CreateConnectionFactory(options.Value),
            RabbitMqConnectionPolicy.Create(options.Value.PublisherChannelLimit, options.Value.PublishTimeoutSeconds),
            TimeProvider.System)
    {
    }

    internal RabbitMqFeatureBus(
        IOptions<RabbitMqFeatureOptions> options,
        ILogger<RabbitMqFeatureBus> logger,
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
            publisherConnection, channelOptions: null, policy, logger);
    }

    public Task PublishRequestedAsync(FeatureRecalculationRequested request, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.RequestedQueue, request, cancellationToken);

    public Task PublishCompletedAsync(FeatureRecalculationCompleted notification, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.CompletedQueue, notification, cancellationToken);

    public Task PublishFailedAsync(FeatureRecalculationFailed notification, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.FailedQueue, notification, cancellationToken);

    public Task ConsumeAsync(
        Func<FeatureRecalculationRequested, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        EnsureEnabled(settings);
        return RabbitMqResilientConsumer.RunAsync(
            consumerConnection,
            (channel, token) => StartConsumingAsync(channel, settings, handler, token),
            "feature recalculation",
            policy,
            logger,
            cancellationToken);
    }

    private async Task StartConsumingAsync(
        IChannel channel,
        RabbitMqFeatureOptions settings,
        Func<FeatureRecalculationRequested, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        await DeclareQueueAsync(channel, settings.RequestedQueue, settings.ConsumerTimeoutMilliseconds, cancellationToken);
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: settings.PrefetchCount,
            global: false,
            cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, args) =>
        {
            FeatureRecalculationRequested request;
            try
            {
                request = JsonSerializer.Deserialize<FeatureRecalculationRequested>(args.Body.Span, JsonOptions) ??
                    throw new InvalidOperationException("Feature recalculation message was empty.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Feature recalculation message was malformed.");
                await RabbitMqConsumerAcknowledgement.TryNackAsync(
                    channel,
                    args.DeliveryTag,
                    logger,
                    "feature recalculation",
                    cancellationToken);
                return;
            }

            if (!await RabbitMqConsumerAcknowledgement.TryAckAsync(
                    channel,
                    args.DeliveryTag,
                    logger,
                    "feature recalculation",
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
                logger.LogError(exception, "Feature recalculation message processing failed.");
            }
        };

        await channel.BasicConsumeAsync(
            queue: settings.RequestedQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
    }

    private async Task PublishAsync<T>(string queue, T payload, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        EnsureEnabled(settings);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await publisherChannels.ExecuteAsync(
            messageCount: 1,
            async (channel, token) =>
            {
                await DeclareQueueAsync(channel, queue, consumerTimeoutMilliseconds: null, token);
                await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: queue,
                    mandatory: false,
                    body: body,
                    cancellationToken: token);
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await publisherChannels.DisposeAsync();
        await publisherConnection.DisposeAsync();
        await consumerConnection.DisposeAsync();
    }

    // A synchronously disposed service provider throws for singletons that are only IAsyncDisposable.
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static void EnsureEnabled(RabbitMqFeatureOptions settings)
    {
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("RabbitMQ feature computation transport is disabled by configuration.");
        }
    }

    private static ConnectionFactory CreateConnectionFactory(RabbitMqFeatureOptions settings) =>
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
            queue,
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
