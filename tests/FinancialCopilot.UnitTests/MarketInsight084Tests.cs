using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Insights;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class MarketInsight084Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-09T10:00:00Z");

    [Fact]
    public void Scoring_ReturnsCriticalSeverity_ForLargeFreshEvidenceBackedEvent()
    {
        var score = new DeterministicInsightScoringService().Score(
            new InsightScoringInput(
                Magnitude: 90m,
                SourceConfidence: 90m,
                EvidenceCompleteness: 95m,
                FreshnessScore: 90m,
                RarityScore: 80m));

        Assert.Equal(InsightSeverity.Critical, score.Severity);
        Assert.InRange(score.ImportanceScore, 85m, 100m);
        Assert.InRange(score.ConfidenceScore, 85m, 100m);
    }

    [Fact]
    public void DeduplicationPolicy_UsesStableSourceBoundKey()
    {
        var policy = new InsightDeduplicationPolicy();

        var first = policy.BuildKey(
            InsightType.MonthlySalesAnomaly,
            " 13226 ",
            "NoavaranCurrentApi",
            InsightSourceEntityType.MonthlyActivityTrendSnapshot,
            "report-1",
            "1405/03");
        var second = policy.BuildKey(
            InsightType.MonthlySalesAnomaly,
            "13226",
            "noavarancurrentapi",
            InsightSourceEntityType.MonthlyActivityTrendSnapshot,
            "REPORT-1",
            "1405/03");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GenerateMarketInsights_EmitsAllV1DetectorTypes_AndUpsertsByDeduplicationKey()
    {
        await using var db = CreateDb();
        SeedDetectorData(db);

        var repository = new InsightEventRepository(db, new FixedTimeProvider(Now));
        var detectors = CreateDetectors(db);
        var useCase = new GenerateMarketInsightsUseCase(
            detectors,
            repository,
            new FixedTimeProvider(Now),
            NullLogger<GenerateMarketInsightsUseCase>.Instance);

        var first = await useCase.ExecuteAsync(new GenerateMarketInsightsRequest(LookbackDays: 7));
        var second = await useCase.ExecuteAsync(new GenerateMarketInsightsRequest(LookbackDays: 7));
        var feed = await repository.QueryAsync(new InsightFeedQuery(Take: 20));

        Assert.Equal(7, first.DetectorsRun);
        Assert.Equal(7, second.DetectorsRun);
        Assert.Equal(7, feed.TotalCount);
        Assert.Contains(feed.Items, item => item.InsightType == InsightType.MonthlyReportPublished);
        Assert.Contains(feed.Items, item => item.InsightType == InsightType.MonthlySalesAnomaly);
        Assert.Contains(feed.Items, item => item.InsightType == InsightType.MonthlyQualityRankingChange);
        Assert.Contains(feed.Items, item => item.InsightType == InsightType.PriceMovement);
        Assert.Contains(feed.Items, item => item.InsightType == InsightType.ComprehensiveAnalysisPublished);
        Assert.Contains(feed.Items, item => item.InsightType == InsightType.FinancialStatementPublished);
        Assert.Contains(feed.Items, item => item.InsightType == InsightType.DataFreshnessWarning);
        Assert.All(feed.Items, item => Assert.NotEmpty(item.Evidence));
        Assert.All(feed.Items, item => Assert.DoesNotContain("buy", item.Summary, StringComparison.OrdinalIgnoreCase));
        Assert.All(feed.Items, item => Assert.DoesNotContain("sell", item.Summary, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RepositoryQuery_FiltersSymbolAndExcludesExpiredInsightsByDefault()
    {
        await using var db = CreateDb();
        var repository = new InsightEventRepository(db, new FixedTimeProvider(Now));
        var scoring = new DeterministicInsightScoringService();
        var policy = new InsightDeduplicationPolicy();
        var score = scoring.Score(new InsightScoringInput(80m, 90m, 90m, 90m, 70m));

        var active = CreateManualInsight("13226", "کچاد", score, "active", Now.AddDays(1), policy);
        var expired = CreateManualInsight("13227", "فملی", score, "expired", Now.AddDays(-1), policy);
        await repository.UpsertAsync([active, expired]);

        var market = await repository.QueryAsync(new InsightFeedQuery(Take: 10));
        var symbol = await repository.QueryAsync(new InsightFeedQuery(Symbol: "کچاد", Take: 10));
        var includeExpired = await repository.QueryAsync(new InsightFeedQuery(IncludeExpired: true, Take: 10));

        Assert.Single(market.Items);
        Assert.Single(symbol.Items);
        Assert.Equal("کچاد", symbol.Items[0].Symbol);
        Assert.Equal(2, includeExpired.TotalCount);
    }

    private static IReadOnlyList<IInsightDetector> CreateDetectors(FinancialIngestionDbContext db)
    {
        var scoring = new DeterministicInsightScoringService();
        var policy = new InsightDeduplicationPolicy();
        return
        [
            new MonthlyReportPublishedDetector(db, scoring, policy),
            new MonthlySalesAnomalyDetector(db, scoring, policy),
            new MonthlyQualityRankingChangeDetector(db, scoring, policy),
            new PriceMovementDetector(db, scoring, policy),
            new ComprehensiveAnalysisPublishedDetector(db, scoring, policy),
            new FinancialStatementPublishedDetector(db, scoring, policy),
            new DataFreshnessDetector(db, scoring, policy)
        ];
    }

    private static InsightEvent CreateManualInsight(
        string externalCompanyId,
        string symbol,
        InsightScore score,
        string sourceEntityId,
        DateTimeOffset? expiresAt,
        IInsightDeduplicationPolicy policy)
    {
        var key = policy.BuildKey(
            InsightType.MonthlySalesAnomaly,
            externalCompanyId,
            "NoavaranCurrentApi",
            InsightSourceEntityType.MonthlyActivityTrendSnapshot,
            sourceEntityId,
            "1405/03");

        return new InsightEvent(
            Guid.NewGuid(),
            externalCompanyId,
            symbol,
            "industry-1",
            InsightType.MonthlySalesAnomaly,
            score.Severity,
            score.ImportanceScore,
            score.ConfidenceScore,
            $"{symbol} anomaly",
            "Important event detected from deterministic evidence.",
            "A configured threshold was crossed.",
            [new InsightEvidenceItem("value", "42", "NoavaranCurrentApi", "1405/03", Now)],
            "NoavaranCurrentApi",
            InsightSourceEntityType.MonthlyActivityTrendSnapshot,
            sourceEntityId,
            "1405/03",
            Now,
            expiresAt,
            key);
    }

    private static void SeedDetectorData(FinancialIngestionDbContext db)
    {
        var companyId = Guid.NewGuid();
        var instrumentId = Guid.NewGuid();

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "13226",
            Name = "Kavire Chadormalu",
            CompanySymbol = "کچاد",
            Ticker = "کچاد",
            IndustryId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            LastSynchronizedAt = Now
        });

        db.MonthlyReports.Add(new NormalizedMonthlyReportRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "13226",
            ExternalReportId = "monthly-13226-140503",
            PeriodStart = new DateOnly(2026, 5, 22),
            PeriodEnd = new DateOnly(2026, 6, 21),
            SourcePayloadChecksum = "monthly-checksum",
            LastSynchronizedAt = Now,
            ReportType = "ProductSales",
            OutputType = 0
        });

        db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "13226",
            CompanySymbol = "کچاد",
            CompanyName = "Kavire Chadormalu",
            IndustryId = 12,
            IndustryTitle = "Metals",
            ReportYear = 1405,
            ReportMonth = 3,
            MonthlySalesAmount = 150m,
            Average12MonthSalesAmount = 100m,
            Average12MonthPeriodCount = 12,
            IsAverage12MonthComplete = true,
            DataCompletenessScore = 95m,
            SourceProviderName = "NoavaranCurrentApi",
            SourceReportId = "monthly-13226-140503",
            CalculatedAtUtc = Now
        });

        db.MonthlySalesQualityRankingSnapshots.AddRange(
            new MonthlySalesQualityRankingSnapshotRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "13226",
                CompanySymbol = "کچاد",
                CompanyName = "Kavire Chadormalu",
                IndustryId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                IndustryTitle = "Metals",
                ReportYear = 1405,
                ReportMonth = 2,
                MonthlySalesAmount = 100m,
                QualityScore = 50m,
                QualityLabel = "Average",
                ConfidenceScore = 80m,
                RankMarket = 10,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = Now.AddMonths(-1),
                IsEligible = true
            },
            new MonthlySalesQualityRankingSnapshotRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "13226",
                CompanySymbol = "کچاد",
                CompanyName = "Kavire Chadormalu",
                IndustryId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                IndustryTitle = "Metals",
                ReportYear = 1405,
                ReportMonth = 3,
                MonthlySalesAmount = 150m,
                QualityScore = 82m,
                QualityLabel = "Strong",
                ConfidenceScore = 90m,
                RankMarket = 3,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = Now,
                IsEligible = true
            });

        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId,
            ProviderName = "TsetmcWebService",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 123456,
            InstrumentIsin = "IRO1KCHD0001",
            Symbol = "کچاد",
            Name = "Kavire Chadormalu",
            MarketCode = "TSE",
            InstrumentKind = "Equity",
            NormalizedCompanyId = companyId,
            IsActive = true,
            SourceChangedAt = Now,
            LastSynchronizedAt = Now
        });

        db.LatestMarketQuotes.Add(new LatestMarketQuoteRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            TradingInstrumentId = instrumentId,
            LatestPrice = 1250m,
            PriceChangePercentage = 6.5m,
            SourceKind = "Intraday",
            TradingDate = new DateOnly(2026, 7, 9),
            AsOf = Now
        });

        db.ComprehensiveAnalyses.Add(new ComprehensiveAnalysisRow
        {
            Id = 1001,
            Title = "New analysis for KCHAD",
            Summary = "<p>summary</p>",
            PlainTextSummary = "summary",
            CreatedAt = Now,
            PersianCreatedAt = "1405/04/18",
            AuthorId = 1,
            AuthorName = "Analyst",
            SyncedAt = Now
        });
        db.ComprehensiveAnalysisTags.Add(new ComprehensiveAnalysisTagRow
        {
            AnalysisId = 1001,
            TagId = 1,
            TagName = "کچاد",
            TagSlug = "kchad",
            TagTypeId = 1,
            IsAnalytic = false
        });

        db.FinancialStatements.Add(new NormalizedFinancialStatementRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "13226",
            ExternalStatementId = "statement-13226-1",
            StatementType = "IncomeStatement",
            PeriodType = "ThreeMonths",
            PeriodStart = new DateOnly(2026, 3, 21),
            PeriodEnd = new DateOnly(2026, 6, 21),
            SourcePayloadChecksum = "statement-checksum",
            LastSynchronizedAt = Now,
            IsAudited = false,
            IsRepresented = false,
            IsComposing = false
        });

        db.NadpcoApiSyncStates.Add(new NadpcoApiSyncStateRow
        {
            Dataset = "MonthlyProductionSales",
            LastSuccessfulSyncAt = Now.AddDays(-5)
        });

        db.SaveChanges();
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
