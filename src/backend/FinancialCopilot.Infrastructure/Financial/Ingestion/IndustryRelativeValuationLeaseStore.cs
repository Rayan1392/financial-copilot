using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Database-backed renewable lease using the existing lease row. Owner contains
/// the state/date/fencing token envelope, so no schema change is required.
/// </summary>
public sealed class IndustryRelativeValuationLeaseStore(
    FinancialIngestionDbContext db,
    TimeProvider clock) : IFeature126LeaseStore, IFeature126LeaseRecoveryStore, IFeature126LeaseReadinessProbe
{
    public async Task<Feature126LeaseReadiness> ProbeReadinessAsync(CancellationToken cancellationToken)
    {
        var liveRow = await db.IndustryRelativeValuationSourceLeases
            .AsNoTracking()
            .AnyAsync(x => x.LeaseName == Feature126ObservabilityConstants.LeaseName, cancellationToken);
        // Renewal is a database-side compare-and-set operation. A live row plus a reachable
        // relational database proves the capability without extending or mutating the lease.
        var renewalCapable = liveRow && db.Database.IsRelational() && await db.Database.CanConnectAsync(cancellationToken);
        return new(liveRow, renewalCapable);
    }

    public Task<LeaseHandle?> TryAcquireAsync(
        string leaseName,
        DateOnly calculationDate,
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        TryAcquireAsync(leaseName, calculationDate, duration, cancellationToken, null);

    public async Task<bool> HasSucceededAsync(string leaseName, DateOnly calculationDate, CancellationToken cancellationToken)
    {
        var row = await db.IndustryRelativeValuationSourceLeases.AsNoTracking()
            .SingleOrDefaultAsync(x => x.LeaseName == leaseName, cancellationToken);
        return row is not null && LeaseFencingEnvelope.TryParse(row.Owner, out var owner) &&
               owner is not null && owner.State == LeaseState.Succeeded && owner.CalculationDate == calculationDate;
    }

    public async Task<LeaseHandle?> TryAcquireAsync(
        string leaseName,
        DateOnly calculationDate,
        TimeSpan duration,
        CancellationToken cancellationToken,
        string? runId = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            var row = await db.IndustryRelativeValuationSourceLeases
                .FromSqlInterpolated($"SELECT * FROM \"IndustryRelativeValuationSourceLeases\" WHERE \"LeaseName\" = {leaseName} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (row is not null && row.ExpiresAtUtc > now)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var recovered = row is not null &&
                LeaseFencingEnvelope.TryParse(row.Owner, out var previous) &&
                previous is not null && previous.CalculationDate == calculationDate &&
                previous.State == LeaseState.Running && row.ExpiresAtUtc <= now;
            var supersededRunId = recovered ? row!.CurrentRunId : null;
            runId ??= Feature126RunId.Create(calculationDate, now);

            var token = Guid.NewGuid();
            var expires = now.Add(duration);
            var owner = new LeaseOwnerId(leaseName, calculationDate, token, LeaseState.Running);
            if (row is null)
            {
                db.IndustryRelativeValuationSourceLeases.Add(new IndustryRelativeValuationSourceLeaseRow
                {
                    LeaseName = leaseName,
                    Owner = owner.Envelope,
                    CurrentRunId = runId,
                    SupersededRunId = null,
                    UpdatedAtUtc = now,
                    ExpiresAtUtc = expires
                });
            }
            else
            {
                row.Owner = owner.Envelope;
                row.CurrentRunId = runId;
                row.SupersededRunId = supersededRunId;
                row.UpdatedAtUtc = now;
                row.ExpiresAtUtc = expires;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new LeaseHandle(leaseName, calculationDate, token, expires, runId, supersededRunId) { RecoveredLease = recovered };
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return null;
        }
    }

    public async Task<bool> RenewAsync(
        LeaseHandle handle,
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        await UpdateOwnedRowAsync(handle, LeaseState.Running, duration, cancellationToken);

    public async Task<bool> IsOwnerAsync(
        LeaseHandle handle,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var row = await db.IndustryRelativeValuationSourceLeases.AsNoTracking()
            .SingleOrDefaultAsync(x => x.LeaseName == handle.LeaseName, cancellationToken);
        return row is not null && row.ExpiresAtUtc > now && HasToken(row.Owner, handle);
    }

    public Task<bool> TransitionAsync(
        LeaseHandle handle,
        LeaseState state,
        CancellationToken cancellationToken) =>
        UpdateOwnedRowAsync(handle, state, TimeSpan.Zero, cancellationToken);

    private async Task<bool> UpdateOwnedRowAsync(
        LeaseHandle handle,
        LeaseState state,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            var atomicNow = clock.GetUtcNow();
            var runningEnvelope = handle.RunningOwner.Envelope;
            var handoffEnvelope = new LeaseOwnerId(
                handle.LeaseName,
                handle.CalculationDate,
                handle.FencingToken,
                LeaseState.Handoff).Envelope;
            var newEnvelope = new LeaseOwnerId(
                handle.LeaseName,
                handle.CalculationDate,
                handle.FencingToken,
                state).Envelope;
            var expiresAt = state == LeaseState.Handoff
                ? handle.ExpiresAtUtc
                : duration > TimeSpan.Zero ? atomicNow.Add(duration) : atomicNow;
            var affected = state == LeaseState.Handoff
                ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "IndustryRelativeValuationSourceLeases"
                SET "Owner" = {newEnvelope},
                    "UpdatedAtUtc" = {atomicNow},
                    "ExpiresAtUtc" = {expiresAt}
                WHERE "LeaseName" = {handle.LeaseName}
                  AND "Owner" = {runningEnvelope}
                  AND "ExpiresAtUtc" > {atomicNow}
                """, cancellationToken)
                : await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "IndustryRelativeValuationSourceLeases"
                SET "Owner" = {newEnvelope},
                    "UpdatedAtUtc" = {atomicNow},
                    "ExpiresAtUtc" = {expiresAt}
                WHERE "LeaseName" = {handle.LeaseName}
                  AND ("Owner" = {runningEnvelope} OR "Owner" = {handoffEnvelope})
                  AND "ExpiresAtUtc" > {atomicNow}
                """, cancellationToken);
            return affected == 1;
        }

        var row = await db.IndustryRelativeValuationSourceLeases
            .SingleOrDefaultAsync(x => x.LeaseName == handle.LeaseName, cancellationToken);
        var now = clock.GetUtcNow();
        if (row is null || row.ExpiresAtUtc <= now || !HasToken(row.Owner, handle))
            return false;
        row.Owner = new LeaseOwnerId(handle.LeaseName, handle.CalculationDate, handle.FencingToken, state).Envelope;
        row.UpdatedAtUtc = now;
        row.ExpiresAtUtc = state == LeaseState.Handoff
            ? handle.ExpiresAtUtc
            : duration > TimeSpan.Zero ? now.Add(duration) : now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool HasToken(string envelope, LeaseHandle handle)
    {
        if (!LeaseFencingEnvelope.TryParse(envelope, out var owner) || owner is null)
            return false;
        return owner.CalculationDate == handle.CalculationDate &&
               owner.FencingToken == handle.FencingToken;
    }
}
