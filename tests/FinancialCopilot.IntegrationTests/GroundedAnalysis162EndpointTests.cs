using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Change «sol-deep-analysis-web-search» T08: Report Discussion with GroundedAnalysis:Enabled, through the real V2
/// endpoint, workflow, billing and persistence. The routing and analysis calls are scripted.
/// </summary>
public sealed class GroundedAnalysis162EndpointTests : IClassFixture<GroundedAnalysis162ApiFactory>
{
    private readonly GroundedAnalysis162ApiFactory _factory;

    public GroundedAnalysis162EndpointTests(GroundedAnalysis162ApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.Model.Reset();
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        return client;
    }

    private static async Task<JsonElement> Post(HttpClient client, string message, Guid? conversationId = null, object? options = null)
    {
        object body = (conversationId, options) switch
        {
            (null, _) => new { message },
            (_, null) => new { message, conversationId },
            _ => new { message, conversationId, analysisOptions = options }
        };
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", body, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string Intent(JsonElement root) => root.GetProperty("intent").GetString() ?? string.Empty;
    private static string Text(JsonElement root) => root.GetProperty("textAnswer").GetString() ?? string.Empty;
    private static decimal Credits(JsonElement root) => root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal();
    private static string Outcome(JsonElement root) => root.GetProperty("outcome").GetString() ?? string.Empty;

    private AssistantMessagePayload Payload(JsonElement answer)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConversationDbContext>();
        var id = answer.GetProperty("assistantMessageId").GetGuid();
        return JsonSerializer.Deserialize<AssistantMessagePayload>(
            db.Messages.AsNoTracking().Single(message => message.Id == id).AssistantPayloadJson!)!;
    }

    private static ReportDiscussionAnalysisReply Echo(ReportDiscussionPrompt prompt, string labelPrefix, string extra = "",
        IReadOnlyList<DeclaredExternalSource>? sources = null, IReadOnlyCollection<string>? consulted = null)
    {
        var calculation = prompt.Data.Calculations.First(item => item.Label.StartsWith(labelPrefix, StringComparison.Ordinal));
        return new ReportDiscussionAnalysisReply(ReportDiscussionDisposition.Answered,
            $"**{calculation.Label} در {calculation.Period} برابر {calculation.Value.ToString("0.##", CultureInfo.InvariantCulture)} است.**{extra}",
            [calculation.EvidenceId], null, sources ?? [], consulted ?? []);
    }

    private async Task<Guid> ShowBalanceSheetAsync(HttpClient client)
    {
        var report = await Post(client, "ترازنامه شگل");
        // FR-4: a discussable report carries the backend price list and defaults.
        var hint = report.GetProperty("groundedAnalysis");
        Assert.True(hint.GetProperty("available").GetBoolean());
        Assert.Equal("deep", hint.GetProperty("defaultDepth").GetString());
        Assert.True(hint.GetProperty("defaultWebSearch").GetBoolean());
        Assert.Equal([8m, 12m, 16m, 25m],
            hint.GetProperty("prices").EnumerateArray().Select(price => price.GetProperty("credits").GetDecimal()).ToArray());
        return report.GetProperty("conversationId").GetGuid();
    }

    private static async Task<JsonElement[]> MessagesAsync(HttpClient client, Guid conversationId)
    {
        using var response = await client.GetAsync($"/api/ai/v1/conversations/{conversationId}/messages", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("messages").EnumerateArray().Select(message => message.Clone()).ToArray();
    }

    private static JsonElement? SavedHint(JsonElement message) =>
        message.TryGetProperty("assistantContent", out var content) &&
        content.ValueKind == JsonValueKind.Object &&
        content.TryGetProperty("groundedAnalysis", out var hint) &&
        hint.ValueKind == JsonValueKind.Object
            ? hint
            : null;

    [Fact] // OQ-113 (a): a reloaded conversation carries the same hint, on its latest assistant message only.
    public async Task ReloadedConversation_CarriesTheHint_OnTheLatestAnswerOnly()
    {
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);

        var afterReport = await MessagesAsync(client, conversation);
        var reportHint = SavedHint(afterReport.Last(message => message.GetProperty("role").GetString() == "Assistant"));
        Assert.NotNull(reportHint);
        Assert.True(reportHint.Value.GetProperty("available").GetBoolean());
        Assert.Equal([8m, 12m, 16m, 25m],
            reportHint.Value.GetProperty("prices").EnumerateArray().Select(price => price.GetProperty("credits").GetDecimal()).ToArray());
        Assert.Equal(JsonValueKind.Null, reportHint.Value.GetProperty("offer").ValueKind);

        _factory.Model.Decision = ReportDiscussionDisposition.Answered;
        _factory.Model.Analysis = prompt => Echo(prompt, "نسبت بدهی");
        await Post(client, "نسبت بدهی این شرکت را تحلیل کن", conversation);

        var afterAnswer = await MessagesAsync(client, conversation);
        var assistants = afterAnswer.Where(message => message.GetProperty("role").GetString() == "Assistant").ToArray();
        Assert.Null(SavedHint(assistants[0]));
        Assert.True(SavedHint(assistants[^1])!.Value.GetProperty("available").GetBoolean());
        // The stored decision has one owner: the flag the turn persisted.
        Assert.True(Payload(await Post(client, "نسبت بدهی این شرکت را تحلیل کن", conversation)).GroundedAnalysisAvailable);
    }

