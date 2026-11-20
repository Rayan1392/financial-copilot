using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 081 change «period-analysis-feature-flag»: with <c>FinancialStatementPeriodAnalysis:Enabled</c> false, a
/// question 081 would answer ends unsupported at zero credits and Feature 163 adds its analysis offer. Statement-table
/// requests keep their route. Real V2 endpoint, workflow, billing and persistence; every model is scripted.
/// </summary>
public sealed class PeriodAnalysisFeatureFlagEndpointTests : IClassFixture<PeriodAnalysisDisabledApiFactory>
{
    private readonly PeriodAnalysisDisabledApiFactory _factory;

    public PeriodAnalysisFeatureFlagEndpointTests(PeriodAnalysisDisabledApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.Fallback.Reset();
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        return client;
    }

    private static async Task<JsonElement> Post(HttpClient client, string message)
    {
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message }, CancellationToken.None);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {content}");
        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private static decimal Credits(JsonElement root) =>
        root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            ? usage.GetProperty("creditsCharged").GetDecimal()
            : 0m;

    private static JsonElement? OfferOf(JsonElement root) =>
        root.TryGetProperty("groundedAnalysis", out var hint) && hint.ValueKind == JsonValueKind.Object &&
        hint.TryGetProperty("offer", out var offer) && offer.ValueKind == JsonValueKind.Object
            ? offer
            : null;

    [Theory] // AC-1, AC-2
    [InlineData("سود و زیان وبملت را تفسیر و تحلیل کن")]
    [InlineData("صورت مالی وبملت را تحلیل کن")]
    public async Task PeriodAnalysisQuestion_WhenSwitchedOff_GetsTheAnalysisOffer(string message)
    {
        using var client = Client();

        var turn = await Post(client, message);

        Assert.Equal("Unsupported", turn.GetProperty("outcome").GetString());
        Assert.NotEqual("FinancialStatementPeriodAnalysis", turn.GetProperty("intent").GetString());
        Assert.Equal(0m, Credits(turn));
        Assert.Equal(0, _factory.Fallback.Calls);
        var offer = OfferOf(turn) ?? throw new Xunit.Sdk.XunitException($"No analysis offer: {turn}");
        Assert.Equal("وبملت", offer.GetProperty("symbol").GetString());
        Assert.DoesNotContain("جمع‌بندی سریع", turn.GetProperty("textAnswer").GetString(), StringComparison.Ordinal);
    }

    [Theory] // AC-3
    [InlineData("صورت سود و زیان فصلی وبملت")]
    [InlineData("ترازنامه وبملت")]
    public async Task StatementTableRequest_WhenSwitchedOff_KeepsTheTable(string message)
    {
        using var client = Client();

        var turn = await Post(client, message);

        Assert.Equal("FinancialStatementTableLookup", turn.GetProperty("intent").GetString());
        Assert.Null(OfferOf(turn));
    }
}

public sealed class PeriodAnalysisDisabledApiFactory : BalanceSheetReport154ApiFactory
{
    public ScriptedSymbolGroundedFallbackModel Fallback { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FinancialStatementPeriodAnalysis:Enabled"] = "false",
                ["ReportDiscussion:Enabled"] = "true",
                ["SymbolGroundedFallback:Enabled"] = "true",
                ["GroundedAnalysis:Enabled"] = "true"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReportDiscussionAnalysisModel>();
            services.AddSingleton<IReportDiscussionAnalysisModel>(new ScriptedReportDiscussionAnalysisModel());
            services.RemoveAll<IReportDiscussionModel>();
            services.AddSingleton<IReportDiscussionModel>(new ScriptedReportDiscussionModel());
            services.RemoveAll<ISymbolGroundedFallbackModel>();
            services.AddSingleton<ISymbolGroundedFallbackModel>(Fallback);
        });
    }
}
