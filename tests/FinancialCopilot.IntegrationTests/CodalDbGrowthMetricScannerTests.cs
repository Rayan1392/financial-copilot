using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Verifies engine-derived (<c>EPS_GROWTH_YOY</c>) and vendor-precomputed (<c>SALES_GROWTH_RATE</c>)
/// growth metrics are both scannable through the existing <c>DerivedMetrics</c> read path with no
/// scanner-engine changes, and that their <c>CalculationPolicyVersion</c>s are distinct.
/// </summary>
public sealed class CodalDbGrowthMetricScannerTests : IClassFixture<CodalDbGrowthScannerApiFactory>
{
    private readonly CodalDbGrowthScannerApiFactory _factory;

    public CodalDbGrowthMetricScannerTests(CodalDbGrowthScannerApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task AiQuery_EpsGrowthYoyAbove30_ReturnsCodalGrowthCompany()
    {
        using var client = _factory.CreateEpsGrowthClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query", new { message = "EPS growth YoY above 30" }, CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = document.RootElement.GetProperty("scannerTable")
            .GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("CODAL2", rows[0].GetProperty("symbolCode").GetString());
    }

    [Fact]
    public async Task AiQuery_SalesGrowthRateAbove10_ReturnsVendorPrecomputedMatch()
    {
        using var client = _factory.CreateSalesGrowthClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query", new { message = "sales growth rate above 10" }, CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = document.RootElement.GetProperty("scannerTable")
            .GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("CODAL2", rows[0].GetProperty("symbolCode").GetString());
    }

    [Fact]
    public async Task EngineAndVendorRows_HaveDistinctCalculationPolicyVersions()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();

        var epsRow   = await db.DerivedMetrics.SingleAsync(m => m.MetricCode == "EPS_GROWTH_YOY");
        var salesRow = await db.DerivedMetrics.SingleAsync(m => m.MetricCode == "SALES_GROWTH_RATE");

        Assert.Equal("yoy-eps-engine-v1", epsRow.CalculationPolicyVersion);
        Assert.Equal("codal-ratio-source-v1", salesRow.CalculationPolicyVersion);
        Assert.NotEqual(epsRow.CalculationPolicyVersion, salesRow.CalculationPolicyVersion);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class CodalDbGrowthScannerApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"codal-growth-scanner-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _lock = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => ReplaceIngestionDbContext(services, _dbName));
    }

    public void EnsureSeeded()
    {
        EnsureBillingSeeded();
        if (_seeded) return;
        lock (_lock)
        {
            if (_seeded) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            SeedTestData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    public HttpClient CreateEpsGrowthClient() =>
        WithFake(new GrowthFakeAiModelClient("eps growth yoy", 30.0));

    public HttpClient CreateSalesGrowthClient() =>
        WithFake(new GrowthFakeAiModelClient("sales growth rate", 10.0));

    private HttpClient WithFake(IAiModelClient fake) =>
        WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => fake);
        })).CreateClient();

    private static void SeedTestData(FinancialIngestionDbContext db)
    {
        var companyId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var now = DateTimeOffset.UtcNow;
        var pStart = new DateOnly(2025, 1, 1);
        var pEnd   = new DateOnly(2025, 3, 31);

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId, Name = "Codal Growth Co",
            ProviderName = "CodalDb", ExternalCompanyId = "5001",
            CompanySymbol = "CODAL2",
            LastSynchronizedAt = now
        });

        // Engine-derived EPS_GROWTH_YOY (distinct policy version from vendor ratios).
        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(), ExternalCompanyId = "5001",
            MetricCode = "EPS_GROWTH_YOY", MetricVersion = "v1",
            CalculationPolicyVersion = "yoy-eps-engine-v1",
            PeriodType = "ThreeMonths", PeriodStart = pStart, PeriodEnd = pEnd,
            Value = 45m, Unit = "Percentage",
            ObservedAt = now, LastSynchronizedAt = now,
            WarningsJson = "[]", SourceEvidenceJson = "[]", DependencyEvidenceJson = "[]"
        });

        // Vendor-precomputed SALES_GROWTH_RATE (codal-ratio-source-v1).
        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(), ExternalCompanyId = "5001",
            MetricCode = "SALES_GROWTH_RATE", MetricVersion = "v1",
            CalculationPolicyVersion = "codal-ratio-source-v1",
            PeriodType = "ThreeMonths", PeriodStart = pStart, PeriodEnd = pEnd,
            Value = 22m, Unit = "Percentage",
            ObservedAt = now, LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[{\"source\":\"CodalDb\",\"vendorPrecomputed\":true}]",
            DependencyEvidenceJson = "[]"
        });

        // MARKET_CAP for default scanner columns.
        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(), ExternalCompanyId = "5001",
            MetricCode = "MARKET_CAP", MetricVersion = "v1", CalculationPolicyVersion = "mktcap_v1",
            PeriodType = "ThreeMonths", PeriodStart = pStart, PeriodEnd = pEnd,
            Value = 2_000_000_000m, Unit = "Amount",
            ObservedAt = now, LastSynchronizedAt = now,
            WarningsJson = "[]", SourceEvidenceJson = "[]", DependencyEvidenceJson = "[]"
        });
    }
}

/// <summary>Routes a single growth-metric condition for scanner integration tests.</summary>
internal sealed class GrowthFakeAiModelClient(string terminology, double threshold) : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "GrowthFake", "fake-v1", AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput |
        AiModelCapability.UsageReporting | AiModelCapability.HealthCheck,
        Enabled: true, Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        var json = request.StructuredOutput?.SchemaName switch
        {
            "IntentDetectionOutput" => "{\"intent\":\"Scanner\",\"confidence\":0.97}",
            "ScannerParseOutput" =>
                $"{{\"detectedLanguage\":\"en\",\"conditions\":[{{\"userTerminology\":\"{terminology}\",\"language\":\"en\",\"operator\":\"GreaterThan\",\"threshold\":{threshold},\"periodHint\":null,\"growthComparison\":null,\"inferredDefault\":false,\"inferredReason\":null}}],\"requestedColumns\":[],\"clarificationRequired\":false,\"clarificationMessage\":null}}",
            "ScannerExplanationOutput" =>
                "{\"explanationText\":\"Growth metric scan.\",\"suggestedFollowUpQuestions\":[\"Filter by revenue growth\"]}",
            _ => "{}"
        };
        return Task.FromResult(new AiModelResult(
            Text: null, StructuredJson: json, ToolCalls: [],
            Usage: new AiExecutionUsageFacts(
                request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
                AiExecutionStatus.Completed, TimeSpan.Zero, AttemptNumber: 0,
                InputTokens: 10, OutputTokens: 5)));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(AiModelRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(AiEmbeddingRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey, Descriptor.ModelKey, Available: true, DateTimeOffset.UtcNow, "OK"));
}
