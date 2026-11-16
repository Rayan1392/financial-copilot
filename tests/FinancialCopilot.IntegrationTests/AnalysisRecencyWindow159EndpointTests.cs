using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 159 change «analysis-recency-window» through the real V2 endpoint: comprehensive analysis mapped to the v2
/// tables (production setting), the Feature 154 income statements, and the Feature 163 fallback with a scripted model.
/// وبملت has only a 400-day-old analysis; شگل has one from yesterday.
/// </summary>
public sealed class AnalysisRecencyWindow159EndpointTests : IClassFixture<AnalysisRecencyWindow159ApiFactory>
{
    private const string NoRecentSentence = "برای نماد وبملت تحلیل تازه‌ای در ۳۰ روز اخیر در تحلیل‌اپ پیدا نشد.";

    private readonly AnalysisRecencyWindow159ApiFactory _factory;

    public AnalysisRecencyWindow159EndpointTests(AnalysisRecencyWindow159ApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.Fallback.Reset();
    }

    private async Task<JsonElement> Post(string message)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string Text(JsonElement root) => root.GetProperty("textAnswer").GetString() ?? string.Empty;
    private static decimal Credits(JsonElement root) => root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal();

    private AssistantMessagePayload Payload(JsonElement answer)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConversationDbContext>();
        var id = answer.GetProperty("assistantMessageId").GetGuid();
        return JsonSerializer.Deserialize<AssistantMessagePayload>(
            db.Messages.AsNoTracking().Single(message => message.Id == id).AssistantPayloadJson!)!;
    }

    [Fact] // D1, D3, D4: the old analysis is never shown; the question is answered by the Feature 163 fallback.
    public async Task OnlyAnOldAnalysis_IsNotShown_AndTheFallbackAnswers()
    {
        _factory.Fallback.Reply = prompt =>
        {
            var fact = prompt.Data.Facts[0];
            return new SymbolGroundedFallbackModelReply(SymbolGroundedFallbackDisposition.Answered,
                $"بر اساس صورت سود و زیان فصلی، {fact.Label} در {fact.Period} برابر {fact.Value.ToString("0.##", CultureInfo.InvariantCulture)} است.",
                "", [fact.EvidenceId]);
        };

        var answer = await Post("سهم وبملت را بررسی کن");

        Assert.True(Text(answer).StartsWith("بر اساس صورت سود و زیان فصلی", StringComparison.Ordinal), answer.ToString());
        Assert.DoesNotContain(AnalysisRecencyWindow159ApiFactory.OldTitle, answer.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("Answered", answer.GetProperty("outcome").GetString());
        Assert.Equal(2m, Credits(answer));
        Assert.Equal("وبملت", _factory.Fallback.Prompts.Single().Data.CompanySymbol);
        var origin = Payload(answer).FallbackOf!;
        Assert.Equal(DialogueOutcome.NoData, origin.Outcome);
        Assert.Equal(DialogueOutcomeReasonCodes.ComprehensiveAnalysisNotRecent, origin.ReasonCode);
    }

    [Fact] // D5: when the fallback cannot answer, the free card says only recent analyses were searched.
    public async Task OnlyAnOldAnalysis_AndTheFallbackFails_IsTheFreeNoRecentAnalysisCard()
    {
        _factory.Fallback.Reply = _ => throw new ReportDiscussionModelUnavailableException("timeout");

        var answer = await Post("سهم وبملت را بررسی کن");

        Assert.Contains(NoRecentSentence, Text(answer), StringComparison.Ordinal);
        Assert.DoesNotContain(AnalysisRecencyWindow159ApiFactory.OldTitle, answer.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("NoData", answer.GetProperty("outcome").GetString());
        Assert.Equal(0m, Credits(answer));
    }

    [Fact] // D1: a recent analysis is still the answer; the fallback is not involved.
    public async Task RecentAnalysis_IsStillShown()
    {
        var answer = await Post("سهم شگل را بررسی کن");

        Assert.Contains(AnalysisRecencyWindow159ApiFactory.RecentTitle, Text(answer), StringComparison.Ordinal);
        Assert.Equal(0, _factory.Fallback.Calls);
    }
}

public sealed class AnalysisRecencyWindow159ApiFactory : BalanceSheetReport154ApiFactory
{
    public const string OldTitle = "وبملت - تحلیل بنیادی قدیمی";
    public const string RecentTitle = "شگل - گزارش عملکرد تازه";

    private readonly object _gate = new();
    private bool _analysesSeeded;

    public ScriptedSymbolGroundedFallbackModel Fallback { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // Production setting: comprehensive analysis reads the v2 analysis tables in NoavaranAmin mode (Feature 159).
        builder.UseSetting("FinancialData:CapabilitySources:comprehensive_analysis", "CyclicalWaves");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["SymbolGroundedFallback:Enabled"] = "true" }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISymbolGroundedFallbackModel>();
            services.AddSingleton<ISymbolGroundedFallbackModel>(Fallback);
        });
    }

    public new void EnsureSeeded()
    {
        base.EnsureSeeded();
        lock (_gate)
        {
            if (_analysesSeeded) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.CyclicalWavesV2Analyses.AddRange(
                Analysis(159101, OldTitle, now.AddDays(-400)),
                Analysis(159102, RecentTitle, now.AddDays(-1)));
            db.SaveChanges();
            _analysesSeeded = true;

            CyclicalWavesV2AnalysisRow Analysis(long id, string title, DateTimeOffset publishedAt) => new()
            {
                Id = id, Title = title, Summary = $"<p>{title}</p>", PlainTextSummary = title, PublishedAt = publishedAt,
                ContentHash = id.ToString(CultureInfo.InvariantCulture).PadLeft(64, '0'),
                FirstSyncedAt = now, LastSyncedAt = now, UpdatedAt = now
            };
        }
    }
}
