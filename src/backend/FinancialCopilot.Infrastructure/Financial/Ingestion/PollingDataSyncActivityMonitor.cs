using System.Threading.Channels;
using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed class DataSyncMonitorOptions
{
    public const string SectionName = "DataSyncMonitor";

    /// <summary>How often the monitor polls for state changes (minimum 2 s, default 5 s).</summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>Maximum active SSE connections per API instance (default 10).</summary>
    public int MaxConnections { get; set; } = 10;
}

/// <summary>
/// Singleton monitor that maintains one polling loop against <see cref="IDataSyncActivityReader"/>,
/// diffs snapshots, and fan-outs events to all active SSE subscriber channels (spec 058 task 7).
/// Uses <see cref="System.Threading.Channels"/> for lock-free fan-out; no external broker.
/// </summary>
public sealed class PollingDataSyncActivityMonitor : IDataSyncActivityMonitor, IHostedService, IAsyncDisposable
{
    private const int HeartbeatSeconds = 15;
    private const int DefaultRecentPerProvider = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataSyncMonitorOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<PollingDataSyncActivityMonitor> _logger;

    private readonly SemaphoreSlim _connectionSlot;
    private readonly object _subscriberLock = new();
    private readonly List<ChannelWriter<DataSyncActivityEvent>> _subscribers = [];

    private CancellationTokenSource? _cts;
    private Task? _pollLoop;

    // Last known snapshot used for diffing; null before first poll.
    private DataSyncActivitySnapshot? _lastSnapshot;

    public PollingDataSyncActivityMonitor(
        IServiceScopeFactory scopeFactory,
        IOptions<DataSyncMonitorOptions> options,
        IHostApplicationLifetime lifetime,
        ILogger<PollingDataSyncActivityMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _lifetime = lifetime;
        _logger = logger;
        _connectionSlot = new SemaphoreSlim(_options.MaxConnections, _options.MaxConnections);
    }

    public int ActiveConnections => _options.MaxConnections - _connectionSlot.CurrentCount;

    // -----------------------------------------------------------------------
    // IHostedService
    // -----------------------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _pollLoop = Task.Run(() => PollLoopAsync(_cts.Token), CancellationToken.None);
        _lifetime.ApplicationStopping.Register(OnApplicationStopping);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            try { await _cts.CancelAsync(); }
            catch (ObjectDisposedException) { }
        }

        if (_pollLoop is not null)
            await _pollLoop.ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // IDataSyncActivityMonitor
    // -----------------------------------------------------------------------

    public async Task<DataSyncActivitySnapshot> GetSnapshotAsync(
        int recentPerProvider,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IDataSyncActivityReader>();
        return await reader.GetSnapshotAsync(recentPerProvider, cancellationToken);
    }

    public async Task SubscribeAsync(
        ChannelWriter<DataSyncActivityEvent> writer,
        CancellationToken cancellationToken)
    {
        if (!await _connectionSlot.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("SSE connection limit reached.");

        try
        {
            // Send the current snapshot immediately as the first event.
            var snapshot = await GetSnapshotAsync(DefaultRecentPerProvider, cancellationToken);
            await writer.WriteAsync(
                new DataSyncActivityEvent(DataSyncActivityEventKind.Snapshot, Snapshot: snapshot),
                cancellationToken);

            lock (_subscriberLock)
                _subscribers.Add(writer);

            try
            {
                // Wait until the client disconnects.
                await cancellationToken.WhenCancelledAsync();
            }
            finally
            {
                lock (_subscriberLock)
                    _subscribers.Remove(writer);
            }
        }
        finally
        {
            _connectionSlot.Release();
            writer.TryComplete();
        }
    }

    // -----------------------------------------------------------------------
    // Poll loop
    // -----------------------------------------------------------------------

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(2, _options.PollingIntervalSeconds));
        var lastHeartbeat = DateTimeOffset.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);

                DataSyncActivitySnapshot newSnapshot;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var reader = scope.ServiceProvider.GetRequiredService<IDataSyncActivityReader>();
                    newSnapshot = await reader.GetSnapshotAsync(DefaultRecentPerProvider, cancellationToken);
                }

                var changedItems = Diff(_lastSnapshot, newSnapshot);
                _lastSnapshot = newSnapshot;

                if (changedItems.Count > 0)
                {
                    lastHeartbeat = DateTimeOffset.UtcNow;
                    await BroadcastAsync(
                        new DataSyncActivityEvent(DataSyncActivityEventKind.Update, UpdatedItems: changedItems),
                        cancellationToken);
                }
                else if ((DateTimeOffset.UtcNow - lastHeartbeat).TotalSeconds >= HeartbeatSeconds)
                {
                    lastHeartbeat = DateTimeOffset.UtcNow;
                    await BroadcastAsync(
                        new DataSyncActivityEvent(DataSyncActivityEventKind.Heartbeat,
                            HeartbeatAt: lastHeartbeat),
                        cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DataSyncActivityMonitor poll iteration failed.");
            }
        }
    }

    private static IReadOnlyList<DataSyncActivityItem> Diff(
        DataSyncActivitySnapshot? previous,
        DataSyncActivitySnapshot current)
    {
        if (previous is null)
            return [.. current.ActiveRuns, .. current.RecentRuns];

        var previousMap = BuildMap(previous);
        var changed = new List<DataSyncActivityItem>();

        foreach (var item in current.ActiveRuns.Concat(current.RecentRuns))
        {
            if (!previousMap.TryGetValue(item.RunId, out var prev)
                || prev.Status != item.Status
                || prev.ProcessedRecords != item.ProcessedRecords
                || prev.ErrorCount != item.ErrorCount)
            {
                changed.Add(item);
            }
        }

        return changed;
    }

    private static Dictionary<string, DataSyncActivityItem> BuildMap(DataSyncActivitySnapshot snapshot)
    {
        var map = new Dictionary<string, DataSyncActivityItem>(StringComparer.Ordinal);
        foreach (var item in snapshot.ActiveRuns.Concat(snapshot.RecentRuns))
            map[item.RunId] = item;
        return map;
    }

    private async Task BroadcastAsync(
        DataSyncActivityEvent @event,
        CancellationToken cancellationToken)
    {
        ChannelWriter<DataSyncActivityEvent>[] snapshot;
        lock (_subscriberLock)
            snapshot = [.. _subscribers];

        foreach (var writer in snapshot)
        {
            try
            {
                await writer.WriteAsync(@event, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Failed to write SSE event to subscriber; likely disconnected.");
            }
        }
    }

    private void OnApplicationStopping()
    {
        var closeEvent = new DataSyncActivityEvent(
            DataSyncActivityEventKind.Close,
            CloseReason: "Server shutting down");

        ChannelWriter<DataSyncActivityEvent>[] snapshot;
        lock (_subscriberLock)
            snapshot = [.. _subscribers];

        foreach (var writer in snapshot)
        {
            try
            {
                writer.TryWrite(closeEvent);
                writer.TryComplete();
            }
            catch
            {
                // Best-effort on shutdown.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            try { await cts.CancelAsync(); }
            catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        _connectionSlot.Dispose();
    }
}

/// <summary>Extension to await a <see cref="CancellationToken"/> as an awaitable task.</summary>
internal static class CancellationTokenExtensions
{
    internal static Task WhenCancelledAsync(this CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), tcs);
        return tcs.Task;
    }
}
