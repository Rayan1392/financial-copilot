using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// Run tracking. The partial unique index on <c>Status = 'Running'</c> is the lease: a second start
/// fails at the database, so two processes can never run a sync at the same time.
/// </summary>
public sealed class EfCoreCyclicalWavesV2SyncRunStore(FinancialIngestionDbContext db) : ICyclicalWavesV2SyncRunStore
{
    private const string Requested = nameof(CyclicalWavesV2SyncRunStatus.Requested);
    private const string Running = nameof(CyclicalWavesV2SyncRunStatus.Running);

    public async Task<CyclicalWavesV2RequestResult> RequestAsync(
        CyclicalWavesV2SyncRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var active = await FindActiveRunIdAsync(cancellationToken);
        if (active is not null)
        {
            return new CyclicalWavesV2RequestResult(active.Value, Accepted: false);
        }

        var row = NewRow(request, Requested, now);
        db.CyclicalWavesV2SyncRuns.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return new CyclicalWavesV2RequestResult(row.Id, Accepted: true);
    }

    public async Task<Guid?> TryStartAsync(
        CyclicalWavesV2SyncRequest request,
        string lockOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        var row = NewRow(request, Running, now);
        row.StartedAt = now;
        row.LockOwner = lockOwner;
        row.LockLeaseExpiresAt = leaseExpiresAt;
        db.CyclicalWavesV2SyncRuns.Add(row);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return row.Id;
        }
        catch (DbUpdateException)
        {
            if (!await IsRunningAsync(cancellationToken))
            {
                throw;
            }

            var skipped = NewRow(request, nameof(CyclicalWavesV2SyncRunStatus.SkippedAlreadyRunning), now);
            skipped.CompletedAt = now;
            db.CyclicalWavesV2SyncRuns.Add(skipped);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<CyclicalWavesV2ClaimedRun?> ClaimNextRequestedAsync(
        string lockOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        var row = await db.CyclicalWavesV2SyncRuns
            .Where(run => run.Status == Requested)
            .OrderBy(run => run.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        row.Status = Running;
        row.StartedAt = now;
        row.LockOwner = lockOwner;
        row.LockLeaseExpiresAt = leaseExpiresAt;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new CyclicalWavesV2ClaimedRun(row.Id, ToRequest(row));
        }
        catch (DbUpdateException)
        {
            if (!await IsRunningAsync(cancellationToken))
            {
                throw;
            }

            // Another run holds the lease; the request stays queued for the next poll.
            return null;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task HeartbeatAsync(Guid runId, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken) =>
        await db.CyclicalWavesV2SyncRuns
            .Where(run => run.Id == runId && run.Status == Running)
            .ExecuteUpdateAsync(setters => setters.SetProperty(run => run.LockLeaseExpiresAt, leaseExpiresAt), cancellationToken);

    public async Task CompleteAsync(
        Guid runId,
        CyclicalWavesV2SyncRunStatus status,
        CyclicalWavesV2RunCounts counts,
        string? errorSummary,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = await db.CyclicalWavesV2SyncRuns.SingleAsync(run => run.Id == runId, cancellationToken);
        row.Status = status.ToString();
        row.CompletedAt = now;
        row.TotalItems = counts.TotalItems;
        row.ProcessedItems = counts.ProcessedItems;
        row.UpdatedItems = counts.UpdatedItems;
        row.UnchangedItems = counts.UnchangedItems;
        row.FailedItems = counts.FailedItems;
        row.ErrorSummary = errorSummary is { Length: > 4_096 } ? errorSummary[..4_096] : errorSummary;
        row.LockOwner = null;
        row.LockLeaseExpiresAt = null;
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<int> ExpireStaleLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stale = await db.CyclicalWavesV2SyncRuns
            .Where(run => run.Status == Running && run.LockLeaseExpiresAt != null)
            .ToListAsync(cancellationToken);
        var expired = stale.Where(run => run.LockLeaseExpiresAt < now).ToList();
        foreach (var run in expired)
        {
            run.Status = nameof(CyclicalWavesV2SyncRunStatus.Failed);
            run.CompletedAt = now;
            run.ErrorSummary = "RunFailed: LeaseExpired";
            run.LockOwner = null;
            run.LockLeaseExpiresAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return expired.Count;
    }

    public async Task<DateTimeOffset?> GetLastCompletedFullRunStartAsync(CancellationToken cancellationToken)
    {
        var full = nameof(CyclicalWavesV2SyncScope.Full);
        var completed = new[]
        {
            nameof(CyclicalWavesV2SyncRunStatus.Succeeded),
            nameof(CyclicalWavesV2SyncRunStatus.CompletedWithErrors)
        };
        var starts = await db.CyclicalWavesV2SyncRuns
            .AsNoTracking()
            .Where(run => run.Scope == full && completed.Contains(run.Status) && run.StartedAt != null)
            .Select(run => run.StartedAt)
            .ToListAsync(cancellationToken);
        return starts.Max();
    }

    public async Task<IReadOnlyList<CyclicalWavesV2SyncRunSummary>> GetRecentRunsAsync(
        int count,
        CancellationToken cancellationToken)
    {
        var rows = await db.CyclicalWavesV2SyncRuns
            .AsNoTracking()
            .OrderByDescending(run => run.RequestedAt)
            .Take(Math.Clamp(count, 1, 100))
            .ToListAsync(cancellationToken);
        return rows.Select(row => new CyclicalWavesV2SyncRunSummary(
                row.Id,
                Enum.Parse<CyclicalWavesV2SyncScope>(row.Scope),
                row.TickerFilter,
                row.AnalysisMode is null ? null : Enum.Parse<CyclicalWavesV2AnalysisMode>(row.AnalysisMode),
                Enum.Parse<CyclicalWavesV2SyncTrigger>(row.TriggerSource),
                Enum.Parse<CyclicalWavesV2SyncRunStatus>(row.Status),
                row.RequestedAt,
                row.StartedAt,
                row.CompletedAt,
                new CyclicalWavesV2RunCounts(row.TotalItems, row.ProcessedItems, row.UpdatedItems, row.UnchangedItems, row.FailedItems),
                row.ErrorSummary,
                row.RequestedBy))
            .ToList();
    }

    private async Task<Guid?> FindActiveRunIdAsync(CancellationToken cancellationToken) =>
        await db.CyclicalWavesV2SyncRuns
            .AsNoTracking()
            .Where(run => run.Status == Requested || run.Status == Running)
            .OrderBy(run => run.RequestedAt)
            .Select(run => (Guid?)run.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<bool> IsRunningAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        return await db.CyclicalWavesV2SyncRuns.AsNoTracking().AnyAsync(run => run.Status == Running, cancellationToken);
    }

    private static CyclicalWavesV2SyncRunRow NewRow(CyclicalWavesV2SyncRequest request, string status, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Scope = request.Scope.ToString(),
        TickerFilter = request.Ticker,
        AnalysisMode = request.AnalysisMode?.ToString(),
        TriggerSource = request.Trigger.ToString(),
        Status = status,
        RequestedAt = now,
        RequestedBy = request.RequestedBy
    };

    private static CyclicalWavesV2SyncRequest ToRequest(CyclicalWavesV2SyncRunRow row) => new(
        Enum.Parse<CyclicalWavesV2SyncScope>(row.Scope),
        row.TickerFilter,
        row.AnalysisMode is null ? null : Enum.Parse<CyclicalWavesV2AnalysisMode>(row.AnalysisMode),
        Enum.Parse<CyclicalWavesV2SyncTrigger>(row.TriggerSource),
        row.RequestedBy);
}
