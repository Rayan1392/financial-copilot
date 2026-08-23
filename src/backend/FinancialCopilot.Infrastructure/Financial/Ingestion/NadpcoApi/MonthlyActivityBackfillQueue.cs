using System.Threading.Channels;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Single-flight in-process queue for the manual monthly-activity backfill.
/// The durable coordinator remains responsible for resume/idempotency after restarts.
/// </summary>
public sealed class MonthlyActivityBackfillQueue : IMonthlyActivityBackfillQueue
{
    private readonly Channel<MonthlyActivityBackfillRequest> _channel =
        Channel.CreateBounded<MonthlyActivityBackfillRequest>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private int _queuedOrRunning;

    public bool TryQueue(MonthlyActivityBackfillRequest request)
    {
        if (Interlocked.CompareExchange(ref _queuedOrRunning, 1, 0) != 0)
        {
            return false;
        }

        if (_channel.Writer.TryWrite(request))
        {
            return true;
        }

        Volatile.Write(ref _queuedOrRunning, 0);
        return false;
    }

    public async Task<MonthlyActivityBackfillRequest> DequeueAsync(
        CancellationToken cancellationToken) =>
        await _channel.Reader.ReadAsync(cancellationToken);

    public void MarkFinished() => Volatile.Write(ref _queuedOrRunning, 0);
}
