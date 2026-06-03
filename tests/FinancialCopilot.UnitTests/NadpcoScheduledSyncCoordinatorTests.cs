using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class NadpcoScheduledSyncCoordinatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-03T10:00:00Z");

    [Fact]
    public async Task AutomaticRun_WhenDisabled_PersistsSkippedDisabledAndDoesNotInvokeOrchestration()
    {
        await using var db = CreateDb();
        var orchestration = new StubNadpcoApiSyncService();
        var coordinator = NewCoordinator(db, orchestration, options: new NadpcoScheduledSyncOptions { Enabled = false });

        var run = await coordinator.RunAsync(
            new NadpcoScheduledSyncRunRequest(NadpcoScheduledSyncTriggerSource.Automatic),
            CancellationToken.None);

        Assert.Equal(NadpcoScheduledSyncRunStatus.SkippedDisabled, run.Status);
        Assert.Equal(0, orchestration.InvocationCount);
        Assert.Single(db.NadpcoScheduledSyncRuns);
    }

    [Fact]
    public async Task ManualRun_ExecutesIncrementalOrchestrationAndPersistsSuccess()
    {
        await using var db = CreateDb();
        var orchestration = new StubNadpcoApiSyncService();
        var coordinator = NewCoordinator(db, orchestration, options: new NadpcoScheduledSyncOptions { Enabled = false });

        var run = await coordinator.RunAsync(
            new NadpcoScheduledSyncRunRequest(NadpcoScheduledSyncTriggerSource.Manual, "operator", Force: true),
            CancellationToken.None);

        Assert.Equal(NadpcoScheduledSyncRunStatus.Succeeded, run.Status);
        Assert.Equal(1, orchestration.InvocationCount);
        Assert.False(orchestration.FullReloadModes.Single());
        Assert.Equal(4, run.ProcessedBatches);
        Assert.Equal("operator", run.ManualReason);
        Assert.NotNull(run.LastSuccessfulExecutionAt);
    }

    [Fact]
    public async Task Run_WhenActiveLeaseExists_PersistsSkippedAlreadyRunning()
    {
        await using var db = CreateDb();
        db.NadpcoScheduledSyncRuns.Add(new NadpcoScheduledSyncRunRow
        {
            Id = Guid.NewGuid(),
            TriggerSource = NadpcoScheduledSyncTriggerSource.Automatic.ToString(),
            Status = NadpcoScheduledSyncRunStatus.Running.ToString(),
            StartedAt = Now.AddMinutes(-10),
            LockOwner = "node-a",
            LockLeaseExpiresAt = Now.AddMinutes(10),
            ScheduleSnapshotJson = "{}",
            DatasetSelectionJson = "[]"
        });
        await db.SaveChangesAsync();
        var orchestration = new StubNadpcoApiSyncService();
        var coordinator = NewCoordinator(db, orchestration);

        var run = await coordinator.RunAsync(
            new NadpcoScheduledSyncRunRequest(NadpcoScheduledSyncTriggerSource.Automatic),
            CancellationToken.None);

        Assert.Equal(NadpcoScheduledSyncRunStatus.SkippedAlreadyRunning, run.Status);
        Assert.Equal(0, orchestration.InvocationCount);
        Assert.Equal(2, await db.NadpcoScheduledSyncRuns.CountAsync());
    }

    [Fact]
    public async Task MissedRecovery_WhenPolicyIsSkip_PersistsMissedAndDoesNotInvokeOrchestration()
    {
        await using var db = CreateDb();
        var orchestration = new StubNadpcoApiSyncService();
        var coordinator = NewCoordinator(
            db,
            orchestration,
            options: new NadpcoScheduledSyncOptions
            {
                Enabled = true,
                MissedScheduleRecoveryPolicy = NadpcoMissedScheduleRecoveryPolicy.Skip
            });

        var run = await coordinator.RunAsync(
            new NadpcoScheduledSyncRunRequest(NadpcoScheduledSyncTriggerSource.MissedRecovery),
            CancellationToken.None);

        Assert.Equal(NadpcoScheduledSyncRunStatus.Missed, run.Status);
        Assert.Equal(0, orchestration.InvocationCount);
    }

    [Fact]
    public async Task Run_WhenOrchestrationFails_RetriesAndEmitsAlert()
    {
        await using var db = CreateDb();
        var orchestration = new StubNadpcoApiSyncService { ExceptionToThrow = new InvalidOperationException("boom") };
        var alertSink = new CapturingAlertSink();
        var coordinator = NewCoordinator(
            db,
            orchestration,
            alertSink,
            new NadpcoScheduledSyncOptions { Enabled = true, RetryCount = 2, RetryDelaySeconds = 0 });

        var run = await coordinator.RunAsync(
            new NadpcoScheduledSyncRunRequest(NadpcoScheduledSyncTriggerSource.Automatic),
            CancellationToken.None);

        Assert.Equal(NadpcoScheduledSyncRunStatus.Failed, run.Status);
        Assert.Equal(3, orchestration.InvocationCount);
        Assert.Equal(3, run.RetryAttempts);
        Assert.True(run.AlertEmitted);
        Assert.Single(alertSink.Alerts);
    }

    [Fact]
    public async Task Status_RecoversExpiredRunningLeaseAsHungRecovered()
    {
        await using var db = CreateDb();
        db.NadpcoScheduledSyncRuns.Add(new NadpcoScheduledSyncRunRow
        {
            Id = Guid.NewGuid(),
            TriggerSource = NadpcoScheduledSyncTriggerSource.Automatic.ToString(),
            Status = NadpcoScheduledSyncRunStatus.Running.ToString(),
            StartedAt = Now.AddHours(-3),
            LockOwner = "node-a",
            LockLeaseExpiresAt = Now.AddMinutes(-1),
            ScheduleSnapshotJson = "{}",
            DatasetSelectionJson = "[]"
        });
        await db.SaveChangesAsync();
        var coordinator = NewCoordinator(db, new StubNadpcoApiSyncService());

        var status = await coordinator.GetStatusAsync(10, CancellationToken.None);

        Assert.True(status.Ready);
        var run = Assert.Single(status.RecentRuns);
        Assert.Equal(NadpcoScheduledSyncRunStatus.HungRecovered, run.Status);
        Assert.Null(run.LockOwner);
        Assert.NotNull(run.CompletedAt);
    }

    private static NadpcoScheduledSyncCoordinator NewCoordinator(
        FinancialIngestionDbContext db,
        StubNadpcoApiSyncService orchestration,
        INadpcoScheduledSyncAlertSink? alertSink = null,
        NadpcoScheduledSyncOptions? options = null)
    {
        var timeProvider = new FixedTimeProvider(Now);
        return new NadpcoScheduledSyncCoordinator(
            orchestration,
            new EfCoreNadpcoScheduledSyncRunRepository(db, timeProvider),
            alertSink ?? new CapturingAlertSink(),
            Options.Create(options ?? new NadpcoScheduledSyncOptions
            {
                Enabled = true,
                RetryDelaySeconds = 0,
                LockLeaseSeconds = 3600
            }),
            timeProvider,
            NullLogger<NadpcoScheduledSyncCoordinator>.Instance);
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class StubNadpcoApiSyncService : INadpcoApiScheduledSyncService
    {
        public int InvocationCount { get; private set; }
        public List<bool> FullReloadModes { get; } = [];
        public Exception? ExceptionToThrow { get; set; }

        public Task<NadpcoApiSyncResult> ExecuteAsync(bool fullReload, CancellationToken cancellationToken)
        {
            InvocationCount++;
            FullReloadModes.Add(fullReload);
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(new NadpcoApiSyncResult(
                fullReload,
                CompaniesConsidered: 4,
                CompaniesEnqueued: 4,
                FailedCompanies: 0,
                FailedCompanyIds: [],
                RequestsEnqueued: 13,
                OverlapFrom: DateTimeOffset.Parse("2026-05-27T10:00:00Z"),
                AdvancedWatermark: Now,
                Duration: TimeSpan.FromSeconds(2)));
        }
    }

    private sealed class CapturingAlertSink : INadpcoScheduledSyncAlertSink
    {
        public List<NadpcoScheduledSyncAlert> Alerts { get; } = [];

        public Task<bool> EmitAsync(NadpcoScheduledSyncAlert alert, CancellationToken cancellationToken)
        {
            Alerts.Add(alert);
            return Task.FromResult(true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
