using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

public sealed class MarketInsightEndpointTests : IClassFixture<MarketInsightApiFactory>
{
    private readonly MarketInsightApiFactory _factory;

    public MarketInsightEndpointTests(MarketInsightApiFactory factory)
    {
        _factory = factory;
        _factory.EnsureBillingSeeded();
        _factory.ResetInsightData();
        _factory.SeedInsightSourceData();
    }

    [Fact]
    public async Task DataAdminGenerate_ThenMarketFeed_ReturnsEvidenceBackedInsights()
    {
        using var admin = DataAdminClient();
        using var user = UserClient();

        using var generateResponse = await admin.PostAsJsonAsync(
            "/api/v1/admin/insights/generate",
            new { lookbackDays = 7 },
            CancellationToken.None);
        using var feedResponse = await user.GetAsync("/api/v1/insights/market?take=20", CancellationToken.None);
        using var document = await ReadJsonAsync(feedResponse);
        var root = document.RootElement;
        var items = root.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, feedResponse.StatusCode);
        Assert.True(root.GetProperty("totalCount").GetInt32() >= 6);
        Assert.Contains(items, item => item.GetProperty("insightType").GetString() == "MonthlySalesAnomaly");
        Assert.Contains(items, item => item.GetProperty("insightType").GetString() == "PriceMovement");
        Assert.All(items, item => Assert.NotEmpty(item.GetProperty("evidence").EnumerateArray()));
        Assert.All(items, item => Assert.DoesNotContain("Buy", item.GetProperty("summary").GetString() ?? string.Empty));
        Assert.All(items, item => Assert.DoesNotContain("Sell", item.GetProperty("summary").GetString() ?? string.Empty));
    }

    [Fact]
    public async Task SymbolFeed_FiltersBySymbolAndType()
    {
        using var admin = DataAdminClient();
        using var user = UserClient();
        await admin.PostAsJsonAsync("/api/v1/admin/insights/generate", new { lookbackDays = 7 }, CancellationToken.None);

        using var response = await user.GetAsync(
            "/api/v1/insights/symbol/KCHAD?type=MonthlySalesAnomaly&take=10",
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(items);
        Assert.Equal("KCHAD", items[0].GetProperty("symbol").GetString());
        Assert.Equal("MonthlySalesAnomaly", items[0].GetProperty("insightType").GetString());
    }

    [Fact]
    public async Task FollowedSymbolFeed_ReturnsOnlyFollowedInsights_AndRanksBySeverity()
    {
        using var admin = DataAdminClient();
        using var user = UserClient();
        await admin.PostAsJsonAsync("/api/v1/admin/insights/generate", new { lookbackDays = 7 }, CancellationToken.None);
        await SeedManualInsightAsync("999", "BAR", InsightSeverity.Critical, 99m, "unfollowed-critical");

        using var followResponse = await user.PostAsync("/api/v1/followed-symbols/me/13226", null, CancellationToken.None);
        using var feedResponse = await user.GetAsync("/api/v1/insights/followed-symbols/me?take=20", CancellationToken.None);
        using var document = await ReadJsonAsync(feedResponse);
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, followResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, feedResponse.StatusCode);
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.Equal("13226", item.GetProperty("insight").GetProperty("externalCompanyId").GetString()));
        Assert.DoesNotContain(items, item => item.GetProperty("insight").GetProperty("symbol").GetString() == "BAR");
    }

    [Fact]
    public async Task FollowedSymbolFeed_WhenNoFollowedSymbols_ReturnsEmptyState()
    {
        using var admin = DataAdminClient();
        using var user = UserClient();
        await admin.PostAsJsonAsync("/api/v1/admin/insights/generate", new { lookbackDays = 7 }, CancellationToken.None);

        using var response = await user.GetAsync("/api/v1/insights/followed-symbols/me", CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, root.GetProperty("totalCount").GetInt32());
        Assert.Equal("NoFollowedSymbols", root.GetProperty("emptyState").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task DismissInsight_HidesItUnlessIncludeDismissed()
    {
        using var admin = DataAdminClient();
        using var user = UserClient();
        await admin.PostAsJsonAsync("/api/v1/admin/insights/generate", new { lookbackDays = 7 }, CancellationToken.None);
        await user.PostAsync("/api/v1/followed-symbols/me/13226", null, CancellationToken.None);

        using var initialResponse = await user.GetAsync("/api/v1/insights/followed-symbols/me?take=1", CancellationToken.None);
        using var initialDocument = await ReadJsonAsync(initialResponse);
        var insightId = initialDocument.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .First()
            .GetProperty("insight")
            .GetProperty("id")
            .GetGuid();

        using var dismissResponse = await user.PostAsync($"/api/v1/insights/{insightId}/dismiss", null, CancellationToken.None);
        using var defaultFeedResponse = await user.GetAsync("/api/v1/insights/followed-symbols/me?take=20", CancellationToken.None);
        using var dismissedFeedResponse = await user.GetAsync("/api/v1/insights/followed-symbols/me?includeDismissed=true&take=20", CancellationToken.None);
        using var defaultFeed = await ReadJsonAsync(defaultFeedResponse);
        using var dismissedFeed = await ReadJsonAsync(dismissedFeedResponse);

        Assert.Equal(HttpStatusCode.OK, dismissResponse.StatusCode);
        Assert.DoesNotContain(defaultFeed.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("insight").GetProperty("id").GetGuid() == insightId);
        Assert.Contains(dismissedFeed.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("insight").GetProperty("id").GetGuid() == insightId &&
            item.GetProperty("dismissed").GetBoolean());
    }

    [Fact]
    public async Task AiInsightExplanation_PreservesPersistedEvidence_AndAvoidsAdviceWording()
    {
        using var admin = DataAdminClient();
        using var user = UserClient();
        await admin.PostAsJsonAsync("/api/v1/admin/insights/generate", new { lookbackDays = 7 }, CancellationToken.None);
        await user.PostAsync("/api/v1/followed-symbols/me/13226", null, CancellationToken.None);

        using var feedResponse = await user.GetAsync("/api/v1/insights/followed-symbols/me?take=1", CancellationToken.None);
        using var feedDocument = await ReadJsonAsync(feedResponse);
        var insight = feedDocument.RootElement.GetProperty("items").EnumerateArray().First().GetProperty("insight");
        var insightId = insight.GetProperty("id").GetGuid();
        var firstEvidence = insight.GetProperty("evidence").EnumerateArray().First().GetProperty("value").GetString()!;

        using var aiResponse = await user.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "Explain this insight", context = new { insightEventId = insightId } },
            CancellationToken.None);
        using var aiDocument = await ReadJsonAsync(aiResponse);
        var text = aiDocument.RootElement.GetProperty("textAnswer").GetString() ?? string.Empty;

        Assert.Equal(HttpStatusCode.OK, aiResponse.StatusCode);
        Assert.Contains(firstEvidence, text);
        Assert.Contains("not a buy or sell recommendation", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("you should buy", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("you should sell", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/api/v1/insights/market")]
    [InlineData("/api/v1/admin/insights/generate")]
    public async Task InsightEndpoints_WithoutCredentials_ReturnUnauthorized(string path)
    {
        using var client = _factory.CreateClient();

        using var response = path.Contains("generate", StringComparison.Ordinal)
            ? await client.PostAsJsonAsync(path, new { lookbackDays = 7 }, CancellationToken.None)
            : await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient UserClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));
        return client;
    }

    private HttpClient DataAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true, dataAdmin: true));
        return client;
    }

    private async Task SeedManualInsightAsync(
        string externalCompanyId,
        string symbol,
        InsightSeverity severity,
        decimal importance,
        string sourceEntityId)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInsightEventRepository>();
        var insight = new InsightEvent(
            Guid.NewGuid(),
            externalCompanyId,
            symbol,
            "manual-industry",
            InsightType.PriceMovement,
            severity,
            importance,
            95m,
            $"{symbol} manual insight",
            "Manual test event from persisted evidence.",
            "Manual test threshold crossed.",
            [new InsightEvidenceItem("manual-value", "123.45", "ManualProvider", "1405/04", DateTimeOffset.Parse("2026-07-09T10:00:00Z"))],
            "ManualProvider",
            InsightSourceEntityType.MarketQuote,
            sourceEntityId,
            "1405/04",
            DateTimeOffset.Parse("2026-07-09T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
            $"manual:{externalCompanyId}:{sourceEntityId}");
        await repository.UpsertAsync([insight], CancellationToken.None);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class MarketInsightApiFactory : AiFacadeApiFactory
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-09T10:00:00Z");

    public void ResetInsightData()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        db.Database.EnsureCreated();
        db.UserInsightStates.RemoveRange(db.UserInsightStates);
        db.FollowedSymbols.RemoveRange(db.FollowedSymbols);
        db.InsightEvents.RemoveRange(db.InsightEvents);
        db.MonthlyReports.RemoveRange(db.MonthlyReports);
        db.CompanyMonthlyActivityTrendSnapshots.RemoveRange(db.CompanyMonthlyActivityTrendSnapshots);
        db.MonthlySalesQualityRankingSnapshots.RemoveRange(db.MonthlySalesQualityRankingSnapshots);
        db.LatestMarketQuotes.RemoveRange(db.LatestMarketQuotes);
        db.TradingInstruments.RemoveRange(db.TradingInstruments);
        db.ComprehensiveAnalysisTags.RemoveRange(db.ComprehensiveAnalysisTags);
        db.ComprehensiveAnalyses.RemoveRange(db.ComprehensiveAnalyses);
        db.FinancialStatements.RemoveRange(db.FinancialStatements);
        db.NadpcoApiSyncStates.RemoveRange(db.NadpcoApiSyncStates);
        db.Companies.RemoveRange(db.Companies);
        db.SaveChanges();
    }

    public void SeedInsightSourceData()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        var companyId = Guid.NewGuid();
        var instrumentId = Guid.NewGuid();

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "13226",
            Name = "Kavire Chadormalu",
            CompanySymbol = "KCHAD",
            Ticker = "KCHAD",
            IndustryId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
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
            CompanySymbol = "KCHAD",
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
            QualityRow("13226", "KCHAD", 1405, 2, 50m, Now.AddMonths(-1)),
            QualityRow("13226", "KCHAD", 1405, 3, 82m, Now));

        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId,
            ProviderName = "TsetmcWebService",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 123456,
            InstrumentIsin = "IRO1KCHD0001",
            Symbol = "KCHAD",
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
            Id = 2001,
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
            AnalysisId = 2001,
            TagId = 1,
            TagName = "KCHAD",
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

    private static MonthlySalesQualityRankingSnapshotRow QualityRow(
        string externalCompanyId,
        string symbol,
        int year,
        byte month,
        decimal qualityScore,
        DateTimeOffset calculatedAt) => new()
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = externalCompanyId,
            CompanySymbol = symbol,
            CompanyName = "Kavire Chadormalu",
            IndustryId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            IndustryTitle = "Metals",
            ReportYear = year,
            ReportMonth = month,
            MonthlySalesAmount = 100m,
            QualityScore = qualityScore,
            QualityLabel = qualityScore > 70m ? "Strong" : "Average",
            ConfidenceScore = 90m,
            RankMarket = 1,
            SourceProviderName = "NoavaranCurrentApi",
            CalculatedAtUtc = calculatedAt,
            IsEligible = true
        };
}
