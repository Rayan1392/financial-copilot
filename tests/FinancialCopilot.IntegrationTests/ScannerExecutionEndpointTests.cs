using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class ScannerExecutionEndpointTests : IClassFixture<ScannerExecutionApiFactory>
{
    private readonly ScannerExecutionApiFactory _factory;

    public ScannerExecutionEndpointTests(ScannerExecutionApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task AiQuery_PeBelowSix_ReturnsTableWithMatchingSymbols()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = document.RootElement.GetProperty("scannerTable");
        Assert.NotEqual(Guid.Empty, table.GetProperty("planId").GetGuid());

        // LIVE (PE=3.5) and FALLBACK (PE=4.8) both qualify; HIGH_PE (PE=12.0) does not
        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var symbols = rows.Select(r => r.GetProperty("symbolCode").GetString()!).ToHashSet();
        Assert.Contains("LIVE", symbols);
        Assert.Contains("FALLBACK", symbols);
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("companyName").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(
                row.GetProperty("cells").GetProperty("COMPANY").GetProperty("formattedValue").GetString()));
        });
    }

    [Fact]
    public async Task AiQuery_DefaultColumns_IncludePriceAndConditionMetric()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        var columns = document.RootElement
            .GetProperty("scannerTable")
            .GetProperty("columns")
            .EnumerateArray()
            .Select(c => c.GetProperty("identifier").GetString()!)
            .ToList();

        Assert.Contains("SYMBOL", columns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("LATEST_PRICE", columns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("MARKET_CAP", columns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DAILY_CHANGE_PCT", columns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PE_TTM", columns, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AiQuery_LiveSymbol_HasLivePriceFreshnessInCell()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        var rows = document.RootElement
            .GetProperty("scannerTable")
            .GetProperty("rows")
            .EnumerateArray()
            .ToList();

        var liveRow = rows.Single(r =>
            string.Equals(r.GetProperty("symbolCode").GetString(), "LIVE", StringComparison.OrdinalIgnoreCase));

        var priceCell = liveRow
            .GetProperty("cells")
            .GetProperty("LATEST_PRICE");

        Assert.Equal("Live", priceCell.GetProperty("freshnessStatus").GetString());
        Assert.True(priceCell.GetProperty("value").GetDecimal() > 0);
    }

    [Fact]
    public async Task AiQuery_FallbackSymbol_HasPreviousTradingDayFreshnessInCell()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        var rows = document.RootElement
            .GetProperty("scannerTable")
            .GetProperty("rows")
            .EnumerateArray()
            .ToList();

        var fallbackRow = rows.Single(r =>
            string.Equals(r.GetProperty("symbolCode").GetString(), "FALLBACK", StringComparison.OrdinalIgnoreCase));

        var priceCell = fallbackRow
            .GetProperty("cells")
            .GetProperty("LATEST_PRICE");

        Assert.Equal("PreviousTradingDay", priceCell.GetProperty("freshnessStatus").GetString());
    }

    [Fact]
    public async Task AiQuery_HighGrowthAndPeBelow6_OnlyMatchingSymbolsReturned()
    {
        // Uses a dual-condition fake: NET_PROFIT_GROWTH_YOY > 50 AND PE_TTM < 6
        // LIVE: growth=75%, PE=3.5 → matches both
        // FALLBACK: growth=30%, PE=4.8 → fails growth condition
        using var client = _factory.CreateDualConditionClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "high growth and P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = document.RootElement
            .GetProperty("scannerTable")
            .GetProperty("rows")
            .EnumerateArray()
            .ToList();

        Assert.Single(rows);
        Assert.Equal("LIVE", rows[0].GetProperty("symbolCode").GetString());
    }

    [Fact]
    public async Task AiQuery_ExecutionFacts_ReportsTotalAndMatchingCounts()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        var facts = document.RootElement
            .GetProperty("scannerTable")
            .GetProperty("executionFacts");

        // 4 companies seeded; 2 match PE < 6 (company-live PE=3.5, company-stale-fallback PE=4.8)
        Assert.Equal(4, facts.GetProperty("totalSymbolsEvaluated").GetInt32());
        Assert.Equal(2, facts.GetProperty("matchingSymbolCount").GetInt32());
        Assert.False(facts.GetProperty("fromCache").GetBoolean());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class CachedScannerExecutionEndpointTests : IClassFixture<CachedScannerExecutionApiFactory>
{
    private readonly CachedScannerExecutionApiFactory _factory;

    public CachedScannerExecutionEndpointTests(CachedScannerExecutionApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task RepeatedScannerQuery_ReturnsCachedFreshnessAndBillsCachedRateOncePerRequest()
    {
        var countBefore = _factory.ReadUsageEntries().Count;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var first = await ReadJsonAsync(firstResponse);
        using var secondResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var second = await ReadJsonAsync(secondResponse);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.False(first.RootElement.GetProperty("scannerTable")
            .GetProperty("executionFacts").GetProperty("fromCache").GetBoolean());
        Assert.True(second.RootElement.GetProperty("scannerTable")
            .GetProperty("executionFacts").GetProperty("fromCache").GetBoolean());
        Assert.Equal(1m, first.RootElement.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.Equal(0.2m, second.RootElement.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.True(second.RootElement.GetProperty("usage").GetProperty("cached").GetBoolean());
        Assert.Equal(
            first.RootElement.GetProperty("usage").GetProperty("remainingSpendingCapacity").GetDecimal() - 0.2m,
            second.RootElement.GetProperty("usage").GetProperty("remainingSpendingCapacity").GetDecimal());

        var firstSymbols = first.RootElement.GetProperty("scannerTable")
            .GetProperty("rows")
            .EnumerateArray()
            .Select(row => row.GetProperty("symbolCode").GetString())
            .ToArray();
        var secondSymbols = second.RootElement.GetProperty("scannerTable")
            .GetProperty("rows")
            .EnumerateArray()
            .Select(row => row.GetProperty("symbolCode").GetString())
            .ToArray();
        Assert.Equal(firstSymbols, secondSymbols);

        var entries = _factory.ReadUsageEntries().OrderBy(entry => entry.OccurredAt).ToList();
        Assert.Equal(countBefore + 2, entries.Count);
        Assert.Equal([1m, 0.2m], entries.TakeLast(2).Select(entry => entry.CreditsCharged).ToArray());
    }

    [Fact]
    public async Task DataInvalidation_StopsPreviouslyCachedResultFromBeingReused()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6 after invalidation" },
            CancellationToken.None);
        using var cachedResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6 after invalidation" },
            CancellationToken.None);
        using var cached = await ReadJsonAsync(cachedResponse);
        Assert.True(cached.RootElement.GetProperty("scannerTable")
            .GetProperty("executionFacts").GetProperty("fromCache").GetBoolean());

        await _factory.InvalidateScannerDataAsync();

        using var refreshedResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6 after invalidation" },
            CancellationToken.None);
        using var refreshed = await ReadJsonAsync(refreshedResponse);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refreshedResponse.StatusCode);
        Assert.False(refreshed.RootElement.GetProperty("scannerTable")
            .GetProperty("executionFacts").GetProperty("fromCache").GetBoolean());
        Assert.Equal(1m, refreshed.RootElement.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
    }
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public class ScannerExecutionApiFactory : AiFacadeApiFactory
{
    private readonly string _seededIngestionDatabaseName = $"scanner-exec-ingestion-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            // Override the empty ingestion DB registered by the base with a seeded one
            ReplaceIngestionDbContext(services, _seededIngestionDatabaseName);
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
            SeedTestData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    public HttpClient CreateDualConditionClient()
    {
        return WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiModelClient>();
                services.AddSingleton<IAiModelClient>(_ => new DualConditionFakeAiModelClient());
            });
        }).CreateClient();
    }

    private static void SeedTestData(FinancialIngestionDbContext db)
    {
        var companyLiveId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var companyFallbackId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var companyHighPeId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var staleFallbackCompanyId = Guid.Parse("10000000-0000-0000-0000-000000000102");

        db.Companies.AddRange(
            new NormalizedCompanyRow
            {
                Id = companyLiveId, Name = "Live Corp",
                ProviderName = "test", ExternalCompanyId = "company-live",
                CompanySymbol = "LIVE",
                LastSynchronizedAt = DateTimeOffset.UtcNow
            },
            new NormalizedCompanyRow
            {
                Id = companyFallbackId, Name = "Fallback Corp",
                ProviderName = "test", ExternalCompanyId = "company-fallback",
                CompanySymbol = "FALLBACK",
                LastSynchronizedAt = DateTimeOffset.UtcNow
            },
            new NormalizedCompanyRow
            {
                Id = staleFallbackCompanyId, Name = "Stale Fallback Corp",
                ProviderName = "test", ExternalCompanyId = "company-stale-fallback",
                CompanySymbol = "FALLBACK",
                LastSynchronizedAt = DateTimeOffset.UtcNow
            },
            new NormalizedCompanyRow
            {
                Id = companyHighPeId, Name = "High PE Corp",
                ProviderName = "test", ExternalCompanyId = "company-highpe",
                LastSynchronizedAt = DateTimeOffset.UtcNow
            });

        // TradingInstruments + LatestMarketQuotes for PersistedMarketDataProvider.
        // The join path is: LatestMarketQuotes → TradingInstruments.NormalizedCompanyId → Companies.Id
        var instrLive = SeedInstrument(db, companyLiveId, "LIVE");
        var instrFallback = SeedInstrument(db, staleFallbackCompanyId, "FALLBACK");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.LatestMarketQuotes.AddRange(
            new LatestMarketQuoteRow
            {
                Id = Guid.NewGuid(), ProviderName = "StockMarketDb",
                TradingInstrumentId = instrLive.Id,
                LatestPrice = 15_000m, PriceChangePercentage = 1.5m,
                SourceKind = "Intraday", TradingDate = today,
                AsOf = DateTimeOffset.UtcNow
            },
            new LatestMarketQuoteRow
            {
                Id = Guid.NewGuid(), ProviderName = "StockMarketDb",
                TradingInstrumentId = instrFallback.Id,
                LatestPrice = 8_000m, PriceChangePercentage = -0.5m,
                SourceKind = "Daily", TradingDate = today.AddDays(-1),
                AsOf = DateTimeOffset.UtcNow.AddDays(-1)
            });

        var now = DateTimeOffset.UtcNow;
        var periodStart = new DateOnly(2025, 1, 1);
        var periodEnd = new DateOnly(2025, 12, 31);

        db.DerivedMetrics.AddRange(
            // PE_TTM: LIVE=3.5, FALLBACK=4.8, HIGHPE=12.0
            MakeDerivedMetric("company-live", "PE_TTM", 3.5m, periodStart, periodEnd, now),
            MakeDerivedMetric("company-stale-fallback", "PE_TTM", 4.8m, periodStart, periodEnd, now),
            MakeDerivedMetric("company-highpe", "PE_TTM", 12.0m, periodStart, periodEnd, now),

            // NET_PROFIT_GROWTH_YOY: LIVE=75%, FALLBACK=30%
            MakeDerivedMetric("company-live", "NET_PROFIT_GROWTH_YOY", 75m, periodStart, periodEnd, now),
            MakeDerivedMetric("company-stale-fallback", "NET_PROFIT_GROWTH_YOY", 30m, periodStart, periodEnd, now),

            // MARKET_CAP
            MakeDerivedMetric("company-live", "MARKET_CAP", 5_000_000_000m, periodStart, periodEnd, now),
            MakeDerivedMetric("company-stale-fallback", "MARKET_CAP", 2_000_000_000m, periodStart, periodEnd, now),
            MakeDerivedMetric("company-highpe", "MARKET_CAP", 800_000_000m, periodStart, periodEnd, now));
    }

    private static TradingInstrumentRow SeedInstrument(FinancialIngestionDbContext db, Guid normalizedCompanyId, string symbol)
    {
        var row = new TradingInstrumentRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "StockMarketDb",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = Math.Abs((long)Guid.NewGuid().GetHashCode()),
            InstrumentIsin = $"IRO1{symbol}0001",
            Symbol = symbol,
            Name = symbol,
            MarketCode = "NO",
            InstrumentKind = "A",
            NormalizedCompanyId = normalizedCompanyId,
            IsActive = true,
            SourceChangedAt = DateTimeOffset.UtcNow,
            LastSynchronizedAt = DateTimeOffset.UtcNow
        };
        db.TradingInstruments.Add(row);
        return row;
    }

    private static DerivedMetricRow MakeDerivedMetric(
        string externalCompanyId,
        string metricCode,
        decimal value,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = externalCompanyId,
            MetricCode = metricCode,
            MetricVersion = "v1",
            CalculationPolicyVersion = $"{metricCode}_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = value,
            Unit = "Ratio",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        };
}

