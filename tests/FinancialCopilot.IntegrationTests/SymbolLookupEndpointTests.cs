using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class SymbolLookupEndpointTests : IClassFixture<SymbolLookupApiFactory>
{
    private readonly SymbolLookupApiFactory _factory;

    public SymbolLookupEndpointTests(SymbolLookupApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task AiQuery_SymbolLookupIntent_KnownSymbolAndMetric_ReturnsLookupTable()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE حفاری چقدر است؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SymbolLookup", document.RootElement.GetProperty("intent").GetString());
        var table = document.RootElement.GetProperty("symbolLookupTable");
        Assert.NotEqual(Guid.Empty, table.GetProperty("planId").GetGuid());

        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("HAF_TSE", rows[0].GetProperty("symbolCode").GetString());
        Assert.NotEqual("HAFARI", rows[0].GetProperty("symbolCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(rows[0].GetProperty("companyName").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(
            rows[0].GetProperty("cells").GetProperty("COMPANY_NAME").GetProperty("formattedValue").GetString()));
        Assert.Equal(5.2m, rows[0].GetProperty("cells").GetProperty("PE_TTM").GetProperty("value").GetDecimal());

        var confidence = document.RootElement.GetProperty("confidenceScore");
        Assert.True(confidence.GetProperty("score").GetDouble() >= 0.95);
        Assert.Equal("v1", confidence.GetProperty("policyVersion").GetString());
    }

    [Fact]
    public async Task AiQuery_SymbolLookup_PersistedProseUsesDeterministicTableValue()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE حفاری چقدر است؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var conversationId = document.RootElement.GetProperty("conversationId").GetGuid();

        using var reload = await client.GetAsync(
            $"/api/ai/v1/conversations/{conversationId}/messages", CancellationToken.None);
        using var reloadDoc = await ReadJsonAsync(reload);

        var assistant = reloadDoc.RootElement.GetProperty("messages").EnumerateArray()
            .First(m => m.GetProperty("role").GetString() == "Assistant");
        var content = assistant.GetProperty("content").GetString()!;
        var confidence = assistant.GetProperty("assistantContent").GetProperty("confidenceScore");

        // Deterministic prose is grounded in the same table cell rendered in the table (5.2).
        Assert.Contains("5.2", content);
        Assert.True(confidence.GetProperty("score").GetDouble() >= 0.95);
    }

    [Fact]
    public async Task AiQuery_UnknownSymbol_DoesNotReturnEmptyLookupTable()
    {
        using var client = _factory.CreateUnknownSymbolClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE شرکتی_که_وجود_ندارد" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SymbolLookup", document.RootElement.GetProperty("intent").GetString());
        var table = document.RootElement.GetProperty("symbolLookupTable");
        Assert.Equal(JsonValueKind.Null, table.ValueKind);
    }

    [Fact]
    public async Task AiQuery_UnknownMetric_ReturnsClarificationRequired()
    {
        using var client = _factory.CreateClarificationClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "metric_xyz_unknown حفاری" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("clarificationRequired").GetBoolean());
    }

    [Fact]
    public async Task AiQuery_MultipleSymbols_MultipleMetrics_ReturnsAllRows()
    {
        using var client = _factory.CreateMultiSymbolClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE و ROE فملی و حفاری" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = document.RootElement.GetProperty("symbolLookupTable");
        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var columns = table.GetProperty("columns").EnumerateArray()
            .Select(column => column.GetProperty("identifier").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PE_TTM", columns);
        Assert.Contains("RETURN_ON_EQUITY", columns);
        var symbols = rows.Select(r => r.GetProperty("symbolCode").GetString()!).ToHashSet();
        Assert.Contains("HAF_TSE", symbols);
        Assert.Contains("FML_TSE", symbols);
        Assert.All(rows, row =>
        {
            Assert.NotEqual(JsonValueKind.Null, row.GetProperty("cells").GetProperty("PE_TTM").GetProperty("value").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, row.GetProperty("cells").GetProperty("RETURN_ON_EQUITY").GetProperty("value").ValueKind);
        });
    }

    [Fact]
    public async Task AiQuery_SymbolLookup_CreatesUsageLedgerEntry()
    {
        var countBefore = _factory.ReadUsageEntries().Count;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE حفاری چقدر است؟" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = _factory.ReadUsageEntries();
        Assert.Equal(countBefore + 1, entries.Count);
        var entry = entries.OrderBy(e => e.OccurredAt).Last();
        Assert.Equal("AiQuery.Scanner", entry.OperationCode);
    }

    [Fact]
    public async Task Messages_SymbolLookupPayload_PersistedAndReloadable()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var queryResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE حفاری چقدر است؟" },
            CancellationToken.None);
        var queryDoc = await ReadJsonAsync(queryResponse);
        var conversationId = queryDoc.RootElement.GetProperty("conversationId").GetGuid();

        using var messagesResponse = await client.GetAsync(
            $"/api/ai/v1/conversations/{conversationId}/messages",
            CancellationToken.None);
        var messagesDoc = await ReadJsonAsync(messagesResponse);
        var assistant = messagesDoc.RootElement
            .GetProperty("messages")
            .EnumerateArray()
            .Last();

        Assert.Equal(HttpStatusCode.OK, messagesResponse.StatusCode);
        Assert.Equal("SymbolLookup",
            assistant.GetProperty("assistantContent").GetProperty("intent").GetString());
        Assert.True(
            assistant.GetProperty("assistantContent").TryGetProperty("symbolLookupTable", out var lt) &&
            lt.ValueKind != JsonValueKind.Null);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class PeSymbolLookupRegressionTests : IClassFixture<PeSymbolLookupRegressionApiFactory>
{
    private readonly PeSymbolLookupRegressionApiFactory _factory;

    public PeSymbolLookupRegressionTests(PeSymbolLookupRegressionApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Theory]
    [InlineData("pe کگل چقدر است؟", "کگل", "4.12")]
    [InlineData("P/E کگل", "کگل", "4.12")]
    [InlineData("پی به ای کگل", "کگل", "4.12")]
    [InlineData("نسبت قیمت به سود کگل", "کگل", "4.12")]
    [InlineData("pe شپنا", "شپنا", "5.17")]
    [InlineData("P/E شبندر", "شبندر", "5.06")]
    public async Task AiQuery_PeLookup_ReturnsPersistedPeTtmWithHighConfidence(
        string message,
        string expectedSymbol,
        string expectedFormattedValue)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());

        var table = root.GetProperty("symbolLookupTable");

        var columns = table.GetProperty("columns").EnumerateArray()
            .Select(c => c.GetProperty("identifier").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SYMBOL", columns);
        Assert.Contains("COMPANY_NAME", columns);
        Assert.Contains("LATEST_PRICE", columns);
        Assert.Contains("DAILY_CHANGE_PCT", columns);
        Assert.Contains("PE_TTM", columns);

        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        Assert.Equal(expectedSymbol, row.GetProperty("symbolCode").GetString());
        var cell = row.GetProperty("cells").GetProperty("PE_TTM");
        Assert.Equal(expectedFormattedValue, cell.GetProperty("formattedValue").GetString());
        Assert.NotEqual(JsonValueKind.Null, cell.GetProperty("value").ValueKind);
        Assert.NotEqual("Missing", cell.GetProperty("freshnessStatus").GetString());
        var latestPrice = row.GetProperty("cells").GetProperty("LATEST_PRICE");
        Assert.False(latestPrice.GetProperty("formattedValue").GetString()?.EndsWith(".00", StringComparison.Ordinal) ?? false);

        var warnings = table.GetProperty("missingDataWarnings").EnumerateArray()
            .Select(w => w.GetString() ?? string.Empty)
            .ToList();
        Assert.DoesNotContain(warnings, warning => warning.Contains("PE_TTM", StringComparison.OrdinalIgnoreCase));

        Assert.True(root.GetProperty("confidenceScore").GetProperty("score").GetDouble() >= 0.95);

        var conversationId = root.GetProperty("conversationId").GetGuid();
        using var reload = await client.GetAsync(
            $"/api/ai/v1/conversations/{conversationId}/messages",
            CancellationToken.None);
        using var reloadDoc = await ReadJsonAsync(reload);
        var assistant = reloadDoc.RootElement.GetProperty("messages").EnumerateArray()
            .First(m => m.GetProperty("role").GetString() == "Assistant");
        var answerText = assistant.GetProperty("content").GetString()!;
        Assert.Contains(expectedFormattedValue, answerText);
    }

    [Fact]
    public async Task AiQuery_PeLookup_ByCompanyNameResolvesToSameRowAsSymbol()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var bySymbolResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "نسبت پی به ای کچاد؟" },
            CancellationToken.None);
        using var bySymbolDocument = await ReadJsonAsync(bySymbolResponse);

        using var byCompanyResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "نسبت پی به ای چادرملو؟" },
            CancellationToken.None);
        using var byCompanyDocument = await ReadJsonAsync(byCompanyResponse);

        Assert.Equal(HttpStatusCode.OK, bySymbolResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byCompanyResponse.StatusCode);

        var symbolRoot = bySymbolDocument.RootElement;
        var companyRoot = byCompanyDocument.RootElement;
        Assert.Equal("SymbolLookup", companyRoot.GetProperty("intent").GetString());
        Assert.False(companyRoot.GetProperty("clarificationRequired").GetBoolean());

        var symbolRow = Assert.Single(symbolRoot.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        var companyTable = companyRoot.GetProperty("symbolLookupTable");
        Assert.NotEqual(JsonValueKind.Null, companyTable.ValueKind);
        var companyRow = Assert.Single(companyTable.GetProperty("rows").EnumerateArray());

        Assert.Equal("کچاد", companyRow.GetProperty("symbolCode").GetString());
        Assert.Equal(symbolRow.GetProperty("symbolCode").GetString(), companyRow.GetProperty("symbolCode").GetString());
        Assert.Equal(
            symbolRow.GetProperty("cells").GetProperty("PE_TTM").GetProperty("formattedValue").GetString(),
            companyRow.GetProperty("cells").GetProperty("PE_TTM").GetProperty("formattedValue").GetString());
        Assert.Equal("9.73", companyRow.GetProperty("cells").GetProperty("PE_TTM").GetProperty("formattedValue").GetString());
        Assert.True(companyRoot.GetProperty("confidenceScore").GetProperty("score").GetDouble() >= 0.95);
        Assert.Contains("9.73", companyRoot.GetProperty("textAnswer").GetString());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class SymbolLookupApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"symbol-lookup-ingestion-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ =>
                new SymbolLookupFakeAiModelClient(
                    symbolLookupSymbol: "حفاری",
                    metricTerm: "نسبت پی به ای",
                    clarificationMetricTerm: null,
                    multiSymbol: false));
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

    public HttpClient CreateUnknownSymbolClient()
    {
        return WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                ReplaceIngestionDbContext(services, _dbName);
                services.RemoveAll<IAiModelClient>();
                services.AddSingleton<IAiModelClient>(_ =>
                    new SymbolLookupFakeAiModelClient(
                        symbolLookupSymbol: "شرکتی_که_وجود_ندارد",
                        metricTerm: "نسبت پی به ای",
                        clarificationMetricTerm: null,
                        multiSymbol: false));
            });
        }).CreateClient();
    }

    public HttpClient CreateClarificationClient()
    {
        return WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiModelClient>();
                services.AddSingleton<IAiModelClient>(_ =>
                    new SymbolLookupFakeAiModelClient(
                        symbolLookupSymbol: "حفاری",
                        metricTerm: "metric_xyz_unknown",
                        clarificationMetricTerm: "metric_xyz_unknown",
                        multiSymbol: false));
            });
        }).CreateClient();
    }

    public HttpClient CreateMultiSymbolClient()
    {
        return WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                ReplaceIngestionDbContext(services, _dbName);
                services.RemoveAll<IAiModelClient>();
                services.AddSingleton<IAiModelClient>(_ =>
                    new SymbolLookupFakeAiModelClient(
                        symbolLookupSymbol: "حفاری",
                        metricTerm: "نسبت پی به ای",
                        clarificationMetricTerm: null,
                        multiSymbol: true));
            });
        }).CreateClient();
    }

    private static void SeedTestData(FinancialIngestionDbContext db)
    {
        var companyHafariId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var companyFmlcoId = Guid.Parse("50000000-0000-0000-0000-000000000002");
        var now = DateTimeOffset.UtcNow;
        var periodStart = new DateOnly(2025, 1, 1);
        var periodEnd = new DateOnly(2025, 12, 31);

        db.Companies.AddRange(
            new NormalizedCompanyRow
            {
                Id = companyHafariId,
                Name = "حفاری شمال",
                ProviderName = "test",
                ExternalCompanyId = "hafari-001",
                Ticker = "حفاری",
                TseSymbol = "HAF_TSE",
                CompanySymbol = "HAFARI",
                LastSynchronizedAt = now
            },
            new NormalizedCompanyRow
            {
                Id = companyFmlcoId,
                Name = "ملی صنایع مس ایران",
                ProviderName = "test",
                ExternalCompanyId = "fmlco-001",
                Ticker = "فملی",
                TseSymbol = "FML_TSE",
                CompanySymbol = "FMLCO",
                LastSynchronizedAt = now
            });

        db.DerivedMetrics.AddRange(
            MakeDerivedMetric("hafari-001", "PE_TTM", 5.2m, periodStart, periodEnd, now),
            MakeDerivedMetric("hafari-001", "RETURN_ON_EQUITY", 12.0m, periodStart, periodEnd, now),
            MakeDerivedMetric("fmlco-001", "PE_TTM", 8.4m, periodStart, periodEnd, now),
            MakeDerivedMetric("fmlco-001", "RETURN_ON_EQUITY", 18.5m, periodStart, periodEnd, now));
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

public sealed class PeSymbolLookupRegressionApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"pe-lookup-regression-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => new PeLookupRegressionFakeAiModelClient());
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
            SeedPeLookupData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static void SeedPeLookupData(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var periodStart = new DateOnly(2025, 1, 1);
        var periodEnd = new DateOnly(2025, 12, 31);

        SeedSymbol(db, "کگل", "معدنی و صنعتی گل گهر", 4.12m, 2110m, 1.25m, now, periodStart, periodEnd, 1);
        SeedSymbol(db, "فملی", "ملی صنایع مس ایران", 6.40m, 10520m, -0.42m, now, periodStart, periodEnd, 2);
        SeedSymbol(db, "شپنا", "پالایش نفت اصفهان", 5.17m, 8120m, 0.88m, now, periodStart, periodEnd, 3);
        SeedSymbol(db, "شبندر", "پالایش نفت بندرعباس", 5.06m, 7340m, 0.35m, now, periodStart, periodEnd, 4);
        SeedSymbol(db, "شتران", "پالایش نفت تهران", 5.89m, 6910m, -0.14m, now, periodStart, periodEnd, 5);
        SeedSymbol(db, "فزرین", "زرین معدن آسیا", 7.21m, 1480m, 2.10m, now, periodStart, periodEnd, 6);
        SeedSymbol(db, "کچاد", "معدنی و صنعتی چادرملو", 9.73m, 0m, 0m, now, periodStart, periodEnd, 7);
    }

    private static void SeedSymbol(
        FinancialIngestionDbContext db,
        string symbol,
        string companyName,
        decimal peTtm,
        decimal latestPrice,
        decimal changePercent,
        DateTimeOffset now,
        DateOnly periodStart,
        DateOnly periodEnd,
        int index)
    {
        var companyId = Guid.Parse($"70000000-0000-0000-0000-{index:000000000000}");

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            Name = companyName,
            ProviderName = "CodalDb",
            ExternalCompanyId = $"company-{index}",
            TseSymbol = symbol,
            CompanySymbol = symbol,
            LastSynchronizedAt = now
        });

        db.DerivedMetrics.AddRange(
            new DerivedMetricRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = $"company-{index}",
                MetricCode = "PE_TTM",
                MetricVersion = "v1",
                CalculationPolicyVersion = "PE_TTM_v1",
                PeriodType = "TrailingTwelveMonths",
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Value = peTtm,
                Unit = "Ratio",
                ObservedAt = now,
                LastSynchronizedAt = now,
                WarningsJson = "[]",
                SourceEvidenceJson = "[]",
                DependencyEvidenceJson = "[]"
            },
            new DerivedMetricRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = $"company-{index}",
                MetricCode = "LATEST_PRICE",
                MetricVersion = "v1",
                CalculationPolicyVersion = "LATEST_PRICE_v1",
                PeriodType = "TrailingTwelveMonths",
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Value = latestPrice,
                Unit = "Price",
                ObservedAt = now,
                LastSynchronizedAt = now,
                WarningsJson = "[]",
                SourceEvidenceJson = "[]",
                DependencyEvidenceJson = "[]"
            },
            new DerivedMetricRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = $"company-{index}",
                MetricCode = "DAILY_CHANGE_PCT",
                MetricVersion = "v1",
                CalculationPolicyVersion = "DAILY_CHANGE_PCT_v1",
                PeriodType = "TrailingTwelveMonths",
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Value = changePercent,
                Unit = "Percent",
                ObservedAt = now,
                LastSynchronizedAt = now,
                WarningsJson = "[]",
                SourceEvidenceJson = "[]",
                DependencyEvidenceJson = "[]"
            });
    }
}