    [Fact] // OQ-113 (a): a reply that cannot lead to an analysis stores false and the reload shows no hint.
    public async Task ReloadedConversation_WithoutADiscussableAnswer_HasNoHint()
    {
        using var client = Client();
        var greeting = await Post(client, "سلام");
        var conversation = greeting.GetProperty("conversationId").GetGuid();

        Assert.False(Payload(greeting).GroundedAnalysisAvailable);
        Assert.All(await MessagesAsync(client, conversation), message => Assert.Null(SavedHint(message)));
    }

    [Fact] // AC-1: no options = the defaults (Deep, web) at 25 credits; the tier is persisted.
    public async Task Answered_WithDefaults_ChargesTheDeepWebTier()
    {
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);
        _factory.Model.Decision = ReportDiscussionDisposition.Answered;
        _factory.Model.Analysis = prompt => Echo(prompt, "نسبت بدهی");

        var answer = await Post(client, "نسبت بدهی این شرکت را تحلیل کن", conversation);

        Assert.Equal("ReportDiscussion", Intent(answer));
        Assert.Equal("Answered", Outcome(answer));
        Assert.Equal(25m, Credits(answer));
        Assert.Equal(new AnalysisChoice(AnalysisDepth.Deep, true), _factory.Model.Choices.Single());
        Assert.Equal(1, _factory.Model.Decisions);
        Assert.Equal("DeepWeb", Payload(answer).GroundedAnalysisRun!.Tier);
        Assert.True(answer.GetProperty("groundedAnalysis").GetProperty("available").GetBoolean());
    }

    [Fact] // Change «local-company-data-context» T22 (FR-1, FR-5, AC-1): the prompt carries the company data pack.
    public async Task Answered_SendsTheCompanyDataPack_WithTheShownReportAsPrimary()
    {
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);
        _factory.Model.Decision = ReportDiscussionDisposition.Answered;
        _factory.Model.Analysis = prompt => Echo(prompt, "نسبت بدهی");

        var answer = await Post(client, "نسبت بدهی این شرکت را تحلیل کن", conversation);

        Assert.Equal("Answered", Outcome(answer));
        Assert.Equal(25m, Credits(answer));
        var prompt = _factory.Model.Prompts.Single();
        Assert.DoesNotContain("<report>", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<report>", prompt.UserContent, StringComparison.Ordinal);
        Assert.Contains("<company_data>", prompt.UserContent, StringComparison.Ordinal);
        var primary = prompt.UserContent.Split('\n').Single(line => line.StartsWith("<section kind=\"BalanceSheet\"", StringComparison.Ordinal));
        Assert.Contains("primary=\"true\"", primary, StringComparison.Ordinal);

        // Every section is either sent or named as not available, never silently missing (FR-4).
        var facts = prompt.DataFacts!;
        Assert.Equal("BalanceSheet", facts.SectionsSent[0]);
        foreach (var kind in Enum.GetNames<CompanyDataSectionKind>())
            Assert.True(facts.SectionsSent.Contains(kind) ^ facts.NotAvailable.Any(gap => gap.Kind.ToString() == kind), kind);
        Assert.Equal(facts.SectionsSent, Payload(answer).GroundedAnalysisRun!.SectionsUsed);
    }

    [Fact] // Amendment A2: a balance sheet is never scored, so a reason the model sends anyway is not shown.
    public async Task BalanceSheet_ShowsNoScoreReason()
    {
        const string reason = "ساختار سرمایه متعادل است.";
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);
        _factory.Model.Decision = ReportDiscussionDisposition.Answered;
        _factory.Model.Analysis = prompt => Echo(prompt, "نسبت بدهی") with { ReportScore = 70, ScoreReason = reason };

        var answer = await Post(client, "نسبت بدهی این شرکت را تحلیل کن", conversation);

        Assert.Equal("Answered", Outcome(answer));
        var text = Text(answer);
        Assert.Contains("نسبت بدهی", text, StringComparison.Ordinal);
        Assert.DoesNotContain(reason, text, StringComparison.Ordinal);
        Assert.DoesNotContain("امتیاز گزارش", text, StringComparison.Ordinal);
    }

    [Theory] // AC-1, AC-2: the tier follows depth and web switch only; web on with no search is the web tier.
    [InlineData("simple", false, 8)]
    [InlineData("simple", true, 12)]
    [InlineData("deep", false, 16)]
    public async Task Answered_ChargesTheChosenTier(string depth, bool web, int credits)
    {
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);
        _factory.Model.Analysis = prompt => Echo(prompt, "نسبت بدهی");

        var answer = await Post(client, "نسبت بدهی را تحلیل کن", conversation, new { depth, webSearch = web });

        Assert.Equal((decimal)credits, Credits(answer));
        Assert.Equal(web, _factory.Model.Choices.Single().WebSearch);
        Assert.Equal(web, _factory.Model.Prompts.Single().SystemPrompt.Contains(GroundedAnalysis.WebRules, StringComparison.Ordinal));
    }

    [Theory] // AC-7, PO-14: a hand-back runs only the routing step; no analysis call and no web search.
    [InlineData(ReportDiscussionDisposition.OutsideReport)]
    [InlineData(ReportDiscussionDisposition.NeedsDifferentData)]
    public async Task HandBack_NeverReachesTheAnalysisModel(ReportDiscussionDisposition decision)
    {
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);
        _factory.Model.Decision = decision;

        var answer = await Post(client, "ترازنامه وبملت", conversation);

        Assert.NotEqual("ReportDiscussion", Intent(answer));
        Assert.Equal(1, _factory.Model.Decisions);
        Assert.Empty(_factory.Model.Choices);
    }

    [Fact] // AC-5, PO-9: a page that showed other prices gets the current list, at zero credits, with no model call.
    public async Task StalePricingVersion_IsFree_AndRunsNothing()
    {
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);

        var answer = await Post(client, "نسبت بدهی را تحلیل کن", conversation,
            new { depth = "deep", webSearch = true, pricingVersion = "000000000000" });

        Assert.Equal("ReportDiscussion", Intent(answer));
        Assert.Equal(0m, Credits(answer));
        Assert.Contains("تعرفه", Text(answer), StringComparison.Ordinal);
        Assert.Contains("۲۵ اعتبار", Text(answer), StringComparison.Ordinal);
        Assert.Equal(0, _factory.Model.Decisions);
        Assert.Empty(_factory.Model.Choices);
        Assert.NotEqual("000000000000", answer.GetProperty("groundedAnalysis").GetProperty("pricingVersion").GetString());
    }

    [Fact] // The current version is accepted.
    public async Task CurrentPricingVersion_Runs()
    {
        using var client = Client();
        var report = await Post(client, "ترازنامه شگل");
        var version = report.GetProperty("groundedAnalysis").GetProperty("pricingVersion").GetString();
        _factory.Model.Analysis = prompt => Echo(prompt, "نسبت بدهی");

        var answer = await Post(client, "نسبت بدهی را تحلیل کن", report.GetProperty("conversationId").GetGuid(),
            new { depth = "simple", webSearch = false, pricingVersion = version });

        Assert.Equal(8m, Credits(answer));
    }

    [Fact] // AC-6: MissingInReport from the routing step is free and never runs the analysis.
    public async Task MissingFromTheRoutingStep_IsFree()
    {
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);
        _factory.Model.Decision = ReportDiscussionDisposition.MissingInReport;

        var answer = await Post(client, "هزینه تحقیق و توسعه؟", conversation);

        Assert.Equal(0m, Credits(answer));
        Assert.Equal("NoData", Outcome(answer));
        Assert.Empty(_factory.Model.Choices);
    }

    [Fact] // AC-6: provider failure, timeout or truncation of the analysis is free.
    public async Task AnalysisFailure_IsFree()
    {
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);
        _factory.Model.Analysis = _ => throw new ReportDiscussionModelUnavailableException("InvalidStructuredOutput:ModelOutputTruncated");

        var answer = await Post(client, "نسبت بدهی را تحلیل کن", conversation);

        Assert.Equal("TemporarilyUnavailable", Outcome(answer));
        Assert.Equal(0m, Credits(answer));
    }

    [Fact] // AC-6, AC-14: an unmarked number that is not in the report fails grounding and is free.
    public async Task UngroundedNumber_IsFree()
    {
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);
        _factory.Model.Analysis = prompt => Echo(prompt, "نسبت بدهی", "\n\n- **تورم:** تورم ۴۵ درصد بود.");

        var answer = await Post(client, "نسبت بدهی را تحلیل کن", conversation);

        Assert.Equal("NoData", Outcome(answer));
        Assert.Equal(0m, Credits(answer));
    }

    [Fact] // FR-10: a consulted, dated source with a matching value is kept; the server writes the source list.
    public async Task ExternalNumber_WithAValidSource_IsKeptAndListed()
    {
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);
        const string url = "https://codal.ir/Reports/Decision.aspx?LetterSerial=x";
        _factory.Model.Analysis = prompt => Echo(prompt, "نسبت بدهی", "\n\n- **نرخ بهره:** نرخ سود بین‌بانکی ۲۳.۵ درصد شد [W1].",
            [new DeclaredExternalSource("W1", url, "گزارش بانک مرکزی", "1404/08/01", "نرخ بین‌بانکی", 23.5m, "درصد")],
            [url + "&utm_source=openai"]);

        var answer = await Post(client, "نسبت بدهی را تحلیل کن", conversation);

        Assert.Equal("Answered", Outcome(answer));
        Assert.Equal(25m, Credits(answer));
        Assert.Contains("**منابع**", Text(answer), StringComparison.Ordinal);
        Assert.Equal("W1", answer.GetProperty("sources")[0].GetProperty("id").GetString());
        Assert.Equal(url, Payload(answer).ExternalSources!.Single().Url);
    }

    [Fact] // FR-10: an unconsulted source removes its item; the rest of the answer stands.
    public async Task ExternalNumber_WithAnUnconsultedSource_IsRemoved()
    {
        using var client = Client();
        var conversation = await ShowBalanceSheetAsync(client);
        _factory.Model.Analysis = prompt => Echo(prompt, "نسبت بدهی", "\n\n- **نرخ بهره:** نرخ سود بین‌بانکی ۲۳.۵ درصد شد [W1].",
            [new DeclaredExternalSource("W1", "https://example.com/x", "خبر", "1404/08/01", "نرخ", 23.5m, "درصد")],
            ["https://codal.ir/other"]);

        var answer = await Post(client, "نسبت بدهی را تحلیل کن", conversation);

        Assert.Equal("Answered", Outcome(answer));
        Assert.DoesNotContain("بین‌بانکی", Text(answer), StringComparison.Ordinal);
        Assert.False(answer.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array);
    }
}

