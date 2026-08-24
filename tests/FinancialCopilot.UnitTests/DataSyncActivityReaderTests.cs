using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 058 — EfCoreDataSyncActivityReader: isolation, provider budget cap, and duration calculation.
/// </summary>
public sealed class DataSyncActivityReaderTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-06-13T08:00:00Z");

    // -----------------------------------------------------------------------
    // Failure isolation: one failing reader must not suppress the others
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FailingDataSyncRunReader_DoesNotBlockOtherSources()
    {
        var reader = CreateReader(
            dataSyncRunReader: new ThrowingDataSyncRunReader(),
            nadpcoRunReader: new StubNadpcoScheduledSyncRunReader(
                [MakeNadpcoRun(NadpcoScheduledSyncRunStatus.Succeeded)]));

        var snapshot = await reader.GetSnapshotAsync(5, CancellationToken.None);

        // Nadpco run must be present despite the DataSyncRunReader throwing.
        Assert.NotEmpty(snapshot.RecentRuns);
        Assert.Contains(snapshot.RecentRuns, item => item.Dataset == "ScheduledSync");
    }

    [Fact]
    public async Task FailingNadpcoRunReader_DoesNotBlockOtherSources()
    {
        var reader = CreateReader(
            nadpcoRunReader: new ThrowingNadpcoScheduledSyncRunReader(),
            dataSyncRunReader: new StubDataSyncRunReader([MakeDataSyncRun(DataSyncRunStatus.Completed)]));

        var snapshot = await reader.GetSnapshotAsync(5, CancellationToken.None);

        // DataSyncRun must still appear.
        Assert.NotEmpty(snapshot.RecentRuns);
    }

    [Fact]
    public async Task FailingArchiveImportRunReader_DoesNotBlockOtherSources()
    {
        var reader = CreateReader(
            archiveImportRunReader: new ThrowingArchiveImportRunReader(),
            dataSyncRunReader: new StubDataSyncRunReader([MakeDataSyncRun(DataSyncRunStatus.Completed)]));

        var snapshot = await reader.GetSnapshotAsync(5, CancellationToken.None);

        Assert.NotEmpty(snapshot.RecentRuns);
    }

    // -----------------------------------------------------------------------
    // recentPerProvider budget cap
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RecentPerProvider_LimitsNadpcoRunsToRequestedBudget()
    {
        const int budget = 2;
        var runs = Enumerable.Range(0, 8)
            .Select(_ => MakeNadpcoRun(NadpcoScheduledSyncRunStatus.Succeeded))
            .ToArray();

        var reader = CreateReader(nadpcoRunReader: new StubNadpcoScheduledSyncRunReader(runs));

        var snapshot = await reader.GetSnapshotAsync(budget, CancellationToken.None);

        var nadpcoRecent = snapshot.RecentRuns
            .Where(r => r.Dataset == "ScheduledSync")
            .ToArray();

        Assert.True(nadpcoRecent.Length <= budget,
            $"Expected ≤ {budget} NADPCO recent runs but got {nadpcoRecent.Length}");
    }

    // -----------------------------------------------------------------------
    // DurationMs calculation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DurationMs_IsCalculatedFromStartAndCompleteTimestamps()
    {
        var start = T0;
        var end = T0.AddSeconds(7.5);
        var run = MakeNadpcoRun(NadpcoScheduledSyncRunStatus.Succeeded, start, end);

        var reader = CreateReader(nadpcoRunReader: new StubNadpcoScheduledSyncRunReader([run]));
        var snapshot = await reader.GetSnapshotAsync(5, CancellationToken.None);

        var item = snapshot.RecentRuns
            .Single(r => r.Dataset == "ScheduledSync");

        Assert.NotNull(item.DurationMs);
        Assert.Equal(7_500L, item.DurationMs.Value);
    }

    [Fact]
    public async Task DurationMs_IsNullWhenRunHasNoCompletedAt()
    {
        var run = MakeNadpcoRun(NadpcoScheduledSyncRunStatus.Running, T0, completedAt: null);

        var reader = CreateReader(nadpcoRunReader: new StubNadpcoScheduledSyncRunReader([run]));
        var snapshot = await reader.GetSnapshotAsync(5, CancellationToken.None);

        var item = snapshot.ActiveRuns
            .Single(r => r.Dataset == "ScheduledSync");

        Assert.Null(item.DurationMs);
    }

    // -----------------------------------------------------------------------
    // Active vs. recent routing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunningDataSyncRun_AppearsInActiveRuns()
    {
        var run = MakeDataSyncRun(DataSyncRunStatus.Running);
        var reader = CreateReader(dataSyncRunReader: new StubDataSyncRunReader([run]));

        var snapshot = await reader.GetSnapshotAsync(5, CancellationToken.None);

        Assert.Contains(snapshot.ActiveRuns, item => item.Status == "Running");
        Assert.DoesNotContain(snapshot.RecentRuns, item => item.Status == "Running");
    }

    [Fact]
    public async Task CompletedDataSyncRun_AppearsInRecentRuns()
    {
        var run = MakeDataSyncRun(DataSyncRunStatus.Completed);
        var reader = CreateReader(dataSyncRunReader: new StubDataSyncRunReader([run]));

        var snapshot = await reader.GetSnapshotAsync(5, CancellationToken.None);

        Assert.Contains(snapshot.RecentRuns, item => item.Status == "Completed");
        Assert.Empty(snapshot.ActiveRuns);
    }

    // -----------------------------------------------------------------------
    // Shamsi month formatting
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("140502", "1405/02")]
    [InlineData("140401", "1404/01")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public async Task ShamsiMonth_IsFormattedCorrectly(string? jalaliStart, string? expected)
    {
        var run = MakeDataSyncRun(
            DataSyncRunStatus.Completed,
            sourceDateRangeStartJalali: jalaliStart);

        var reader = CreateReader(dataSyncRunReader: new StubDataSyncRunReader([run]));
        var snapshot = await reader.GetSnapshotAsync(5, CancellationToken.None);

        var item = snapshot.RecentRuns.FirstOrDefault(r => r.Dataset == "MonthlyProductionSales");
        if (expected is null)
            Assert.True(item is null || item.RequestedShamsiMonth is null);
        else
            Assert.Equal(expected, item?.RequestedShamsiMonth);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static EfCoreDataSyncActivityReader CreateReader(
        IDataSyncRunReader? dataSyncRunReader = null,
        INadpcoScheduledSyncRunReader? nadpcoRunReader = null,
        IStockMarketDbSyncStateReader? stockMarketStateReader = null,
        ITsetmcSyncStateReader? tsetmcStateReader = null,
        IArchiveImportRunReader? archiveImportRunReader = null,
        IMonthlyActivityBackfillCoordinator? monthlyBackfill = null,
        IFundamentalIndexCatchUpRunReader? catchUpRunReader = null) =>
        new(
            dataSyncRunReader ?? new StubDataSyncRunReader([]),
            nadpcoRunReader ?? new StubNadpcoScheduledSyncRunReader([]),
            stockMarketStateReader ?? new StubStockMarketDbSyncStateReader([]),
            tsetmcStateReader ?? new StubTsetmcSyncStateReader([]),
            archiveImportRunReader ?? new StubArchiveImportRunReader([]),
            monthlyBackfill ?? new StubMonthlyActivityBackfillCoordinator(),
            catchUpRunReader ?? new StubFundamentalIndexCatchUpRunReader([]),
            NullLogger<EfCoreDataSyncActivityReader>.Instance);

    private static DataSyncRun MakeDataSyncRun(
        DataSyncRunStatus status,
        string? sourceDateRangeStartJalali = null) =>
        new(
            Id: Guid.NewGuid(),
            IdempotencyKey: "test",
            Dataset: ProviderDataset.MonthlyProductionSales,
            ExternalReference: null,
            Status: status,
            RequestedAt: T0,
            StartedAt: T0,
            CompletedAt: status == DataSyncRunStatus.Completed ? T0.AddSeconds(2) : null,
            ProcessedRecords: 10,
            ErrorCount: 0,
            ErrorMessage: null,
            SourcePayloadChecksum: null,
            ProviderName: ProviderSources.NoavaranCurrentApiName,
            SourceDateRangeStartJalali: sourceDateRangeStartJalali);

    private static NadpcoScheduledSyncRun MakeNadpcoRun(
        NadpcoScheduledSyncRunStatus status,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null) =>
        new(
            RunId: Guid.NewGuid(),
            TriggerSource: NadpcoScheduledSyncTriggerSource.Automatic,
            Status: status,
            StartedAt: startedAt ?? T0,
            CompletedAt: completedAt ?? (status == NadpcoScheduledSyncRunStatus.Running ? null : T0.AddSeconds(7.5)),
            LastSuccessfulExecutionAt: null,
            ProcessedBatches: 3,
            FailedBatches: 0,
            RetryAttempts: 0,
            Diagnostics: null,
            ScheduleSnapshotJson: "{}",
            DatasetSelectionJson: "[]",
            LockOwner: null,
            LockLeaseExpiresAt: null,
            AlertEmitted: false,
            ManualReason: null);

    // -----------------------------------------------------------------------
    // Stub implementations
    // -----------------------------------------------------------------------

    private sealed class StubDataSyncRunReader(IReadOnlyCollection<DataSyncRun> runs) : IDataSyncRunReader
    {
        public Task<IReadOnlyCollection<DataSyncRun>> QueryRecentAsync(int maximumCount, CancellationToken ct) =>
            Task.FromResult(runs);
    }

    private sealed class ThrowingDataSyncRunReader : IDataSyncRunReader
    {
        public Task<IReadOnlyCollection<DataSyncRun>> QueryRecentAsync(int maximumCount, CancellationToken ct) =>
            throw new InvalidOperationException("Simulated database failure.");
    }

    private sealed class StubNadpcoScheduledSyncRunReader(
        IReadOnlyCollection<NadpcoScheduledSyncRun> runs) : INadpcoScheduledSyncRunReader
    {
        public Task<IReadOnlyCollection<NadpcoScheduledSyncRun>> QueryRecentAsync(int maximumCount, CancellationToken ct) =>
            Task.FromResult(runs);
    }

    private sealed class ThrowingNadpcoScheduledSyncRunReader : INadpcoScheduledSyncRunReader
    {
        public Task<IReadOnlyCollection<NadpcoScheduledSyncRun>> QueryRecentAsync(int maximumCount, CancellationToken ct) =>
            throw new InvalidOperationException("Simulated failure.");
    }

    private sealed class StubStockMarketDbSyncStateReader(
        IReadOnlyCollection<StockMarketSyncState> states) : IStockMarketDbSyncStateReader
    {
        public Task<IReadOnlyCollection<StockMarketSyncState>> QueryAsync(CancellationToken ct) =>
            Task.FromResult(states);
    }

    private sealed class StubTsetmcSyncStateReader(
        IReadOnlyCollection<TsetmcSyncState> states) : ITsetmcSyncStateReader
    {
        public Task<IReadOnlyCollection<TsetmcSyncState>> QueryAsync(CancellationToken ct) =>
            Task.FromResult(states);
    }

    private sealed class StubArchiveImportRunReader(
        IReadOnlyCollection<ArchiveImportRun> runs) : IArchiveImportRunReader
    {
        public Task<IReadOnlyCollection<ArchiveImportRun>> QueryRecentAsync(int maximumCount, CancellationToken ct) =>
            Task.FromResult(runs);
    }

    private sealed class ThrowingArchiveImportRunReader : IArchiveImportRunReader
    {
        public Task<IReadOnlyCollection<ArchiveImportRun>> QueryRecentAsync(int maximumCount, CancellationToken ct) =>
            throw new InvalidOperationException("Simulated failure.");
    }

    private sealed class StubMonthlyActivityBackfillCoordinator : IMonthlyActivityBackfillCoordinator
    {
        public Task<MonthlyActivityBackfillStartResult> StartAsync(
            MonthlyActivityBackfillRequest request, CancellationToken ct) =>
            Task.FromResult(new MonthlyActivityBackfillStartResult("NoOp", 0, 0, 0,
                new MonthlyActivityBackfillProgress(false, false, "Pending", null, null, null, [])));

        public Task<MonthlyActivityBackfillProgress> GetProgressAsync(CancellationToken ct) =>
            Task.FromResult(new MonthlyActivityBackfillProgress(false, false, "Pending", null, null, null, []));

        public Task<MonthlyActivityBackfillBatch?> GetBatchAsync(Guid batchId, CancellationToken ct) =>
            Task.FromResult<MonthlyActivityBackfillBatch?>(null);

        public Task<IReadOnlyCollection<MonthlyActivityBackfillBatch>> ListBatchesAsync(
            int limit,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<MonthlyActivityBackfillBatch>>([]);
    }

    private sealed class StubFundamentalIndexCatchUpRunReader(
        IReadOnlyCollection<FundamentalIndexCatchUpRun> runs) : IFundamentalIndexCatchUpRunReader
    {
        public Task<IReadOnlyCollection<FundamentalIndexCatchUpRun>> QueryRecentAsync(int maximumCount, CancellationToken ct) =>
            Task.FromResult(runs);
    }
}