internal sealed class PeLookupRegressionFakeAiModelClient : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "PeLookupRegressionFake",
        "fake-v1",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput |
        AiModelCapability.UsageReporting | AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        var userMessage = request.Messages.LastOrDefault(m => m.Role == AiMessageRole.User)?.Content ?? string.Empty;
        var json = request.StructuredOutput?.SchemaName switch
        {
            "IntentDetectionOutput" =>
                "{\"intent\":\"Unknown\",\"confidence\":0.1}",

            "SymbolLookupParseOutput" =>
                BuildParseJson(userMessage),

            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: new AiExecutionUsageFacts(
                request.CorrelationId,
                Descriptor.ProviderKey,
                Descriptor.ModelKey,
                AiExecutionStatus.Completed,
                TimeSpan.Zero,
                AttemptNumber: 0,
                InputTokens: 12,
                OutputTokens: 6)));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey,
            Descriptor.ModelKey,
            Available: true,
            DateTimeOffset.UtcNow,
            "OK"));

    private static string BuildParseJson(string userMessage)
    {
        var symbol = ResolveSymbol(userMessage);
        var metric = ResolveMetricTerm(userMessage);
        return $$"""{"detectedLanguage":"fa","pairs":[{"symbolName":"{{symbol}}","metricTerm":"{{metric}}"}],"clarificationRequired":false,"clarificationMessage":null}""";
    }

    private static string ResolveSymbol(string userMessage)
    {
        if (userMessage.Contains("چادرملو", StringComparison.OrdinalIgnoreCase))
            return "چادرملو";
        if (userMessage.Contains("کچاد", StringComparison.OrdinalIgnoreCase))
            return "کچاد";

        foreach (var symbol in new[] { "کگل", "فملی", "شپنا", "شبندر", "شتران", "فزرین" })
        {
            if (userMessage.Contains(symbol, StringComparison.OrdinalIgnoreCase))
                return symbol;
        }

        return "کگل";
    }

    private static string ResolveMetricTerm(string userMessage)
    {
        if (userMessage.Contains("نسبت قیمت به سود", StringComparison.OrdinalIgnoreCase))
            return "نسبت قیمت به سود";
        if (userMessage.Contains("پی به ای", StringComparison.OrdinalIgnoreCase))
            return "پی به ای";
        if (userMessage.Contains("P/E", StringComparison.OrdinalIgnoreCase))
            return "P/E";
        return "pe";
    }
}

