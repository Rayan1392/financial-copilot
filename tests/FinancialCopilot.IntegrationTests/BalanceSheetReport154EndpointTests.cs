using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 154 T21 through the real V2 endpoint: typed response, persistence and replay without recomputation, one
/// Billing operation per request, and Feature 083 coexistence. Data is the supplied وبملت fixture plus clearly
/// labelled SYNTHETIC baselines.
/// </summary>
public sealed class BalanceSheetReport154EndpointTests : IClassFixture<BalanceSheetReport154ApiFactory>
{
    private readonly BalanceSheetReport154ApiFactory _factory;

    public BalanceSheetReport154EndpointTests(BalanceSheetReport154ApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }

    [Fact]
    public async Task DefaultReport_IsTyped_PersistedAndReplayedWithoutRecomputation_AndChargedOnce()
    {
        using var client = Client();
        var usageBefore = _factory.ReadUsageEntries().Count;
        var reservationsBefore = _factory.ReadBillingReservations().Count;

        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = "ترازنامه وبملت" }, CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);
        var table = root.GetProperty("financialStatementTableResult");
        Assert.Equal(JsonValueKind.Null, table.GetProperty("source").ValueKind);
        var report = table.GetProperty("balanceSheetReport");
        Assert.Equal("154.3", report.GetProperty("contractVersion").GetString());
        Assert.Equal("available", report.GetProperty("outcome").GetString());
        Assert.Equal("BankingBalanceSheet", report.GetProperty("templateId").GetString());
        Assert.Equal("default", report.GetProperty("requestedView").GetString());
        var candidates = report.GetProperty("comparison").GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal(["fiscal_year_end", "previous_fiscal_quarter_end", "previous_report", "same_stage_previous_year"],
            candidates.Select(item => item.GetProperty("kind").GetString()));
        Assert.Equal("fiscal_year_end", report.GetProperty("comparison").GetProperty("selectedBaselineKind").GetString());
        var anchorId = report.GetProperty("anchorStatementId").GetString()!;
        var anchor = report.GetProperty("snapshots").GetProperty(anchorId);
        Assert.Equal("409865", anchor.GetProperty("statement").GetProperty("externalStatementId").GetString());
        Assert.True(report.GetProperty("snapshots").EnumerateObject().Count() <= 9);
        Assert.True(report.GetProperty("chart").GetProperty("points").GetArrayLength() <= 5);
        var text = root.GetProperty("textAnswer").GetString()!;
        Assert.Contains("ترازنامه (صورت وضعیت مالی)", text);
        Assert.Contains("۱۰٫۹۷٪", text);

        var conversationId = root.GetProperty("conversationId").GetGuid();
        using var replayResponse = await client.GetAsync($"/api/ai/v1/conversations/{conversationId}/messages", CancellationToken.None);
        using var replay = await ReadJsonAsync(replayResponse);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replayed = replay.RootElement.GetProperty("messages").EnumerateArray().Last()
            .GetProperty("assistantContent").GetProperty("financialStatementTableResult").GetProperty("balanceSheetReport");
        Assert.Equal(report.GetRawText(), replayed.GetRawText());
        Assert.Equal(usageBefore + 1, _factory.ReadUsageEntries().Count);
        Assert.Equal(reservationsBefore + 1, _factory.ReadBillingReservations().Count);
    }

    [Fact]
    public async Task ComparisonRequest_SelectsMappedBaseline_AndIsBilledAsItsOwnRequest()
    {
        using var client = Client();
        var usageBefore = _factory.ReadUsageEntries().Count;

        using var response = await client.PostAsJsonAsync("/api/ai/v1/query",
            new { message = "ترازنامه وبملت را با گزارش قبلی مقایسه کن" }, CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = document.RootElement.GetProperty("financialStatementTableResult").GetProperty("balanceSheetReport");
        Assert.Equal("comparison", report.GetProperty("requestedView").GetString());
        Assert.Equal("previous_report", report.GetProperty("comparison").GetProperty("selectedBaselineKind").GetString());
        var previous = report.GetProperty("comparison").GetProperty("candidates").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == "previous_report");
        Assert.Equal("available", previous.GetProperty("state").GetString());
        Assert.Equal(9_110_304_681m, previous.GetProperty("rowChanges").GetProperty("total_assets").GetProperty("amount").GetDecimal());
        Assert.Contains("گزارش قبلی (۱۴۰۵/۰۳/۳۱)", document.RootElement.GetProperty("textAnswer").GetString());
        Assert.Equal(usageBefore + 1, _factory.ReadUsageEntries().Count);
    }

    [Fact]
    public async Task SingleDateAndUnsupportedVariant_AreTypedOutcomes()
    {
        using var client = Client();
        using var single = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = "ترازنامه وبملت در ۱۴۰۵/۰۴/۳۱" }, CancellationToken.None);
        using var singleDocument = await ReadJsonAsync(single);
        var noSnapshot = singleDocument.RootElement.GetProperty("financialStatementTableResult").GetProperty("balanceSheetReport");
        Assert.Equal("no_snapshot_at_date", noSnapshot.GetProperty("outcome").GetString());
        Assert.Contains("۱۴۰۵/۰۴/۳۱", singleDocument.RootElement.GetProperty("textAnswer").GetString());

        using var variant = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = "ترازنامه تلفیقی وبملت ۱۴۰۵/۰۳/۳۱" }, CancellationToken.None);
        using var variantDocument = await ReadJsonAsync(variant);
        var unsupported = variantDocument.RootElement.GetProperty("financialStatementTableResult").GetProperty("balanceSheetReport");
        Assert.Equal("unsupported_date_with_variant", unsupported.GetProperty("outcome").GetString());
        Assert.Equal("تلفیقی", unsupported.GetProperty("unsupportedFilter").GetString());
        var message = variantDocument.RootElement.GetProperty("textAnswer").GetString()!;
        Assert.Contains("۱۴۰۵/۰۳/۳۱", message);
        Assert.Contains("تلفیقی", message);
    }

    [Fact]
    public async Task FullTableRequest_StillReturnsFeature083()
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = "ترازنامه تلفیقی وبملت" }, CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var table = document.RootElement.GetProperty("financialStatementTableResult");
        Assert.Equal(JsonValueKind.Null, table.GetProperty("balanceSheetReport").ValueKind);
    }
}