public sealed class CachedScannerExecutionApiFactory : ScannerExecutionApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IScannerCache>();
            services.RemoveAll<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
            services.AddDistributedMemoryCache();
            services.AddSingleton<IScannerCache, DistributedScannerCache>();
        });
    }

    public async Task InvalidateScannerDataAsync()
    {
        using var scope = Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IScannerCache>();
        await cache.InvalidateAsync(
            new ScannerCacheInvalidation("integration-test", DateTimeOffset.UtcNow),
            CancellationToken.None);
    }
}

// Fake AI client that returns two conditions: NET_PROFIT_GROWTH_YOY > 50 AND PE_TTM < 6
internal sealed class DualConditionFakeAiModelClient : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "DualConditionFake", "fake-v1", AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput |
        AiModelCapability.UsageReporting | AiModelCapability.HealthCheck,
        Enabled: true, Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        var json = request.StructuredOutput?.SchemaName switch
        {
            "IntentDetectionOutput" => "{\"intent\":\"Scanner\",\"confidence\":0.97}",
            "ScannerParseOutput" =>
                """{"detectedLanguage":"en","conditions":[{"userTerminology":"net profit growth yoy","language":"en","operator":"GreaterThan","threshold":50.0,"periodHint":null,"growthComparison":"YearOverYear","inferredDefault":false,"inferredReason":null},{"userTerminology":"P/E","language":"en","operator":"LessThan","threshold":6.0,"periodHint":null,"growthComparison":null,"inferredDefault":false,"inferredReason":null}],"requestedColumns":[],"clarificationRequired":false,"clarificationMessage":null}""",
            "ScannerExplanationOutput" =>
                """{"explanationText":"Found matching symbols meeting dual screening criteria.","suggestedFollowUpQuestions":["Show high dividend yield stocks","Filter by revenue growth above 30%","Find value stocks with PE below 5"]}""",
            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: new AiExecutionUsageFacts(
                request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
                AiExecutionStatus.Completed, TimeSpan.Zero, AttemptNumber: 0,
                InputTokens: 15, OutputTokens: 8)));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(AiModelRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(AiEmbeddingRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey, Descriptor.ModelKey, Available: true, DateTimeOffset.UtcNow, "OK"));
}

