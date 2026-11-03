using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class Feature126DurableEventAppendIntegrationTests(PostgreSqlIntegrationFixture fixture)
{
    [SkippableFact]
    public async Task Restart_replay_reads_durable_sequence_and_state()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        await using (var first = database.CreateContext())
        {
            await SeedRunningLeaseAsync(first, runId, token);
            Assert.Equal(1, (await Appender(first).AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token), CancellationToken.None)).EventSequence);
        }

        await using var restarted = database.CreateContext();
        var replayed = await Appender(restarted).AppendAsync(Request("heartbeat", runId, Feature126LifecycleEventType.Heartbeat, "Running", token), CancellationToken.None);
        Assert.Equal(2, replayed.EventSequence);
        Assert.Equal(2, await restarted.Feature126Events.CountAsync());
    }

    [SkippableFact]
    public async Task Duplicate_event_is_rejected_idempotently_after_new_appender_instance()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        var request = Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token);
        await using (var first = database.CreateContext())
        {
            await SeedRunningLeaseAsync(first, runId, token);
            Assert.False((await Appender(first).AppendAsync(request, CancellationToken.None)).IsDuplicate);
        }
        await using var replay = database.CreateContext();
        var result = await Appender(replay).AppendAsync(request, CancellationToken.None);
        Assert.True(result.IsDuplicate);
        Assert.Equal(1, result.EventSequence);
        Assert.Equal(1, await replay.Feature126Events.CountAsync());
    }

    [SkippableFact]
    public async Task Same_event_id_with_different_payload_is_rejected_without_mutating_stream()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        var original = Request("same-id", runId, Feature126LifecycleEventType.RunStarted, "None", token,
            new Dictionary<string, object?> { ["attempt"] = 1 });
        await using var context = database.CreateContext();
        await SeedRunningLeaseAsync(context, runId, token);
        await Appender(context).AppendAsync(original, CancellationToken.None);

        var conflict = original with
        {
            Fields = new Dictionary<string, object?> { ["attempt"] = 2 }
        };
        var exception = await Assert.ThrowsAsync<Feature126EventAppendException>(() =>
            Appender(context).AppendAsync(conflict, CancellationToken.None));

        Assert.Equal(Feature126AppendRejection.EventIdentityConflict, exception.Rejection);
        var stream = await context.Feature126EventStreams.SingleAsync(x => x.RunId == runId);
        Assert.Equal(2, stream.NextSequence);
        Assert.Equal("Running", stream.State);
        Assert.Equal(1, await context.Feature126Events.CountAsync());
    }

    [SkippableFact]
    public async Task Same_event_id_across_different_run_ids_is_rejected()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var firstRunId = RunId();
        var secondRunId = RunId();
        var firstToken = Guid.NewGuid();
        var secondToken = Guid.NewGuid();
        await using var context = database.CreateContext();
        await SeedRunningLeaseAsync(context, firstRunId, firstToken);
        await Appender(context).AppendAsync(
            Request("same-id", firstRunId, Feature126LifecycleEventType.RunStarted, "None", firstToken), CancellationToken.None);

        // Model a completed lease takeover so the duplicate identity check is reached
        // under the new authoritative owner instead of being rejected as stale first.
        var lease = await context.IndustryRelativeValuationSourceLeases.SingleAsync();
        lease.Owner = new LeaseOwnerId(Feature126ObservabilityConstants.LeaseName,
            new(2026, 8, 13), secondToken, LeaseState.Running).Envelope;
        lease.CurrentRunId = secondRunId;
        lease.SupersededRunId = firstRunId;
        lease.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5);
        lease.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<Feature126EventAppendException>(() =>
            Appender(context).AppendAsync(
                Request("same-id", secondRunId, Feature126LifecycleEventType.RunStarted, "None", secondToken), CancellationToken.None));

        Assert.Equal(Feature126AppendRejection.EventIdentityConflict, exception.Rejection);
        Assert.Equal(1, await context.Feature126Events.CountAsync());
        Assert.False(await context.Feature126EventStreams.AnyAsync(x => x.RunId == secondRunId));
    }

    [SkippableFact]
    public async Task Same_event_id_with_different_terminal_event_is_rejected()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        await using var context = database.CreateContext();
        await SeedRunningLeaseAsync(context, runId, token);
        var original = Request("terminal-id", runId, Feature126LifecycleEventType.RunSucceeded, "Running", token);
        await Appender(context).AppendAsync(
            Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token), CancellationToken.None);
        await Appender(context).AppendAsync(original, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<Feature126EventAppendException>(() =>
            Appender(context).AppendAsync(original with { EventType = Feature126LifecycleEventType.RunFailed }, CancellationToken.None));

        Assert.Equal(Feature126AppendRejection.EventIdentityConflict, exception.Rejection);
        var stream = await context.Feature126EventStreams.SingleAsync(x => x.RunId == runId);
        Assert.Equal(3, stream.NextSequence);
        Assert.Equal("Success", stream.State);
        Assert.Equal(2, await context.Feature126Events.CountAsync());
    }

    [SkippableFact]
    public async Task Concurrent_workers_cannot_commit_conflicting_lifecycle_transitions()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            await SeedRunningLeaseAsync(seed, runId, token);
            await Appender(seed).AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token), CancellationToken.None);
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstTask = Appender(first).AppendAsync(Request("handoff", runId, Feature126LifecycleEventType.HandoffStarted, "Running", token), CancellationToken.None);
        var secondTask = Appender(second).AppendAsync(Request("failed", runId, Feature126LifecycleEventType.RunFailed, "Running", token), CancellationToken.None);
        var results = await Task.WhenAll(Capture(firstTask), Capture(secondTask));
        Assert.Single(results, x => x?.Acknowledgement is not null);
        Assert.Single(results, x => x?.Rejection is Feature126AppendRejection.InvalidPredecessor or Feature126AppendRejection.TerminalConflict);
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.Feature126Events.CountAsync());
    }

    [SkippableFact]
    public async Task Stale_fencing_token_is_rejected_durably()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        await using var context = database.CreateContext();
        await SeedRunningLeaseAsync(context, runId, token);
        await Appender(context).AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token), CancellationToken.None);
        var exception = await Assert.ThrowsAsync<Feature126EventAppendException>(() =>
            Appender(context).AppendAsync(Request("stale", runId, Feature126LifecycleEventType.Heartbeat, "Running", Guid.NewGuid()), CancellationToken.None));
        Assert.Equal(Feature126AppendRejection.StaleOwner, exception.Rejection);
    }

    [SkippableFact]
    public async Task Lease_row_fences_old_owner_and_authorizes_new_owner()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var oldRun = RunId();
        var newRun = RunId();
        var oldToken = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            seed.IndustryRelativeValuationSourceLeases.Add(new IndustryRelativeValuationSourceLeaseRow
            {
                LeaseName = Feature126ObservabilityConstants.LeaseName,
                Owner = new LeaseOwnerId("feature126", new(2026, 8, 13), oldToken, LeaseState.Running).Envelope,
                CurrentRunId = oldRun,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
            });
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var leases = new IndustryRelativeValuationLeaseStore(context, TimeProvider.System);
        var takeover = await leases.TryAcquireAsync(Feature126ObservabilityConstants.LeaseName,
            new(2026, 8, 13), TimeSpan.FromMinutes(5), CancellationToken.None, newRun);
        Assert.NotNull(takeover);
        Assert.Equal(oldRun, takeover!.SupersededRunId);

        await Assert.ThrowsAsync<Feature126EventAppendException>(() =>
            Appender(context).AppendAsync(Request("old-owner", oldRun, Feature126LifecycleEventType.RunStarted,
                "None", oldToken), CancellationToken.None));
        var accepted = await Appender(context).AppendAsync(Request("new-owner", newRun,
            Feature126LifecycleEventType.RunStarted, "None", takeover.FencingToken), CancellationToken.None);
        Assert.Equal(1, accepted.EventSequence);
    }

    [SkippableFact]
    public async Task Recovery_lineage_is_taken_from_superseded_lease_run()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var oldRun = RunId();
        var newRun = RunId();
        var token = Guid.NewGuid();
        await using var context = database.CreateContext();
        context.IndustryRelativeValuationSourceLeases.Add(new IndustryRelativeValuationSourceLeaseRow
        {
            LeaseName = Feature126ObservabilityConstants.LeaseName,
            Owner = new LeaseOwnerId("feature126", new(2026, 8, 13), token, LeaseState.Running).Envelope,
            CurrentRunId = newRun,
            SupersededRunId = oldRun,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        await Appender(context).AppendAsync(Request("recovery", newRun, Feature126LifecycleEventType.RunStarted,
            "None", token) with { RecoveredFromRunId = "caller-correlation-must-not-win" }, CancellationToken.None);
        Assert.Equal(oldRun, await context.Feature126Events.Select(x => x.RecoveredFromRunId).SingleAsync());
    }

    [SkippableFact]
    public async Task Terminal_state_is_immutable_and_terminal_event_is_singleton()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        await using var context = database.CreateContext();
        await SeedRunningLeaseAsync(context, runId, token);
        await Appender(context).AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token), CancellationToken.None);
        await Appender(context).AppendAsync(Request("terminal", runId, Feature126LifecycleEventType.RunSucceeded, "Running", token), CancellationToken.None);
        var exception = await Assert.ThrowsAsync<Feature126EventAppendException>(() =>
            Appender(context).AppendAsync(Request("after-terminal", runId, Feature126LifecycleEventType.Heartbeat, "Running", token), CancellationToken.None));
        Assert.Equal(Feature126AppendRejection.TerminalConflict, exception.Rejection);
        Assert.Equal(2, await context.Feature126Events.CountAsync());
    }

    [SkippableFact]
    public async Task Terminal_success_commits_event_and_lease_transition_atomically()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        await using var context = database.CreateContext();
        await SeedRunningLeaseAsync(context, runId, token);
        var appender = Appender(context);
        await appender.AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token), CancellationToken.None);

        await ((IFeature126TerminalEventAppender)appender).AppendTerminalAsync(
            Request("terminal", runId, Feature126LifecycleEventType.RunSucceeded, "Running", token),
            LeaseState.Succeeded, CancellationToken.None);

        var lease = await context.IndustryRelativeValuationSourceLeases.SingleAsync();
        Assert.True(LeaseFencingEnvelope.TryParse(lease.Owner, out var owner));
        Assert.Equal(LeaseState.Succeeded, owner!.State);
        Assert.Equal("Success", await context.Feature126EventStreams.Where(x => x.RunId == runId).Select(x => x.State).SingleAsync());
    }

    [SkippableFact]
    public async Task Terminal_failure_commits_failed_event_and_failed_lease_transition_atomically()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        await using var context = database.CreateContext();
        await SeedRunningLeaseAsync(context, runId, token);
        var appender = Appender(context);
        await appender.AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token), CancellationToken.None);

        await ((IFeature126TerminalEventAppender)appender).AppendTerminalAsync(
            Request("terminal", runId, Feature126LifecycleEventType.RunFailed, "Running", token),
            LeaseState.Failed, CancellationToken.None);

        var lease = await context.IndustryRelativeValuationSourceLeases.SingleAsync();
        Assert.True(LeaseFencingEnvelope.TryParse(lease.Owner, out var owner));
        Assert.Equal(LeaseState.Failed, owner!.State);
        Assert.Equal("Failed", await context.Feature126EventStreams.Where(x => x.RunId == runId).Select(x => x.State).SingleAsync());
    }

    [SkippableFact]
    public async Task Identical_terminal_event_replay_is_idempotent_after_lease_transition()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        var terminal = Request("terminal-replay", runId, Feature126LifecycleEventType.RunSucceeded, "Running", token);
        await using (var first = database.CreateContext())
        {
            await SeedRunningLeaseAsync(first, runId, token);
            var appender = Appender(first);
            await appender.AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token), CancellationToken.None);
            await ((IFeature126TerminalEventAppender)appender).AppendTerminalAsync(terminal, LeaseState.Succeeded, CancellationToken.None);
        }

        await using var replay = database.CreateContext();
        var acknowledgement = await Appender(replay).AppendAsync(terminal, CancellationToken.None);

        Assert.True(acknowledgement.IsDuplicate);
        Assert.Equal(2, acknowledgement.EventSequence);
        Assert.Equal(2, await replay.Feature126Events.CountAsync());
        Assert.Equal("Success", await replay.Feature126EventStreams.Where(x => x.RunId == runId).Select(x => x.State).SingleAsync());
    }

    [SkippableFact]
    public async Task Changed_terminal_event_payload_replay_is_rejected_as_identity_conflict_after_lease_transition()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        var terminal = Request("terminal-conflict", runId, Feature126LifecycleEventType.RunFailed, "Running", token,
            new Dictionary<string, object?> { ["reason"] = "original" });
        await using (var first = database.CreateContext())
        {
            await SeedRunningLeaseAsync(first, runId, token);
            var appender = Appender(first);
            await appender.AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token), CancellationToken.None);
            await ((IFeature126TerminalEventAppender)appender).AppendTerminalAsync(terminal, LeaseState.Failed, CancellationToken.None);
        }

        await using var replay = database.CreateContext();
        var exception = await Assert.ThrowsAsync<Feature126EventAppendException>(() =>
            Appender(replay).AppendAsync(terminal with
            {
                Fields = new Dictionary<string, object?> { ["reason"] = "changed" }
            }, CancellationToken.None));

        Assert.Equal(Feature126AppendRejection.EventIdentityConflict, exception.Rejection);
        Assert.Equal(2, await replay.Feature126Events.CountAsync());
        Assert.Equal("Failed", await replay.Feature126EventStreams.Where(x => x.RunId == runId).Select(x => x.State).SingleAsync());
    }

    [SkippableFact]
    public async Task Terminal_append_failure_rolls_back_event_and_does_not_change_running_lease()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runId = RunId();
        var token = Guid.NewGuid();
        await using var context = database.CreateContext();
        await SeedRunningLeaseAsync(context, runId, token);
        var appender = Appender(context);
        await appender.AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token), CancellationToken.None);

        var tooLongEventId = new string('x', 257);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            ((IFeature126TerminalEventAppender)appender).AppendTerminalAsync(
                Request(tooLongEventId, runId, Feature126LifecycleEventType.RunSucceeded, "Running", token),
                LeaseState.Succeeded, CancellationToken.None));

        Assert.Equal(1, await context.Feature126Events.CountAsync());
        var lease = await context.IndustryRelativeValuationSourceLeases.SingleAsync();
        Assert.True(LeaseFencingEnvelope.TryParse(lease.Owner, out var owner));
        Assert.Equal(LeaseState.Running, owner!.State);
        Assert.Equal("Running", await context.Feature126EventStreams.Where(x => x.RunId == runId).Select(x => x.State).SingleAsync());
    }

    private static Feature126EventAppender Appender(FinancialIngestionDbContext context) =>
        new(new Feature126PostgresEventSink(context, Telemetry(), TimeProvider.System));

    private static SeqFeature126EventSink Telemetry() => new(new HttpClient(new SuccessHandler()),
        new Feature126TelemetryOptions { Enabled = true, SeqEndpoint = "https://seq.test" }, TimeProvider.System);

    private static string RunId() => Feature126RunId.Create(new(2026, 8, 13), DateTimeOffset.UtcNow);

    private static async Task SeedRunningLeaseAsync(
        FinancialIngestionDbContext context,
        string runId,
        Guid fencingToken)
    {
        var now = DateTimeOffset.UtcNow;
        context.IndustryRelativeValuationSourceLeases.Add(new IndustryRelativeValuationSourceLeaseRow
        {
            LeaseName = Feature126ObservabilityConstants.LeaseName,
            Owner = new LeaseOwnerId("feature126", new(2026, 8, 13), fencingToken, LeaseState.Running).Envelope,
            CurrentRunId = runId,
            ExpiresAtUtc = now.AddMinutes(5),
            UpdatedAtUtc = now
        });
        await context.SaveChangesAsync();
    }

    private static Feature126EventAppendRequest Request(string id, string runId, Feature126LifecycleEventType type, string predecessor, Guid token,
        IReadOnlyDictionary<string, object?>? fields = null) =>
        new(id, runId, type, predecessor, "worker", token, "2026-08-13", "integration", null,
            DateTimeOffset.UtcNow, fields ?? new Dictionary<string, object?>());

    private static async Task<Captured?> Capture(Task<Feature126EventAppendAcknowledgement> task)
    {
        try { return new(await task, null); }
        catch (Feature126EventAppendException exception) { return new(null, exception.Rejection); }
    }

    private sealed record Captured(Feature126EventAppendAcknowledgement? Acknowledgement, Feature126AppendRejection? Rejection);

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