internal sealed class SymbolLookupFakeAiModelClient(
    string symbolLookupSymbol,
    string metricTerm,
    string? clarificationMetricTerm,
    bool multiSymbol) : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "SymbolLookupFake",
        "fake-v1",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput |
        AiModelCapability.UsageReporting | AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        var json = request.StructuredOutput?.SchemaName switch
        {
            "IntentDetectionOutput" =>
                "{\"intent\":\"SymbolLookup\",\"confidence\":0.96}",

            "SymbolLookupParseOutput" when clarificationMetricTerm is not null =>
                BuildClarificationJson(clarificationMetricTerm),

            "SymbolLookupParseOutput" when multiSymbol =>
                BuildMultiSymbolJson(),

            "SymbolLookupParseOutput" =>
                BuildSinglePairJson(symbolLookupSymbol, metricTerm),

            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: new AiExecutionUsageFacts(
                request.CorrelationId,
                Descriptor.ProviderKey,
                Descriptor.ModelKey,
                AiExecutionStatus.Completed,
                TimeSpan.Zero,
                AttemptNumber: 0,
                InputTokens: 12,
                OutputTokens: 6)));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey,
            Descriptor.ModelKey,
            Available: true,
            DateTimeOffset.UtcNow,
            "OK"));

    private static string BuildSinglePairJson(string symbol, string metric) =>
        $$"""{"detectedLanguage":"fa","pairs":[{"symbolName":"{{symbol}}","metricTerm":"{{metric}}"}],"clarificationRequired":false,"clarificationMessage":null}""";

    private static string BuildMultiSymbolJson() =>
        """{"detectedLanguage":"fa","pairs":[{"symbolName":"حفاری","metricTerm":"نسبت پی به ای"},{"symbolName":"FMLCO","metricTerm":"نسبت پی به ای"}],"clarificationRequired":false,"clarificationMessage":null}""";

    private static string BuildClarificationJson(string metric) =>
        $$"""{"detectedLanguage":"fa","pairs":[{"symbolName":"حفاری","metricTerm":"{{metric}}"}],"clarificationRequired":false,"clarificationMessage":null}""";
}

// ── Spec 057: «آخرین فروش» answers from Noavaran monthly-activity data ─────────────────────────

/// <summary>
/// Verifies that an explicit monthly sales question resolves through the MONTHLY_SALES alias to
/// the latest persisted company-month in DerivedMetrics — never a quarterly REVENUE substitute.
/// </summary>
public sealed class MonthlySalesLookupTests : IClassFixture<MonthlySalesLookupApiFactory>
{
    private readonly MonthlySalesLookupApiFactory _factory;

    public MonthlySalesLookupTests(MonthlySalesLookupApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task AiQuery_LatestMonthlySales_ReturnsLatestPersistedMonth()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "آخرین فروش غگلپا چقدر است؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        Assert.Contains(
            FormatMillionRials(MonthlySalesLookupApiFactory.LatestMonthSales),
            root.GetProperty("textAnswer").GetString());

