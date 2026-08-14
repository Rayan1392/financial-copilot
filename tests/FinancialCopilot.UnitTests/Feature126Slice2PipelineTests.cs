using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class Feature126Slice2PipelineTests
{
    [Fact]
    public async Task Run_ProcessesEveryAdmittedSymbolAcrossPages()
    {
        var universe = Enumerable.Range(1, 5)
            .Select(i => new RelativeValuationEligibleSymbol($"ISIN{i:00000000000}", Guid.NewGuid()))
            .ToArray();
        var facts = new MemoryFactStore();
        var pipeline = Create(universe, facts, pageSize: 2);

        var result = await pipeline.RunAsync("run-1", CancellationToken.None);

        Assert.Equal(5, result.AdmittedSymbols);
        Assert.Equal(3, result.PagesProcessed);
        Assert.Equal(15, result.SuccessfulAcquisitions);
        Assert.Equal(15, facts.Keys.Count);
        Assert.Equal(15, result.Outcomes.Count);
    }

    [Fact]
    public async Task Run_RetriesOnlyRetryableMetricAndKeepsOtherMetricsIndependent()
    {
        var symbol = new RelativeValuationEligibleSymbol("ISIN000000001", Guid.NewGuid());
        var provider = new TestProvider { FailPeOnce = true };
        var ps = new TestPsOperation();
        var facts = new MemoryFactStore();
        var pipeline = Create(new[] { symbol }, facts, pageSize: 10, retryCount: 1, provider, ps);

        var result = await pipeline.RunAsync("run-2", CancellationToken.None);

        Assert.Equal(3, result.SuccessfulAcquisitions);
        Assert.Equal(2, provider.PeCalls);
        Assert.Contains(result.Outcomes, x => x.Metric == RelativeValuationSourceKind.PEGauge && x.Attempts == 2);
        Assert.All(result.Outcomes, x => Assert.Equal("Succeeded", x.Status));
    }

    [Fact]
    public async Task Run_RecordsPartialFailureAndReplayIsIdempotent()
    {
        var symbol = new RelativeValuationEligibleSymbol("ISIN000000001", Guid.NewGuid());
        var provider = new TestProvider { AlwaysFailEquilibrium = true };
        var facts = new MemoryFactStore();
        var pipeline = Create(new[] { symbol }, facts, pageSize: 10, retryCount: 1, provider);

        var first = await pipeline.RunAsync("run-3", CancellationToken.None);
        var second = await pipeline.RunAsync("run-4", CancellationToken.None);

        Assert.Equal(1, first.PartialCompanies);
        Assert.Equal(1, second.PartialCompanies);
        Assert.Equal(2, first.FactsPersisted);
        Assert.Equal(0, second.FactsPersisted);
        Assert.Equal(2, second.FactsUnchanged);
        Assert.Equal(2, facts.Keys.Count);
        Assert.Contains(second.Outcomes, x => x.Status == "Failed" && x.FailureCode == "NotFoundOrNoData");
    }

    [Fact]
    public async Task Run_CompanyTimeoutProducesTerminalOutcomesAndOtherCompaniesContinue()
    {
        var slow = new RelativeValuationEligibleSymbol("SLOW", Guid.NewGuid());
        var fast = new RelativeValuationEligibleSymbol("FAST", Guid.NewGuid());
        var facts = new MemoryFactStore();
        var pipeline = Create(new[] { slow, fast }, facts, pageSize: 10,
            provider: new TestProvider { SlowIsin = "SLOW" }, companyTimeoutSeconds: 1);

        var result = await pipeline.RunAsync("timeout", CancellationToken.None);

        Assert.Contains(result.Outcomes, x => x.SymbolIsin == "SLOW" && x.Status == "Failed" && x.FailureCode == "Timeout");
        Assert.Equal(3, result.Outcomes.Count(x => x.SymbolIsin == "SLOW"));
        Assert.Equal(3, result.Outcomes.Count(x => x.SymbolIsin == "FAST" && x.Status == "Succeeded"));
    }

    [Fact]
    public async Task Run_EnforcesConfiguredCompanyConcurrencyLimit()
    {
        var symbols = Enumerable.Range(1, 5)
            .Select(i => new RelativeValuationEligibleSymbol($"ISIN{i}", Guid.NewGuid()))
            .ToArray();
        var provider = new TestProvider { DelayMilliseconds = 40, TrackConcurrency = true };
        var pipeline = Create(symbols, new MemoryFactStore(), pageSize: 10, provider: provider, maximumConcurrency: 2);

        await pipeline.RunAsync("concurrency", CancellationToken.None);

        Assert.InRange(provider.MaximumConcurrentPeCalls, 1, 2);
    }

    [Fact]
    public async Task Run_IsolatesMetricExceptionFromOtherMetrics()
    {
        var provider = new TestProvider { ThrowPe = true };
        var result = await Create(
            new[] { new RelativeValuationEligibleSymbol("ISIN000000001", Guid.NewGuid()) },
            new MemoryFactStore(), pageSize: 10, provider: provider).RunAsync("metric-failure", CancellationToken.None);

        Assert.Contains(result.Outcomes, x => x.Metric == RelativeValuationSourceKind.PEGauge && x.Status == "Failed" && x.FailureCode == "NetworkFailure");
        Assert.Equal(2, result.SuccessfulAcquisitions);
    }

    [Fact]
    public async Task Run_RenewsLeaseDuringLongExecution()
    {
        var lease = new TestLeaseStore();
        var pipeline = Create(new[] { new RelativeValuationEligibleSymbol("SLOW", Guid.NewGuid()) },
            new MemoryFactStore(), pageSize: 10, provider: new TestProvider { SlowIsin = "SLOW", DelayMilliseconds = 1500 },
            lease: lease, heartbeatSeconds: 1);

        await pipeline.RunAsync("heartbeat", CancellationToken.None);

        Assert.True(lease.RenewCalls > 0);
    }

    [Fact]
    public async Task Run_LeaseLossAbortsWithoutReportingSuccess()
    {
        var lease = new TestLeaseStore { RenewResult = false };
        var pipeline = Create(new[] { new RelativeValuationEligibleSymbol("SLOW", Guid.NewGuid()) },
            new MemoryFactStore(), pageSize: 10, provider: new TestProvider { SlowIsin = "SLOW", DelayMilliseconds = 1500 },
            lease: lease, heartbeatSeconds: 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.RunAsync("lease-loss", CancellationToken.None));

        Assert.Contains("lease was lost", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(lease.Transitions, x => x == LeaseState.Succeeded);
    }

    [Fact]
    public async Task Run_RejectsSuccessfulTerminalTransition()
    {
        var lease = new TestLeaseStore { SucceededTransitionResult = false };
        var pipeline = Create(new[] { new RelativeValuationEligibleSymbol("FAST", Guid.NewGuid()) },
            new MemoryFactStore(), pageSize: 10, lease: lease);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.RunAsync("rejected-terminal", CancellationToken.None));
        Assert.Contains(LeaseState.Succeeded, lease.Transitions);
    }

    private static RelativeValuationPipeline Create(
        IReadOnlyList<RelativeValuationEligibleSymbol> symbols,
        MemoryFactStore facts,
        int pageSize,
        int retryCount = 0,
        TestProvider? provider = null,
        TestPsOperation? ps = null,
        int maximumConcurrency = 4,
        int companyTimeoutSeconds = 10,
        TestLeaseStore? lease = null,
        int heartbeatSeconds = 0) =>
        new(
            new TestUniverse(symbols),
            ps ?? new TestPsOperation(),
            provider ?? new TestProvider(),
            facts,
            lease ?? new TestLeaseStore(),
            Options.Create(new RelativeValuationIngestionOptions
            {
                Enabled = true, PageSize = pageSize, RetryCount = retryCount,
                CompanyTimeoutSeconds = companyTimeoutSeconds, LeaseMinutes = 10,
                MaximumConcurrency = maximumConcurrency, LeaseHeartbeatSeconds = heartbeatSeconds
            }),
            TimeProvider.System,
            NullLogger<RelativeValuationPipeline>.Instance,
            new TestHandoffBoundary());

    private sealed class TestUniverse(IReadOnlyList<RelativeValuationEligibleSymbol> symbols) : IEligibleUniverseReader
    { public Task<IReadOnlyList<RelativeValuationEligibleSymbol>> ReadAsync(CancellationToken _) => Task.FromResult(symbols); }

    private sealed class TestPsOperation : ICyclicalWavesPsAcceptedOperation
    {
        public Task<PsProviderResult<PsGaugeDistribution>> AcquireAcceptedPsGaugeAsync(string isin, CancellationToken _)
            => Task.FromResult(new PsProviderResult<PsGaugeDistribution>(new PsGaugeDistribution(1, 1, 1, 1, 1, 1, 2, 3, 1, 1, 2, 4, 5), PsVisualizationSyncErrorCode.None));
    }

    private sealed class TestProvider : ICyclicalWavesRelativeValuationProviderClient
    {
        public bool FailPeOnce { get; init; }
        public bool AlwaysFailEquilibrium { get; init; }
        public bool ThrowPe { get; init; }
        public string? SlowIsin { get; init; }
        public int DelayMilliseconds { get; init; }
        public bool TrackConcurrency { get; init; }
        public int PeCalls { get; private set; }
        public int MaximumConcurrentPeCalls { get; private set; }
        private int concurrentPeCalls;
        public async Task<RelativeValuationProviderResult> GetPeGaugeAsync(string isin, CancellationToken token)
        {
            PeCalls++;
            if (ThrowPe) throw new InvalidOperationException("test network failure");
            if (SlowIsin == isin) await Task.Delay(Timeout.Infinite, token);
            if (DelayMilliseconds > 0)
            {
                var current = Interlocked.Increment(ref concurrentPeCalls);
                MaximumConcurrentPeCalls = Math.Max(MaximumConcurrentPeCalls, current);
                try { await Task.Delay(DelayMilliseconds, token); }
                finally { Interlocked.Decrement(ref concurrentPeCalls); }
            }
            return FailPeOnce && PeCalls == 1 ? Failure(RelativeValuationSourceKind.PEGauge, RelativeValuationFactReadiness.NetworkFailure, isin) : Ready(RelativeValuationSourceKind.PEGauge, isin);
        }
        public async Task<RelativeValuationProviderResult> GetEquilibriumGaugeAsync(string isin, CancellationToken token)
        {
            if (SlowIsin == isin) await Task.Delay(Timeout.Infinite, token);
            return AlwaysFailEquilibrium ? Failure(RelativeValuationSourceKind.EquilibriumGauge, RelativeValuationFactReadiness.NotFoundOrNoData, isin) : Ready(RelativeValuationSourceKind.EquilibriumGauge, isin);
        }
        private static RelativeValuationProviderResult Ready(RelativeValuationSourceKind kind, string isin) => new(kind, 2, 3, kind + "-observation-" + isin, "endpoint", "identity", RelativeValuationFactReadiness.Ready, "Valid", "hash", "{}");
        private static RelativeValuationProviderResult Failure(RelativeValuationSourceKind kind, RelativeValuationFactReadiness readiness, string isin) => new(kind, null, null, kind + "-failure-" + isin, "endpoint", "identity", readiness, readiness.ToString(), "", "{}");
    }

    private sealed class MemoryFactStore : IFeature126SourceFactStore
    {
        public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);
        public Task<Feature126SourceFactWriteResult> PersistAcceptedAsync(Guid _, RelativeValuationProviderResult result, LeaseHandle __, CancellationToken ___)
        {
            var key = result.SourceKind + ":" + result.SourceObservationId;
            return Task.FromResult(Keys.Add(key) ? Feature126SourceFactWriteResult.Persisted : Feature126SourceFactWriteResult.Unchanged);
        }

        public Task<Feature126SourceSnapshotEvidence> ReadCurrentSnapshotAsync(DateOnly date, CancellationToken _)
            => Task.FromResult(Feature126SourceSnapshotEvidence.Create(date, Array.Empty<Feature126SourceFactEvidence>()));
    }

    private sealed class TestHandoffBoundary : IFeature125HandoffSubmissionBoundary
    {
        public Task<Feature125HandoffValidationResult> SubmitAsync(
            Feature126HandoffPackage package,
            Feature126HandoffLeaseState lease,
            DateTimeOffset _,
            CancellationToken __)
        {
            Assert.True(package.IsComplete);
            Assert.Equal(LeaseState.Handoff, lease.State);
            Assert.Equal(package.FencingToken, lease.FencingToken);
            return Task.FromResult(Feature125HandoffValidationResult.Accept());
        }
    }

    private sealed class TestLeaseStore : IFeature126LeaseStore
    {
        private readonly LeaseHandle handle = new("feature126", DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));
        public bool RenewResult { get; init; } = true;
        public bool SucceededTransitionResult { get; init; } = true;
        public int RenewCalls { get; private set; }
        public List<LeaseState> Transitions { get; } = new();
        public Task<LeaseHandle?> TryAcquireAsync(string _, DateOnly date, TimeSpan __, CancellationToken ___) => Task.FromResult<LeaseHandle?>(handle with { CalculationDate = date });
        public Task<bool> RenewAsync(LeaseHandle _, TimeSpan __, CancellationToken ___)
        {
            RenewCalls++;
            return Task.FromResult(RenewResult);
        }
        public Task<bool> IsOwnerAsync(LeaseHandle _, CancellationToken __) => Task.FromResult(true);
        public Task<bool> TransitionAsync(LeaseHandle _, LeaseState state, CancellationToken ___)
        {
            Transitions.Add(state);
            return Task.FromResult(state != LeaseState.Succeeded || SucceededTransitionResult);
        }
    }
}
