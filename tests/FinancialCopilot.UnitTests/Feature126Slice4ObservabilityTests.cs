using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Worker;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class Feature126Slice4ObservabilityTests
{
    [Fact]
    public void RunIdUsesTehranDateAndSortableUlidShape()
    {
        var id = Feature126RunId.Create(new(2026, 8, 13), DateTimeOffset.Parse("2026-08-13T08:00:00Z"));
        Assert.Matches("^fx126-20260813-[0-9A-HJKMNP-TV-Z]{26}$", id);
    }

    [Fact]
    public async Task AppenderAwaitsSinkAcknowledgementAndPreservesEventId()
    {
        var sink = new RecordingSink();
        var appender = new Feature126EventAppender(sink);
        var request = new Feature126EventAppendRequest(
            "event-1", "fx126-20260813-01ARZ3NDEKTSV4RRFFQ69G5FAV", Feature126LifecycleEventType.RunStarted,
            "None", "deployment", Guid.NewGuid(), "2026-08-13", "scheduled", null,
            DateTimeOffset.UtcNow, new Dictionary<string, object?>(), ExpectedNextSequence: 1);

        var acknowledgement = await appender.AppendAsync(request, CancellationToken.None);

        Assert.Equal("event-1", acknowledgement.EventId);
        Assert.Same(request, sink.Request);
    }

    [Fact]
    public async Task OutOfOrderEventIsRejectedBeforeSink()
    {
        var sink = new RecordingSink();
        var appender = new Feature126EventAppender(sink);
        var request = new Feature126EventAppendRequest(
            "event-2", "run", Feature126LifecycleEventType.HandoffStarted, "None", "deployment", Guid.NewGuid(),
            "2026-08-13", "scheduled", null, DateTimeOffset.UtcNow, new Dictionary<string, object?>());

        await Assert.ThrowsAsync<Feature126EventAppendException>(() => appender.AppendAsync(request, CancellationToken.None));
        Assert.Null(sink.Request);
    }

    [Fact]
    public async Task DuplicateTerminalEventIsIdempotent()
    {
        var sink = new RecordingSink();
        var appender = new Feature126EventAppender(sink);
        var runId = Feature126RunId.Create(new(2026, 8, 13), DateTimeOffset.UtcNow);
        await appender.AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", Guid.NewGuid(), 1), CancellationToken.None);
        var token = sink.LastToken;
        var terminal = Request("terminal", runId, Feature126LifecycleEventType.RunSucceeded, "Running", token, null);
        var first = await appender.AppendAsync(terminal, CancellationToken.None);
        var second = await appender.AppendAsync(terminal, CancellationToken.None);
        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Equal(2, sink.CallCount);
    }

    [Fact]
    public async Task FailedTerminalEventIsAcceptedAndMakesTheStreamImmutable()
    {
        var sink = new RecordingSink();
        var appender = new Feature126EventAppender(sink);
        var runId = Feature126RunId.Create(new(2026, 8, 13), DateTimeOffset.UtcNow);
        var token = Guid.NewGuid();
        await appender.AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token, 1), CancellationToken.None);

        await appender.AppendAsync(Request("failed", runId, Feature126LifecycleEventType.RunFailed, "Running", token, null), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<Feature126EventAppendException>(() =>
            appender.AppendAsync(Request("after-failed", runId, Feature126LifecycleEventType.Heartbeat, "Running", token, null), CancellationToken.None));
        Assert.Equal(Feature126AppendRejection.TerminalConflict, exception.Rejection);
    }

    [Fact]
    public void DurableStartupAcknowledgementThenMarkRunningMakesReadinessReady()
    {
        var health = new Feature126WorkerHealth();
        health.Configure("revision");
        health.Acknowledge("run-126", "2026-08-13");

        ((IFeature126RuntimeLifecycleObserver)health).MarkRunning("run-126", new(2026, 8, 13));

        Assert.Equal(Feature126WorkerReadiness.ready, health.State);
        Assert.Equal("Running", health.Snapshot().LastRunState);
    }

    [Fact]
    public async Task StaleOwnerTerminalEventIsRejected()
    {
        var sink = new RecordingSink();
        var appender = new Feature126EventAppender(sink);
        var runId = Feature126RunId.Create(new(2026, 8, 13), DateTimeOffset.UtcNow);
        var current = Guid.NewGuid();
        await appender.AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", current, 1), CancellationToken.None);
        await Assert.ThrowsAsync<Feature126EventAppendException>(() =>
            appender.AppendAsync(Request("stale-terminal", runId, Feature126LifecycleEventType.RunSucceeded, "Running", Guid.NewGuid(), null), CancellationToken.None));
    }

    [Fact]
    public async Task ValidAppendAllocatesTheNextSequence()
    {
        var sink = new RecordingSink();
        var appender = new Feature126EventAppender(sink);
        var runId = Feature126RunId.Create(new(2026, 8, 13), DateTimeOffset.UtcNow);
        var token = Guid.NewGuid();
        var first = await appender.AppendAsync(Request("start", runId, Feature126LifecycleEventType.RunStarted, "None", token, 1), CancellationToken.None);
        var second = await appender.AppendAsync(Request("heartbeat", runId, Feature126LifecycleEventType.Heartbeat, "Running", token, null), CancellationToken.None);
        Assert.Equal(1, first.EventSequence);
        Assert.Equal(2, second.EventSequence);
        Assert.Equal(2, sink.CallCount);
    }

    private static Feature126EventAppendRequest Request(string eventId, string runId, Feature126LifecycleEventType type, string predecessor, Guid token, long? sequence) =>
        new(eventId, runId, type, predecessor, "deployment", token, "2026-08-13", "scheduled", null, DateTimeOffset.UtcNow,
            new Dictionary<string, object?>(), ExpectedNextSequence: sequence);

    [Fact]
    public void HealthReadinessTransitionsExposeOperationalStates()
    {
        var health = new Feature126WorkerHealth();
        Assert.Equal(Feature126WorkerReadiness.starting, health.State);
        health.Set(Feature126WorkerReadiness.telemetry_unavailable);
        Assert.True(health.Live);
        Assert.Equal("telemetry_unavailable", health.Snapshot().State);
        health.Set(Feature126WorkerReadiness.ready);
        health.Acknowledge("fx126-20260813-01ARZ3NDEKTSV4RRFFQ69G5FAV", "2026-08-13");
        Assert.Equal("ready", health.Snapshot().State);
        Assert.Equal("2026-08-13", health.Snapshot().TehranDate);
    }

    [Fact]
    public void HealthStatesAndMetricsReflectTelemetryLeaseAndRunState()
    {
        var health = new Feature126WorkerHealth();
        health.MarkDisabled();
        Assert.Equal("disabled", health.Snapshot().State);
        Assert.Contains("feature126_readiness{state=\"disabled\"} 1", Feature126PrometheusMetrics.Render(health.Snapshot()));

        health.MarkRunning("fx126-20260813-01ARZ3NDEKTSV4RRFFQ69G5FAV", "2026-08-13");
        Assert.Equal("ready", health.Snapshot().State);
        health.MarkTelemetryUnavailable();
        Assert.Equal("telemetry_unavailable", health.Snapshot().State);
        health.MarkLeaseLost();
        Assert.Equal("lease_lost", health.Snapshot().State);
        Assert.Contains("feature126_lease_state{state=\"Lost\"} 1", Feature126PrometheusMetrics.Render(health.Snapshot()));
        Assert.Contains("feature126_lifecycle_state{state=\"Running\"} 1", Feature126PrometheusMetrics.Render(health.Snapshot()));
    }

    [Fact]
    public void HealthRecoveryAndRequiredMetricSignalsAreObservable()
    {
        var health = new Feature126WorkerHealth();
        health.MarkRunning("run-126", "2026-08-13");
        health.MarkTelemetryUnavailable();
        Assert.Equal(Feature126WorkerReadiness.telemetry_unavailable, health.State);
        health.MarkTelemetryAvailable();
        Assert.Equal(Feature126WorkerReadiness.ready, health.State);

        health.MarkLeaseLost();
        Assert.Equal(Feature126WorkerReadiness.lease_lost, health.State);
        health.MarkLeaseRestored();
        Assert.Equal(Feature126WorkerReadiness.ready, health.State);

        health.MarkDatabaseUnavailable();
        Assert.Equal(Feature126WorkerReadiness.database_unavailable, health.State);
        health.MarkDatabaseAvailable();
        Assert.Equal(Feature126WorkerReadiness.ready, health.State);
        health.RecordRun(new Feature126IngestionRunResult(
            "run-126", new(2026, 8, 13), 3, 1, 2, 1, 0, 0, 2, 0, Array.Empty<Feature126MetricOutcome>())
        { OperationalSummary = Summary() });
        health.RecordProviderLatency(12);
        var metrics = Feature126PrometheusMetrics.Render(health.Snapshot());
        Assert.Contains("feature126_runs_successful_total", metrics);
        Assert.Contains("feature126_runs_failed_total", metrics);
        Assert.Contains("feature126_last_successful_run_timestamp_seconds", metrics);
        Assert.Contains("feature126_last_run_duration_ms", metrics);
        Assert.Contains("feature126_run_duration_ms_bucket", metrics);
        Assert.Contains("feature126_runs_terminal_total{outcome=\"PartialSuccess\",lifecycle_state=\"PartialSuccess\"}", metrics);
        Assert.Contains("feature126_terminal_company_progress_total{outcome=\"succeeded\"}", metrics);
        Assert.Contains("feature126_endpoint_attempts_total", metrics);
        Assert.DoesNotContain("feature126_endpoint_result_total", metrics);
        Assert.Contains("feature126_provider_latency_ms 12", metrics);
        Assert.DoesNotContain("SymbolIsin", metrics);
        Assert.DoesNotContain("exception", metrics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShutdownIsVisibleButLivenessRemainsProcessOnly()
    {
        var health = new Feature126WorkerHealth();
        health.MarkStopping();
        Assert.Equal(Feature126WorkerReadiness.stopping, health.State);
        Assert.True(health.Live);
        Assert.Contains("feature126_readiness{state=\"stopping\"} 0", Feature126PrometheusMetrics.Render(health.Snapshot()));
    }

    [Fact]
    public async Task SeqAuthenticationFailureFailsClosedWithoutRetryingAsSuccess()
    {
        using var http = new HttpClient(new FixedStatusHandler(System.Net.HttpStatusCode.Unauthorized));
        var sink = new SeqFeature126EventSink(http,
            new Feature126TelemetryOptions { Enabled = true, SeqEndpoint = "https://seq.invalid", MaxRetryAttempts = 3 },
            TimeProvider.System);
        var request = new Feature126EventAppendRequest("event-auth", "fx126-20260813-01ARZ3NDEKTSV4RRFFQ69G5FAV", Feature126LifecycleEventType.RunStarted,
            "None", "deployment", Guid.NewGuid(), "2026-08-13", "scheduled", null, DateTimeOffset.UtcNow,
            new Dictionary<string, object?>(), ExpectedNextSequence: 1);

        await Assert.ThrowsAsync<Feature126TelemetryUnavailableException>(() => sink.AppendAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task SeqExporterUsesClefAndAcceptsSeqIngestionAcknowledgementBody()
    {
        var handler = new CapturingSuccessHandler();
        using var http = new HttpClient(handler);
        var sink = new SeqFeature126EventSink(http,
            new Feature126TelemetryOptions { Enabled = true, SeqEndpoint = "https://seq.test", SeqApiKey = "test-key" },
            TimeProvider.System);
        var request = new Feature126EventAppendRequest("event-clef", "fx126-20260813-01ARZ3NDEKTSV4RRFFQ69G5FAV",
            Feature126LifecycleEventType.RunStarted, "None", "deployment", Guid.NewGuid(), "2026-08-13", "scheduled", null,
            DateTimeOffset.UtcNow, new Dictionary<string, object?>(), ExpectedNextSequence: 1);

        var acknowledgement = await sink.AppendAsync(request, CancellationToken.None);

        Assert.Equal("event-clef", acknowledgement.EventId);
        Assert.Equal("application/vnd.serilog.clef", handler.ContentType);
        Assert.Equal("test-key", handler.ApiKey);
        Assert.Contains("\"@t\"", handler.Body);
        Assert.Contains("\"@mt\"", handler.Body);
    }

    private static Feature126OperationalSummary Summary(Feature126RunState state = Feature126RunState.PartialSuccess)
    {
        var metrics = new Dictionary<string, Feature126MetricCounts>
        {
            ["EquilibriumGauge"] = new(2, 0, 1), ["PSGauge"] = new(3, 1, 0), ["PEGauge"] = new(2, 0, 1)
        };
        var failures = new Dictionary<string, long?> { ["InvalidValue"] = 1 };
        var endpoints = new Dictionary<string, Feature126EndpointCounts>
        {
            ["PEGauge"] = new(3, 2, 1), ["PSGauge"] = new(4, 4, 0), ["EquilibriumGauge"] = new(3, 2, 1)
        };
        return Feature126OperationalSummaryFactory.Create("run-126", new(2026, 8, 12, 8, 0, 0, 12, TimeSpan.Zero), new(2026, 8, 12, 8, 0, 1, 15, TimeSpan.Zero), new(2026, 8, 12), true, state, Feature126LeaseStatus.Recovered, true, 3, 3, 2, 1, metrics, failures, endpoints, "CompletedWithMetricFailures", Feature126HandoffStatus.Succeeded, 2, 1);
    }

    [Fact]
    public void MapsHaveFixedKeysAndOrdering()
    {
        var summary = Summary();
        Assert.Equal(["PSGauge", "PEGauge", "EquilibriumGauge"], summary.MetricCounts.Keys);
        Assert.Equal(Feature126FailureCodes.Ordered, summary.FailureCodeCounts.Keys);
        Assert.Equal(["PSGauge", "PEGauge", "EquilibriumGauge"], summary.EndpointCounts.Keys);
        Assert.Equal(1, summary.FailureCodeCounts["InvalidValue"]);
        Assert.Equal(0, summary.FailureCodeCounts["NoData"]);
    }

    [Fact]
    public void CanonicalSerializationIsByteEqualAndUtf8WithoutBom()
    {
        var first = Feature126CanonicalJsonSerializer.Serialize(Summary());
        var second = Feature126CanonicalJsonSerializer.Serialize(Summary());
        Assert.Equal(first, second);
        Assert.NotEqual(0xEF, first[0]);
        var json = Encoding.UTF8.GetString(first);
        Assert.StartsWith("{\"CorrelationId\":", json);
        Assert.Contains("\"TehranDate\":\"2026-08-12\"", json);
        Assert.Contains("\"InvalidValue\":1", json);
        Assert.DoesNotContain(" ", json);
    }

    [Fact]
    public void CanonicalEscapingUsesShortControlsAndPreservesUnicodeAndSlash()
    {
        var summary = Summary();
        var withEscapedCorrelation = summary with { CorrelationId = "a\nب/" };
        var json = Encoding.UTF8.GetString(Feature126CanonicalJsonSerializer.Serialize(withEscapedCorrelation));
        Assert.Contains("a\\nب/", json);
        Assert.DoesNotContain("\\u000A", json);
    }

    [Fact]
    public void CanonicalSerializationIgnoresCallerDictionaryInsertionOrder()
    {
        var summary = Summary();
        var reordered = summary with
        {
            MetricCounts = summary.MetricCounts.Reverse().ToDictionary(x => x.Key, x => x.Value),
            FailureCodeCounts = summary.FailureCodeCounts.Reverse().ToDictionary(x => x.Key, x => x.Value),
            EndpointCounts = summary.EndpointCounts.Reverse().ToDictionary(x => x.Key, x => x.Value)
        };

        Assert.Equal(
            Feature126CanonicalJsonSerializer.Serialize(summary),
            Feature126CanonicalJsonSerializer.Serialize(reordered));
    }

    [Fact]
    public void EveryRuntimeFailureCodeMapsToOneCanonicalBucket()
    {
        var raw = new Dictionary<string, long?>
        {
            ["InvalidNonPositiveInput"] = 1,
            ["PersistenceRejected"] = 2,
            ["MissingAdmissionIdentity"] = 3,
            ["LeaseContended"] = 4,
            ["MissingConfigurationRevision"] = 5,
            ["MissingDeploymentIdentifier"] = 6,
            ["ConflictingOwnerActivation"] = 7,
            ["an-unregistered-runtime-code"] = 8
        };

        var summary = Feature126OperationalSummaryFactory.Create(
            "mapping", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new(2026, 8, 12), true,
            Feature126RunState.Failed, failures: raw);

        Assert.Equal(1, summary.FailureCodeCounts["InvalidNonPositiveInput"]);
        Assert.Equal(2, summary.FailureCodeCounts["PersistenceRejected"]);
        Assert.Equal(3, summary.FailureCodeCounts["MissingAdmissionIdentity"]);
        Assert.Equal(4, summary.FailureCodeCounts["LeaseContended"]);
        Assert.Equal(5, summary.FailureCodeCounts["MissingConfigurationRevision"]);
        Assert.Equal(6, summary.FailureCodeCounts["MissingDeploymentIdentifier"]);
        Assert.Equal(7, summary.FailureCodeCounts["ConflictingOwnerActivation"]);
        Assert.Equal(8, summary.FailureCodeCounts["UnexpectedFailure"]);
        Assert.Equal(raw.Values.Sum(x => x ?? 0), summary.FailureCodeCounts.Values.Sum(x => x ?? 0));
    }

    [Fact]
    public void RecoveryIsVisibleAndDoesNotImplySuccess()
    {
        var recovered = Summary(Feature126RunState.LeaseLost);
        Assert.True(recovered.RecoveredLease);
        Assert.Equal(Feature126LeaseStatus.Recovered, recovered.LeaseStatus);
        var lost = recovered with { LeaseStatus = Feature126LeaseStatus.Lost };
        Assert.Equal(Feature126RunState.LeaseLost, lost.RunState);
        Assert.Equal(1, lost.FailureCodeCounts["InvalidValue"]);
    }

    [Fact]
    public void DisabledSummaryUsesUnavailableCountsAndNullDuration()
    {
        var summary = Feature126OperationalSummaryFactory.Create("disabled", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new(2026, 8, 12), false, Feature126RunState.Disabled, terminationCode: "Disabled");
        Assert.Null(summary.DurationMilliseconds);
        Assert.All(summary.FailureCodeCounts.Values, Assert.Null);
        Assert.Null(summary.PublishedCount);
        Assert.Equal(Feature126HandoffStatus.NotApplicable, summary.HandoffStatus);
    }

    private sealed class RecordingSink : IFeature126DurableEventSink
    {
        private readonly Dictionary<string, Feature126EventAppendAcknowledgement> acknowledgements = new();
        private readonly Dictionary<string, (Guid Token, long Next, string State, bool Terminal)> streams = new();
        public Feature126EventAppendRequest? Request { get; private set; }
        public int CallCount { get; private set; }
        public Guid LastToken { get; private set; }
        public Task<Feature126EventAppendAcknowledgement> AppendAsync(Feature126EventAppendRequest request, CancellationToken _)
        {
            if (acknowledgements.TryGetValue(request.EventId, out var duplicate))
                return Task.FromResult(duplicate with { IsDuplicate = true });
            streams.TryGetValue(request.RunId, out var stream);
            if (stream.Token != Guid.Empty && stream.Token != request.FencingToken)
                throw new Feature126EventAppendException(Feature126AppendRejection.StaleOwner, "stale");
            if (stream.Terminal)
                throw new Feature126EventAppendException(Feature126AppendRejection.TerminalConflict, "terminal");
            if (stream.Token != Guid.Empty && stream.State != request.ExpectedPredecessorState)
                throw new Feature126EventAppendException(Feature126AppendRejection.InvalidPredecessor, "predecessor");
            var sequence = stream.Token == Guid.Empty ? 1 : stream.Next;
            if (request.ExpectedNextSequence is not null && request.ExpectedNextSequence != sequence)
                throw new Feature126EventAppendException(Feature126AppendRejection.OutOfOrder, "sequence");
            Request = request;
            CallCount++;
            LastToken = request.FencingToken;
            var acknowledgement = new Feature126EventAppendAcknowledgement(request.EventId, request.RunId, sequence, false, false, DateTimeOffset.UtcNow);
            acknowledgements[request.EventId] = acknowledgement;
            streams[request.RunId] = (request.FencingToken, sequence + 1,
                Feature126EventOrderingContract.NextState(request.EventType, request.ExpectedPredecessorState),
                request.EventType is Feature126LifecycleEventType.RunSucceeded or Feature126LifecycleEventType.RunPartiallySucceeded or Feature126LifecycleEventType.RunFailed or Feature126LifecycleEventType.RunCancelled or Feature126LifecycleEventType.RunTimedOut or Feature126LifecycleEventType.RunLeaseLost or Feature126LifecycleEventType.HandoffFailed);
            return Task.FromResult(acknowledgement);
        }
        public Task<bool> ProbeAsync(CancellationToken _) => Task.FromResult(true);
    }

    private sealed class FixedStatusHandler(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class CapturingSuccessHandler : HttpMessageHandler
    {
        public string? ContentType { get; private set; }
        public string? ApiKey { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            ApiKey = request.Headers.TryGetValues("X-Seq-ApiKey", out var values) ? values.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.Created)
            {
                Content = new StringContent("{\"MinimumLevelAccepted\":\"Warning\"}")
            };
        }
    }
}
