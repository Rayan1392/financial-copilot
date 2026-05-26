using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
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
        await DeclareQueueAsync(channel, settings.RequestQueue, cancellationToken);
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
        await DeclareQueueAsync(channel, settings.RequestQueue, cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                var request = JsonSerializer.Deserialize<DataSyncRequest>(args.Body.Span, JsonOptions) ??
                    throw new InvalidOperationException("Data synchronization message was empty.");
                await handler(request, cancellationToken);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Data synchronization message processing failed.");
                await channel.BasicNackAsync(
                    args.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken);
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
        CancellationToken cancellationToken) =>
        channel.QueueDeclareAsync(
            queue: queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
