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
using System.Text.Json;

namespace FinancialCopilot.UnitTests;

public sealed class MonthlyActivityBackfillCoordinatorTests
{
    // 2026-06-10 = 20 Khordad 1405 â†’ newest backfill month is Ordibehesht 1405 (1405/02);
    // permitted floor is 1404/01 â†’ 14 months total.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-10T08:00:00Z");

    [Fact]
    public async Task Start_EnqueuesNewestMonthFirstWithBoundedJalaliWindows()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        var publisher = new RecordingPublisher(db);
        var coordinator = NewCoordinator(db, publisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal("Started", result.Outcome);
        Assert.Equal(14, result.MonthsPlanned);
        Assert.Equal(1, result.CompaniesPlanned);
        Assert.Equal(14 * 5, result.RequestsEnqueued);
        var first = publisher.Requests[0];
        Assert.Equal(ProviderDataset.MonthlyProductionSales, first.Dataset);
        Assert.Equal("13150", first.ExternalReference);
        Assert.Equal("1405/02/01", first.SourceDateRangeStartJalali);
        Assert.Equal("1405/02/31", first.SourceDateRangeEndJalali);
        Assert.Equal(0, first.MonthlyActivityOutputType);
        Assert.Equal("nadpco-monthlybf-140502-13150-ot0", first.IdempotencyKey);
        var last = publisher.Requests[^1];
        Assert.Equal(4, last.MonthlyActivityOutputType);
        Assert.Equal("1404/01/01", last.SourceDateRangeStartJalali);
        Assert.Equal("nadpco-monthlybf-140401-13150-ot4", last.IdempotencyKey);
        Assert.Equal(1, publisher.DurableBatchCount);
    }

    [Fact]
    public async Task Start_WithTargetMonth_EnqueuesOnlyThatMonth()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        var publisher = new RecordingPublisher(db);
        var coordinator = NewCoordinator(db, publisher);

        var result = await coordinator.StartAsync(
            new MonthlyActivityBackfillRequest("test:admin", new ShamsiMonth(1405, 5)),
            CancellationToken.None);

