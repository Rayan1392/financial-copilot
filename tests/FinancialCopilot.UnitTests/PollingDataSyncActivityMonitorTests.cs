using System.Threading.Channels;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 058 — PollingDataSyncActivityMonitor: diff correctness, heartbeat, and connection cap.
/// </summary>
public sealed class PollingDataSyncActivityMonitorTests : IAsyncDisposable
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-06-13T08:00:00Z");

    private readonly ControllableDataSyncActivityReader _reader = new();
    private readonly PollingDataSyncActivityMonitor _monitor;
    private readonly CancellationTokenSource _testCts = new();

    public PollingDataSyncActivityMonitorTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDataSyncActivityReader>(_ => _reader);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new DataSyncMonitorOptions
        {
            PollingIntervalSeconds = 1,
            MaxConnections = 2
        });

        _monitor = new PollingDataSyncActivityMonitor(
            scopeFactory,
            options,
            new NullHostApplicationLifetime(),
            NullLogger<PollingDataSyncActivityMonitor>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _testCts.CancelAsync();
        await _monitor.StopAsync(CancellationToken.None);
        await _monitor.DisposeAsync();
        _testCts.Dispose();
    }

    // -----------------------------------------------------------------------
    // No Update event when snapshot is unchanged
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NoUpdate_WhenSnapshotIsUnchanged()
    {
        var item = MakeItem("run-001", "Running");
        _reader.SetSnapshot(new DataSyncActivitySnapshot([item], []));

        var channel = Channel.CreateUnbounded<DataSyncActivityEvent>();
        await _monitor.StartAsync(CancellationToken.None);

        using var subCts = CancellationTokenSource.CreateLinkedTokenSource(_testCts.Token);
        var subscribeTask = Task.Run(() => _monitor.SubscribeAsync(channel.Writer, subCts.Token), _testCts.Token);

        // Read the initial Snapshot event (always sent on subscribe).
        var first = await ReadWithTimeoutAsync(channel.Reader, TimeSpan.FromSeconds(5));
        Assert.Equal(DataSyncActivityEventKind.Snapshot, first.Kind);

        // The first poll will diff against null and emit an Update for all items — drain it.
        var secondEvent = await ReadWithTimeoutAsync(channel.Reader, TimeSpan.FromSeconds(5));
        Assert.Equal(DataSyncActivityEventKind.Update, secondEvent.Kind);

        // From here the snapshot is stable. Wait two more poll cycles.
        await Task.Delay(TimeSpan.FromSeconds(3), _testCts.Token);

        // No further Update event should have arrived (only possible Heartbeat after 15 s, but we're within that window).
        bool hasExtraUpdate = channel.Reader.TryRead(out var extra) && extra.Kind == DataSyncActivityEventKind.Update;
        Assert.False(hasExtraUpdate, "Expected no Update event when snapshot is unchanged after the first poll.");

        await subCts.CancelAsync();
        await subscribeTask;
    }

    // -----------------------------------------------------------------------
    // Update event emitted when status changes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Update_EmittedWhenStatusChanges()
    {
        var initial = MakeItem("run-002", "Running");
        _reader.SetSnapshot(new DataSyncActivitySnapshot([initial], []));

        var channel = Channel.CreateUnbounded<DataSyncActivityEvent>();
        await _monitor.StartAsync(CancellationToken.None);

        using var subCts = CancellationTokenSource.CreateLinkedTokenSource(_testCts.Token);
        var subscribeTask = Task.Run(() => _monitor.SubscribeAsync(channel.Writer, subCts.Token), _testCts.Token);

        // Consume the initial Snapshot event.
        var first = await ReadWithTimeoutAsync(channel.Reader, TimeSpan.FromSeconds(5));
        Assert.Equal(DataSyncActivityEventKind.Snapshot, first.Kind);

        // Transition the run to Completed.
        var updated = MakeItem("run-002", "Completed");
        _reader.SetSnapshot(new DataSyncActivitySnapshot([], [updated]));

        // Wait for the Update event.
        DataSyncActivityEvent? updateEvent = null;
        using var deadlineCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var evt in channel.Reader.ReadAllAsync(deadlineCts.Token))
        {
            if (evt.Kind == DataSyncActivityEventKind.Update)
            {
                updateEvent = evt;
                break;
            }
        }

        Assert.NotNull(updateEvent);
        Assert.NotNull(updateEvent.UpdatedItems);
        Assert.Contains(updateEvent.UpdatedItems, i => i.RunId == "run-002" && i.Status == "Completed");

        await subCts.CancelAsync();
        await subscribeTask;
    }

    // -----------------------------------------------------------------------
    // Connection cap rejects N+1 connection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConnectionCap_RejectsExcessConnections()
    {
        _reader.SetSnapshot(new DataSyncActivitySnapshot([], []));
        await _monitor.StartAsync(CancellationToken.None);

        using var subCts = CancellationTokenSource.CreateLinkedTokenSource(_testCts.Token);

        // Open MaxConnections (2) concurrent subscriptions.
        var ch1 = Channel.CreateUnbounded<DataSyncActivityEvent>();
        var ch2 = Channel.CreateUnbounded<DataSyncActivityEvent>();
        var ch3 = Channel.CreateUnbounded<DataSyncActivityEvent>();

        var sub1 = Task.Run(() => _monitor.SubscribeAsync(ch1.Writer, subCts.Token), _testCts.Token);
        var sub2 = Task.Run(() => _monitor.SubscribeAsync(ch2.Writer, subCts.Token), _testCts.Token);

        // Give the subscriptions time to acquire the semaphore slots.
        await Task.Delay(TimeSpan.FromMilliseconds(200), _testCts.Token);

        // The 3rd connection must be immediately rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _monitor.SubscribeAsync(ch3.Writer, CancellationToken.None));

        await subCts.CancelAsync();
        await Task.WhenAll(sub1, sub2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<DataSyncActivityEvent> ReadWithTimeoutAsync(
        ChannelReader<DataSyncActivityEvent> reader,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await reader.ReadAsync(cts.Token);
    }

    private static DataSyncActivityItem MakeItem(string runId, string status) =>
        new(
            RunId: runId,
            Provider: "TestProvider",
            Dataset: "TestDataset",
            Status: status,
            StartedAt: T0,
            CompletedAt: status == "Completed" ? T0.AddSeconds(2) : null,
            DurationMs: status == "Completed" ? 2000L : null,
            ProcessedRecords: 0,
            ErrorCount: 0,
            ErrorMessage: null,
            TriggerSource: "Manual",
            RequestedShamsiMonth: null,
            LogicalVendor: null,
            PhysicalSource: null,
            SourceMode: null);

    // -----------------------------------------------------------------------
    // Stubs / fakes
    // -----------------------------------------------------------------------

    private sealed class ControllableDataSyncActivityReader : IDataSyncActivityReader
    {
        private DataSyncActivitySnapshot _snapshot = new([], []);

        public void SetSnapshot(DataSyncActivitySnapshot snapshot) => _snapshot = snapshot;

        public Task<DataSyncActivitySnapshot> GetSnapshotAsync(
            int recentPerProvider, CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);
    }

    private sealed class NullHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() { }
    }
}