        var table = root.GetProperty("symbolLookupTable");
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        var columnIds = columns
            .Select(c => c.GetProperty("identifier").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("LATEST_PRICE", columnIds);
        Assert.DoesNotContain("DAILY_CHANGE_PCT", columnIds);

        Assert.Equal("فروش ماه مشابه دوره قبل", GetColumnDisplayName(columns, "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH"));
        Assert.DoesNotContain("AVG_12M_MONTHLY_SALES", columnIds);
        AssertNoForbiddenAverageSalesDisplayLabels(columns);
        Assert.Equal("فروش YTD", GetColumnDisplayName(columns, "MONTHLY_SALES_YTD"));
        Assert.Equal("فروش YTD تا ماه قبل", GetColumnDisplayName(columns, "MONTHLY_SALES_YTD_PREVIOUS_MONTH"));

        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        Assert.Equal("غگلپا", row.GetProperty("symbolCode").GetString());

        // The latest month (Ordibehesht 1405 window) wins over the older month, and the value is
        // the monthly-activity amount — not the quarterly REVENUE (which is deliberately seeded
        // with a different value to catch silent substitution.)
        var cell = row.GetProperty("cells").GetProperty("MONTHLY_SALES");
        Assert.Equal(MonthlySalesLookupApiFactory.LatestMonthSales, cell.GetProperty("value").GetDecimal());
        Assert.Equal(FormatMillionRials(MonthlySalesLookupApiFactory.LatestMonthSales), cell.GetProperty("formattedValue").GetString());
        Assert.NotEqual("Missing", cell.GetProperty("freshnessStatus").GetString());

        var priorYear = row.GetProperty("cells").GetProperty("MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH");
        Assert.Equal(MonthlySalesLookupApiFactory.PriorYearSameMonthSales, priorYear.GetProperty("value").GetDecimal());
        Assert.Equal(FormatMillionRials(MonthlySalesLookupApiFactory.PriorYearSameMonthSales), priorYear.GetProperty("formattedValue").GetString());
        Assert.NotEqual("Missing", priorYear.GetProperty("freshnessStatus").GetString());

        var ytd = row.GetProperty("cells").GetProperty("MONTHLY_SALES_YTD");
        Assert.Equal(MonthlySalesLookupApiFactory.YearToDateSales, ytd.GetProperty("value").GetDecimal());
        Assert.Equal(FormatMillionRials(MonthlySalesLookupApiFactory.YearToDateSales), ytd.GetProperty("formattedValue").GetString());
        Assert.NotEqual("Missing", ytd.GetProperty("freshnessStatus").GetString());

        var ytdPreviousMonth = row.GetProperty("cells").GetProperty("MONTHLY_SALES_YTD_PREVIOUS_MONTH");
        Assert.Equal(MonthlySalesLookupApiFactory.YearToPreviousMonthSales, ytdPreviousMonth.GetProperty("value").GetDecimal());
        Assert.Equal(FormatMillionRials(MonthlySalesLookupApiFactory.YearToPreviousMonthSales), ytdPreviousMonth.GetProperty("formattedValue").GetString());
        Assert.NotEqual("Missing", ytdPreviousMonth.GetProperty("freshnessStatus").GetString());
    }

    [Fact]
    public async Task AiQuery_LatestMonthlySalesKchad_RendersOnlyTableAndUnitLabel()
    {
        using var client = _factory.CreateKchadClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "آخرین فروش کچاد چقدر بوده؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        AssertMonthlySalesNarrativeFieldsAreOnlyUnitLabel(root);
        AssertNoForbiddenMonthlySalesProse(root.GetRawText());

        var table = root.GetProperty("symbolLookupTable");
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        var columnIds = columns
            .Select(c => c.GetProperty("identifier").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("فروش ماه مشابه دوره قبل", GetColumnDisplayName(columns, "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH"));
        Assert.DoesNotContain("AVG_12M_MONTHLY_SALES", columnIds);
        AssertNoForbiddenAverageSalesDisplayLabels(columns);

        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        Assert.Equal("کچاد", row.GetProperty("symbolCode").GetString());
        Assert.Equal("معدنی و صنعتی چادرملو", row.GetProperty("companyName").GetString());

        var cells = row.GetProperty("cells");
        Assert.Equal("90,879,722", cells.GetProperty("MONTHLY_SALES").GetProperty("formattedValue").GetString());
        Assert.Equal(JsonValueKind.Null, cells.GetProperty("MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH").GetProperty("formattedValue").ValueKind);
        Assert.Equal("787,016,400", cells.GetProperty("MONTHLY_SALES_YTD").GetProperty("formattedValue").GetString());
        Assert.Equal("605,344,668", cells.GetProperty("MONTHLY_SALES_YTD_PREVIOUS_MONTH").GetProperty("formattedValue").GetString());

        var conversationId = root.GetProperty("conversationId").GetGuid();
        using var reload = await client.GetAsync(
            $"/api/ai/v1/conversations/{conversationId}/messages",
            CancellationToken.None);
        using var reloadDoc = await ReadJsonAsync(reload);

        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);
        AssertNoForbiddenMonthlySalesProse(reloadDoc.RootElement.GetRawText());

        var assistant = reloadDoc.RootElement.GetProperty("messages").EnumerateArray()
            .First(m => m.GetProperty("role").GetString() == "Assistant");
        Assert.Contains("90,879,722", assistant.GetProperty("content").GetString());

        var assistantContent = assistant.GetProperty("assistantContent");
        AssertMonthlySalesNarrativeFieldsAreOnlyUnitLabel(assistantContent);
        Assert.Equal(JsonValueKind.Object, assistantContent.GetProperty("symbolLookupTable").ValueKind);
    }

    [Fact]
    public async Task AiQuery_ExplicitSameMonthPreviousSalesKchad_UsesPriorYearLayoutAndMissingDoesNotFallbackToAverage()
    {
        using var client = _factory.CreateKchadClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "فروش ماه مشابه دوره قبل کچاد چقدر بوده؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        AssertMonthlySalesNarrativeFieldsAreOnlyUnitLabel(root);
        AssertNoForbiddenMonthlySalesProse(root.GetRawText());

        var table = root.GetProperty("symbolLookupTable");
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        var columnIds = columns
            .Select(c => c.GetProperty("identifier").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal("فروش ماه مشابه دوره قبل", GetColumnDisplayName(columns, "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH"));
        Assert.DoesNotContain("AVG_12M_MONTHLY_SALES", columnIds);
        Assert.DoesNotContain("متوسط فروش ۱۲ ماهه", columns.Select(c => c.GetProperty("displayName").GetString()));
        AssertNoForbiddenAverageSalesDisplayLabels(columns);

        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        var cells = row.GetProperty("cells");
        Assert.Equal("90,879,722", cells.GetProperty("MONTHLY_SALES").GetProperty("formattedValue").GetString());

        var priorYear = cells.GetProperty("MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH");
        Assert.Equal(JsonValueKind.Null, priorYear.GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Null, priorYear.GetProperty("formattedValue").ValueKind);
        Assert.Equal("Missing", priorYear.GetProperty("freshnessStatus").GetString());

        Assert.Equal("787,016,400", cells.GetProperty("MONTHLY_SALES_YTD").GetProperty("formattedValue").GetString());
        Assert.Equal("605,344,668", cells.GetProperty("MONTHLY_SALES_YTD_PREVIOUS_MONTH").GetProperty("formattedValue").GetString());
    }

    private static string FormatMillionRials(decimal value) =>
        Math.Round(value / 1_000_000m, 0, MidpointRounding.AwayFromZero).ToString("N0");

    private static string? GetColumnDisplayName(IReadOnlyCollection<JsonElement> columns, string identifier) =>
        columns
            .First(c => string.Equals(c.GetProperty("identifier").GetString(), identifier, StringComparison.OrdinalIgnoreCase))
            .GetProperty("displayName")
            .GetString();

    private static void AssertNoForbiddenAverageSalesDisplayLabels(IReadOnlyCollection<JsonElement> columns)
    {
        var displayNames = columns
            .Select(c => c.GetProperty("displayName").GetString())
            .Where(name => name is not null)
            .ToList();

        Assert.DoesNotContain("AVG_12M_MONTHLY_SALES", displayNames);
        Assert.DoesNotContain("Average 12 Month Sales", displayNames);
        Assert.DoesNotContain("Average 12-Month Monthly Sales", displayNames);
    }

    private static void AssertMonthlySalesNarrativeFieldsAreOnlyUnitLabel(JsonElement element)
    {
        AssertOptionalStringIsNullEmptyOrUnitLabel(element, "textAnswer", requireUnitWhenPresent: true);
        AssertOptionalStringIsNullEmptyOrUnitLabel(element, "clarificationMessage", requireUnitWhenPresent: false);

        if (element.TryGetProperty("explainableAnswer", out var explainableAnswer) &&
            explainableAnswer.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            AssertOptionalStringIsNullEmptyOrUnitLabel(explainableAnswer, "explanationText", requireUnitWhenPresent: false);
            if (explainableAnswer.TryGetProperty("suggestedFollowUpQuestions", out var followUps) &&
                followUps.ValueKind == JsonValueKind.Array)
            {
                Assert.Empty(followUps.EnumerateArray());
            }
        }
    }

    private static void AssertOptionalStringIsNullEmptyOrUnitLabel(
        JsonElement element,
        string propertyName,
        bool requireUnitWhenPresent)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            Assert.False(requireUnitWhenPresent, $"{propertyName} must be present as the monthly-sales value sentence.");
            return;
        }

