using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class CashFlowReport161RoutingBaselineEndpointTests : IClassFixture<CashFlow161BaselineFactory>, IClassFixture<BalanceSheetReport154ApiFactory>, IClassFixture<CashFlow161V1BaselineFactory>
{
    private readonly CashFlow161BaselineFactory _factory;
    private readonly BalanceSheetReport154ApiFactory _balanceSheetFactory;
    private readonly CashFlow161V1BaselineFactory _v1Factory;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public CashFlowReport161RoutingBaselineEndpointTests(CashFlow161BaselineFactory factory, BalanceSheetReport154ApiFactory balanceSheetFactory, CashFlow161V1BaselineFactory v1Factory)
    {
        _factory = factory;
        _balanceSheetFactory = balanceSheetFactory;
        _v1Factory = v1Factory;
        _factory.EnsureSeeded();
    }

    private static string BaselineDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "specs"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "FinancialCopilot.UnitTests", "TestData", "Feature161");
    }

    [Fact]
    public async Task EndpointRoutingAndSequentialConversation_MatchPreFeatureBaseline()
    {
        var phrases = JsonDocument.Parse(File.ReadAllText(Path.Combine(BaselineDir(), "routing-baseline.json")))
            .RootElement.EnumerateArray().Select(item => item.GetProperty("Phrase").GetString()!).ToArray();
        Assert.Equal(67, phrases.Length);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        var snapshots = new List<object>();
        async Task<Guid?> Capture(string phrase, Guid? conversationId, string caseId)
        {
            _factory.Probe.Reset();
            _factory.Table.Reset();
            using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = phrase, conversationId });
            var raw = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{caseId}: HTTP {(int)response.StatusCode}: {raw}");
            using var parsed = JsonDocument.Parse(raw);
            var root = parsed.RootElement;
            string? value(string name) => root.TryGetProperty(name, out var item) && item.ValueKind != JsonValueKind.Null ? item.ToString() : null;
            snapshots.Add(new
            {
                Case = caseId, Phrase = phrase, IsFollowUp = conversationId is not null, Status = (int)response.StatusCode,
                Intent = value("intent"), Clarification = value("clarificationRequired"),
                Executor = _factory.Probe.LastInvocation?.CapabilityCode,
                TableQueries = _factory.Table.Queries.ToArray()
            });
            return root.TryGetProperty("conversationId", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var guid) ? guid : null;
        }
        for (var i = 0; i < phrases.Length; i++) await Capture(phrases[i], null, $"fresh-{i}");
        Guid? conversation = null;
        for (var i = 60; i < 67; i++)
        {
            var next = await Capture(phrases[i], conversation, $"U{i - 59}");
            Assert.NotNull(next);
            if (conversation is not null) Assert.Equal(conversation, next);
            conversation = next;
        }
        var path = Path.Combine(BaselineDir(), "endpoint-routing-baseline.json");
        var actual = JsonSerializer.Serialize(snapshots, Json);
        if (Environment.GetEnvironmentVariable("FEATURE161_WRITE_ROUTING_BASELINE") == "1") File.WriteAllText(path, actual);
        Assert.True(File.Exists(path));
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task ExistingBalanceSheetFollowUp_MatchesEndpointBaseline()
    {
        _balanceSheetFactory.EnsureSeeded();
        using var client = _balanceSheetFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        async Task<JsonDocument> Post(string message, Guid? conversationId = null)
        {
            using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message, conversationId });
            var raw = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, raw);
            return JsonDocument.Parse(raw);
        }
        using var first = await Post("ترازنامه وبملت");
        var id = first.RootElement.GetProperty("conversationId").GetGuid();
        using var second = await Post("ترازنامه‌اش", id);
        Assert.Equal(id, second.RootElement.GetProperty("conversationId").GetGuid());
        var root = second.RootElement;
        var report = root.GetProperty("financialStatementTableResult").GetProperty("balanceSheetReport");
        var actual = JsonSerializer.Serialize(new
        {
            Intent = root.GetProperty("intent").GetString(),
            Clarification = root.GetProperty("clarificationRequired").GetBoolean(),
            Outcome = report.GetProperty("outcome").GetString(),
            Symbol = report.GetProperty("company").GetProperty("symbol").GetString(),
            View = report.GetProperty("requestedView").GetString()
        }, Json);
        var path = Path.Combine(BaselineDir(), "balance-followup-endpoint-baseline.json");
        if (Environment.GetEnvironmentVariable("FEATURE161_WRITE_ROUTING_BASELINE") == "1") File.WriteAllText(path, actual);
        Assert.True(File.Exists(path));
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task V1EndpointRequests_MatchPreFeatureBaseline()
    {
        _v1Factory.EnsureBillingSeeded();
        var phrases = JsonDocument.Parse(File.ReadAllText(Path.Combine(BaselineDir(), "routing-baseline.json")))
            .RootElement.EnumerateArray().Select(item => item.GetProperty("Phrase").GetString()!).ToArray();
        using var client = _v1Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        var snapshots = new List<object>();
        for (var i = 0; i < phrases.Length; i++)
        {
            _v1Factory.Table.Reset();
            using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = phrases[i] });
            var raw = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"V1-{i}: HTTP {(int)response.StatusCode}: {raw}");
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            string? value(string name) => root.TryGetProperty(name, out var item) && item.ValueKind != JsonValueKind.Null ? item.ToString() : null;
            snapshots.Add(new { Case = i, Phrase = phrases[i], Intent = value("intent"), Clarification = value("clarificationRequired"), TableQueries = _v1Factory.Table.Queries.ToArray() });
        }
        var path = Path.Combine(BaselineDir(), "v1-endpoint-routing-baseline.json");
        var actual = JsonSerializer.Serialize(snapshots, Json);
        if (Environment.GetEnvironmentVariable("FEATURE161_WRITE_ROUTING_BASELINE") == "1") File.WriteAllText(path, actual);
        Assert.True(File.Exists(path));
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual.Replace("\r\n", "\n"));
    }
}