// ── CodalDB vendor-precomputed ratio scanner tests ─────────────────────────────────────────────

/// <summary>
/// Verifies that vendor-precomputed CodalDB ratios persisted as <c>DerivedMetricRow</c>s with
/// <c>CalculationPolicyVersion = "codal-ratio-source-v1"</c> are scannable through the existing
/// <c>DerivedMetrics</c> read path with no scanner-engine changes.
/// </summary>
public sealed class CodalDbRatioScannerTests : IClassFixture<CodalDbRatioScannerApiFactory>
{
    private readonly CodalDbRatioScannerApiFactory _factory;

    public CodalDbRatioScannerTests(CodalDbRatioScannerApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task AiQuery_ReturnOnEquityAbove15_ReturnsCodalCompanyWithVendorRatio()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "ROE above 15" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var rows = document.RootElement.GetProperty("scannerTable")
            .GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("CODAL1", rows[0].GetProperty("symbolCode").GetString());
    }

    [Fact]
    public async Task AiQuery_CurrentRatioAbove1_ReturnsMatchingCodalCompany()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "current ratio above 1" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var rows = document.RootElement.GetProperty("scannerTable")
            .GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("CODAL1", rows[0].GetProperty("symbolCode").GetString());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class CodalDbRatioScannerApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"codal-ratio-scanner-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _lock = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => new CodalDbRatioFakeAiModelClient());
        });
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

    private static void SeedTestData(FinancialIngestionDbContext db)
    {
        var companyId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var now = DateTimeOffset.UtcNow;
        var periodStart = new DateOnly(2024, 4, 1);
        var periodEnd   = new DateOnly(2025, 3, 31);

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId, Name = "Codal Company One",
            ProviderName = "CodalDb", ExternalCompanyId = "3001",
            CompanySymbol = "CODAL1",
            LastSynchronizedAt = now
        });

        // ROE = 18.5% (CodalDB vendor-precomputed) — qualifies for "ROE > 15"
        db.DerivedMetrics.Add(MakeVendorRatio("3001", "RETURN_ON_EQUITY", 18.5m, periodStart, periodEnd, now));
        // CURRENT_RATIO = 2.3 — qualifies for "current ratio > 1"
        db.DerivedMetrics.Add(MakeVendorRatio("3001", "CURRENT_RATIO", 2.3m, periodStart, periodEnd, now));
        // MARKET_CAP for default columns
        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(), ExternalCompanyId = "3001",
            MetricCode = "MARKET_CAP", MetricVersion = "v1", CalculationPolicyVersion = "mktcap_v1",
            PeriodType = "TwelveMonths", PeriodStart = periodStart, PeriodEnd = periodEnd,
            Value = 3_000_000_000m, Unit = "Amount",
            ObservedAt = now, LastSynchronizedAt = now,
            WarningsJson = "[]", SourceEvidenceJson = "[]", DependencyEvidenceJson = "[]"
        });
    }

    private static DerivedMetricRow MakeVendorRatio(
        string externalCompanyId, string metricCode, decimal value,
        DateOnly periodStart, DateOnly periodEnd, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = externalCompanyId,
            MetricCode = metricCode,
            MetricVersion = "v1",
            CalculationPolicyVersion = "codal-ratio-source-v1",
            PeriodType = "TwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = value,
            Unit = "Percentage",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[{\"source\":\"CodalDb\",\"vendorPrecomputed\":true}]",
            DependencyEvidenceJson = "[]"
        };
}

/// <summary>Routes scanner conditions for RETURN_ON_EQUITY and CURRENT_RATIO.</summary>
internal sealed class CodalDbRatioFakeAiModelClient : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "CodalRatioFake", "fake-v1", AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput |
        AiModelCapability.UsageReporting | AiModelCapability.HealthCheck,
        Enabled: true, Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        var isRoe = request.Messages.Any(m => m.Content?.Contains("ROE", StringComparison.OrdinalIgnoreCase) == true
            || m.Content?.Contains("return on equity", StringComparison.OrdinalIgnoreCase) == true);

        var condition = isRoe
            ? """{"userTerminology":"ROE","language":"en","operator":"GreaterThan","threshold":15.0,"periodHint":null,"growthComparison":null,"inferredDefault":false,"inferredReason":null}"""
            : """{"userTerminology":"current ratio","language":"en","operator":"GreaterThan","threshold":1.0,"periodHint":null,"growthComparison":null,"inferredDefault":false,"inferredReason":null}""";

        var json = request.StructuredOutput?.SchemaName switch
        {
            "IntentDetectionOutput" => "{\"intent\":\"Scanner\",\"confidence\":0.97}",
            "ScannerParseOutput" =>
                $$"""{"detectedLanguage":"en","conditions":[{{condition}}],"requestedColumns":[],"clarificationRequired":false,"clarificationMessage":null}""",
            "ScannerExplanationOutput" =>
                """{"explanationText":"Vendor-precomputed ratio from CodalDB (codal-ratio-source-v1).","suggestedFollowUpQuestions":["Show P/E ratio","Filter by current ratio > 2"]}""",
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

public sealed class NadpcoFundamentalIndexScannerTests : IClassFixture<NadpcoFundamentalIndexScannerApiFactory>
{
    private readonly NadpcoFundamentalIndexScannerApiFactory _factory;

    public NadpcoFundamentalIndexScannerTests(NadpcoFundamentalIndexScannerApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task AiQuery_CurrentRatioAboveOne_ReturnsNadpcoSourceMetric()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "current ratio above 1" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = document.RootElement.GetProperty("scannerTable")
            .GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("NADPCO1", rows[0].GetProperty("symbolCode").GetString());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class NadpcoFundamentalIndexScannerApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"nadpco-fundamental-index-scanner-{Guid.NewGuid():N}";
    private readonly object _lock = new();
    private bool _seeded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => new CodalDbRatioFakeAiModelClient());
        });
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

    private static void SeedTestData(FinancialIngestionDbContext db)
    {
        var companyId = Guid.Parse("30000000-0000-0000-0000-000000000041");
        var now = DateTimeOffset.UtcNow;
        var periodStart = new DateOnly(2021, 3, 21);
        var periodEnd = new DateOnly(2021, 9, 22);

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            Name = "NADPCO Company One",
            ProviderName = "NadpcoApi",
            ExternalCompanyId = "4",
            CompanySymbol = "NADPCO1",
            LastSynchronizedAt = now
        });
        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "4",
            MetricCode = "CURRENT_RATIO",
            MetricVersion = "v1",
            CalculationPolicyVersion = "nadpco-api-fundamental-index-source-v1",
            PeriodType = "SixMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 1.03m,
            Unit = "Ratio",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[{\"source\":\"NadpcoApi\",\"vendorPrecomputed\":true}]",
            DependencyEvidenceJson = "[]"
        });
    }
}
