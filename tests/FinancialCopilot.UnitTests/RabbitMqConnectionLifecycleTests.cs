using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Features;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Features.Messaging;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Messaging;
using FinancialCopilot.Infrastructure.Financial.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Regression coverage for specs/bugs/rabbitmq-data-sync-connection-churn-2026-11-01: the adapters must
/// keep long-lived connections instead of opening one per message, never share a channel between
/// concurrent publishes, pace reconnects, and preserve the existing delivery contract.
/// </summary>
public sealed class RabbitMqConnectionLifecycleTests
{
    private const string DataSyncQueue = "financialcopilot.data-sync.requests";

    private static readonly RabbitMqConnectionPolicy FastPolicy = new(
        InitialRetryDelay: TimeSpan.FromMilliseconds(20),
        MaxRetryDelay: TimeSpan.FromMilliseconds(200),
        PermanentFailureCooldown: TimeSpan.FromMinutes(5),
        RecoveryGracePeriod: TimeSpan.FromSeconds(30),
        PublishTimeout: TimeSpan.FromSeconds(5),
        PublisherChannelLimit: 4);

    [Fact]
    public async Task ManySequentialPublishes_ReuseOneConnectionAndOneChannel()
    {
        var broker = new FakeBroker();
        await using var bus = CreateDataSyncBus(broker);

        for (var i = 0; i < 200; i++)
        {
            await bus.PublishAsync(CreateRequest(i), CancellationToken.None);
        }

        var connection = Assert.Single(broker.Connections);
        Assert.Equal("FinancialCopilot.DataSync.Publisher", connection.ClientProvidedName);
        var channel = Assert.Single(connection.Channels);
        Assert.Equal(200, broker.Published.Count);
        Assert.True(channel.Options?.PublisherConfirmationsEnabled);
        Assert.True(channel.Options?.PublisherConfirmationTrackingEnabled);
    }

    [Fact]
    public async Task Publish_KeepsRoutingDurabilityAndMessageProperties()
    {
        var broker = new FakeBroker();
        await using var bus = CreateDataSyncBus(broker);
        var request = CreateRequest(7);

        await bus.PublishAsync(request, CancellationToken.None);

        var message = Assert.Single(broker.Published);
        Assert.Equal(string.Empty, message.Exchange);
        Assert.Equal(DataSyncQueue, message.RoutingKey);
        Assert.True(message.Mandatory);
        var properties = Assert.IsType<BasicProperties>(message.Properties);
        Assert.True(properties.Persistent);
        Assert.Equal("application/json", properties.ContentType);
        Assert.Equal(request.RequestId.ToString(), properties.MessageId);
        Assert.Equal(request.IdempotencyKey, properties.CorrelationId);
        var declared = Assert.Single(broker.Connections.Single().Channels.Single().Declarations);
        Assert.Equal((DataSyncQueue, true, false, false), declared);
        Assert.Equal(request.RequestId, JsonSerializer.Deserialize<DataSyncRequest>(message.Body, JsonWeb)!.RequestId);
    }

