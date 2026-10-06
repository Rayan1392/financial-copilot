using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class MonthlySalesQualityRankingPeriodIntegrationTests
    : IClassFixture<MonthlySalesQualityRankingPeriodApiFactory>
{
    private readonly MonthlySalesQualityRankingPeriodApiFactory _factory;

    public MonthlySalesQualityRankingPeriodIntegrationTests(MonthlySalesQualityRankingPeriodApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task V2AiQuery_ImplicitMonthlyRankingUsesPreviousCompletedCalendarMonth()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "\u0631\u062a\u0628\u0647 \u0628\u0646\u062f\u06cc \u06af\u0632\u0627\u0631\u0634 \u0645\u0627\u0647\u0627\u0646\u0647\u061f" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected OK, received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(CancellationToken.None)}");
        var root = document.RootElement;
        Assert.Equal("MonthlySalesQualityRanking", root.GetProperty("intent").GetString());

        var ranking = root.GetProperty("monthlySalesQualityRankingResult");
        Assert.Equal(1405, ranking.GetProperty("reportYear").GetInt32());
        Assert.Equal(6, ranking.GetProperty("reportMonth").GetInt32());

        var item = Assert.Single(ranking.GetProperty("items").EnumerateArray());
        Assert.Equal("KOUCHIN", item.GetProperty("symbol").GetString());
    }

    [Fact]
    public async Task ExplicitApiPeriod_RemainsRespected()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.GetAsync(
            "/api/ai/monthly-sales-quality-rankings?reportYear=1405&reportMonth=7",
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1405, document.RootElement.GetProperty("reportYear").GetInt32());
        Assert.Equal(7, document.RootElement.GetProperty("reportMonth").GetInt32());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class MonthlySalesQualityRankingPeriodApiFactory : AiFacadeApiFactory
{
    private readonly string _databaseName = $"monthly-ranking-period-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            }));
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _databaseName);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse("2026-10-06T08:00:00Z")));
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient, V2MonthlySalesRoutingFakeAiModelClient>();
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
            db.MonthlySalesQualityRankingSnapshots.AddRange(
                CreateSnapshot("kouchin-id", "KOUCHIN", 1405, 6, 88m),
                CreateSnapshot("current-month-id", "CURRENT", 1405, 7, 99m));
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static MonthlySalesQualityRankingSnapshotRow CreateSnapshot(
        string idSuffix,
        string symbol,
        int year,
        byte month,
        decimal score) => new()
    {
        Id = Guid.NewGuid(),
        ExternalCompanyId = idSuffix,
        CompanySymbol = symbol,
        CompanyName = symbol,
        ReportYear = year,
        ReportMonth = month,
        MonthlySalesAmount = 100m,
        Avg12MonthSalesAmount = 80m,
        QualityScore = score,
        QualityLabel = "Strong",
        ConfidenceScore = 90m,
        RankMarket = 1,
        DimensionScoresJson = "{}",
        PositiveDriversJson = "[]",
        NegativeDriversJson = "[]",
        DataCoverageJson = "{}",
        SourceProviderName = "Test",
        CalculatedAtUtc = DateTimeOffset.Parse("2026-10-06T08:00:00Z"),
        IsEligible = true
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
