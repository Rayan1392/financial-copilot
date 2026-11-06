using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Domain.Financial.CodalAlerts;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Infrastructure.Financial.CodalAlerts;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Insights;
using FinancialCopilot.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Watchlist «رویدادها» regressions: a re-sync, backfill or recalculation of historical data must not
/// resurface as a new event, re-running the detector must not refresh an event's age, and ranking
/// recalculation must not mint duplicate events. Freshness is judged by the source fact's publication.
/// </summary>
public sealed class InsightSourceFreshnessTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-06T06:00:00Z");
    private const string Provider = "NoavaranCurrentApi";
    private const string CompanyId = "13226";

    [Fact]
    public async Task ResyncedHistoricalStatement_IsNotPublished_AndDoesNotAlertSubscribers()
    {
        await using var db = CreateDb();
        AddStatement(db, "old-statement", publishedAt: DateOnly.FromDateTime(Now.UtcDateTime).AddYears(-1), lastSync: Now);
        AddStatement(db, "no-announcement-date", publishedAt: null, lastSync: Now);
        AddSubscription(db, """["FinancialStatement"]""");
        db.SaveChanges();

        var events = await RunAsync(db, Now);

        Assert.DoesNotContain(events, e => e.InsightType == InsightType.FinancialStatementPublished);
        Assert.DoesNotContain(events, e => e.InsightType == InsightType.CodalAnnouncementMatched);
        Assert.Empty(db.NotificationIntents);
    }

    [Fact]
    public async Task NewlyPublishedStatement_IsPublished_WithExpiryFromAnnouncementDate()
    {
        await using var db = CreateDb();
        var publishedAt = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-2);
        AddStatement(db, "new-statement", publishedAt, lastSync: Now);
        db.SaveChanges();

        var events = await RunAsync(db, Now);

        var statement = Assert.Single(events, e => e.InsightType == InsightType.FinancialStatementPublished);
        Assert.Equal(new DateTimeOffset(publishedAt.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(30), statement.ExpiresAtUtc);
    }

    [Fact]
    public async Task MonthlyBackfillOfOldMonths_OnlyLatestAcceptedPeriodIsNew()
    {
        await using var db = CreateDb();
        AddMonthlyReport(db, "r-1405-01", new DateOnly(2026, 4, 20), lastSync: Now);
        AddMonthlyReport(db, "r-1405-02", new DateOnly(2026, 5, 21), lastSync: Now);
        AddMonthlyReport(db, "r-1405-03", new DateOnly(2026, 6, 21), lastSync: Now);
        AddMonthlyReport(db, "r-1405-04-pending", new DateOnly(2026, 7, 22), lastSync: Now, accepted: false);
        db.SaveChanges();

        var events = await RunAsync(db, Now);

        var monthly = Assert.Single(events, e => e.InsightType == InsightType.MonthlyReportPublished);
        Assert.Equal("2026-06-21", monthly.SourcePeriod);
    }

    [Fact]
    public async Task RecalculatedSnapshots_WithoutNewReport_ProduceNoAnomalyOrQualityEvent()
    {
        await using var db = CreateDb();
        AddMonthlyReport(db, "r-1405-03", new DateOnly(2026, 6, 21), lastSync: Now.AddDays(-60));
        AddTrendSnapshot(db, "r-1405-03", 1405, 3, sales: 200m, average: 100m, calculatedAt: Now);
        AddQualitySnapshot(db, 1405, 2, score: 40m, calculatedAt: Now);
        AddQualitySnapshot(db, 1405, 3, score: 90m, calculatedAt: Now);
        db.SaveChanges();

        var events = await RunAsync(db, Now);

        Assert.DoesNotContain(events, e => e.InsightType == InsightType.MonthlySalesAnomaly);
        Assert.DoesNotContain(events, e => e.InsightType == InsightType.MonthlyQualityRankingChange);
    }

    [Fact]
    public async Task QualityRankingRecalculation_KeepsOneEventPerCompanyPeriod()
    {
        await using var db = CreateDb();
        AddMonthlyReport(db, "r-1405-03", new DateOnly(2026, 6, 21), lastSync: Now.AddDays(-1));
        AddTrendSnapshot(db, "r-1405-03", 1405, 3, sales: 200m, average: 100m, calculatedAt: Now);
        AddQualitySnapshot(db, 1405, 2, score: 40m, calculatedAt: Now);
        var current = AddQualitySnapshot(db, 1405, 3, score: 90m, calculatedAt: Now);
        db.SaveChanges();

        await RunAsync(db, Now);
        // A recalculation replaces the snapshot row (new id) for the same company/period.
        db.MonthlySalesQualityRankingSnapshots.Remove(current);
        AddQualitySnapshot(db, 1405, 3, score: 91m, calculatedAt: Now.AddMinutes(5));
        db.SaveChanges();
        await RunAsync(db, Now.AddMinutes(5));

        Assert.Single(db.InsightEvents, e => e.InsightType == nameof(InsightType.MonthlyQualityRankingChange));
        Assert.Single(db.InsightEvents, e => e.InsightType == nameof(InsightType.MonthlySalesAnomaly));
    }

    [Fact]
    public async Task Rerun_KeepsFirstDetectionTime_AndExpiryFromSourceFact()
    {
        await using var db = CreateDb();
        var firstStoredAt = Now.AddDays(-1);
        AddMonthlyReport(db, "r-1405-03", new DateOnly(2026, 6, 21), lastSync: firstStoredAt);
        db.SaveChanges();

        await RunAsync(db, Now);
        await RunAsync(db, Now.AddHours(3));

        var row = Assert.Single(db.InsightEvents, e => e.InsightType == nameof(InsightType.MonthlyReportPublished));
        Assert.Equal(Now, row.DetectedAtUtc);
        Assert.Equal(firstStoredAt.AddDays(30), row.ExpiresAtUtc);
    }

    private static async Task<IReadOnlyList<InsightFeedItem>> RunAsync(FinancialIngestionDbContext db, DateTimeOffset now)
    {
        var clock = new FixedTimeProvider(now);
        var scoring = new DeterministicInsightScoringService();
        var policy = new InsightDeduplicationPolicy();
        IInsightDetector[] detectors =
        [
            new MonthlyReportPublishedDetector(db, scoring, policy),
            new MonthlySalesAnomalyDetector(db, scoring, policy),
            new MonthlyQualityRankingChangeDetector(db, scoring, policy),
            new FinancialStatementPublishedDetector(db, scoring, policy),
            new SubscribedCodalAnnouncementDetector(
                db, scoring, policy,
                new EfCoreCodalAlertSubscriptionRepository(db),
                new EfCoreNotificationIntentPublisher(db, clock))
        ];
        var repository = new InsightEventRepository(db, clock);
        var useCase = new GenerateMarketInsightsUseCase(detectors, repository, clock, NullLogger<GenerateMarketInsightsUseCase>.Instance);
        await useCase.ExecuteAsync(new GenerateMarketInsightsRequest(LookbackDays: 7));
        db.ChangeTracker.Clear();
        return (await repository.QueryAsync(new InsightFeedQuery(Take: 100))).Items;
    }

    private static void AddStatement(FinancialIngestionDbContext db, string id, DateOnly? publishedAt, DateTimeOffset lastSync) =>
        db.FinancialStatements.Add(new NormalizedFinancialStatementRow
        {
            Id = Guid.NewGuid(),
            ProviderName = Provider,
            ExternalCompanyId = CompanyId,
            ExternalStatementId = id,
            StatementType = "IncomeStatement",
            PeriodType = "ThreeMonths",
            PeriodStart = new DateOnly(2025, 3, 21),
            PeriodEnd = new DateOnly(2025, 6, 21),
            SourcePayloadChecksum = id,
            LastSynchronizedAt = lastSync,
            PublishedAt = publishedAt
        });

    private static void AddMonthlyReport(
        FinancialIngestionDbContext db, string id, DateOnly periodEnd, DateTimeOffset lastSync, bool accepted = true) =>
        db.MonthlyReports.Add(new NormalizedMonthlyReportRow
        {
            Id = Guid.NewGuid(),
            ProviderName = Provider,
            ExternalCompanyId = CompanyId,
            ExternalReportId = id,
            PeriodStart = periodEnd.AddDays(-30),
            PeriodEnd = periodEnd,
            SourcePayloadChecksum = id,
            LastSynchronizedAt = lastSync,
            ReportType = "ProductSales",
            OutputType = 0,
            IsAccepted = accepted,
            RevisionStatus = accepted ? "Accepted" : "Pending"
        });

    private static void AddTrendSnapshot(
        FinancialIngestionDbContext db, string reportId, int year, byte month, decimal sales, decimal average, DateTimeOffset calculatedAt) =>
        db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = CompanyId,
            CompanySymbol = "کچاد",
            CompanyName = "Kavire Chadormalu",
            ReportYear = year,
            ReportMonth = month,
            MonthlySalesAmount = sales,
            Average12MonthSalesAmount = average,
            Average12MonthPeriodCount = 12,
            IsAverage12MonthComplete = true,
            DataCompletenessScore = 95m,
            SourceProviderName = Provider,
            SourceReportId = reportId,
            CalculatedAtUtc = calculatedAt
        });

    private static MonthlySalesQualityRankingSnapshotRow AddQualitySnapshot(
        FinancialIngestionDbContext db, int year, byte month, decimal score, DateTimeOffset calculatedAt)
    {
        var row = new MonthlySalesQualityRankingSnapshotRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = CompanyId,
            CompanySymbol = "کچاد",
            CompanyName = "Kavire Chadormalu",
            ReportYear = year,
            ReportMonth = month,
            MonthlySalesAmount = 100m,
            QualityScore = score,
            QualityLabel = "Average",
            ConfidenceScore = 90m,
            RankMarket = 1,
            SourceProviderName = Provider,
            CalculatedAtUtc = calculatedAt,
            IsEligible = true
        };
        db.MonthlySalesQualityRankingSnapshots.Add(row);
        return row;
    }

    private static void AddSubscription(FinancialIngestionDbContext db, string typesJson) =>
        db.CodalAlertSubscriptions.Add(new CodalAlertSubscriptionRow
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorType = "User",
            ExternalCompanyId = CompanyId,
            AnnouncementTypesJson = typesJson,
            MinimumImportance = CodalAnnouncementImportance.Notice.ToString(),
            RawAlertEnabled = true,
            AiSummaryEnabled = false,
            State = CodalAlertSubscriptionState.Active.ToString(),
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        });

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