public sealed class BalanceSheetReport154ApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"feature154-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    public V2MonthlySalesRoutingFakeAiModelClient Fake { get; } = new();

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2",
                // The routing-regression tests issue many requests from one test actor (test-only override).
                ["RateLimiting:AuthenticatedActor:PermitLimit"] = "1000"
            }));
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IQuarterlyFinancialReportsRepository>();
            services.AddSingleton<IQuarterlyFinancialReportsRepository, QuarterlyFinancialReports144RepositoryFake>();
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(Fake);
        });
    }

    public void EnsureSeeded()
    {
        EnsureBillingSeeded();
        if (_seeded) return;
        lock (_seedLock)
        {
            if (_seeded) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "FS-BANK", Name = "بانک ملت",
                CompanySymbol = "وبملت", TseSymbol = "وبملت", Ticker = "وبملت", LastSynchronizedAt = now
            });
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "FS-SHEGOL", Name = "گلتاش",
                CompanySymbol = "شگل", TseSymbol = "شگل", Ticker = "شگل", LastSynchronizedAt = now
            });
            var ids = new[] { 1, 57, 291, 223, 170, 135, 137, 139, 140, 138, 143, 147, 148, 18, 47, 51, 306, 307 };
            var catalog = ids.ToDictionary(id => id, id => new FinancialStatementSourceItemCatalogRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", StatementType = "BalanceSheet", SourceItemId = id,
                TitleFa = $"bs-{id}", Unit = "N/A", LastSynchronizedAt = now
            });
            db.FinancialStatementSourceItems.AddRange(catalog.Values);
            void Add(string ext, string periodType, DateOnly end, string fye, Dictionary<int, decimal> values, DateOnly? published = null, bool composing = false,
                string company = "FS-BANK")
            {
                var row = new NormalizedFinancialStatementRow
                {
                    Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = company, ExternalStatementId = ext,
                    StatementType = "BalanceSheet", PeriodType = periodType, PeriodStart = end.AddMonths(-6), PeriodEnd = end,
                    SourcePayloadChecksum = $"c-{ext}-{composing}", LastSynchronizedAt = now, IsComposing = composing, PublishedAt = published,
                    WarningsJson = $"[{{\"code\":\"NadpcoApiStatementSelection\",\"isComposing\":{(composing ? "true" : "false")},\"jalaliFiscalYearEnd\":\"{fye}\",\"assumedScale\":\"MillionRials\",\"sourceAmountUnit\":\"N/A\"}}]"
                };
                db.FinancialStatements.Add(row);
                foreach (var (id, value) in values)
                    db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                        { Id = Guid.NewGuid(), FinancialStatementId = row.Id, SourceItemCatalogId = catalog[id].Id, Value = value });
            }

            Add("409865", "SixMonths", new DateOnly(2026, 9, 22), "1405/12/29", new()
            {
                [1] = 409_287_621m, [57] = 3_382_000_000m, [291] = 45_209_234_799m, [223] = 27_590_264_477m, [170] = -181_354_538m,
                [135] = 51_498_227_670m, [137] = 7_612_077_011m, [139] = 52_501_759_193m, [140] = 126_830_069m,
                [138] = 59_110_304_681m, [143] = 52_628_589_262m, [147] = 6_481_715_419m, [148] = 59_110_304_681m
            }, new DateOnly(2026, 10, 22));
            // SYNTHETIC baselines.
            Add("403733", "ThreeMonths", new DateOnly(2026, 6, 21), "1405/12/29", new()
                { [138] = 50_000_000_000m, [143] = 44_523_000_000m, [147] = 5_477_000_000m, [148] = 50_000_000_000m });
            Add("403749", "TwelveMonths", new DateOnly(2026, 3, 20), "1404/12/29", new()
                { [138] = 48_000_000_000m, [143] = 42_000_000_000m, [147] = 6_000_000_000m, [148] = 48_000_000_000m });
            Add("384526", "SixMonths", new DateOnly(2025, 9, 22), "1404/12/29", new()
                { [138] = 30_000_000_000m, [143] = 26_000_000_000m, [147] = 4_000_000_000m, [148] = 30_000_000_000m });
            Add("370048", "SixMonths", new DateOnly(2024, 9, 21), "1403/12/30", new()
                { [138] = 20_000_000_000m, [143] = 18_000_000_000m, [147] = 2_000_000_000m, [148] = 20_000_000_000m }, composing: true);
            // شگل (supplied §11.1, General template via the fake classification).
            Add("s-1405-06", "NineMonths", new DateOnly(2026, 9, 22), "1405/09/30", new()
            {
                [1] = 3_263_057m, [57] = 6_149_600m, [18] = 10_266_826m, [47] = 21_347_261m, [51] = 0m, [306] = 26_549_068m,
                [307] = 16_116_006m, [138] = 63_522_534m, [143] = 41_014_336m, [147] = 22_508_198m, [148] = 63_522_534m
            }, company: "FS-SHEGOL");
            // PD-27: فاذر (General template via the fake classification) and فولاد (company only; the Feature 144
            // quarterly income statement comes from the fake repository).
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "FS-FAZAR", Name = "صنایع آذر آب",
                CompanySymbol = "فاذر", TseSymbol = "فاذر", Ticker = "فاذر", LastSynchronizedAt = now
            });
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "FS-FOLAD", Name = "فولاد مبارکه اصفهان",
                CompanySymbol = "فولاد", TseSymbol = "فولاد", Ticker = "فولاد", LastSynchronizedAt = now
            });
            Add("f-1405-03", "ThreeMonths", new DateOnly(2026, 6, 21), "1405/12/29", new()
            {
                [1] = 733_835m, [57] = 18_126_475m, [18] = 3_496_963m, [47] = 4_388_395m, [51] = 0m, [306] = 36_753_822m,
                [307] = 30_611_284m, [138] = 146_756_903m, [143] = 53_654_316m, [147] = 93_102_587m, [148] = 146_756_903m
            }, new DateOnly(2026, 7, 21), company: "FS-FAZAR");
            db.SaveChanges();
            _seeded = true;
        }
    }
}
