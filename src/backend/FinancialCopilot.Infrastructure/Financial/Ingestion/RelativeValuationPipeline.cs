using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed class RelativeValuationIngestionOptions
{
    public const string SectionName = "Feature126:RelativeValuationIngestion";
    public bool Enabled { get; init; }
    public int DailyCadenceMinutes { get; init; } = 1440;
    public int PageSize { get; init; } = 100;
    public int MaximumConcurrency { get; init; } = 1;
    public int RetryCount { get; init; } = 2;
    public int RetryDelayMilliseconds { get; init; }
    public int CompanyTimeoutSeconds { get; init; } = 120;
    public int LeaseMinutes { get; init; } = 120;
    public int LeaseHeartbeatSeconds { get; init; }
    public string ConfigurationRevision { get; init; } = "local-development";
    public string DeploymentIdentifier { get; init; } = "local-development";
    public bool LegacyFeature114PsOwnerEnabled { get; init; }
    public bool NadpcoFeature125TriggerEnabled { get; init; }
}

public interface RuntimeActivationGate
{
    Feature126ActivationDecision Evaluate();
}

public sealed class Feature126RuntimeActivationGate(
    IOptions<RelativeValuationIngestionOptions> options) : RuntimeActivationGate
{
    public Feature126ActivationDecision Evaluate()
    {
        var value = options.Value;
        return Feature126ActivationGuard.EvaluateActivation(
            value.ConfigurationRevision,
            value.DeploymentIdentifier,
            new Feature126OwnerActivationStates(
                value.Enabled,
                value.LegacyFeature114PsOwnerEnabled,
                value.NadpcoFeature125TriggerEnabled));
    }
}

public interface IFeature126RelativeValuationPipeline
{
    Task<Feature126IngestionRunResult> RunAsync(string? correlationId, CancellationToken cancellationToken);
}