public sealed class CashFlow161BaselineFactory : AiFacadeApiFactory
{
    public Feature128ExecutorProbe Probe { get; } = new();
    public CashFlow161TableProbe Table { get; } = new();
    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2",
            ["CashFlowReport:Enabled"] = "false",
            ["SemanticRouting:DefaultMode"] = "SemanticPrimary",
            ["RateLimiting:AuthenticatedActor:PermitLimit"] = "1000"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient, V2MonthlySalesRoutingFakeAiModelClient>();
            services.RemoveAll<ISemanticCapabilityExecutionObserver>();
            services.AddSingleton<ISemanticCapabilityExecutionObserver>(Probe);
            services.RemoveAll<IFinancialStatementTableQueryUseCase>();
            services.AddSingleton<IFinancialStatementTableQueryUseCase>(Table);
        });
    }

    public void EnsureSeeded()
    {
        EnsureBillingSeeded();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        if (db.Companies.Any()) return;
        string[] symbols = ["فلوله", "فولاد", "وبملت", "شگل", "فملی", "کگل", "کچاد"];
        for (var i = 0; i < symbols.Length; i++)
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = Guid.Parse($"61000000-0000-0000-0000-{i + 10:D12}"), ProviderName = "NoavaranCurrentApi",
                ExternalCompanyId = $"baseline-{i}", Name = symbols[i], Ticker = symbols[i],
                CompanySymbol = symbols[i], TseSymbol = symbols[i], LastSynchronizedAt = DateTimeOffset.UtcNow
            });
        db.SaveChanges();
    }
}

public sealed class CashFlow161TableProbe : IFinancialStatementTableQueryUseCase
{
    public List<FinancialStatementTableQuery> Queries { get; } = [];
    public void Reset() => Queries.Clear();
    public Task<FinancialStatementTableResult?> ExecuteAsync(FinancialStatementTableQuery query, CancellationToken ct = default)
    {
        Queries.Add(query);
        var source = new FinancialStatementTableSource(
            Guid.Parse("61000000-0000-0000-0000-000000000100"), "fixture", "NoavaranCurrentApi", "fixture",
            query.CompanyQuery ?? "fixture", null, query.StatementType ?? FinancialCopilot.Domain.Financial.Entities.FinancialStatementType.CashFlow,
            "SixMonths", 6, new DateOnly(2026, 3, 21), new DateOnly(2026, 9, 22), null,
            "1405/06/31", "1405/12/29", null, false, false, false, "MillionRials");
        return Task.FromResult<FinancialStatementTableResult?>(new(source, [], [], [], "fixture", DateTimeOffset.UnixEpoch));
    }
}

public sealed class CashFlow161V1BaselineFactory : AiFacadeApiFactory
{
    public CashFlow161TableProbe Table { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CashFlowReport:Enabled"] = "false", ["RateLimiting:AuthenticatedActor:PermitLimit"] = "1000"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFinancialStatementTableQueryUseCase>();
            services.AddSingleton<IFinancialStatementTableQueryUseCase>(Table);
        });
    }
}