    [Fact]
    public async Task ConcurrentFirstUseAndPublishes_OpenOneConnection_AndNeverShareAChannel()
    {
        var broker = new FakeBroker
        {
            ConnectDelay = TimeSpan.FromMilliseconds(50),
            PublishDelay = TimeSpan.FromMilliseconds(5)
        };
        await using var bus = CreateDataSyncBus(broker);

        await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => bus.PublishAsync(CreateRequest(i), CancellationToken.None))));

        Assert.Equal(1, broker.ConnectionAttempts);
        Assert.InRange(broker.Connections.Single().Channels.Count, 1, FastPolicy.PublisherChannelLimit);
        Assert.Equal(0, broker.ConcurrentChannelUseViolations);
        Assert.Equal(100, broker.Published.Count);
    }

    [Fact]
    public async Task ClosedOrFailedChannel_IsDiscardedAndReplaced_OnTheSameConnection()
    {
        var broker = new FakeBroker();
        await using var bus = CreateDataSyncBus(broker);
        await bus.PublishAsync(CreateRequest(1), CancellationToken.None);
        var connection = broker.Connections.Single();
        var first = connection.Channels.Single();

        await first.SimulateBrokerCloseAsync(Constants.InternalError, "channel error");
        await bus.PublishAsync(CreateRequest(2), CancellationToken.None);

        Assert.True(first.Disposed);
        var second = connection.Channels[1];
        second.FailNextPublish = new AlreadyClosedException(
            new ShutdownEventArgs(ShutdownInitiator.Peer, Constants.InternalError, "lost"));
        await Assert.ThrowsAsync<AlreadyClosedException>(
            () => bus.PublishAsync(CreateRequest(3), CancellationToken.None));
        await bus.PublishAsync(CreateRequest(4), CancellationToken.None);

        Assert.True(second.Disposed);
        Assert.Equal(3, connection.Channels.Count);
        Assert.Single(broker.Connections);
        Assert.Equal(3, broker.Published.Count);
    }

    [Fact]
    public async Task TransientConnectFailure_IsPaced_InsteadOfRetriedForEveryMessage()
    {
        var clock = new ManualTimeProvider();
        var broker = new FakeBroker
        {
            ConnectFailure = () => new BrokerUnreachableException(new IOException("connection refused"))
        };
        await using var bus = CreateDataSyncBus(broker, clock);

        for (var i = 0; i < 50; i++)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => bus.PublishAsync(CreateRequest(i), CancellationToken.None));
        }

        Assert.Equal(1, broker.ConnectionAttempts);

        broker.ConnectFailure = null;
        clock.Advance(FastPolicy.MaxRetryDelay);
        await bus.PublishAsync(CreateRequest(99), CancellationToken.None);

        Assert.Equal(2, broker.ConnectionAttempts);
        Assert.Single(broker.Published);
    }

    [Fact]
    public async Task RejectedCredentials_AreTreatedAsPermanent_AndNotRetriedUntilTheCooldown()
    {
        var clock = new ManualTimeProvider();
        var broker = new FakeBroker
        {
            ConnectFailure = () => new BrokerUnreachableException(new AuthenticationFailureException("ACCESS_REFUSED"))
        };
        await using var bus = CreateDataSyncBus(broker, clock);

        await Assert.ThrowsAnyAsync<Exception>(() => bus.PublishAsync(CreateRequest(1), CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAnyAsync<Exception>(() => bus.PublishAsync(CreateRequest(2), CancellationToken.None));
        Assert.Equal(1, broker.ConnectionAttempts);

        clock.Advance(FastPolicy.PermanentFailureCooldown);
        await Assert.ThrowsAnyAsync<Exception>(() => bus.PublishAsync(CreateRequest(3), CancellationToken.None));
        Assert.Equal(2, broker.ConnectionAttempts);
    }

    [Fact]
    public async Task LostConnection_IsLeftToClientRecovery_ThenReplacedOnlyAfterTheGracePeriod()
    {
        var clock = new ManualTimeProvider();
        var broker = new FakeBroker();
        await using var bus = CreateDataSyncBus(broker, clock);
        await bus.PublishAsync(CreateRequest(1), CancellationToken.None);
        var original = broker.Connections.Single();

        await original.SimulateLossAsync();
        await Assert.ThrowsAsync<RabbitMqUnavailableException>(
            () => bus.PublishAsync(CreateRequest(2), CancellationToken.None));
        Assert.Equal(1, broker.ConnectionAttempts);

        await original.SimulateRecoveryAsync();
        await bus.PublishAsync(CreateRequest(3), CancellationToken.None);
        Assert.Equal(1, broker.ConnectionAttempts);

        await original.SimulateLossAsync();
        clock.Advance(FastPolicy.RecoveryGracePeriod + TimeSpan.FromSeconds(1));
        await bus.PublishAsync(CreateRequest(4), CancellationToken.None);

        Assert.True(original.Disposed);
        Assert.Equal(2, broker.ConnectionAttempts);
        Assert.Single(broker.Connections, connection => !connection.Disposed);
        // Requests 1, 3 and 4 reached the broker; request 2 was rejected while recovery was pending.
        Assert.Equal(3, broker.Published.Count);
    }

    [Fact]
    public async Task StuckPublishConfirm_FailsAsTimeout_AndReleasesTheChannelLease()
    {
        var broker = new FakeBroker { PublishDelay = Timeout.InfiniteTimeSpan };
        var policy = FastPolicy with { PublishTimeout = TimeSpan.FromMilliseconds(100), PublisherChannelLimit = 1 };
        await using var bus = CreateDataSyncBus(broker, policy: policy);

        await Assert.ThrowsAsync<TimeoutException>(() => bus.PublishAsync(CreateRequest(1), CancellationToken.None));
        broker.PublishDelay = TimeSpan.Zero;
        await bus.PublishAsync(CreateRequest(2), CancellationToken.None);

        Assert.Single(broker.Published);
        Assert.True(broker.Connections.Single().Channels[0].Disposed);
    }

    [Fact]
    public async Task Dispose_ClosesConnectionAndChannels_AndRejectsLaterPublishes()
    {
        var broker = new FakeBroker();
        var bus = CreateDataSyncBus(broker);
        await bus.PublishAsync(CreateRequest(1), CancellationToken.None);

        await bus.DisposeAsync();

        var connection = broker.Connections.Single();
        Assert.True(connection.Disposed);
        Assert.True(connection.Channels.Single().Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => bus.PublishAsync(CreateRequest(2), CancellationToken.None));
    }

    [Fact]
    public async Task CompetingConsumers_ShareOneConnection_EachOnItsOwnChannel_AndStopOnCancellation()
    {
        var broker = new FakeBroker();
        await using var bus = CreateDataSyncBus(broker);
        using var stopping = new CancellationTokenSource();

        var consumers = Enumerable.Range(0, 4)
            .Select(_ => bus.ConsumeAsync((_, _) => Task.CompletedTask, stopping.Token))
            .ToArray();
        await WaitUntilAsync(() => broker.RegisteredConsumers.Count == 4);

        var connection = Assert.Single(broker.Connections);
        Assert.Equal("FinancialCopilot.DataSync.Consumer", connection.ClientProvidedName);
        Assert.Equal(4, connection.Channels.Count);
        Assert.All(connection.Channels, channel =>
        {
            Assert.Equal((ushort)1, channel.PrefetchCount);
            Assert.False(channel.AutoAck);
            Assert.Equal(DataSyncQueue, channel.ConsumedQueue);
        });

        stopping.Cancel();
        foreach (var consumer in consumers)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consumer);
        }

        Assert.All(connection.Channels, channel => Assert.True(channel.Disposed));
    }

    [Fact]
    public async Task Consumer_AcksAfterSuccess_RequeuesHandlerFailures_AndRejectsMalformedMessages()
    {
        var broker = new FakeBroker();
        await using var bus = CreateDataSyncBus(broker);
        using var stopping = new CancellationTokenSource();
        var handled = new ConcurrentQueue<Guid>();
        var failing = CreateRequest(2);
        var consumer = bus.ConsumeAsync(
            (request, _) =>
            {
                handled.Enqueue(request.RequestId);
                return request.RequestId == failing.RequestId
                    ? throw new InvalidOperationException("processing failed")
                    : Task.CompletedTask;
            },
            stopping.Token);
        await WaitUntilAsync(() => broker.RegisteredConsumers.Count == 1);
        var channel = broker.Connections.Single().Channels.Single();

        await channel.DeliverAsync(1, JsonSerializer.SerializeToUtf8Bytes(CreateRequest(1), JsonWeb));
        await channel.DeliverAsync(2, JsonSerializer.SerializeToUtf8Bytes(failing, JsonWeb));
        await channel.DeliverAsync(3, Encoding.UTF8.GetBytes("{not json"));

        Assert.Equal([1UL], channel.Acks);
        Assert.Equal([(2UL, true), (3UL, false)], channel.Nacks);
        Assert.Equal(2, handled.Count);

        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consumer);
    }

    [Fact]
    public async Task ChannelClosedByBroker_IsReplaced_WithoutOpeningAnotherConnection()
    {
        var broker = new FakeBroker();
        await using var bus = CreateDataSyncBus(broker);
        using var stopping = new CancellationTokenSource();
        var consumer = bus.ConsumeAsync((_, _) => Task.CompletedTask, stopping.Token);
        await WaitUntilAsync(() => broker.RegisteredConsumers.Count == 1);
        var connection = broker.Connections.Single();
        var first = connection.Channels.Single();

        await first.SimulateBrokerCloseAsync(Constants.PreconditionFailed, "delivery acknowledgement timed out");
        await WaitUntilAsync(() => broker.RegisteredConsumers.Count == 2);

        Assert.True(first.Disposed);
        Assert.Equal(2, connection.Channels.Count);
        Assert.False(connection.Channels[1].Disposed);
        Assert.Equal(1, broker.ConnectionAttempts);

        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consumer);
    }

    [Fact]
    public async Task UnreachableBroker_ConsumerReconnectsWithBackoff_NotInAHotLoop()
    {
        var broker = new FakeBroker
        {
            ConnectFailure = () => new BrokerUnreachableException(new IOException("connection refused"))
        };
        await using var bus = CreateDataSyncBus(broker);
        using var stopping = new CancellationTokenSource();
        var consumer = bus.ConsumeAsync((_, _) => Task.CompletedTask, stopping.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        var attemptsWhileDown = broker.ConnectionAttempts;
        broker.ConnectFailure = null;
        await WaitUntilAsync(() => broker.RegisteredConsumers.Count == 1);

        // Backoff starts at 20 ms and doubles toward 200 ms, so half a second allows only a handful
        // of attempts; a hot loop would make thousands.
        Assert.InRange(attemptsWhileDown, 2, 12);
        Assert.Single(broker.Connections);

        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consumer);
    }

    [Fact]
    public async Task RejectedCredentials_StopTheConsumer_WithoutRetrying()
    {
        var broker = new FakeBroker
        {
            ConnectFailure = () => new BrokerUnreachableException(new AuthenticationFailureException("ACCESS_REFUSED"))
        };
        await using var bus = CreateDataSyncBus(broker);

        await Assert.ThrowsAsync<BrokerUnreachableException>(
            () => bus.ConsumeAsync((_, _) => Task.CompletedTask, CancellationToken.None));
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Equal(1, broker.ConnectionAttempts);
    }

    [Fact]
    public async Task FeaturePublishes_ReuseOneConnection_WithoutConfirms_AndKeepTheirQueues()
    {
        var broker = new FakeBroker();
        var options = new RabbitMqFeatureOptions { Enabled = true };
        await using var bus = new RabbitMqFeatureBus(
            Options.Create(options),
            NullLogger<RabbitMqFeatureBus>.Instance,
            broker.Factory,
            FastPolicy,
            TimeProvider.System);

        for (var i = 0; i < 10; i++)
        {
            await bus.PublishRequestedAsync(CreateFeatureRequest(), CancellationToken.None);
            await bus.PublishCompletedAsync(
                new FeatureRecalculationCompleted(Guid.NewGuid(), "job", Guid.NewGuid(), DateTimeOffset.UtcNow),
                CancellationToken.None);
            await bus.PublishFailedAsync(
                new FeatureRecalculationFailed(Guid.NewGuid(), "job", "failed", DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        var connection = Assert.Single(broker.Connections);
        Assert.Equal("FinancialCopilot.Features.Publisher", connection.ClientProvidedName);
        var channel = Assert.Single(connection.Channels);
        Assert.Null(channel.Options);
        Assert.Equal(30, broker.Published.Count);
        Assert.All(broker.Published, message => Assert.False(message.Mandatory));
        Assert.Equal(
            [options.RequestedQueue, options.CompletedQueue, options.FailedQueue],
            broker.Published.Select(message => message.RoutingKey).Distinct().ToArray());
    }

    [Fact]
    public async Task FeatureConsumer_StillAcknowledgesBeforeRunningTheHandler()
    {
        var broker = new FakeBroker();
        await using var bus = new RabbitMqFeatureBus(
            Options.Create(new RabbitMqFeatureOptions { Enabled = true }),
            NullLogger<RabbitMqFeatureBus>.Instance,
            broker.Factory,
            FastPolicy,
            TimeProvider.System);
        using var stopping = new CancellationTokenSource();
        var acksSeenByHandler = -1;
        FakeChannel? channel = null;
        var consumer = bus.ConsumeAsync(
            (_, _) =>
            {
                acksSeenByHandler = channel!.Acks.Count;
                return Task.CompletedTask;
            },
            stopping.Token);
        await WaitUntilAsync(() => broker.RegisteredConsumers.Count == 1);
        channel = broker.Connections.Single().Channels.Single();

        await channel.DeliverAsync(5, JsonSerializer.SerializeToUtf8Bytes(CreateFeatureRequest(), JsonWeb));

        Assert.Equal(1, acksSeenByHandler);
        Assert.Equal([5UL], channel.Acks);

        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consumer);
    }

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private static RabbitMqDataSyncRequestBus CreateDataSyncBus(
        FakeBroker broker,
        TimeProvider? clock = null,
        RabbitMqConnectionPolicy? policy = null) =>
        new(
            Options.Create(new RabbitMqDataSyncOptions { Enabled = true, RequestQueue = DataSyncQueue }),
            NullLogger<RabbitMqDataSyncRequestBus>.Instance,
            broker.Factory,
            policy ?? FastPolicy,
            clock ?? TimeProvider.System);

    private static DataSyncRequest CreateRequest(int index) => new(
        Guid.NewGuid(),
        ProviderDataset.FinancialStatements,
        index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.UtcNow,
        $"test-{index}-{Guid.NewGuid():N}");

    private static FeatureRecalculationRequested CreateFeatureRequest() =>
        new(
            Guid.NewGuid(),
            new FeatureCode("TEST_FEATURE"),
            new FeatureVersion("v1"),
            "ext-1",
            FeatureComputationPeriod.From(FiscalPeriod.Closed(
                FiscalPeriodType.Monthly,
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 4, 30))),
            $"feature-job-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within 5 seconds.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private sealed record PublishedMessage(
        string Exchange,
        string RoutingKey,
        bool Mandatory,
        object? Properties,
        byte[] Body);

    private sealed class FakeBroker
    {
        private int connectionAttempts;
        private int concurrentChannelUseViolations;

        public FakeBroker() => Factory = InterfaceFake.Create<IConnectionFactory>(Handle);

        public IConnectionFactory Factory { get; }

        public List<FakeConnection> Connections { get; } = [];

        public ConcurrentQueue<PublishedMessage> Published { get; } = new();

        public ConcurrentQueue<IAsyncBasicConsumer> RegisteredConsumers { get; } = new();

        public Func<Exception>? ConnectFailure { get; set; }

        public TimeSpan ConnectDelay { get; set; }

        public TimeSpan PublishDelay { get; set; }

        public int ConnectionAttempts => Volatile.Read(ref connectionAttempts);

        public int ConcurrentChannelUseViolations => Volatile.Read(ref concurrentChannelUseViolations);

        public void RecordConcurrentChannelUse() => Interlocked.Increment(ref concurrentChannelUseViolations);

        private object? Handle(MethodInfo method, object?[] args)
        {
            if (method.Name == nameof(IConnectionFactory.CreateConnectionAsync) &&
                args is [string name, CancellationToken token])
            {
                return ConnectAsync(name, token);
            }

            throw new NotSupportedException(method.Name);
        }

        private async Task<IConnection> ConnectAsync(string clientProvidedName, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref connectionAttempts);
            if (ConnectDelay > TimeSpan.Zero)
            {
                await Task.Delay(ConnectDelay, cancellationToken);
            }

            if (ConnectFailure?.Invoke() is { } failure)
            {
                throw failure;
            }

            var connection = new FakeConnection(this, clientProvidedName);
            lock (Connections)
            {
                Connections.Add(connection);
            }

            return connection.Proxy;
        }
    }

    private sealed class FakeConnection
    {
        private readonly FakeBroker broker;
        private readonly List<AsyncEventHandler<ShutdownEventArgs>> shutdownHandlers = [];
        private readonly List<AsyncEventHandler<AsyncEventArgs>> recoveryHandlers = [];
        private readonly List<FakeChannel> channels = [];

        public FakeConnection(FakeBroker broker, string clientProvidedName)
        {
            this.broker = broker;
            ClientProvidedName = clientProvidedName;
            Proxy = InterfaceFake.Create<IConnection>(Handle);
        }

        public IConnection Proxy { get; }

        public string ClientProvidedName { get; }

        public bool IsOpen { get; private set; } = true;

        public bool Disposed { get; private set; }

        public List<FakeChannel> Channels
        {
            get
            {
                lock (channels)
                {
                    return [.. channels];
                }
            }
        }

        public async Task SimulateLossAsync()
        {
            IsOpen = false;
            var args = new ShutdownEventArgs(ShutdownInitiator.Library, Constants.ConnectionForced, "connection lost");
            foreach (var handler in shutdownHandlers.ToArray())
            {
                await handler(Proxy, args);
            }

            foreach (var channel in Channels.Where(channel => !channel.Disposed))
            {
                await channel.SimulateBrokerCloseAsync(Constants.ConnectionForced, "connection lost");
            }
        }

        public async Task SimulateRecoveryAsync()
        {
            IsOpen = true;
            foreach (var channel in Channels.Where(channel => !channel.Disposed))
            {
                channel.Reopen();
            }

            foreach (var handler in recoveryHandlers.ToArray())
            {
                await handler(Proxy, new AsyncEventArgs());
            }
        }

        private object? Handle(MethodInfo method, object?[] args)
        {
            switch (method.Name)
            {
                case "get_IsOpen":
                    return IsOpen;
                case "get_ClientProvidedName":
                    return ClientProvidedName;
                case "add_ConnectionShutdownAsync":
                    shutdownHandlers.Add((AsyncEventHandler<ShutdownEventArgs>)args[0]!);
                    return null;
                case "remove_ConnectionShutdownAsync":
                    shutdownHandlers.Remove((AsyncEventHandler<ShutdownEventArgs>)args[0]!);
                    return null;
                case "add_RecoverySucceededAsync":
                    recoveryHandlers.Add((AsyncEventHandler<AsyncEventArgs>)args[0]!);
                    return null;
                case "remove_RecoverySucceededAsync":
                    recoveryHandlers.Remove((AsyncEventHandler<AsyncEventArgs>)args[0]!);
                    return null;
                case nameof(IConnection.CreateChannelAsync):
                    return CreateChannelAsync((CreateChannelOptions?)args[0]);
                case nameof(IAsyncDisposable.DisposeAsync):
                    Disposed = true;
                    IsOpen = false;
                    return ValueTask.CompletedTask;
                case nameof(IDisposable.Dispose):
                    Disposed = true;
                    IsOpen = false;
                    return null;
                default:
                    return InterfaceFake.DefaultResult(method);
            }
        }

        private Task<IChannel> CreateChannelAsync(CreateChannelOptions? options)
        {
            if (!IsOpen)
            {
                throw new AlreadyClosedException(
                    new ShutdownEventArgs(ShutdownInitiator.Library, Constants.ConnectionForced, "closed"));
            }

            var channel = new FakeChannel(broker, options);
            lock (channels)
            {
                channels.Add(channel);
            }

            return Task.FromResult(channel.Proxy);
        }
    }

    private sealed class FakeChannel
    {
        private readonly FakeBroker broker;
        private readonly List<AsyncEventHandler<ShutdownEventArgs>> shutdownHandlers = [];
        private readonly ConcurrentQueue<ulong> acks = new();
        private readonly ConcurrentQueue<(ulong, bool)> nacks = new();
        private IAsyncBasicConsumer? consumer;
        private int inFlightPublishes;

        public FakeChannel(FakeBroker broker, CreateChannelOptions? options)
        {
            this.broker = broker;
            Options = options;
            Proxy = InterfaceFake.Create<IChannel>(Handle);
        }

        public IChannel Proxy { get; }

        public CreateChannelOptions? Options { get; }

        public bool IsOpen { get; private set; } = true;

        public bool Disposed { get; private set; }

        public ShutdownEventArgs? CloseReason { get; private set; }

        public Exception? FailNextPublish { get; set; }

        public List<(string Queue, bool Durable, bool Exclusive, bool AutoDelete)> Declarations { get; } = [];

        public ushort PrefetchCount { get; private set; }

        public bool AutoAck { get; private set; } = true;

        public string? ConsumedQueue { get; private set; }

        public List<ulong> Acks => [.. acks];

        public List<(ulong, bool)> Nacks => [.. nacks];

        public async Task SimulateBrokerCloseAsync(ushort replyCode, string replyText)
        {
            IsOpen = false;
            CloseReason = new ShutdownEventArgs(ShutdownInitiator.Peer, replyCode, replyText);
            foreach (var handler in shutdownHandlers.ToArray())
            {
                await handler(Proxy, CloseReason);
            }
        }

        public void Reopen()
        {
            IsOpen = true;
            CloseReason = null;
        }

        public Task DeliverAsync(ulong deliveryTag, byte[] body) =>
            consumer!.HandleBasicDeliverAsync(
                "consumer-tag",
                deliveryTag,
                redelivered: false,
                exchange: string.Empty,
                routingKey: ConsumedQueue!,
                new BasicProperties(),
                body);

        private object? Handle(MethodInfo method, object?[] args)
        {
            switch (method.Name)
            {
                case "get_IsOpen":
                    return IsOpen;
                case "get_IsClosed":
                    return !IsOpen;
                case "get_CloseReason":
                    return CloseReason;
                case "add_ChannelShutdownAsync":
                    shutdownHandlers.Add((AsyncEventHandler<ShutdownEventArgs>)args[0]!);
                    return null;
                case "remove_ChannelShutdownAsync":
                    shutdownHandlers.Remove((AsyncEventHandler<ShutdownEventArgs>)args[0]!);
                    return null;
                case nameof(IChannel.QueueDeclareAsync):
                    lock (Declarations)
                    {
                        Declarations.Add(((string)args[0]!, (bool)args[1]!, (bool)args[2]!, (bool)args[3]!));
                    }

                    return Task.FromResult(new QueueDeclareOk((string)args[0]!, 0, 0));
                case nameof(IChannel.BasicQosAsync):
                    PrefetchCount = (ushort)args[1]!;
                    return Task.CompletedTask;
                case nameof(IChannel.BasicConsumeAsync):
                    ConsumedQueue = (string)args[0]!;
                    AutoAck = (bool)args[1]!;
                    consumer = args.OfType<IAsyncBasicConsumer>().Single();
                    broker.RegisteredConsumers.Enqueue(consumer);
                    return Task.FromResult("consumer-tag");
                case nameof(IChannel.BasicPublishAsync):
                    return new ValueTask(PublishAsync(args));
                case nameof(IChannel.BasicAckAsync):
                    acks.Enqueue((ulong)args[0]!);
                    return ValueTask.CompletedTask;
                case nameof(IChannel.BasicNackAsync):
                    nacks.Enqueue(((ulong)args[0]!, (bool)args[2]!));
                    return ValueTask.CompletedTask;
                case nameof(IAsyncDisposable.DisposeAsync):
                    Disposed = true;
                    IsOpen = false;
                    return ValueTask.CompletedTask;
                case nameof(IDisposable.Dispose):
                    Disposed = true;
                    IsOpen = false;
                    return null;
                default:
                    return InterfaceFake.DefaultResult(method);
            }
        }

        private async Task PublishAsync(object?[] args)
        {
            if (Interlocked.Increment(ref inFlightPublishes) > 1)
            {
                broker.RecordConcurrentChannelUse();
            }

            try
            {
                if (FailNextPublish is { } failure)
                {
                    FailNextPublish = null;
                    throw failure;
                }

                var cancellationToken = (CancellationToken)args[^1]!;
                if (broker.PublishDelay != TimeSpan.Zero)
                {
                    await Task.Delay(broker.PublishDelay, cancellationToken);
                }

                broker.Published.Enqueue(new PublishedMessage(
                    (string)args[0]!,
                    (string)args[1]!,
                    (bool)args[2]!,
                    args[3],
                    ((ReadOnlyMemory<byte>)args[4]!).ToArray()));
            }
            finally
            {
                Interlocked.Decrement(ref inFlightPublishes);
            }
        }
    }
}

/// <summary>
/// Minimal interface fake: routes every member call to a handler, so tests implement only the
/// RabbitMQ client members the adapters actually use.
/// </summary>
public class InterfaceFake : DispatchProxy
{
    private Func<MethodInfo, object?[], object?> handler = (method, _) => DefaultResult(method);

    public static T Create<T>(Func<MethodInfo, object?[], object?> handler)
        where T : class
    {
        var proxy = Create<T, InterfaceFake>();
        ((InterfaceFake)(object)proxy).handler = handler;
        return proxy;
    }

    public static object? DefaultResult(MethodInfo method)
    {
        var type = method.ReturnType;
        if (type == typeof(void))
        {
            return null;
        }

        if (type == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (type == typeof(ValueTask))
        {
            return ValueTask.CompletedTask;
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        handler(targetMethod!, args ?? []);
}
