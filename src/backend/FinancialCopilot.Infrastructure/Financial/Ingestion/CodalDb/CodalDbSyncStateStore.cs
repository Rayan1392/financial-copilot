using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Per-dataset watermark used by the nightly CodalDB incremental sync. One logical aggregate
/// ("CodalDb") keeps the highest <c>ModifiedDateTime</c> we have successfully enqueued — the next
/// run only considers rows newer than this.
/// </summary>
public interface ICodalDbSyncStateStore
{
    Task<DateTimeOffset?> GetWatermarkAsync(string dataset, CancellationToken cancellationToken);

    Task RecordRunStartAsync(string dataset, DateTimeOffset startedAt, CancellationToken cancellationToken);

    Task AdvanceWatermarkAsync(
        string dataset,
        DateTimeOffset watermark,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);
}

public sealed class EfCoreCodalDbSyncStateStore(FinancialIngestionDbContext dbContext) : ICodalDbSyncStateStore
{
    public async Task<DateTimeOffset?> GetWatermarkAsync(string dataset, CancellationToken cancellationToken)
    {
        var row = await dbContext.CodalDbSyncStates.AsNoTracking()
            .SingleOrDefaultAsync(state => state.Dataset == dataset, cancellationToken);
        return row?.LastSyncedModifiedDateTime;
    }

    public async Task RecordRunStartAsync(string dataset, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var row = await dbContext.CodalDbSyncStates
            .SingleOrDefaultAsync(state => state.Dataset == dataset, cancellationToken);
        if (row is null)
        {
            row = new CodalDbSyncStateRow { Dataset = dataset };
            dbContext.CodalDbSyncStates.Add(row);
        }

        row.LastRunStartedAt = startedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AdvanceWatermarkAsync(
        string dataset,
        DateTimeOffset watermark,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.CodalDbSyncStates
            .SingleOrDefaultAsync(state => state.Dataset == dataset, cancellationToken);
        if (row is null)
        {
            row = new CodalDbSyncStateRow { Dataset = dataset };
            dbContext.CodalDbSyncStates.Add(row);
        }

        // Watermark is monotonically non-decreasing.
        if (row.LastSyncedModifiedDateTime is null || watermark > row.LastSyncedModifiedDateTime)
        {
            row.LastSyncedModifiedDateTime = watermark;
        }
        row.LastRunCompletedAt = completedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
