using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class MonthlyActivityBackfillCoordinatorTests
{
    // 2026-06-10 = 20 Khordad 1405 → newest backfill month is Ordibehesht 1405 (1405/02);
    // permitted floor is 1404/01 → 14 months total.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-10T08:00:00Z");

    [Fact]
    public async Task Start_EnqueuesNewestMonthFirstWithBoundedJalaliWindows()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        var publisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, publisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal("Started", result.Outcome);
        Assert.Equal(14, result.MonthsPlanned);
        Assert.Equal(1, result.CompaniesPlanned);
        Assert.Equal(14, result.RequestsEnqueued);
        var first = publisher.Requests[0];
        Assert.Equal(ProviderDataset.MonthlyProductionSales, first.Dataset);
        Assert.Equal("13150", first.ExternalReference);
        Assert.Equal("1405/02/01", first.SourceDateRangeStartJalali);
        Assert.Equal("1405/02/31", first.SourceDateRangeEndJalali);
        Assert.Equal("nadpco-monthlybf-140502-13150", first.IdempotencyKey);
        var last = publisher.Requests[^1];
        Assert.Equal("1404/01/01", last.SourceDateRangeStartJalali);
        Assert.Equal("nadpco-monthlybf-140401-13150", last.IdempotencyKey);
    }

    [Fact]
    public async Task Start_SkipsCompanyMonthsWhoseRunsAlreadyCompleted()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        // The newest month already completed in a previous invocation.
        db.SyncRuns.Add(new DataSyncRunRow
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = "nadpco-monthlybf-140502-13150",
            Dataset = ProviderDataset.MonthlyProductionSales.ToString(),
            Status = DataSyncRunStatus.Completed.ToString(),
            RequestedAt = Now.AddHours(-1)
        });
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, publisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal(13, result.RequestsEnqueued);
        Assert.DoesNotContain(publisher.Requests, request => request.IdempotencyKey.Contains("140502"));
    }

    [Fact]
    public async Task Progress_RecordsCompletionMarkerWhenEveryPlannedMonthCompleted()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        var publisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, publisher);
        await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        // Simulate the consumer completing every enqueued company-month.
        foreach (var request in publisher.Requests)
        {
            db.SyncRuns.Add(new DataSyncRunRow
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = request.IdempotencyKey,
                Dataset = request.Dataset.ToString(),
                Status = DataSyncRunStatus.Completed.ToString(),
                RequestedAt = Now
            });
        }
        await db.SaveChangesAsync();

        var progress = await coordinator.GetProgressAsync(CancellationToken.None);

        Assert.True(progress.IsCompleted);
        Assert.NotNull(progress.CompletedAt);
        Assert.All(progress.Months, month => Assert.Equal("Completed", month.Status));
        Assert.True(await coordinator.IsBackfillCompletedAsync(CancellationToken.None));

        // Starting again after completion is a no-op.
        var again = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);
        Assert.Equal("AlreadyCompleted", again.Outcome);
    }

    [Fact]
    public async Task Progress_FailedMonthIsReportedAndDoesNotCompleteTheBackfill()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        var publisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, publisher);
        await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        foreach (var request in publisher.Requests)
        {
            db.SyncRuns.Add(new DataSyncRunRow
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = request.IdempotencyKey,
                Dataset = request.Dataset.ToString(),
                Status = request.IdempotencyKey.Contains("140407")
                    ? DataSyncRunStatus.Failed.ToString()
                    : DataSyncRunStatus.Completed.ToString(),
                RequestedAt = Now
            });
        }
        await db.SaveChangesAsync();

        var progress = await coordinator.GetProgressAsync(CancellationToken.None);

        Assert.False(progress.IsCompleted);
        var failedMonth = Assert.Single(progress.Months, month => month.Status == "CompletedWithFailures");
        Assert.Equal(7, failedMonth.ShamsiMonth);
        Assert.Equal(1, failedMonth.CompaniesFailed);
        Assert.False(await coordinator.IsBackfillCompletedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Start_TargetsOnlyEligibleCompanies()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        // حق تقدم and off-market listings must never reach the vendor.
        var rights = EligibleCompany("9001");
        rights.PrecedencyRight = 1;
        db.Companies.Add(rights);
        var fund = EligibleCompany("9002");
        fund.MarketId = Guid.NewGuid();
        db.Companies.Add(fund);
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, publisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal(1, result.CompaniesPlanned);
        Assert.All(publisher.Requests, request => Assert.Equal("13150", request.ExternalReference));
    }

    private static MonthlyActivityBackfillCoordinator NewCoordinator(
        FinancialIngestionDbContext db,
        RecordingPublisher publisher) =>
        new(
            db,
            publisher,
            Options.Create(new NadpcoApiProviderOptions()),
            new FixedTimeProvider(Now),
            NullLogger<MonthlyActivityBackfillCoordinator>.Instance);

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static void SeedEligibleCompany(FinancialIngestionDbContext db, string externalCompanyId)
    {
        db.Companies.Add(EligibleCompany(externalCompanyId));
        db.SaveChanges();
    }

    private static NormalizedCompanyRow EligibleCompany(string externalCompanyId) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalCompanyId = externalCompanyId,
            Name = $"Company {externalCompanyId}",
            PrecedencyRight = 0,
            MarketId = NoavaranCompanyScope.FaraBourseMarketId,
            LastSynchronizedAt = Now
        };

    private sealed class RecordingPublisher : IDataSyncRequestPublisher
    {
        public List<DataSyncRequest> Requests { get; } = [];

        public Task PublishAsync(DataSyncRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