        var value = property.GetString();
        if (requireUnitWhenPresent)
        {
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.Contains("میلیون ریال", value);
            Assert.DoesNotContain("Unit: million Rials", value);
        }
        else
        {
            Assert.True(string.IsNullOrEmpty(value),
                $"{propertyName} must be empty/null for monthly-sales value answers.");
        }
    }

    private static void AssertNoForbiddenMonthlySalesProse(string json)
    {
        Assert.DoesNotContain("آخرین داده فروش", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("برنگشت", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("اگر منظورت", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("مشخص کن", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("فروش فصلی", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("درآمد عملیاتی", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("کدال", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("did not return", json, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class CyclicalWavesMonthlySalesLookupTests : IClassFixture<CyclicalWavesMonthlySalesLookupApiFactory>
{
    private readonly CyclicalWavesMonthlySalesLookupApiFactory _factory;

    public CyclicalWavesMonthlySalesLookupTests(CyclicalWavesMonthlySalesLookupApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task AiQuery_CyclicalWavesLatestMonthlySales_UsesAverage12MonthLayout()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "آخرین فروش کچاد چقدر بوده؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        AssertMonthlySalesNarrativeFieldsAreOnlyUnitLabel(root);
        AssertNoForbiddenMonthlySalesProse(root.GetRawText());

        var table = root.GetProperty("symbolLookupTable");
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        var columnIds = columns
            .Select(c => c.GetProperty("identifier").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var displayNames = columns
            .Select(c => c.GetProperty("displayName").GetString())
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("AVG_12M_MONTHLY_SALES", columnIds);
        Assert.Contains("MONTHLY_SALES", columnIds);
        Assert.Contains("MONTHLY_SALES_YTD", columnIds);
        Assert.Contains("MONTHLY_SALES_YTD_PREVIOUS_MONTH", columnIds);
        Assert.DoesNotContain("MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH", columnIds);
        Assert.DoesNotContain("REVENUE", columnIds);
        Assert.DoesNotContain("LATEST_PRICE", columnIds);
        Assert.DoesNotContain("DAILY_CHANGE_PCT", columnIds);
        Assert.Contains("فروش ماهانه", displayNames);
        Assert.Contains("متوسط فروش ۱۲ ماهه", displayNames);
        Assert.Contains("فروش YTD", displayNames);
        Assert.Contains("فروش YTD تا ماه قبل", displayNames);
        Assert.DoesNotContain("آخرین قیمت", displayNames);
        Assert.DoesNotContain("تغییر روزانه %", displayNames);
        Assert.DoesNotContain("فروش ماه مشابه دوره قبل", displayNames);
        Assert.Equal("متوسط فروش ۱۲ ماهه", GetColumnDisplayName(columns, "AVG_12M_MONTHLY_SALES"));
        AssertNoForbiddenAverageSalesDisplayLabels(columns);

        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        var cells = row.GetProperty("cells");
        Assert.Equal("90,879,722", cells.GetProperty("MONTHLY_SALES").GetProperty("formattedValue").GetString());
        Assert.Equal("82,500,000", cells.GetProperty("AVG_12M_MONTHLY_SALES").GetProperty("formattedValue").GetString());
        Assert.Equal("787,016,400", cells.GetProperty("MONTHLY_SALES_YTD").GetProperty("formattedValue").GetString());
        Assert.Equal("605,344,668", cells.GetProperty("MONTHLY_SALES_YTD_PREVIOUS_MONTH").GetProperty("formattedValue").GetString());
    }

    [Fact]
    public async Task AiQuery_CyclicalWavesAverage12MonthSalesQuestion_StillUsesMonthlySalesSnapshot()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "متوسط فروش 12 ماهه کچاد" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = document.RootElement.GetProperty("symbolLookupTable");
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        var columnIds = columns
            .Select(c => c.GetProperty("identifier").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("AVG_12M_MONTHLY_SALES", columnIds);
        Assert.DoesNotContain("MONTHLY_SALES", columnIds);
        Assert.DoesNotContain("MONTHLY_SALES_YTD", columnIds);
        Assert.DoesNotContain("MONTHLY_SALES_YTD_PREVIOUS_MONTH", columnIds);
        Assert.DoesNotContain("REVENUE", columnIds);
        Assert.DoesNotContain("LATEST_PRICE", columnIds);
        Assert.DoesNotContain("DAILY_CHANGE_PCT", columnIds);

        Assert.Equal("متوسط فروش ۱۲ ماهه", GetColumnDisplayName(columns, "AVG_12M_MONTHLY_SALES"));
        AssertNoForbiddenAverageSalesDisplayLabels(columns);

        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        var cells = row.GetProperty("cells");
        Assert.Equal("82,500,000", cells.GetProperty("AVG_12M_MONTHLY_SALES").GetProperty("formattedValue").GetString());
    }

    [Fact]
    public async Task AiQuery_CyclicalWavesExplicitSameMonthPreviousSales_DoesNotFallbackToAverage()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "فروش ماه مشابه دوره قبل کچاد چقدر بوده؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = document.RootElement.GetProperty("symbolLookupTable");
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        var columnIds = columns
            .Select(c => c.GetProperty("identifier").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH", columnIds);
        Assert.DoesNotContain("AVG_12M_MONTHLY_SALES", columnIds);
        Assert.Equal("فروش ماه مشابه دوره قبل", GetColumnDisplayName(columns, "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH"));
        AssertNoForbiddenAverageSalesDisplayLabels(columns);

        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        var priorYear = row.GetProperty("cells").GetProperty("MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH");
        Assert.Equal(JsonValueKind.Null, priorYear.GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Null, priorYear.GetProperty("formattedValue").ValueKind);
        Assert.Equal("Missing", priorYear.GetProperty("freshnessStatus").GetString());
    }

    private static string? GetColumnDisplayName(IReadOnlyCollection<JsonElement> columns, string identifier) =>
        columns
            .First(c => string.Equals(c.GetProperty("identifier").GetString(), identifier, StringComparison.OrdinalIgnoreCase))
            .GetProperty("displayName")
            .GetString();

    private static void AssertNoForbiddenAverageSalesDisplayLabels(IReadOnlyCollection<JsonElement> columns)
    {
        var displayNames = columns
            .Select(c => c.GetProperty("displayName").GetString())
            .Where(name => name is not null)
            .ToList();

        Assert.DoesNotContain("AVG_12M_MONTHLY_SALES", displayNames);
        Assert.DoesNotContain("Average 12 Month Sales", displayNames);
        Assert.DoesNotContain("Average 12-Month Monthly Sales", displayNames);
    }

    private static void AssertMonthlySalesNarrativeFieldsAreOnlyUnitLabel(JsonElement element)
    {
        var textAnswer = element.GetProperty("textAnswer").GetString();
        Assert.False(string.IsNullOrWhiteSpace(textAnswer));
        Assert.Contains("میلیون ریال", textAnswer);
        Assert.DoesNotContain("Unit: million Rials", textAnswer);
        if (element.TryGetProperty("clarificationMessage", out var clarificationMessage) &&
            clarificationMessage.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            Assert.True(string.IsNullOrEmpty(clarificationMessage.GetString()));
        }
    }

    private static void AssertNoForbiddenMonthlySalesProse(string json)
    {
        Assert.DoesNotContain("آخرین داده فروش", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("برنگشت", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("اگر منظورت", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("مشخص کن", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("فروش فصلی", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("درآمد عملیاتی", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("کدال", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("did not return", json, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class MonthlySalesLookupApiFactory : AiFacadeApiFactory
{
    public const decimal LatestMonthSales = 987_654_321m;
    public const decimal PriorYearSameMonthSales = 777_000_000m;
    public const decimal YearToDateSales = 1_777_654_321m;
    public const decimal YearToPreviousMonthSales = 790_000_000m;
    public const decimal KchadLatestMonthSales = 90_879_722_000_000m;
    public const decimal KchadYearToDateSales = 787_016_400_000_000m;
    public const decimal KchadYearToPreviousMonthSales = 605_344_668_000_000m;

    private readonly string _dbName = $"monthly-sales-lookup-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ =>
                new SymbolLookupFakeAiModelClient(
                    symbolLookupSymbol: "غگلپا",
                    metricTerm: "آخرین فروش",
                    clarificationMetricTerm: null,
                    multiSymbol: false));
        });
    }

    public HttpClient CreateKchadClient() =>
        WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiModelClient>();
                services.AddSingleton<IAiModelClient>(_ =>
                    new SymbolLookupFakeAiModelClient(
                        symbolLookupSymbol: "کچاد",
                        metricTerm: "آخرین فروش",
                        clarificationMetricTerm: null,
                        multiSymbol: false));
            });
        }).CreateClient();

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

    private static void SeedTestData(FinancialIngestionDbContext db)
    {
        var companyId = Guid.Parse("80000000-0000-0000-0000-000000000001");
        var now = DateTimeOffset.Parse("2026-06-10T08:00:00Z");

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            Name = "شیر پاستوریزه پگاه گلپایگان",
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "13150",
            CompanySymbol = "غگلپا",
            LastSynchronizedAt = now
        });

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000002"),
            Name = "معدنی و صنعتی چادرملو",
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "20001",
            CompanySymbol = "کچاد",
            LastSynchronizedAt = now
        });

        // Farvardin 1405 (older) and Ordibehesht 1405 (latest) monthly observations, plus a
        // quarterly REVENUE row that must NOT be substituted for the monthly ask.
        db.DerivedMetrics.AddRange(
            MonthlySales("13150", new DateOnly(2026, 3, 21), new DateOnly(2026, 4, 20), 555_000_000m, now),
            MonthlySales("13150", new DateOnly(2026, 4, 21), new DateOnly(2026, 5, 21), LatestMonthSales, now),
            MonthlySales("13150", new DateOnly(2025, 4, 21), new DateOnly(2025, 5, 21), PriorYearSameMonthSales, now),
            MonthlyMetric("13150", "MONTHLY_SALES_YTD", "monthly-sales-ytd-source-v1",
                new DateOnly(2026, 4, 21), new DateOnly(2026, 5, 21), YearToDateSales, now),
            MonthlyMetric("13150", "MONTHLY_SALES_YTD_PREVIOUS_MONTH", "monthly-sales-ytd-previous-month-source-v1",
                new DateOnly(2026, 4, 21), new DateOnly(2026, 5, 21), YearToPreviousMonthSales, now),
            new DerivedMetricRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "13150",
                MetricCode = "REVENUE",
                MetricVersion = "v1",
                CalculationPolicyVersion = "normalized-source-v1",
                PeriodType = "ThreeMonths",
                PeriodStart = new DateOnly(2026, 1, 1),
                PeriodEnd = new DateOnly(2026, 3, 31),
                Value = 111m,
                Unit = "Amount",
                ObservedAt = now,
                LastSynchronizedAt = now,
                WarningsJson = "[]",
                SourceEvidenceJson = "[]",
                DependencyEvidenceJson = "[]"
            },
            MonthlySales("20001", new DateOnly(2026, 4, 21), new DateOnly(2026, 5, 21), KchadLatestMonthSales, now),
            MonthlyMetric("20001", "MONTHLY_SALES_YTD", "monthly-sales-ytd-source-v1",
                new DateOnly(2026, 4, 21), new DateOnly(2026, 5, 21), KchadYearToDateSales, now),
            MonthlyMetric("20001", "MONTHLY_SALES_YTD_PREVIOUS_MONTH", "monthly-sales-ytd-previous-month-source-v1",
                new DateOnly(2026, 4, 21), new DateOnly(2026, 5, 21), KchadYearToPreviousMonthSales, now));
    }

    private static DerivedMetricRow MonthlySales(
        string externalCompanyId,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal value,
        DateTimeOffset now) =>
        MonthlyMetric(
            externalCompanyId,
            "MONTHLY_SALES",
            "monthly-sales-source-v1",
            periodStart,
            periodEnd,
            value,
            now);

    private static DerivedMetricRow MonthlyMetric(
        string externalCompanyId,
        string metricCode,
        string policyVersion,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal value,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = externalCompanyId,
            MetricCode = metricCode,
            MetricVersion = "v1",
            CalculationPolicyVersion = policyVersion,
            PeriodType = "Monthly",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = value,
            Unit = "Amount",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[{\"source\":\"NoavaranCurrentApi\"}]",
            DependencyEvidenceJson = "[]"
        };
}

public sealed class CyclicalWavesMonthlySalesLookupApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"cyclicalwaves-monthly-sales-lookup-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ =>
                new SymbolLookupFakeAiModelClient(
                    symbolLookupSymbol: "کچاد",
                    metricTerm: "فروش",
                    clarificationMetricTerm: null,
                    multiSymbol: false));
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

    private static void SeedTestData(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.Parse("2026-06-10T08:00:00Z");
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.Parse("81000000-0000-0000-0000-000000000001"),
            Name = "معدنی و صنعتی چادرملو",
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "30001",
            CompanySymbol = "کچاد",
            LastSynchronizedAt = now
        });

        db.DerivedMetrics.AddRange(
            CyclicalWavesMonthlyMetric(
                "MONTHLY_SALES",
                "monthly-sales-source-v1",
                90_879_722_000_000m,
                now),
            CyclicalWavesMonthlyMetric(
                "AVG_12M_MONTHLY_SALES",
                "avg-12m-monthly-sales-source-v1",
                82_500_000_000_000m,
                now),
            CyclicalWavesMonthlyMetric(
                "MONTHLY_SALES_YTD",
                "monthly-sales-ytd-source-v1",
                787_016_400_000_000m,
                now),
            CyclicalWavesMonthlyMetric(
                "MONTHLY_SALES_YTD_PREVIOUS_MONTH",
                "monthly-sales-ytd-previous-month-source-v1",
                605_344_668_000_000m,
                now));
    }

    private static DerivedMetricRow CyclicalWavesMonthlyMetric(
        string metricCode,
        string policyVersion,
        decimal value,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "30001",
            MetricCode = metricCode,
            MetricVersion = "v1",
            CalculationPolicyVersion = policyVersion,
            PeriodType = "Monthly",
            PeriodStart = new DateOnly(2026, 4, 21),
            PeriodEnd = new DateOnly(2026, 5, 21),
            Value = value,
            Unit = "Amount",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[{\"source\":\"CyclicalWaves\"}]",
            DependencyEvidenceJson = "[]"
        };
}

public sealed class CyclicalWavesDirectPeriodMetricLookupTests : IClassFixture<CyclicalWavesDirectPeriodMetricLookupApiFactory>
{
    private readonly CyclicalWavesDirectPeriodMetricLookupApiFactory _factory;

    public CyclicalWavesDirectPeriodMetricLookupTests(CyclicalWavesDirectPeriodMetricLookupApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task AiQuery_PreviousQuarterNetProfitMargin_ReturnsExactQuarterRow()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "حاشیه سود خالص فصل قبل کچاد" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = document.RootElement.GetProperty("symbolLookupTable");
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal("حاشیه سود خالص فصل قبل", GetColumnDisplayName(columns, "NET_PROFIT_MARGIN"));

        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        var cell = row.GetProperty("cells").GetProperty("NET_PROFIT_MARGIN");
        Assert.Equal(28.1m, cell.GetProperty("value").GetDecimal());
        Assert.Equal("28.1", cell.GetProperty("formattedValue").GetString());
        Assert.Equal("Persisted", cell.GetProperty("freshnessStatus").GetString());
    }

    [Fact]
    public async Task AiQuery_PreviousMonthSales_ReturnsSinglePeriodMetricWithoutQuoteColumns()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "فروش ماه قبل کچاد" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = document.RootElement.GetProperty("symbolLookupTable");
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        var columnIds = columns.Select(c => c.GetProperty("identifier").GetString()).ToList();
        Assert.Equal("فروش ماه قبل", GetColumnDisplayName(columns, "MONTHLY_SALES"));
        Assert.Contains("MONTHLY_SALES", columnIds);
        Assert.DoesNotContain("LATEST_PRICE", columnIds);
        Assert.DoesNotContain("DAILY_CHANGE_PCT", columnIds);
        Assert.DoesNotContain("AVG_12M_MONTHLY_SALES", columnIds);
        Assert.DoesNotContain("MONTHLY_SALES_YTD", columnIds);

        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        var cell = row.GetProperty("cells").GetProperty("MONTHLY_SALES");
        Assert.Equal(88_111_000_000_000m, cell.GetProperty("value").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(cell.GetProperty("formattedValue").GetString()));
    }

    [Fact]
    public async Task AiQuery_LastYearAverage12MonthSales_ReturnsM12Average()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "متوسط فروش 12 ماهه سال قبل کچاد" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = document.RootElement.GetProperty("symbolLookupTable");
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal("متوسط فروش ۱۲ ماهه سال قبل", GetColumnDisplayName(columns, "AVG_12M_MONTHLY_SALES"));

        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        var cell = row.GetProperty("cells").GetProperty("AVG_12M_MONTHLY_SALES");
        Assert.Equal(71_250_000_000_000m, cell.GetProperty("value").GetDecimal());
        Assert.Equal("71,250,000", cell.GetProperty("formattedValue").GetString());
    }

    [Fact]
    public async Task AiQuery_PeLookup_StillReturnsValuationMetric()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE کچاد" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = document.RootElement.GetProperty("symbolLookupTable");
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal("نسبت قیمت به سود", GetColumnDisplayName(columns, "PE_TTM"));

        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        var cell = row.GetProperty("cells").GetProperty("PE_TTM");
        Assert.Equal(9.73m, cell.GetProperty("value").GetDecimal());
        Assert.Equal("9.73", cell.GetProperty("formattedValue").GetString());
    }

    [Fact]
    public async Task AiQuery_PreviousMonthSales_WhenExactPeriodMissing_ReturnsMissingWithoutFallback()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "فروش ماه قبل شغدیر" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = document.RootElement.GetProperty("symbolLookupTable");
        var columns = table.GetProperty("columns").EnumerateArray().ToList();
        Assert.Contains(
            columns.Select(c => c.GetProperty("identifier").GetString()),
            identifier => string.Equals(identifier, "MONTHLY_SALES", StringComparison.OrdinalIgnoreCase));
        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        Assert.True(row.GetProperty("cells").TryGetProperty("MONTHLY_SALES", out var cell));
        Assert.Equal(JsonValueKind.Null, cell.GetProperty("value").ValueKind);
        Assert.Equal("Missing", cell.GetProperty("freshnessStatus").GetString());
    }

    private static string? GetColumnDisplayName(IReadOnlyCollection<JsonElement> columns, string identifier) =>
        columns
            .First(c => string.Equals(c.GetProperty("identifier").GetString(), identifier, StringComparison.OrdinalIgnoreCase))
            .GetProperty("displayName")
            .GetString();

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class CyclicalWavesDirectPeriodMetricLookupApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"cyclicalwaves-direct-period-lookup-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => new PeriodAwareSymbolLookupFakeAiModelClient());
            services.Configure<FinancialCopilot.Infrastructure.Financial.Scanner.MonthlyActivityLookupOptions>(options =>
                options.DirectLookupSourceMode =
                    FinancialCopilot.Infrastructure.Financial.Scanner.MonthlyActivityDirectLookupSourceMode.TrendSnapshot);
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

    private static void SeedTestData(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.Parse("2026-06-10T08:00:00Z");
        db.Companies.AddRange(
            new NormalizedCompanyRow
            {
                Id = Guid.Parse("82000000-0000-0000-0000-000000000001"),
                Name = "معدنی و صنعتی چادرملو",
                ProviderName = "NoavaranCurrentApi",
                ExternalCompanyId = "30001",
                CompanySymbol = "کچاد",
                TseSymbol = "کچاد",
                Ticker = "کچاد",
                LastSynchronizedAt = now
            },
            new NormalizedCompanyRow
            {
                Id = Guid.Parse("82000000-0000-0000-0000-000000000002"),
                Name = "شغدیر",
                ProviderName = "NoavaranCurrentApi",
                ExternalCompanyId = "30002",
                CompanySymbol = "شغدیر",
                TseSymbol = "شغدیر",
                Ticker = "شغدیر",
                LastSynchronizedAt = now
            });

        db.DerivedMetrics.AddRange(
            Metric("30001", "NET_PROFIT_MARGIN", "ThreeMonths", new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 20), 25.5m, now),
            Metric("30001", "NET_PROFIT_MARGIN", "ThreeMonths", new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 20), 28.1m, now),
            Metric("30001", "NET_PROFIT_MARGIN", "ThreeMonths", new DateOnly(2025, 12, 21), new DateOnly(2026, 3, 20), 30.2m, now),
            Metric("30001", "GROSS_PROFIT_MARGIN", "ThreeMonths", new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 20), 21.4m, now),
            Metric("30001", "GROSS_PROFIT_MARGIN", "ThreeMonths", new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 20), 23.8m, now),
            Metric("30001", "GROSS_PROFIT_MARGIN", "ThreeMonths", new DateOnly(2025, 12, 21), new DateOnly(2026, 3, 20), 24.99m, now),
            Metric("30001", "OPERATING_PROFIT_MARGIN", "ThreeMonths", new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 20), 19.25m, now),
            Metric("30001", "OPERATING_PROFIT_MARGIN", "ThreeMonths", new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 20), 20.55m, now),
            Metric("30001", "OPERATING_PROFIT_MARGIN", "ThreeMonths", new DateOnly(2025, 12, 21), new DateOnly(2026, 3, 20), 21.73m, now),
            Metric("30001", "MONTHLY_SALES", "Monthly", new DateOnly(2025, 4, 21), new DateOnly(2025, 5, 21), 69_220_219_000_000m, now),
            Metric("30001", "MONTHLY_SALES", "Monthly", new DateOnly(2026, 3, 21), new DateOnly(2026, 4, 21), 88_111_000_000_000m, now),
            Metric("30001", "MONTHLY_SALES", "Monthly", new DateOnly(2026, 4, 21), new DateOnly(2026, 5, 21), 90_879_722_000_000m, now),
            Metric("30001", "AVG_12M_MONTHLY_SALES", "Monthly", new DateOnly(2025, 4, 21), new DateOnly(2025, 5, 21), 71_250_000_000_000m, now),
            Metric("30001", "AVG_12M_MONTHLY_SALES", "Monthly", new DateOnly(2026, 4, 21), new DateOnly(2026, 5, 21), 82_500_000_000_000m, now),
            Metric("30001", "PE_TTM", "ThreeMonths", new DateOnly(2025, 12, 21), new DateOnly(2026, 3, 20), 9.73m, now),
            Metric("30001", "PS_TTM", "ThreeMonths", new DateOnly(2025, 12, 21), new DateOnly(2026, 3, 20), 2.14m, now),
            Metric("30002", "MONTHLY_SALES", "Monthly", new DateOnly(2026, 4, 21), new DateOnly(2026, 5, 21), 11_500_000_000_000m, now));

        db.CompanyMonthlyActivityTrendSnapshots.AddRange(
            Snapshot(
                "30001",
                "کچاد",
                "معدنی و صنعتی چادرملو",
                reportYear: 2026,
                reportMonth: 2,
                monthlySalesAmount: 90_879_722_000_000m,
                sameMonthPreviousYearSalesAmount: 69_220_219_000_000m,
                average12MonthSalesAmount: 82_500_000_000_000m,
                ytdSalesAmount: 787_016_400_000_000m,
                ytdPreviousMonthSalesAmount: 605_344_668_000_000m,
                now),
            Snapshot(
                "30001",
                "کچاد",
                "معدنی و صنعتی چادرملو",
                reportYear: 2026,
                reportMonth: 1,
                monthlySalesAmount: 88_111_000_000_000m,
                sameMonthPreviousYearSalesAmount: 69_220_219_000_000m,
                average12MonthSalesAmount: 71_250_000_000_000m,
                ytdSalesAmount: 605_344_668_000_000m,
                ytdPreviousMonthSalesAmount: 517_233_668_000_000m,
                now),
            Snapshot(
                "30002",
                "شغدیر",
                "شغدیر",
                reportYear: 2026,
                reportMonth: 2,
                monthlySalesAmount: 11_500_000_000_000m,
                sameMonthPreviousYearSalesAmount: null,
                average12MonthSalesAmount: null,
                ytdSalesAmount: null,
                ytdPreviousMonthSalesAmount: null,
                now));
    }

    private static DerivedMetricRow Metric(
        string externalCompanyId,
        string metricCode,
        string periodType,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal value,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = externalCompanyId,
            MetricCode = metricCode,
            MetricVersion = "v1",
            CalculationPolicyVersion = $"{metricCode.ToLowerInvariant()}-source-v1",
            PeriodType = periodType,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = value,
            Unit = "Amount",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[{\"source\":\"CyclicalWaves\"}]",
            DependencyEvidenceJson = "[]"
        };

    private static CompanyMonthlyActivityTrendSnapshotRow Snapshot(
        string externalCompanyId,
        string companySymbol,
        string companyName,
        int reportYear,
        byte reportMonth,
        decimal monthlySalesAmount,
        decimal? sameMonthPreviousYearSalesAmount,
        decimal? average12MonthSalesAmount,
        decimal? ytdSalesAmount,
        decimal? ytdPreviousMonthSalesAmount,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = externalCompanyId,
            CompanySymbol = companySymbol,
            CompanyName = companyName,
            ReportYear = reportYear,
            ReportMonth = reportMonth,
            MonthlySalesAmount = monthlySalesAmount,
            SameMonthPreviousYearSalesAmount = sameMonthPreviousYearSalesAmount,
            Average12MonthSalesAmount = average12MonthSalesAmount,
            Average12MonthPeriodCount = average12MonthSalesAmount.HasValue ? 12 : 0,
            YtdSalesAmount = ytdSalesAmount,
            YtdPreviousMonthSalesAmount = ytdPreviousMonthSalesAmount,
            SourceProviderName = "CyclicalWaves",
            IsComparablePreviousYearAvailable = sameMonthPreviousYearSalesAmount.HasValue,
            IsAverage12MonthComplete = average12MonthSalesAmount.HasValue,
            DataCompletenessScore = 1m,
            CalculatedAtUtc = now
        };
}

internal sealed class PeriodAwareSymbolLookupFakeAiModelClient : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "PeriodAwareSymbolLookupFake",
        "fake-v1",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput |
        AiModelCapability.UsageReporting | AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        var userMessage = request.Messages.Last(message => message.Role == AiMessageRole.User).Content ?? string.Empty;
        var json = request.StructuredOutput?.SchemaName switch
        {
            "IntentDetectionOutput" => "{\"intent\":\"SymbolLookup\",\"confidence\":0.98}",
            "SymbolLookupParseOutput" => BuildSinglePairJson(userMessage),
            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: new AiExecutionUsageFacts(
                request.CorrelationId,
                Descriptor.ProviderKey,
                Descriptor.ModelKey,
                AiExecutionStatus.Completed,
                TimeSpan.Zero,
                AttemptNumber: 0,
                InputTokens: 12,
                OutputTokens: 6)));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey,
            Descriptor.ModelKey,
            Available: true,
            DateTimeOffset.UtcNow,
            "OK"));

    private static string BuildSinglePairJson(string userMessage)
    {
        var normalized = userMessage.Replace('ي', 'ی').Replace('ك', 'ک');
        var symbol = normalized.Contains("شغدیر", StringComparison.OrdinalIgnoreCase) ? "شغدیر" : "کچاد";
        var metricTerm = normalized.Contains("حاشیه سود عملیاتی", StringComparison.OrdinalIgnoreCase) ? "حاشیه سود عملیاتی"
            : normalized.Contains("حاشیه سود ناخالص", StringComparison.OrdinalIgnoreCase) ? "حاشیه سود ناخالص"
            : normalized.Contains("حاشیه سود خالص", StringComparison.OrdinalIgnoreCase) ? "حاشیه سود خالص"
            : normalized.Contains("متوسط فروش", StringComparison.OrdinalIgnoreCase) || normalized.Contains("میانگین فروش", StringComparison.OrdinalIgnoreCase) ? "متوسط فروش 12 ماهه"
            : normalized.Contains("قیمت به فروش", StringComparison.OrdinalIgnoreCase) || normalized.Contains("ps", StringComparison.OrdinalIgnoreCase) ? "ps"
            : normalized.Contains("قیمت به سود", StringComparison.OrdinalIgnoreCase) || normalized.Contains("pe", StringComparison.OrdinalIgnoreCase) ? "pe"
            : "فروش";

        return $$"""{"detectedLanguage":"fa","pairs":[{"symbolName":"{{symbol}}","metricTerm":"{{metricTerm}}"}],"clarificationRequired":false,"clarificationMessage":null}""";
    }
}
