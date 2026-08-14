using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// PostgreSQL is the Feature 126 lifecycle authority. Seq is called only after the commit as a
/// telemetry export and can never allocate, acknowledge, or mutate lifecycle state.
public sealed class Feature126PostgresEventSink(
    FinancialIngestionDbContext db,
    SeqFeature126EventSink telemetry,
    TimeProvider clock) : IFeature126DurableEventSink, IFeature126AtomicTerminalEventSink
{
    public async Task<Feature126EventAppendAcknowledgement> AppendAsync(
        Feature126EventAppendRequest request, CancellationToken cancellationToken)
        => await AppendCoreAsync(request, null, cancellationToken);

    public async Task<Feature126EventAppendAcknowledgement> AppendTerminalAsync(
        Feature126EventAppendRequest request, LeaseState terminalState, CancellationToken cancellationToken)
        => await AppendCoreAsync(request, terminalState, cancellationToken);

    private async Task<Feature126EventAppendAcknowledgement> AppendCoreAsync(
        Feature126EventAppendRequest request, LeaseState? terminalState, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({request.RunId}, 0))", cancellationToken);

        // Event identity is authoritative for replays. Check it before consulting the live
        // lease so an identical event remains idempotent after its terminal transition expired
        // or fenced the lease.
        var duplicate = await db.Feature126Events.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EventId == request.EventId, cancellationToken);
        if (duplicate is not null)
        {
            if (!HasSameImmutableIdentity(duplicate, request))
                throw new Feature126EventAppendException(Feature126AppendRejection.EventIdentityConflict,
                    "The Feature 126 event id is already bound to different immutable event identity or content.");

            await transaction.CommitAsync(cancellationToken);
            return Acknowledgement(duplicate, true, false);
        }

        // The lease row is the fencing authority. The event stream is only a lifecycle
        // projection and must never be sufficient to authorize an append.
        var lease = db.Database.IsRelational()
            ? await db.IndustryRelativeValuationSourceLeases
                .FromSqlInterpolated($"SELECT * FROM \"IndustryRelativeValuationSourceLeases\" WHERE \"LeaseName\" = {Feature126ObservabilityConstants.LeaseName} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken)
            : await db.IndustryRelativeValuationSourceLeases
                .SingleOrDefaultAsync(x => x.LeaseName == Feature126ObservabilityConstants.LeaseName, cancellationToken);
        var now = clock.GetUtcNow();
        if (lease is null || lease.ExpiresAtUtc <= now ||
            !string.Equals(lease.CurrentRunId, request.RunId, StringComparison.Ordinal) ||
            !LeaseFencingEnvelope.TryParse(lease.Owner, out var leaseOwner) || leaseOwner is null ||
            leaseOwner.FencingToken != request.FencingToken ||
            leaseOwner.State is not (LeaseState.Running or LeaseState.Handoff))
            throw new Feature126EventAppendException(Feature126AppendRejection.StaleOwner,
                "The Feature 126 append is not authorized by the active lease row.");

        // Recovery lineage is copied from the takeover recorded on the locked lease row;
        // request correlation is never accepted as lineage authority.
        var authoritativeRequest = request with { RecoveredFromRunId = lease.SupersededRunId };

        var stream = await db.Feature126EventStreams
            .SingleOrDefaultAsync(x => x.RunId == request.RunId, cancellationToken);
        if (stream is not null)
        {
            if (stream.FencingToken != authoritativeRequest.FencingToken)
                throw new Feature126EventAppendException(Feature126AppendRejection.StaleOwner,
                    "A stale Feature 126 owner cannot append lifecycle events.");
            if (stream.IsTerminal)
                throw new Feature126EventAppendException(Feature126AppendRejection.TerminalConflict,
                    "Feature 126 run is already terminal.");
            if (!string.Equals(stream.State, authoritativeRequest.ExpectedPredecessorState, StringComparison.Ordinal))
                throw new Feature126EventAppendException(Feature126AppendRejection.InvalidPredecessor,
                    "Feature 126 event predecessor does not match the durable run state.");
        }
        else if (authoritativeRequest.EventType != Feature126LifecycleEventType.RunStarted ||
                 authoritativeRequest.ExpectedPredecessorState != "None")
        {
            throw new Feature126EventAppendException(Feature126AppendRejection.InvalidPredecessor,
                "A new Feature 126 stream must begin with RunStarted from None.");
        }

        var sequence = stream?.NextSequence ?? 1;
        if (authoritativeRequest.ExpectedNextSequence is not null && authoritativeRequest.ExpectedNextSequence != sequence)
            throw new Feature126EventAppendException(Feature126AppendRejection.OutOfOrder,
                "Feature 126 event sequence is not the next durable sequence.");

        var nextState = Feature126EventOrderingContract.NextState(authoritativeRequest.EventType, authoritativeRequest.ExpectedPredecessorState);
        var terminal = IsTerminal(authoritativeRequest.EventType);
        if (stream is null)
        {
            db.Feature126EventStreams.Add(new Feature126EventStreamRow
            {
                RunId = authoritativeRequest.RunId, TehranDate = authoritativeRequest.TehranDate, OwnerId = authoritativeRequest.OwnerId,
                FencingToken = authoritativeRequest.FencingToken, NextSequence = 2, State = nextState,
                IsTerminal = terminal, UpdatedAtUtc = now
            });
        }
        else
        {
            stream.NextSequence = sequence + 1;
            stream.State = nextState;
            stream.IsTerminal = terminal;
            stream.UpdatedAtUtc = now;
        }

        var row = new Feature126EventRow
        {
            EventId = authoritativeRequest.EventId, RunId = authoritativeRequest.RunId, EventSequence = sequence,
            EventType = authoritativeRequest.EventType.ToString(), ExpectedPredecessorState = authoritativeRequest.ExpectedPredecessorState,
            OwnerId = authoritativeRequest.OwnerId, FencingToken = authoritativeRequest.FencingToken, TehranDate = authoritativeRequest.TehranDate,
            AttemptReason = authoritativeRequest.AttemptReason, RecoveredFromRunId = authoritativeRequest.RecoveredFromRunId,
            OccurredAtUtc = authoritativeRequest.OccurredAtUtc, FieldsJson = JsonSerializer.Serialize(authoritativeRequest.Fields),
            SchemaVersion = authoritativeRequest.SchemaVersion, AppendedAtUtc = now
        };
        db.Feature126Events.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        if (terminalState is not null)
        {
            lease.Owner = new LeaseOwnerId(
                Feature126ObservabilityConstants.LeaseName,
                leaseOwner.CalculationDate,
                request.FencingToken,
                terminalState.Value).Envelope;
            lease.UpdatedAtUtc = now;
            lease.ExpiresAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        // Export is deliberately post-commit and best effort. It is not part of the authority.
        if (authoritativeRequest.EventType == Feature126LifecycleEventType.RunStarted)
        {
            // Startup is fail-closed: PostgreSQL has recorded the durable event, but provider
            // execution is not admitted until the required external acknowledgement completes.
            await telemetry.AppendAsync(authoritativeRequest with { ExpectedNextSequence = sequence }, CancellationToken.None);
        }
        else
        {
            try { await telemetry.AppendAsync(authoritativeRequest with { ExpectedNextSequence = sequence }, CancellationToken.None); }
            catch (Exception) { /* PostgreSQL remains authoritative when Seq is unavailable. */ }
        }
        return Acknowledgement(row, false, false);
    }

    public Task<bool> ProbeAsync(CancellationToken cancellationToken) => telemetry.ProbeAsync(cancellationToken);

    private static Feature126EventAppendAcknowledgement Acknowledgement(
        Feature126EventRow row, bool duplicate, bool stale) =>
        new(row.EventId, row.RunId, row.EventSequence, duplicate, stale, row.AppendedAtUtc);

    private static bool HasSameImmutableIdentity(
        Feature126EventRow existing, Feature126EventAppendRequest request) =>
        string.Equals(existing.RunId, request.RunId, StringComparison.Ordinal) &&
        string.Equals(existing.EventType, request.EventType.ToString(), StringComparison.Ordinal) &&
        string.Equals(existing.ExpectedPredecessorState, request.ExpectedPredecessorState, StringComparison.Ordinal) &&
        string.Equals(existing.OwnerId, request.OwnerId, StringComparison.Ordinal) &&
        existing.FencingToken == request.FencingToken &&
        string.Equals(existing.TehranDate, request.TehranDate, StringComparison.Ordinal) &&
        string.Equals(existing.AttemptReason, request.AttemptReason, StringComparison.Ordinal) &&
        string.Equals(existing.RecoveredFromRunId, request.RecoveredFromRunId, StringComparison.Ordinal) &&
        ToPostgresPrecision(existing.OccurredAtUtc) == ToPostgresPrecision(request.OccurredAtUtc) &&
        existing.SchemaVersion == request.SchemaVersion &&
        JsonNode.DeepEquals(ParseJson(existing.FieldsJson), JsonSerializer.SerializeToNode(request.Fields));

    private static JsonNode? ParseJson(string json) =>
        JsonNode.Parse(json);

    private static DateTimeOffset ToPostgresPrecision(DateTimeOffset value) =>
        new(value.UtcDateTime.Ticks - (value.UtcDateTime.Ticks % TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);

    private static bool IsTerminal(Feature126LifecycleEventType eventType) => eventType is
        Feature126LifecycleEventType.RunSucceeded or Feature126LifecycleEventType.RunPartiallySucceeded or
        Feature126LifecycleEventType.RunFailed or Feature126LifecycleEventType.RunCancelled or
        Feature126LifecycleEventType.RunTimedOut or Feature126LifecycleEventType.RunLeaseLost or
        Feature126LifecycleEventType.HandoffFailed;
}