        Assert.Equal("Started", result.Outcome);
        Assert.Equal(1, result.MonthsPlanned);
        Assert.Equal(1, result.CompaniesPlanned);
        Assert.Equal(5, result.RequestsEnqueued);
        var requests = publisher.Requests;
        Assert.Equal(5, requests.Count);
        Assert.Equal([0, 1, 2, 3, 4], requests.Select(request => request.MonthlyActivityOutputType));
        var request = requests[0];
        Assert.Equal(ProviderDataset.MonthlyProductionSales, request.Dataset);
        Assert.Equal("13150", request.ExternalReference);
        Assert.Equal("1405/05/01", request.SourceDateRangeStartJalali);
        Assert.Equal("1405/05/31", request.SourceDateRangeEndJalali);
        Assert.Equal("nadpco-monthlybf-140505-13150-ot0", request.IdempotencyKey);
        Assert.Equal(1, publisher.DurableBatchCount);
    }

    [Fact]
    public async Task Start_WhenDurableBatchIsActive_ReturnsSameBatchWithoutDuplicateOutboxRows()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        var relay = new RecordingPublisher(db);
        var coordinator = NewCoordinator(db, relay);

        var first = await coordinator.StartAsync(
            new MonthlyActivityBackfillRequest("test:admin", new ShamsiMonth(1405, 5)),
            CancellationToken.None);
        var second = await coordinator.StartAsync(
            new MonthlyActivityBackfillRequest("test:admin", new ShamsiMonth(1405, 5)),
            CancellationToken.None);

        Assert.Equal("Started", first.Outcome);
        Assert.Equal("AlreadyInProgress", second.Outcome);
        Assert.Equal(first.BatchId, second.BatchId);
        Assert.Single(db.MonthlyActivityBackfillBatches);
        Assert.Equal(5, db.MonthlyActivityBackfillOutbox.Count());
    }

    [Fact]
    public async Task Start_ActivePlanCrossingShamsiMonthBoundary_AppendsNewlyEligibleMonth()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-20T08:00:00Z"));
        var publisher = new RecordingPublisher(db);
        var coordinator = NewCoordinator(db, publisher, clock);

        var initial = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);
        Assert.Equal(16, initial.MonthsPlanned);
        Assert.DoesNotContain(publisher.Requests, request => request.IdempotencyKey.Contains("140505"));

        var activeBatch = await db.MonthlyActivityBackfillBatches.SingleAsync(batch => batch.ActiveSlot != null);
        activeBatch.ActiveSlot = null;
        activeBatch.Status = "CompletedWithRetryables";
        activeBatch.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();

        clock.UtcNow = DateTimeOffset.Parse("2026-08-23T08:00:00Z");
        var resumed = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal(17, resumed.MonthsPlanned);
        Assert.Contains(publisher.Requests, request => request.IdempotencyKey == "nadpco-monthlybf-140505-13150-ot0");
        var state = await db.MonthlyActivityBackfillStates.SingleAsync();
        Assert.Contains("\"Year\":1405,\"Month\":5", state.PlannedMonthsJson);
    }

    [Fact]
    public async Task Start_SkipsCompanyMonthsWhoseRunsAlreadyCompleted()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        // The newest month already completed in a previous invocation.
        for (var outputType = 0; outputType <= 4; outputType++)
        {
            db.SyncRuns.Add(new DataSyncRunRow
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = $"nadpco-monthlybf-140502-13150-ot{outputType}",
                Dataset = ProviderDataset.MonthlyProductionSales.ToString(),
                Status = DataSyncRunStatus.Completed.ToString(),
                RequestedAt = Now.AddHours(-1),
                ProviderName = ProviderSources.NoavaranCurrentApiName,
                ExternalReference = "13150",
                SourceDateRangeStartJalali = "1405/02/01",
                SourceDateRangeEndJalali = "1405/02/31"
            });
            SeedPersistedMonthlyReport(db, "13150", 1405, 2, outputType);
        }
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher(db);
        var coordinator = NewCoordinator(db, publisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal(13 * 5, result.RequestsEnqueued);
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
        var publisher = new RecordingPublisher(db);
        var coordinator = NewCoordinator(db, publisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal(14 * 5, result.RequestsEnqueued);
        Assert.Contains(publisher.Requests, request => request.IdempotencyKey == "nadpco-monthlybf-140502-13150-ot0");
    }

    [Fact]
    public async Task Progress_RecordsCompletionMarkerWhenEveryPlannedMonthCompleted()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        var publisher = new RecordingPublisher(db);
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
            SeedPersistedMonthlyReport(db, request.ExternalReference!, year, month, request.MonthlyActivityOutputType ?? 0);
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
        var publisher = new RecordingPublisher(db);
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
                SeedPersistedMonthlyReport(db, request.ExternalReference!, year, month, request.MonthlyActivityOutputType ?? 0);
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
        var publisher = new RecordingPublisher(db);
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
        var publisher = new RecordingPublisher(db);
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

        var initialPublisher = new RecordingPublisher(db);
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
                SeedPersistedMonthlyReport(db, request.ExternalReference!, year, month, request.MonthlyActivityOutputType ?? 0);
            }
        }

        var state = await db.MonthlyActivityBackfillStates.SingleAsync();
        state.IsCompleted = true;
        state.CompletedAt = Now;
        await db.SaveChangesAsync();

        var retryPublisher = new RecordingPublisher(db);
        var coordinator = NewCoordinator(db, retryPublisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal("Started", result.Outcome);
        Assert.Equal(5, result.RequestsEnqueued);
        Assert.Equal(5, retryPublisher.Requests.Count);
        Assert.Equal("nadpco-monthlybf-140407-13150-ot0", retryPublisher.Requests[0].IdempotencyKey);
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

        var initialPublisher = new RecordingPublisher(db);
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
                SeedPersistedMonthlyReport(db, request.ExternalReference!, year, month, request.MonthlyActivityOutputType ?? 0);
            }
        }

        var state = await db.MonthlyActivityBackfillStates.SingleAsync();
        state.IsCompleted = true;
        state.CompletedAt = Now;
        await db.SaveChangesAsync();

        var retryPublisher = new RecordingPublisher(db);
        var coordinator = NewCoordinator(db, retryPublisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal("Started", result.Outcome);
        Assert.Equal(5, result.RequestsEnqueued);
        Assert.Equal(5, retryPublisher.Requests.Count);
        Assert.Equal("nadpco-monthlybf-140502-13150-ot0", retryPublisher.Requests[0].IdempotencyKey);
        Assert.False(result.Progress.IsCompleted);
        Assert.Equal("CompletedWithFailures", result.Progress.Status);
    }

    [Fact]
    public async Task Start_CompletedGlobalStateWithoutRetryables_ReturnsAlreadyCompleted()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");

        var initialPublisher = new RecordingPublisher(db);
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
            SeedPersistedMonthlyReport(db, request.ExternalReference!, year, month, request.MonthlyActivityOutputType ?? 0);
        }

        var state = await db.MonthlyActivityBackfillStates.SingleAsync();
        state.IsCompleted = true;
        state.CompletedAt = Now;
        await db.SaveChangesAsync();

        var retryPublisher = new RecordingPublisher(db);
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
        // Ø­Ù‚ ØªÙ‚Ø¯Ù… and off-market listings must never reach the vendor.
        var rights = EligibleCompany("9001");
        rights.PrecedencyRight = 1;
        db.Companies.Add(rights);
        var fund = EligibleCompany("9002");
        fund.MarketId = Guid.NewGuid();
        db.Companies.Add(fund);
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher(db);
        var coordinator = NewCoordinator(db, publisher);

        var result = await coordinator.StartAsync(new MonthlyActivityBackfillRequest("test:admin"), CancellationToken.None);

        Assert.Equal(1, result.CompaniesPlanned);
        Assert.All(publisher.Requests, request => Assert.Equal("13150", request.ExternalReference));
    }

    [Fact]
    public async Task Start_EnqueuesMonthlyRequestsInOutputTypeWaves()
    {
        await using var db = CreateDb();
        SeedEligibleCompany(db, "13150");
        SeedEligibleCompany(db, "13151");
        var publisher = new RecordingPublisher(db);
        var coordinator = NewCoordinator(db, publisher);

        await coordinator.StartAsync(
            new MonthlyActivityBackfillRequest("test:admin", new ShamsiMonth(1405, 5)),
            CancellationToken.None);

        var requests = publisher.Requests;
        Assert.Equal(10, requests.Count);
        Assert.Equal([0, 0, 1, 1, 2, 2, 3, 3, 4, 4], requests.Select(r => r.MonthlyActivityOutputType));
        Assert.Equal(["13150", "13151", "13150", "13151", "13150", "13151", "13150", "13151", "13150", "13151"],
            requests.Select(r => r.ExternalReference));
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
        int jalaliMonth,
        int outputType = 0)
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
            ExternalReportId = $"ProductSales:{externalCompanyId}:{jalaliYear}-{jalaliMonth:D2}:output-{outputType}",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            OutputType = outputType,
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

    private sealed class RecordingPublisher : IMonthlyActivityBackfillOutboxRelay
    {
        private readonly FinancialIngestionDbContext _db;
        private readonly HashSet<Guid> _existingMessageIds;

        public RecordingPublisher(FinancialIngestionDbContext db)
        {
            _db = db;
            _existingMessageIds = db.MonthlyActivityBackfillOutbox
                .Select(row => row.Id)
                .ToHashSet();
        }

        public IReadOnlyList<DataSyncRequest> Requests =>
            _db.MonthlyActivityBackfillOutbox.AsNoTracking()
                .Where(row => !_existingMessageIds.Contains(row.Id))
                .OrderBy(row => row.CreatedAt)
                .ThenBy(row => row.Sequence)
                .AsEnumerable()
                .Select(row => JsonSerializer.Deserialize<DataSyncRequest>(row.PayloadJson, JsonOptions)!)
                .ToArray();

        public int DurableBatchCount =>
            _db.MonthlyActivityBackfillOutbox.AsNoTracking()
                .Where(row => !_existingMessageIds.Contains(row.Id))
                .Select(row => row.BatchId)
                .Distinct()
                .Count();

        public Task<int> RelayPendingAsync(int maximumCount, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public async Task<int> ReconcileActiveBatchesAsync(CancellationToken cancellationToken)
        {
            var active = await _db.MonthlyActivityBackfillBatches
                .Where(batch => batch.ActiveSlot != null)
                .ToArrayAsync(cancellationToken);
            foreach (var batch in active)
            {
                var keys = await _db.MonthlyActivityBackfillOutbox.AsNoTracking()
                    .Where(row => row.BatchId == batch.Id)
                    .Select(row => row.IdempotencyKey)
                    .ToArrayAsync(cancellationToken);
                var terminalCount = await _db.SyncRuns.AsNoTracking()
                    .CountAsync(run => keys.Contains(run.IdempotencyKey) &&
                        (run.Status == "Completed" || run.Status == "Failed"), cancellationToken);
                if (terminalCount == keys.Length)
                {
                    batch.ActiveSlot = null;
                    batch.Status = "CompletedWithRetryables";
                    batch.CompletedAt = Now;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            return active.Length;
        }

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
