using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Globalization;

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
    public async Task Start_WithTargetMonth_EnqueuesOnlyThatMonth()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        var publisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, publisher);

        var result = await coordinator.StartAsync(
            new MonthlyActivityBackfillRequest("test:admin", new ShamsiMonth(1405, 5)),
            CancellationToken.None);

        Assert.Equal("Started", result.Outcome);
        Assert.Equal(1, result.MonthsPlanned);
        Assert.Equal(1, result.CompaniesPlanned);
        Assert.Equal(1, result.RequestsEnqueued);
        var request = Assert.Single(publisher.Requests);
        Assert.Equal(ProviderDataset.MonthlyProductionSales, request.Dataset);
        Assert.Equal("13150", request.ExternalReference);
        Assert.Equal("1405/05/01", request.SourceDateRangeStartJalali);
        Assert.Equal("1405/05/31", request.SourceDateRangeEndJalali);
        Assert.Equal("nadpco-monthlybf-140505-13150", request.IdempotencyKey);
    }

    [Fact]
    public async Task Start_ActivePlanCrossingShamsiMonthBoundary_AppendsNewlyEligibleMonth()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-20T08:00:00Z"));
        var publisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, publisher, clock);

        var initial = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);
        Assert.Equal(16, initial.MonthsPlanned);
        Assert.DoesNotContain(publisher.Requests, request => request.IdempotencyKey.Contains("140505"));

        clock.UtcNow = DateTimeOffset.Parse("2026-08-23T08:00:00Z");
        var resumed = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal(17, resumed.MonthsPlanned);
        Assert.Contains(publisher.Requests, request => request.IdempotencyKey == "nadpco-monthlybf-140505-13150");
        var state = await db.MonthlyActivityBackfillStates.SingleAsync();
        Assert.Contains("\"Year\":1405,\"Month\":5", state.PlannedMonthsJson);
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
            RequestedAt = Now.AddHours(-1),
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalReference = "13150",
            SourceDateRangeStartJalali = "1405/02/01",
            SourceDateRangeEndJalali = "1405/02/31"
        });
        SeedPersistedMonthlyReport(db, "13150", 1405, 2);
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, publisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal(13, result.RequestsEnqueued);
        Assert.DoesNotContain(publisher.Requests, request => request.IdempotencyKey.Contains("140502"));
    }

    [Fact]
    public async Task Start_ReenqueuesCompletedCompanyMonthWhenNoMonthlyReportRowsWerePersisted()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        db.SyncRuns.Add(new DataSyncRunRow
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = "nadpco-monthlybf-140502-13150",
            Dataset = ProviderDataset.MonthlyProductionSales.ToString(),
            Status = DataSyncRunStatus.Completed.ToString(),
            RequestedAt = Now.AddHours(-1),
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalReference = "13150",
            ProcessedRecords = 0,
            SourceDateRangeStartJalali = "1405/02/01",
            SourceDateRangeEndJalali = "1405/02/31"
        });
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, publisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal(14, result.RequestsEnqueued);
        Assert.Contains(publisher.Requests, request => request.IdempotencyKey == "nadpco-monthlybf-140502-13150");
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
                RequestedAt = Now,
                ProviderName = ProviderSources.NoavaranCurrentApiName,
                ExternalReference = request.ExternalReference,
                SourceDateRangeStartJalali = request.SourceDateRangeStartJalali,
                SourceDateRangeEndJalali = request.SourceDateRangeEndJalali
            });
            var (year, month) = ParseMonthFromKey(request.IdempotencyKey);
            SeedPersistedMonthlyReport(db, request.ExternalReference!, year, month);
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
            var isFailed = request.IdempotencyKey.Contains("140407");
            db.SyncRuns.Add(new DataSyncRunRow
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = request.IdempotencyKey,
                Dataset = request.Dataset.ToString(),
                Status = isFailed
                    ? DataSyncRunStatus.Failed.ToString()
                    : DataSyncRunStatus.Completed.ToString(),
                RequestedAt = Now,
                ProviderName = ProviderSources.NoavaranCurrentApiName,
                ExternalReference = request.ExternalReference,
                SourceDateRangeStartJalali = request.SourceDateRangeStartJalali,
                SourceDateRangeEndJalali = request.SourceDateRangeEndJalali,
                ErrorMessage = isFailed ? "boom" : null
            });

            if (!isFailed)
            {
                var (year, month) = ParseMonthFromKey(request.IdempotencyKey);
                SeedPersistedMonthlyReport(db, request.ExternalReference!, year, month);
            }
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
    public async Task Progress_CompletedRunWithoutPersistedRows_IsReportedAsNoDataYet()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        var publisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, publisher);
        await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        db.SyncRuns.Add(new DataSyncRunRow
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = "nadpco-monthlybf-140502-13150",
            Dataset = ProviderDataset.MonthlyProductionSales.ToString(),
            Status = DataSyncRunStatus.Completed.ToString(),
            RequestedAt = Now,
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalReference = "13150",
            ProcessedRecords = 0,
            SourceDateRangeStartJalali = "1405/02/01",
            SourceDateRangeEndJalali = "1405/02/31"
        });
        await db.SaveChangesAsync();

        var progress = await coordinator.GetProgressAsync(CancellationToken.None);

        var month = Assert.Single(progress.Months, item => item.ShamsiYear == 1405 && item.ShamsiMonth == 2);
        Assert.Equal(0, month.CompaniesCompleted);
        Assert.Equal(1, month.CompaniesNoDataYet);
        Assert.Equal("NoDataYet", month.Status);
        Assert.False(progress.IsCompleted);
    }

    [Fact]
    public async Task Progress_MixedCompletedAndNoDataYet_SeparatesCountsAndKeepsBackfillRetryable()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        SeedEligibleCompany(db, "13151");
        var publisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, publisher);
        await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        db.SyncRuns.Add(new DataSyncRunRow
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = "nadpco-monthlybf-140502-13150",
            Dataset = ProviderDataset.MonthlyProductionSales.ToString(),
            Status = DataSyncRunStatus.Completed.ToString(),
            RequestedAt = Now,
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalReference = "13150",
            SourceDateRangeStartJalali = "1405/02/01",
            SourceDateRangeEndJalali = "1405/02/31"
        });
        SeedPersistedMonthlyReport(db, "13150", 1405, 2);

        db.SyncRuns.Add(new DataSyncRunRow
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = "nadpco-monthlybf-140502-13151",
            Dataset = ProviderDataset.MonthlyProductionSales.ToString(),
            Status = DataSyncRunStatus.Failed.ToString(),
            RequestedAt = Now,
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalReference = "13151",
            SourceDateRangeStartJalali = "1405/02/01",
            SourceDateRangeEndJalali = "1405/02/31",
            ErrorMessage = "NoDataYet - no monthly report rows were persisted for this company/month."
        });
        await db.SaveChangesAsync();

        var progress = await coordinator.GetProgressAsync(CancellationToken.None);

        var month = Assert.Single(progress.Months, item => item.ShamsiYear == 1405 && item.ShamsiMonth == 2);
        Assert.Equal(2, month.CompaniesPlanned);
        Assert.Equal(1, month.CompaniesCompleted);
        Assert.Equal(1, month.CompaniesNoDataYet);
        Assert.Equal(0, month.CompaniesFailed);
        Assert.Equal("CompletedWithRetryables", month.Status);
        Assert.False(progress.IsCompleted);
        Assert.Equal("Retryable", progress.Status);
    }

    [Fact]
    public async Task Start_CompletedGlobalStateWithFailedMonth_ReopensAndReenqueuesOnlyRetryableRuns()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");

        var initialPublisher = new RecordingPublisher();
        var initialCoordinator = NewCoordinator(db, initialPublisher);
        await initialCoordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        foreach (var request in initialPublisher.Requests)
        {
            var isFailed = request.IdempotencyKey.Contains("140407");
            db.SyncRuns.Add(new DataSyncRunRow
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = request.IdempotencyKey,
                Dataset = request.Dataset.ToString(),
                Status = isFailed
                    ? DataSyncRunStatus.Failed.ToString()
                    : DataSyncRunStatus.Completed.ToString(),
                RequestedAt = Now,
                ProviderName = ProviderSources.NoavaranCurrentApiName,
                ExternalReference = request.ExternalReference,
                SourceDateRangeStartJalali = request.SourceDateRangeStartJalali,
                SourceDateRangeEndJalali = request.SourceDateRangeEndJalali,
                ErrorMessage = isFailed ? "boom" : null
            });

            if (!isFailed)
            {
                var (year, month) = ParseMonthFromKey(request.IdempotencyKey);
                SeedPersistedMonthlyReport(db, request.ExternalReference!, year, month);
            }
        }

        var state = await db.MonthlyActivityBackfillStates.SingleAsync();
        state.IsCompleted = true;
        state.CompletedAt = Now;
        await db.SaveChangesAsync();

        var retryPublisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, retryPublisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal("Started", result.Outcome);
        Assert.Equal(1, result.RequestsEnqueued);
        Assert.Single(retryPublisher.Requests);
        Assert.Equal("nadpco-monthlybf-140407-13150", retryPublisher.Requests[0].IdempotencyKey);
        Assert.False(result.Progress.IsCompleted);
        Assert.Equal("CompletedWithFailures", result.Progress.Status);

        var reopenedState = await db.MonthlyActivityBackfillStates.SingleAsync();
        Assert.False(reopenedState.IsCompleted);
        Assert.Null(reopenedState.CompletedAt);
    }

    [Fact]
    public async Task Start_CompletedGlobalStateWithNoDataMonth_ReopensAndReenqueuesOnlyRetryableRuns()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");

        var initialPublisher = new RecordingPublisher();
        var initialCoordinator = NewCoordinator(db, initialPublisher);
        await initialCoordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        foreach (var request in initialPublisher.Requests)
        {
            var isNoDataYet = request.IdempotencyKey.Contains("140502");
            db.SyncRuns.Add(new DataSyncRunRow
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = request.IdempotencyKey,
                Dataset = request.Dataset.ToString(),
                Status = isNoDataYet
                    ? DataSyncRunStatus.Failed.ToString()
                    : DataSyncRunStatus.Completed.ToString(),
                RequestedAt = Now,
                ProviderName = ProviderSources.NoavaranCurrentApiName,
                ExternalReference = request.ExternalReference,
                SourceDateRangeStartJalali = request.SourceDateRangeStartJalali,
                SourceDateRangeEndJalali = request.SourceDateRangeEndJalali,
                ErrorMessage = isNoDataYet ? "NoDataYet - no monthly report rows were persisted for this company/month." : null
            });

            if (!isNoDataYet)
            {
                var (year, month) = ParseMonthFromKey(request.IdempotencyKey);
                SeedPersistedMonthlyReport(db, request.ExternalReference!, year, month);
            }
        }

        var state = await db.MonthlyActivityBackfillStates.SingleAsync();
        state.IsCompleted = true;
        state.CompletedAt = Now;
        await db.SaveChangesAsync();

        var retryPublisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, retryPublisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal("Started", result.Outcome);
        Assert.Equal(1, result.RequestsEnqueued);
        Assert.Single(retryPublisher.Requests);
        Assert.Equal("nadpco-monthlybf-140502-13150", retryPublisher.Requests[0].IdempotencyKey);
        Assert.False(result.Progress.IsCompleted);
        Assert.Equal("CompletedWithFailures", result.Progress.Status);
    }

    [Fact]
    public async Task Start_CompletedGlobalStateWithoutRetryables_ReturnsAlreadyCompleted()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");

        var initialPublisher = new RecordingPublisher();
        var initialCoordinator = NewCoordinator(db, initialPublisher);
        await initialCoordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        foreach (var request in initialPublisher.Requests)
        {
            db.SyncRuns.Add(new DataSyncRunRow
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = request.IdempotencyKey,
                Dataset = request.Dataset.ToString(),
                Status = DataSyncRunStatus.Completed.ToString(),
                RequestedAt = Now,
                ProviderName = ProviderSources.NoavaranCurrentApiName,
                ExternalReference = request.ExternalReference,
                SourceDateRangeStartJalali = request.SourceDateRangeStartJalali,
                SourceDateRangeEndJalali = request.SourceDateRangeEndJalali
            });

            var (year, month) = ParseMonthFromKey(request.IdempotencyKey);
            SeedPersistedMonthlyReport(db, request.ExternalReference!, year, month);
        }

        var state = await db.MonthlyActivityBackfillStates.SingleAsync();
        state.IsCompleted = true;
        state.CompletedAt = Now;
        await db.SaveChangesAsync();

        var retryPublisher = new RecordingPublisher();
        var coordinator = NewCoordinator(db, retryPublisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal("AlreadyCompleted", result.Outcome);
        Assert.Equal(0, result.RequestsEnqueued);
        Assert.True(result.Progress.IsCompleted);
        Assert.Equal("Completed", result.Progress.Status);
        Assert.Empty(retryPublisher.Requests);
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
        RecordingPublisher publisher,
        TimeProvider? clock = null) =>
        new(
            db,
            publisher,
            Options.Create(new NadpcoApiProviderOptions()),
            clock ?? new FixedTimeProvider(Now),
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

    private static void SeedPersistedMonthlyReport(
        FinancialIngestionDbContext db,
        string externalCompanyId,
        int jalaliYear,
        int jalaliMonth)
    {
        var calendar = new PersianCalendar();
        var periodStart = DateOnly.FromDateTime(calendar.ToDateTime(jalaliYear, jalaliMonth, 1, 0, 0, 0, 0));
        var periodEnd = DateOnly.FromDateTime(
            calendar.ToDateTime(jalaliYear, jalaliMonth, calendar.GetDaysInMonth(jalaliYear, jalaliMonth), 0, 0, 0, 0));
        var report = new NormalizedMonthlyReportRow
        {
            Id = Guid.NewGuid(),
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalCompanyId = externalCompanyId,
            ExternalReportId = $"ProductSales:{externalCompanyId}:{jalaliYear}-{jalaliMonth:D2}:output-0",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            OutputType = 0,
            ReportType = "ProductSales",
            SourcePayloadChecksum = Guid.NewGuid().ToString("N"),
            LastSynchronizedAt = Now
        };
        db.MonthlyReports.Add(report);
        db.MonthlyReportLineItems.Add(new NormalizedMonthlyReportLineItemRow
        {
            Id = Guid.NewGuid(),
            MonthlyReportId = report.Id,
            ProductCode = "TEST-PRODUCT",
            SalesAmount = 1m,
            SalesQuantity = 1m
        });
    }

    private static (int Year, int Month) ParseMonthFromKey(string idempotencyKey)
    {
        var parts = idempotencyKey.Split('-');
        var monthToken = parts[2];
        return (
            int.Parse(monthToken[..4], CultureInfo.InvariantCulture),
            int.Parse(monthToken[4..6], CultureInfo.InvariantCulture));
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

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