public sealed class GroundedAnalysis162ApiFactory : BalanceSheetReport154ApiFactory
{
    public ScriptedReportDiscussionAnalysisModel Model { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReportDiscussion:Enabled"] = "true",
                ["GroundedAnalysis:Enabled"] = "true"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReportDiscussionAnalysisModel>();
            services.AddSingleton<IReportDiscussionAnalysisModel>(Model);
            services.RemoveAll<IReportDiscussionModel>();
            services.AddSingleton<IReportDiscussionModel>(new ScriptedReportDiscussionModel());
        });
    }
}

public sealed class ScriptedReportDiscussionAnalysisModel : IReportDiscussionAnalysisModel
{
    private readonly object _gate = new();
    private readonly List<ReportDiscussionPrompt> _prompts = [];
    private readonly List<AnalysisChoice> _choices = [];
    private int _decisions;

    public ReportDiscussionDisposition Decision { get; set; } = ReportDiscussionDisposition.Answered;

    public Func<ReportDiscussionPrompt, ReportDiscussionAnalysisReply> Analysis { get; set; } = Unexpected;

    public int Decisions { get { lock (_gate) return _decisions; } }

    public IReadOnlyList<AnalysisChoice> Choices { get { lock (_gate) return _choices.ToArray(); } }

    public IReadOnlyList<ReportDiscussionPrompt> Prompts { get { lock (_gate) return _prompts.ToArray(); } }

    public void Reset()
    {
        lock (_gate)
        {
            _prompts.Clear();
            _choices.Clear();
            _decisions = 0;
        }
        Decision = ReportDiscussionDisposition.Answered;
        Analysis = Unexpected;
    }

    public Task<ReportDiscussionDisposition> DecideAsync(ReportDiscussionPrompt prompt, Guid tenantId, string correlationId, CancellationToken cancellationToken)
    {
        lock (_gate) _decisions++;
        return Task.FromResult(Decision);
    }

    public Task<ReportDiscussionAnalysisReply> AnalyzeAsync(ReportDiscussionPrompt prompt, AnalysisChoice choice, Guid tenantId, string correlationId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _prompts.Add(prompt);
            _choices.Add(choice);
        }
        return Task.FromResult(Analysis(prompt));
    }

    private static ReportDiscussionAnalysisReply Unexpected(ReportDiscussionPrompt prompt) =>
        throw new InvalidOperationException("The analysis model was not expected to be called.");
}
