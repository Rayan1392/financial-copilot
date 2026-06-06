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
}

public sealed class RabbitMqFeatureBus(
    IOptions<RabbitMqFeatureOptions> options,
    ILogger<RabbitMqFeatureBus> logger) : IFeatureRecalculationPublisher, IFeatureRecalculationConsumer
{
    public Task PublishRequestedAsync(FeatureRecalculationRequested request, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.RequestedQueue, request, cancellationToken);

    public Task PublishCompletedAsync(FeatureRecalculationCompleted notification, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.CompletedQueue, notification, cancellationToken);

    public Task PublishFailedAsync(FeatureRecalculationFailed notification, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.FailedQueue, notification, cancellationToken);

    public async Task ConsumeAsync(
        Func<FeatureRecalculationRequested, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        EnsureEnabled(settings);
        await using var connection = await CreateConnectionAsync(settings, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
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
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private async Task PublishAsync<T>(string queue, T payload, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        EnsureEnabled(settings);
        await using var connection = await CreateConnectionAsync(settings, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await DeclareQueueAsync(channel, queue, consumerTimeoutMilliseconds: null, cancellationToken);
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queue,
            mandatory: false,
            body: JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions),
            cancellationToken: cancellationToken);
    }

    private static void EnsureEnabled(RabbitMqFeatureOptions settings)
    {
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("RabbitMQ feature computation transport is disabled by configuration.");
        }
    }

    private static Task<IConnection> CreateConnectionAsync(
        RabbitMqFeatureOptions settings,
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

        return factory.CreateConnectionAsync("FinancialCopilot.Features", cancellationToken);
    }

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
