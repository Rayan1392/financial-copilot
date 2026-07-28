using System.Threading.Channels;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Normalised view of one sync run, regardless of which provider or sub-system produced it.
/// Used by the live-monitor snapshot endpoint and SSE stream (spec 058).
/// </summary>
public sealed record DataSyncActivityItem(
    string RunId,
    string Provider,
    string Dataset,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMs,
    int ProcessedRecords,
    int ErrorCount,
    string? ErrorMessage,
    string TriggerSource,
    string? RequestedShamsiMonth,
    string? LogicalVendor,
    string? PhysicalSource,
    string? SourceMode);

/// <summary>
/// Aggregated activity snapshot returned by <see cref="IDataSyncActivityReader"/> (spec 058).
/// </summary>
public sealed record DataSyncActivitySnapshot(
    IReadOnlyCollection<DataSyncActivityItem> ActiveRuns,
    IReadOnlyCollection<DataSyncActivityItem> RecentRuns);

/// <summary>
/// Reads current activity across all sync providers without any writes or side-effects.
/// </summary>
public interface IDataSyncActivityReader
{
    /// <param name="recentPerProvider">
    /// Max number of terminal runs to include per logical provider (1–20; default 5).
    /// </param>
    Task<DataSyncActivitySnapshot> GetSnapshotAsync(
        int recentPerProvider,
        CancellationToken cancellationToken);
}

// ---------------------------------------------------------------------------
// SSE monitor contracts
// ---------------------------------------------------------------------------

public enum DataSyncActivityEventKind
{
    Snapshot = 0,
    Update = 1,
    Heartbeat = 2,
    Close = 3
}

/// <summary>
/// Discriminated event emitted by <see cref="IDataSyncActivityMonitor"/> to SSE subscribers.
/// </summary>
public sealed record DataSyncActivityEvent(
    DataSyncActivityEventKind Kind,
    DataSyncActivitySnapshot? Snapshot = null,
    IReadOnlyCollection<DataSyncActivityItem>? UpdatedItems = null,
    DateTimeOffset? HeartbeatAt = null,
    string? CloseReason = null);

/// <summary>
/// Singleton monitor that polls <see cref="IDataSyncActivityReader"/>, diffs snapshots, and
/// fan-outs events to all active SSE subscriber channels (spec 058).
/// </summary>
public interface IDataSyncActivityMonitor
{
    Task<DataSyncActivitySnapshot> GetSnapshotAsync(
        int recentPerProvider,
        CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes <paramref name="writer"/> to the live event stream. The monitor writes
    /// <see cref="DataSyncActivityEvent"/> instances until the <paramref name="cancellationToken"/>
    /// is cancelled or the monitor emits a <see cref="DataSyncActivityEventKind.Close"/> event.
    /// </summary>
    Task SubscribeAsync(
        ChannelWriter<DataSyncActivityEvent> writer,
        CancellationToken cancellationToken);

    /// <summary>Current number of active SSE subscribers (for connection-cap enforcement).</summary>
    int ActiveConnections { get; }
}