/// <summary>Feature 126 Slice 2 acquisition boundary. It does not calculate or publish Feature 125 results.</summary>
public sealed class RelativeValuationPipeline(
    IEligibleUniverseReader universeReader,
    ICyclicalWavesPsAcceptedOperation psOperation,
    ICyclicalWavesRelativeValuationProviderClient provider,
    IFeature126SourceFactStore factStore,
    IFeature126LeaseStore leaseStore,
    IOptions<RelativeValuationIngestionOptions> options,
    TimeProvider clock,
    ILogger<RelativeValuationPipeline> logger,
    IFeature125HandoffSubmissionBoundary handoffBoundary,
    RuntimeActivationGate? activationGate = null,
    IFeature126OperationalSummarySink? summarySink = null,
    IFeature126EventAppender? eventAppender = null,
    IFeature114AcceptedPsVisualizationPersistence? visualizationPersistence = null,
    IFeature126RuntimeLifecycleObserver? lifecycleObserver = null) : IFeature126RelativeValuationPipeline
{
    private const string LeaseName = "feature126";
    private readonly RelativeValuationIngestionOptions settings = options.Value;

    public async Task<Feature126IngestionRunResult> RunAsync(string? correlationId, CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId.Trim();
        var startedAtUtc = clock.GetUtcNow();
        var tehranDate = TehranDate(startedAtUtc);
        if (!settings.Enabled)
            return Publish(new(id, tehranDate, 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<Feature126MetricOutcome>()),
                CreateSummary(id, startedAtUtc, tehranDate, Feature126RunState.Disabled,
                    Feature126LeaseStatus.NotAttempted, false, false, null,
                    Array.Empty<Feature126MetricOutcome>(), "Disabled", Feature126HandoffStatus.NotApplicable, null));

        var decision = (activationGate ?? new Feature126RuntimeActivationGate(options)).Evaluate();
        if (!decision.Allowed)
        {
            logger.LogWarning("Feature 126 activation rejected. reason={Reason} correlationId={CorrelationId}.",
                decision.RejectionReason, id);
            var activationOutcomes = new[] {
                new Feature126MetricOutcome(null, null, RelativeValuationSourceKind.PEGauge,
                    "Skipped", decision.RejectionReason?.ToString()) };
            return Publish(new(id, tehranDate, 0, 0, 0, 0, 0, 0, 0, 0, activationOutcomes),
                CreateSummary(id, startedAtUtc, tehranDate, Feature126RunState.ActivationGuardRejected,
                    Feature126LeaseStatus.NotAttempted, false, true, null, activationOutcomes,
                    decision.RejectionReason?.ToString(), Feature126HandoffStatus.NotApplicable,
                    decision.RejectionReason?.ToString()));
        }

        // Caller correlation is diagnostic context only; lifecycle lineage is durable run state.
        id = Feature126RunId.Create(tehranDate, startedAtUtc);

        if (leaseStore is IFeature126LeaseRecoveryStore recoveryStore &&
            await recoveryStore.HasSucceededAsync(LeaseName, tehranDate, cancellationToken))
            return Publish(new(id, tehranDate, 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<Feature126MetricOutcome>()),
                CreateSummary(id, startedAtUtc, tehranDate, Feature126RunState.CurrentDaySucceededNoOp,
                    Feature126LeaseStatus.NotAttempted, false, true, null,
                    Array.Empty<Feature126MetricOutcome>(), "CurrentDaySucceededNoOp", Feature126HandoffStatus.NotApplicable, null));

        LeaseHandle? owner;
        try
        {
            owner = await leaseStore.TryAcquireAsync(LeaseName, tehranDate,
                TimeSpan.FromMinutes(Math.Max(1, settings.LeaseMinutes)), cancellationToken, id);
        }
        catch (Exception)
        {
            var acquisitionFailure = cancellationToken.IsCancellationRequested
                ? Feature126RunState.Cancelled
                : Feature126RunState.Failed;
            var acquisitionCode = cancellationToken.IsCancellationRequested ? "Cancelled" : "UnexpectedFailure";
            var acquisitionSummary = CreateSummary(id, startedAtUtc, tehranDate, acquisitionFailure,
                Feature126LeaseStatus.NotAttempted, false, true, null, Array.Empty<Feature126MetricOutcome>(),
                acquisitionCode, Feature126HandoffStatus.NotApplicable, acquisitionCode);
            Publish(new(id, tehranDate, 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<Feature126MetricOutcome>()), acquisitionSummary);
            throw;
        }
        if (owner is null)
        {
            var contentionOutcomes = new[] { new Feature126MetricOutcome(null, null, RelativeValuationSourceKind.PEGauge, "Skipped", "LeaseContended") };
            return Publish(new(id, tehranDate, 0, 0, 0, 0, 0, 0, 0, 0, contentionOutcomes),
                CreateSummary(id, startedAtUtc, tehranDate, Feature126RunState.Failed,
                    Feature126LeaseStatus.Contended, false, true, null, contentionOutcomes,
                    "LeaseContended", Feature126HandoffStatus.NotApplicable, "LeaseContended"));
        }

        // Allocation is not evidence. The durable acknowledgement below is the sole gate before
        // any universe read or provider call. A scheduled attempt always gets a new sortable id.
        await AppendRequiredAsync(new Feature126EventAppendRequest(
            EventId: $"{id}:run_started", RunId: id, EventType: Feature126LifecycleEventType.RunStarted,
            ExpectedPredecessorState: "None", OwnerId: settings.DeploymentIdentifier,
            FencingToken: owner.FencingToken, TehranDate: tehranDate.ToString("yyyy-MM-dd"),
            AttemptReason: owner.RecoveredLease ? "takeover" : "scheduled", RecoveredFromRunId: owner.SupersededRunId,
            OccurredAtUtc: startedAtUtc, Fields: new Dictionary<string, object?>
            {
                ["lifecycle_state"] = "Running", ["lease_name"] = LeaseName,
                ["configuration_revision"] = settings.ConfigurationRevision,
                ["lease_status"] = owner.RecoveredLease ? "Recovered" : "Owned"
            }, ExpectedNextSequence: 1), cancellationToken);
        lifecycleObserver?.MarkRunning(id, tehranDate);

        var outcomes = new List<Feature126MetricOutcome>();
        var persisted = 0;
        var unchanged = 0;
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseLost = 0;
        var handoffStarted = false;
        IReadOnlyList<RelativeValuationEligibleSymbol> admitted = Array.Empty<RelativeValuationEligibleSymbol>();
        var pages = 0;
        var failed = 0;
        var partial = 0;
        var skipped = 0;
        var terminalEventAppended = false;
        var heartbeat = StartHeartbeatAsync(id, tehranDate, owner, runCancellation, () => Interlocked.Exchange(ref leaseLost, 1));
        using var persistenceGate = new SemaphoreSlim(1, 1);
        try
        {
            admitted = (await universeReader.ReadAsync(runCancellation.Token))
                .OrderBy(x => x.SymbolIsin ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(x => x.CompanyId)
                .ToArray();
            var pageSize = Math.Max(1, settings.PageSize);
            foreach (var page in admitted.Chunk(pageSize))
            {
                pages++;
                foreach (var symbol in page)
                {
                    var result = await ProcessCompanyAsync(symbol, owner, runCancellation.Token, persistenceGate);
                    persisted += result.Persisted;
                    unchanged += result.Unchanged;
                    failed += result.Failed;
                    partial += result.Partial;
                    skipped += result.Skipped;
                    outcomes.AddRange(result.Outcomes);
                }
            }

            var snapshot = await factStore.ReadCurrentSnapshotAsync(tehranDate, admitted, CancellationToken.None);
            var package = Feature126HandoffPackage.Create(
                new Feature126RunIdentity(id, tehranDate),
                owner.FencingToken,
                snapshot.Facts);
            handoffStarted = true;
            await AppendRequiredAsync(new Feature126EventAppendRequest(
                $"{id}:handoff_started", id, Feature126LifecycleEventType.HandoffStarted, "Running",
                settings.DeploymentIdentifier, owner.FencingToken, tehranDate.ToString("yyyy-MM-dd"),
                owner.RecoveredLease ? "takeover" : "scheduled", owner.SupersededRunId, clock.GetUtcNow(),
                new Dictionary<string, object?> { ["lifecycle_state"] = "Handoff", ["eligible_companies"] = admitted.Count }),
                CancellationToken.None);
            if (!await leaseStore.TransitionAsync(owner, LeaseState.Handoff, CancellationToken.None))
                throw new InvalidOperationException("Feature 126 lease rejected the handoff transition.");

            await StopHeartbeatAsync(heartbeat, runCancellation);
            var handoffLease = new Feature126HandoffLeaseState(
                owner.LeaseName,
                owner.CalculationDate,
                LeaseState.Handoff,
                owner.FencingToken,
                owner.ExpiresAtUtc);
            var handoff = await handoffBoundary.SubmitAsync(
                package,
                handoffLease,
                clock.GetUtcNow(),
                CancellationToken.None);
            if (!handoff.Accepted)
                throw new InvalidOperationException(
                    $"Feature 125 handoff rejected: {handoff.RejectionReason}.");

            if (Volatile.Read(ref leaseLost) != 0 || !await leaseStore.IsOwnerAsync(owner, CancellationToken.None))
                throw new InvalidOperationException("Feature 126 lease was lost during acquisition.");
            await AppendTerminalRequiredAsync(new Feature126EventAppendRequest(
                $"{id}:terminal", id,
                failed > 0 ? Feature126LifecycleEventType.RunPartiallySucceeded : Feature126LifecycleEventType.RunSucceeded,
                "Handoff", settings.DeploymentIdentifier, owner.FencingToken, tehranDate.ToString("yyyy-MM-dd"),
                owner.RecoveredLease ? "takeover" : "scheduled", owner.SupersededRunId, clock.GetUtcNow(),
                new Dictionary<string, object?> { ["lifecycle_state"] = failed > 0 ? "PartialSuccess" : "Success", ["handoff_status"] = "Succeeded", ["failed_companies"] = failed }),
                LeaseState.Succeeded, owner, CancellationToken.None);
            terminalEventAppended = true;
            logger.LogInformation("Feature 126 ingestion completed. correlationId={CorrelationId} admitted={Admitted} pages={Pages} persisted={Persisted} unchanged={Unchanged} failed={Failed} partial={Partial} skipped={Skipped}.", id, admitted.Count, pages, persisted, unchanged, failed, partial, skipped);
            var successResult = new Feature126IngestionRunResult(id, tehranDate, admitted.Count, pages,
                outcomes.Count(x => x.Status is "Succeeded" or "Unchanged"), failed, partial, skipped,
                persisted, unchanged, outcomes);
            return Publish(successResult, CreateSummary(id, startedAtUtc, tehranDate,
                failed > 0 ? Feature126RunState.PartialSuccess : Feature126RunState.Success,
                owner.RecoveredLease ? Feature126LeaseStatus.Recovered : Feature126LeaseStatus.Owned,
                 owner.RecoveredLease, true, admitted.Count, outcomes, "Completed",
                 Feature126HandoffStatus.Succeeded, null));
        }
        catch (Exception exception)
        {
            if (terminalEventAppended)
                throw;
            runCancellation.Cancel();
            await StopHeartbeatAsync(heartbeat, runCancellation);
            if (Volatile.Read(ref leaseLost) != 0)
            {
                var summary = CreateSummary(id, startedAtUtc, tehranDate, Feature126RunState.LeaseLost,
                    Feature126LeaseStatus.Lost, owner.RecoveredLease, true, admitted.Count, outcomes,
                    "LeaseLost", handoffStarted ? Feature126HandoffStatus.Failed : Feature126HandoffStatus.NotApplicable,
                    "LeaseLost");
                Publish(new(id, tehranDate, admitted.Count, pages, outcomes.Count(x => x.Status is "Succeeded" or "Unchanged"), failed, partial, skipped, persisted, unchanged, outcomes), summary);
                await AppendRequiredAsync(new Feature126EventAppendRequest(
                    $"{id}:terminal", id, Feature126LifecycleEventType.RunLeaseLost, handoffStarted ? "Handoff" : "Running",
                    settings.DeploymentIdentifier, owner.FencingToken, tehranDate.ToString("yyyy-MM-dd"), "takeover", owner.SupersededRunId,
                    clock.GetUtcNow(), new Dictionary<string, object?> { ["lifecycle_state"] = "LeaseLost", ["termination_code"] = "LeaseLost" }), CancellationToken.None);
                throw new InvalidOperationException("Feature 126 lease was lost during execution.", exception);
            }
            var cancelled = exception is OperationCanceledException || cancellationToken.IsCancellationRequested;
            var timeout = exception is TimeoutException;
            var state = handoffStarted ? Feature126RunState.HandoffFailed : cancelled ? Feature126RunState.Cancelled : timeout ? Feature126RunState.Timeout : Feature126RunState.Failed;
            var code = handoffStarted ? "HandoffFailed" : cancelled ? "Cancelled" : timeout ? "Timeout" : "UnexpectedFailure";
            var failureSummary = CreateSummary(id, startedAtUtc, tehranDate, state,
                owner.RecoveredLease ? Feature126LeaseStatus.Recovered : Feature126LeaseStatus.Owned,
                owner.RecoveredLease, true, admitted.Count, outcomes, code,
                handoffStarted ? Feature126HandoffStatus.Failed : Feature126HandoffStatus.NotApplicable, code);
            Publish(new(id, tehranDate, admitted.Count, pages, outcomes.Count(x => x.Status is "Succeeded" or "Unchanged"), failed, partial, skipped, persisted, unchanged, outcomes), failureSummary);
            await AppendTerminalRequiredAsync(new Feature126EventAppendRequest(
                $"{id}:terminal", id, state switch { Feature126RunState.Cancelled => Feature126LifecycleEventType.RunCancelled, Feature126RunState.Timeout => Feature126LifecycleEventType.RunTimedOut, Feature126RunState.HandoffFailed => Feature126LifecycleEventType.HandoffFailed, _ => Feature126LifecycleEventType.RunFailed },
                handoffStarted ? "Handoff" : "Running", settings.DeploymentIdentifier, owner.FencingToken, tehranDate.ToString("yyyy-MM-dd"),
                owner.RecoveredLease ? "takeover" : "scheduled", owner.SupersededRunId, clock.GetUtcNow(),
                new Dictionary<string, object?> { ["lifecycle_state"] = state.ToString(), ["termination_code"] = code }),
                LeaseState.Failed, owner, CancellationToken.None);
            throw;
        }
    }

    private Feature126IngestionRunResult Publish(Feature126IngestionRunResult result, Feature126OperationalSummary summary)
    {
        summarySink?.Publish(summary);
        return result with { OperationalSummary = summary };
    }

    private Feature126OperationalSummary CreateSummary(
        string correlationId,
        DateTimeOffset startedAtUtc,
        DateOnly tehranDate,
        Feature126RunState state,
        Feature126LeaseStatus leaseStatus,
        bool recoveredLease,
        bool enabled,
        int? eligibleCompanies,
        IReadOnlyList<Feature126MetricOutcome> outcomes,
        string? terminationCode,
        Feature126HandoffStatus handoffStatus,
        string? failureCode)
    {
        var failures = outcomes.Where(x => !string.IsNullOrWhiteSpace(x.FailureCode))
            .GroupBy(x => Feature126FailureCodes.Canonicalize(x.FailureCode), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => (long?)x.LongCount(), StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(failureCode))
            failures[Feature126FailureCodes.Canonicalize(failureCode)] = failures.GetValueOrDefault(Feature126FailureCodes.Canonicalize(failureCode)) + 1;
        var metrics = Enum.GetValues<RelativeValuationSourceKind>()
            .Where(x => x is RelativeValuationSourceKind.PSGauge or RelativeValuationSourceKind.PEGauge or RelativeValuationSourceKind.EquilibriumGauge)
            .ToDictionary(metric => metric.ToString(), metric => new Feature126MetricCounts(
                outcomes.LongCount(x => x.Metric == metric && x.Status == "Succeeded"),
                outcomes.LongCount(x => x.Metric == metric && x.Status == "Unchanged"),
                outcomes.LongCount(x => x.Metric == metric && x.Status == "Failed")), StringComparer.Ordinal);
        var endpoints = metrics.ToDictionary(x => x.Key, x => new Feature126EndpointCounts(
            outcomes.LongCount(outcome => outcome.Metric.ToString() == x.Key && outcome.Status != "Skipped"),
            outcomes.LongCount(outcome => outcome.Metric.ToString() == x.Key && outcome.Status is "Succeeded" or "Unchanged"),
            outcomes.LongCount(outcome => outcome.Metric.ToString() == x.Key && outcome.Status == "Failed")), StringComparer.Ordinal);
        var succeededCompanies = outcomes.Where(x => x.CompanyId is not null && (x.Status is "Succeeded" or "Unchanged"))
            .GroupBy(x => x.CompanyId).Count(group => group.Count() >= 3);
        var failedCompanies = outcomes.Where(x => x.CompanyId is not null && x.Status == "Failed").Select(x => x.CompanyId).Distinct().Count();
        var now = clock.GetUtcNow();
        return Feature126OperationalSummaryFactory.Create(correlationId, startedAtUtc, now, tehranDate, enabled, state,
            leaseStatus, recoveredLease, eligibleCompanies, outcomes.Select(x => x.CompanyId).Where(x => x is not null).Distinct().Count(),
            succeededCompanies, failedCompanies, metrics, failures, endpoints, terminationCode, handoffStatus, null, null);
    }

    private async Task<CompanyResult> ProcessCompanyAsync(
        RelativeValuationEligibleSymbol symbol,
        LeaseHandle owner,
        CancellationToken runToken,
        SemaphoreSlim persistenceGate)
    {
        if (symbol.CompanyId is null || string.IsNullOrWhiteSpace(symbol.SymbolIsin))
            return new(0, 0, 0, 0, 1, AddSkippedOutcomesToArray(symbol));

        using var companyTimeout = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        companyTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.CompanyTimeoutSeconds)));
        // Keep provider traffic strictly serial: PE, then PS, then equilibrium.
        var acquisitions = new List<Acquisition>(3);
        foreach (var metric in new[]
        {
            RelativeValuationSourceKind.PEGauge,
            RelativeValuationSourceKind.PSGauge,
            RelativeValuationSourceKind.EquilibriumGauge
        })
        {
            acquisitions.Add(await AcquireAsync(metric, symbol.SymbolIsin!, companyTimeout.Token, runToken));
        }

        var resultOutcomes = new List<Feature126MetricOutcome>();
        var companySuccess = 0;
        var companyPersisted = 0;
        var companyUnchanged = 0;
        var companyFailed = 0;
        foreach (var acquisition in acquisitions)
        {
            if (!acquisition.Result.IsSuccess)
            {
                logger.LogError("Feature 126 {Metric} acquisition returned failure for {SymbolIsin}. Readiness={Readiness}, QualityCode={QualityCode}, Endpoint={Endpoint}, Attempts={Attempts}.",
                    acquisition.Metric, symbol.SymbolIsin, acquisition.Result.Readiness, acquisition.Result.QualityCode,
                    acquisition.Result.SourceEndpoint, acquisition.Attempts);
                companyFailed++;
                resultOutcomes.Add(new(symbol.CompanyId, symbol.SymbolIsin, acquisition.Metric, "Failed", acquisition.Result.QualityCode, acquisition.Attempts));
                continue;
            }

            await persistenceGate.WaitAsync(runToken);
            Feature126SourceFactWriteResult write;
            try
            {
                write = await factStore.PersistAcceptedAsync(symbol.CompanyId.Value, acquisition.Result, owner, runToken);
                if (write != Feature126SourceFactWriteResult.Rejected &&
                    acquisition.Metric == RelativeValuationSourceKind.PSGauge &&
                    acquisition.Gauge is not null && visualizationPersistence is not null)
                    await visualizationPersistence.PersistAcceptedGaugeAsync(
                        symbol.CompanyId.Value, symbol.SymbolIsin!, acquisition.Gauge,
                        acquisition.Result.FetchedAtUtc ?? clock.GetUtcNow(), null, runToken);
            }
            finally
            {
                persistenceGate.Release();
            }
            if (write == Feature126SourceFactWriteResult.Persisted) companyPersisted++;
            if (write == Feature126SourceFactWriteResult.Unchanged) companyUnchanged++;
            if (write == Feature126SourceFactWriteResult.Rejected)
            {
                logger.LogError("Feature 126 {Metric} fact persistence was rejected for {SymbolIsin}. Readiness={Readiness}, QualityCode={QualityCode}, Endpoint={Endpoint}.",
                    acquisition.Metric, symbol.SymbolIsin, acquisition.Result.Readiness, acquisition.Result.QualityCode,
                    acquisition.Result.SourceEndpoint);
                companyFailed++;
                resultOutcomes.Add(new(symbol.CompanyId, symbol.SymbolIsin, acquisition.Metric, "Failed", "PersistenceRejected", acquisition.Attempts));
                continue;
            }
            companySuccess++;
            resultOutcomes.Add(new(symbol.CompanyId, symbol.SymbolIsin, acquisition.Metric,
                write == Feature126SourceFactWriteResult.Unchanged ? "Unchanged" : "Succeeded", null, acquisition.Attempts));
        }
        return new(companyPersisted, companyUnchanged, companyFailed,
            companySuccess is > 0 and < 3 ? 1 : 0, 0, resultOutcomes);
    }

    private async Task StartHeartbeatAsync(string runId, DateOnly tehranDate, LeaseHandle owner, CancellationTokenSource runCancellation, Action leaseLost)
    {
        var seconds = settings.LeaseHeartbeatSeconds > 0
            ? settings.LeaseHeartbeatSeconds
            : Math.Max(1, settings.LeaseMinutes * 60 / 3);
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), runCancellation.Token);
                if (!await leaseStore.RenewAsync(owner, TimeSpan.FromMinutes(Math.Max(1, settings.LeaseMinutes)), runCancellation.Token))
                {
                    leaseLost();
                    runCancellation.Cancel();
                    return;
                }
                await AppendRequiredAsync(new Feature126EventAppendRequest(
                    $"{runId}:heartbeat:{clock.GetUtcNow().ToUnixTimeMilliseconds()}", runId,
                    Feature126LifecycleEventType.Heartbeat, "Running", settings.DeploymentIdentifier,
                    owner.FencingToken, tehranDate.ToString("yyyy-MM-dd"), owner.RecoveredLease ? "takeover" : "scheduled",
                    null, clock.GetUtcNow(), new Dictionary<string, object?> { ["last_heartbeat_at_utc"] = clock.GetUtcNow() }), runCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Feature 126 lease heartbeat failed.");
            leaseLost();
            runCancellation.Cancel();
        }
    }

    private async Task AppendRequiredAsync(Feature126EventAppendRequest request, CancellationToken cancellationToken)
    {
        if (eventAppender is null) return; // legacy/unit composition; production DI always supplies Seq.
        await eventAppender.AppendAsync(request, cancellationToken);
    }

    private async Task AppendTerminalRequiredAsync(
        Feature126EventAppendRequest request, LeaseState terminalState, LeaseHandle owner, CancellationToken cancellationToken)
    {
        if (eventAppender is IFeature126TerminalEventAppender atomicAppender)
        {
            await atomicAppender.AppendTerminalAsync(request, terminalState, cancellationToken);
            return;
        }

        // Test/legacy compositions without the PostgreSQL atomic boundary retain the old
        // behavior; production DI always uses Feature126PostgresEventSink.
        await AppendRequiredAsync(request, cancellationToken);
        if (!await leaseStore.TransitionAsync(owner, terminalState, cancellationToken))
            throw new InvalidOperationException("Feature 126 lease rejected the terminal transition.");
    }

    private static async Task StopHeartbeatAsync(Task heartbeat, CancellationTokenSource cancellation)
    {
        cancellation.Cancel();
        await heartbeat;
    }

    private static IReadOnlyList<Feature126MetricOutcome> AddSkippedOutcomesToArray(RelativeValuationEligibleSymbol symbol) =>
        Enum.GetValues<RelativeValuationSourceKind>()
            .Where(x => x is RelativeValuationSourceKind.PSGauge or RelativeValuationSourceKind.PEGauge or RelativeValuationSourceKind.EquilibriumGauge)
            .Select(metric => new Feature126MetricOutcome(symbol.CompanyId, symbol.SymbolIsin, metric, "Skipped", "MissingAdmissionIdentity"))
            .ToArray();

    private sealed record CompanyResult(int Persisted, int Unchanged, int Failed, int Partial, int Skipped, IReadOnlyList<Feature126MetricOutcome> Outcomes);

    private async Task<Acquisition> AcquireAsync(
        RelativeValuationSourceKind metric,
        string isin,
        CancellationToken companyToken,
        CancellationToken runToken)
    {
        var attempts = 0;
        RelativeValuationProviderResult result;
        PsGaugeDistribution? gauge = null;
        do
        {
            attempts++;
            try
            {
                if (metric == RelativeValuationSourceKind.PSGauge)
                {
                    var ps = await AcquirePsAsync(isin, companyToken);
                    result = ps.Result;
                    gauge = ps.Gauge;
                }
                else
                    result = metric == RelativeValuationSourceKind.PEGauge
                        ? await provider.GetPeGaugeAsync(isin, companyToken)
                        : await provider.GetEquilibriumGaugeAsync(isin, companyToken);
            }
            catch (OperationCanceledException) when (!runToken.IsCancellationRequested)
            {
                logger.LogError("Feature 126 {Metric} acquisition timed out for {SymbolIsin} on attempt {Attempt}.", metric, isin, attempts);
                result = Failure(metric, "Timeout");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Feature 126 {Metric} acquisition failed for {SymbolIsin} on attempt {Attempt}. ExceptionType={ExceptionType}.", metric, isin, attempts, ex.GetType().FullName);
                result = Failure(metric, ex is TimeoutException ? "Timeout" : "NetworkFailure");
            }

            if (result.IsSuccess || !IsRetryable(result.Readiness) || attempts > settings.RetryCount)
                break;
            if (settings.RetryDelayMilliseconds > 0)
                await Task.Delay(settings.RetryDelayMilliseconds, companyToken);
        } while (true);
        return new(metric, result, attempts, gauge);
    }

    private async Task<(RelativeValuationProviderResult Result, PsGaugeDistribution? Gauge)> AcquirePsAsync(string isin, CancellationToken token)
    {
        var result = await psOperation.AcquireAcceptedPsGaugeAsync(isin, token);
        if (!result.IsSuccess || result.Value is null)
            return (Failure(RelativeValuationSourceKind.PSGauge, MapPsReadiness(result.ErrorCode)), null);
        var gauge = result.Value;
        if (gauge.GaugeClose <= 0m || gauge.GaugeAverage <= 0m)
            return (Failure(RelativeValuationSourceKind.PSGauge, "InvalidNonPositiveInput"), null);
        var raw = $"{gauge.GaugeClose:G29}|{gauge.GaugeAverage:G29}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return (new(RelativeValuationSourceKind.PSGauge, gauge.GaugeClose, gauge.GaugeAverage,
            $"ps/circle-chart-data/{isin}:{hash}", $"ps/circle-chart-data/{isin}", $"requested-isin:{isin}",
            RelativeValuationFactReadiness.Ready, "Valid", hash, raw, clock.GetUtcNow()), gauge);
    }

    private static bool IsRetryable(RelativeValuationFactReadiness readiness) => readiness is
        RelativeValuationFactReadiness.Timeout or RelativeValuationFactReadiness.RateLimited or
        RelativeValuationFactReadiness.AuthenticationFailed or RelativeValuationFactReadiness.RemoteServerFailure or
        RelativeValuationFactReadiness.NetworkFailure;

    private static string MapPsReadiness(PsVisualizationSyncErrorCode code) => code switch
    {
        PsVisualizationSyncErrorCode.TimeoutOrNetworkFailure => nameof(RelativeValuationFactReadiness.NetworkFailure),
        PsVisualizationSyncErrorCode.RateLimited => nameof(RelativeValuationFactReadiness.RateLimited),
        PsVisualizationSyncErrorCode.AuthenticationFailed => nameof(RelativeValuationFactReadiness.AuthenticationFailed),
        PsVisualizationSyncErrorCode.RemoteServerFailure => nameof(RelativeValuationFactReadiness.RemoteServerFailure),
        PsVisualizationSyncErrorCode.NotFoundOrNoData => nameof(RelativeValuationFactReadiness.NotFoundOrNoData),
        _ => nameof(RelativeValuationFactReadiness.InvalidPayload)
    };

    private static RelativeValuationProviderResult Failure(RelativeValuationSourceKind metric, string code) =>
        new(metric, null, null, $"feature126:{metric}:{code}", string.Empty, string.Empty,
            Enum.TryParse<RelativeValuationFactReadiness>(code, out var readiness) ? readiness : RelativeValuationFactReadiness.InvalidPayload,
            code, string.Empty, string.Empty);

    private static void AddSkippedOutcomes(List<Feature126MetricOutcome> outcomes, RelativeValuationEligibleSymbol symbol)
    {
        outcomes.AddRange(Enum.GetValues<RelativeValuationSourceKind>().Where(x => x is RelativeValuationSourceKind.PSGauge or RelativeValuationSourceKind.PEGauge or RelativeValuationSourceKind.EquilibriumGauge)
            .Select(metric => new Feature126MetricOutcome(symbol.CompanyId, symbol.SymbolIsin, metric, "Skipped", "MissingAdmissionIdentity")));
    }

    private static DateOnly TehranDate(DateTimeOffset utc)
    {
        foreach (var id in new[] { "Asia/Tehran", "Iran Standard Time" })
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, zone).DateTime);
        return DateOnly.FromDateTime(utc.UtcDateTime);
    }

    private sealed record Acquisition(RelativeValuationSourceKind Metric, RelativeValuationProviderResult Result, int Attempts, PsGaugeDistribution? Gauge = null);
}
