using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
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
/// Feature 163 — Symbol-Grounded Fallback through the real V2 endpoint, workflow, billing and persistence, with
/// Feature 154's seeded quarterly income statements. Both models (fallback and Report Discussion) are scripted.
/// </summary>
public sealed class SymbolGroundedFallback163EndpointTests : IClassFixture<SymbolGroundedFallback163ApiFactory>
{
    private const string AdviceQuestion = "وبملت ارزش خرید دارد؟";
    // The base factory turns the cash-flow report on, so the cash-flow offer is part of the list (FR-7).
    private const string Closing = "می‌خواهید روند فروش ماهانه، صورت وضعیت مالی یا صورت جریان وجوه نقد وبملت را هم بررسی کنم؟";

    private readonly SymbolGroundedFallback163ApiFactory _factory;

    public SymbolGroundedFallback163EndpointTests(SymbolGroundedFallback163ApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.Fallback.Reset();
        factory.Discussion.Reset();
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        return client;
    }

    private static async Task<JsonElement> Post(HttpClient client, string message, Guid? conversationId = null)
    {
        object body = conversationId is null ? new { message } : new { message, conversationId };
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", body, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string Intent(JsonElement root) => root.GetProperty("intent").GetString() ?? string.Empty;
    private static string Text(JsonElement root) => root.GetProperty("textAnswer").GetString() ?? string.Empty;
    private static string Outcome(JsonElement root) => root.GetProperty("outcome").GetString() ?? string.Empty;
    private static decimal Credits(JsonElement root) =>
        root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            ? usage.GetProperty("creditsCharged").GetDecimal()
            : 0m;

    private AssistantMessagePayload Payload(JsonElement answer)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConversationDbContext>();
        var id = answer.GetProperty("assistantMessageId").GetGuid();
        var json = db.Messages.AsNoTracking().Single(message => message.Id == id).AssistantPayloadJson;
        return JsonSerializer.Deserialize<AssistantMessagePayload>(json!)!;
    }

    private static SymbolGroundedFallbackModelReply EchoFirstFact(SymbolGroundedFallbackPrompt prompt, string closing = Closing)
    {
        var fact = prompt.Data.Facts[0];
        return new SymbolGroundedFallbackModelReply(SymbolGroundedFallbackDisposition.Answered,
            $"بر اساس صورت سود و زیان فصلی، {fact.Label} در {fact.Period} برابر {fact.Value.ToString("0.##", CultureInfo.InvariantCulture)} است. این پاسخ ارزش‌گذاری یا توصیه سرمایه‌گذاری نیست.",
            closing, [fact.EvidenceId]);
    }

    [Fact] // AC-2, AC-3, AC-5: an investment-advice question is answered from the hidden income statement and charged 2 credits.
    public async Task UnsupportedQuestionWithOneSymbol_IsAnsweredFromTheHiddenIncomeStatement()
    {
        using var client = Client();
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt);

        var answer = await Post(client, AdviceQuestion);

        Assert.Equal("ReportDiscussion", Intent(answer));
        Assert.Equal("Answered", Outcome(answer));
        Assert.StartsWith("بر اساس صورت سود و زیان فصلی", Text(answer), StringComparison.Ordinal);
        Assert.EndsWith(Closing, Text(answer), StringComparison.Ordinal);
        Assert.Equal(2m, Credits(answer));
        Assert.Equal(0, answer.GetProperty("suggestedActions").GetArrayLength());
        // The statement is not shown.
        Assert.True(!answer.TryGetProperty("financialStatementTableResult", out var shown) || shown.ValueKind == JsonValueKind.Null);

        var prompt = _factory.Fallback.Prompts.Single();
        Assert.Equal(ReportFamily.QuarterlyIncomeStatement, prompt.Data.Family);
        Assert.Equal("وبملت", prompt.Data.CompanySymbol);
        Assert.True(prompt.EstimatedInputTokens <= 5000);
        Assert.Contains($"<question>\n{AdviceQuestion}\n</question>", prompt.UserContent, StringComparison.Ordinal);
        Assert.Contains("<offers>\nروند فروش ماهانه وبملت\nصورت وضعیت مالی وبملت\nصورت جریان وجوه نقد وبملت\n</offers>", prompt.UserContent, StringComparison.Ordinal);

        var payload = Payload(answer);
        Assert.NotNull(payload.GroundingReport?.QuarterlyResult);
        Assert.Null(payload.GroundingReport!.RenderedAnswer);
        Assert.Null(payload.FinancialStatementTableResult);
        Assert.Equal(DialogueOutcome.Unsupported, payload.FallbackOf!.Outcome);
        Assert.Equal(UnavailableTopicCatalog.InvestmentAdviceReasonCode, payload.FallbackOf.ReasonCode);
    }

    [Fact] // AC-3: the hidden statement is the conversation's active report for the next free-text turn.
    public async Task NextQuestion_IsAnsweredByReportDiscussionFromTheHiddenStatement()
    {
        using var client = Client();
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt);
        var first = await Post(client, AdviceQuestion);
        var conversation = first.GetProperty("conversationId").GetGuid();

        _factory.Discussion.Reply = prompt => new ReportDiscussionModelReply(ReportDiscussionDisposition.Answered,
            $"{prompt.Data.Facts[0].Label} در {prompt.Data.Facts[0].Period} برابر {prompt.Data.Facts[0].Value.ToString("0.##", CultureInfo.InvariantCulture)} است.",
            [prompt.Data.Facts[0].EvidenceId]);
        var next = await Post(client, "این عدد را بیشتر توضیح بده", conversation);

        Assert.Equal("ReportDiscussion", Intent(next));
        Assert.Equal("Answered", Outcome(next));
        Assert.Equal(1, _factory.Fallback.Calls);
        var discussion = _factory.Discussion.Prompts.Single();
        Assert.Equal(ReportFamily.QuarterlyIncomeStatement, discussion.Data.Family);
        Assert.Equal("وبملت", discussion.Data.CompanySymbol);
        Assert.Equal(first.GetProperty("assistantMessageId").GetGuid(), Payload(next).ReportDiscussion!.SourceAssistantMessageId);
    }

    [Fact] // AC-5: a valuation question reaches the fallback too.
    public async Task ValuationQuestion_ReachesTheFallback()
    {
        using var client = Client();
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt);

        var answer = await Post(client, "ارزش ذاتی وبملت چقدر است؟");

        Assert.Equal("Answered", Outcome(answer));
        Assert.Equal(UnavailableTopicCatalog.ValuationReasonCode, Payload(answer).FallbackOf!.ReasonCode);
    }

    [Fact] // 162 change «analytical-answer-and-report-score» AC-7: the score goes before the closing offer, which stays last.
    public async Task OverallAnalysis_ShowsTheScoreBeforeTheClosingOffer()
    {
        using var client = Client();
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt) with { ReportScore = 64 };

        var answer = await Post(client, AdviceQuestion);
        var text = Text(answer);

        Assert.Equal("Answered", Outcome(answer));
        var score = text.IndexOf(": ۶۴ از ۱۰۰**", StringComparison.Ordinal);
        Assert.True(score > 0);
        Assert.True(score < text.IndexOf("توصیهٔ خرید یا فروش نیست", StringComparison.Ordinal));
        Assert.EndsWith(Closing, text, StringComparison.Ordinal);
        Assert.Contains(_factory.Fallback.Prompts.Single().Data.Facts[0].Unit, new[] { ReportDisplayUnits.Hemat, ReportDisplayUnits.BillionToman, "percent", "rial_per_share" });
    }

    [Fact] // FR-7: an empty closing offer is replaced by the fixed sentence.
    public async Task EmptyClosingOffer_UsesTheFixedSentence()
    {
        using var client = Client();
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt, closing: "");

        var answer = await Post(client, AdviceQuestion);

        Assert.EndsWith(Closing, Text(answer), StringComparison.Ordinal);
    }

    [Fact] // AC-4: not answerable keeps the non-answer, adds the income-statement offer, charges nothing, stores no report.
    public async Task NotAnswerable_KeepsTheNonAnswerAndOffersTheIncomeStatement()
    {
        using var client = Client();
        _factory.Fallback.Reply = _ => new SymbolGroundedFallbackModelReply(SymbolGroundedFallbackDisposition.NotAnswerable, "", "", []);

        var answer = await Post(client, AdviceQuestion);

        Assert.Equal("Unsupported", Outcome(answer));
        Assert.Equal(0m, Credits(answer));
        Assert.StartsWith(ChatCapabilityAvailability.InvestmentAdviceUnavailableMessageFa, Text(answer), StringComparison.Ordinal);
        Assert.EndsWith("می‌توانم صورت سود و زیان وبملت را تحلیل کنم.", Text(answer), StringComparison.Ordinal);
        var payload = Payload(answer);
        Assert.Null(payload.GroundingReport);
        Assert.Null(payload.FallbackOf);
        var chip = Assert.Single(answer.GetProperty("suggestedActions").EnumerateArray());
        Assert.Equal("گزارش فصلی وبملت", chip.GetProperty("message").GetString());
    }

    [Fact] // AC-5: the technical-analysis refusal stays and no model is called.
    public async Task TechnicalAnalysis_KeepsItsFixedRefusal()
    {
        using var client = Client();

        var answer = await Post(client, "تحلیل تکنیکال وبملت");

        Assert.Equal("Unsupported", Outcome(answer));
        Assert.Equal(ChatCapabilityAvailability.TechnicalAnalysisUnavailableMessageFa, Text(answer));
        Assert.Equal(0, _factory.Fallback.Calls);
    }

    [Theory] // AC-8: no symbol or two symbols leave the turn unchanged without a model call.
    [InlineData("این سهم ارزش خرید دارد؟")]
    [InlineData("وبملت یا شگل ارزش خرید دارد؟")]
    public async Task NoSingleCompany_LeavesTheTurnUnchanged(string question)
    {
        using var client = Client();

        var answer = await Post(client, question);

        Assert.NotEqual("Answered", Outcome(answer));
        Assert.Equal(0, _factory.Fallback.Calls);
        Assert.Null(Payload(answer).GroundingReport);
    }

    [Fact] // AC-10: a number that is not in the statement leaves the turn unchanged and free.
    public async Task UngroundedNumber_LeavesTheTurnUnchangedAndFree()
    {
        using var client = Client();
        _factory.Fallback.Reply = _ => new SymbolGroundedFallbackModelReply(
            SymbolGroundedFallbackDisposition.Answered, "سود خالص ۹۸۷٫۶۵ میلیون ریال است.", Closing, []);

        var answer = await Post(client, AdviceQuestion);

        Assert.Equal("Unsupported", Outcome(answer));
        Assert.Equal(ChatCapabilityAvailability.InvestmentAdviceUnavailableMessageFa, Text(answer));
        Assert.Equal(0m, Credits(answer));
        Assert.Equal(1, _factory.Fallback.Calls);
        Assert.Null(Payload(answer).GroundingReport);
    }

    [Fact] // AC-11: a provider failure leaves the turn unchanged and free.
    public async Task ProviderFailure_LeavesTheTurnUnchangedAndFree()
    {
        using var client = Client();
        _factory.Fallback.Reply = _ => throw new ReportDiscussionModelUnavailableException("timeout");

        var answer = await Post(client, AdviceQuestion);

        Assert.Equal("Unsupported", Outcome(answer));
        Assert.Equal(ChatCapabilityAvailability.InvestmentAdviceUnavailableMessageFa, Text(answer));
        Assert.Equal(0m, Credits(answer));
    }

    [Fact] // AC-7: an answered capability is never replaced.
    public async Task AnsweredCapability_IsNotTouched()
    {
        using var client = Client();

        var report = await Post(client, "صورت سود و زیان وبملت");

        Assert.Equal("Answered", Outcome(report));
        Assert.Equal(0, _factory.Fallback.Calls);
    }
}

public sealed class SymbolGroundedFallback163ApiFactory : BalanceSheetReport154ApiFactory
{
    public ScriptedSymbolGroundedFallbackModel Fallback { get; } = new();
    public ScriptedReportDiscussionModel Discussion { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReportDiscussion:Enabled"] = "true",
                ["SymbolGroundedFallback:Enabled"] = "true"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReportDiscussionModel>();
            services.AddSingleton<IReportDiscussionModel>(Discussion);
            services.RemoveAll<ISymbolGroundedFallbackModel>();
            services.AddSingleton<ISymbolGroundedFallbackModel>(Fallback);
        });
    }
}

public sealed class ScriptedSymbolGroundedFallbackModel : ISymbolGroundedFallbackModel
{
    private readonly object _gate = new();
    private readonly List<SymbolGroundedFallbackPrompt> _prompts = [];

    public Func<SymbolGroundedFallbackPrompt, SymbolGroundedFallbackModelReply> Reply { get; set; } = Unexpected;

    public int Calls { get { lock (_gate) return _prompts.Count; } }

    public IReadOnlyList<SymbolGroundedFallbackPrompt> Prompts { get { lock (_gate) return _prompts.ToArray(); } }

    public void Reset()
    {
        lock (_gate) _prompts.Clear();
        Reply = Unexpected;
    }

    public Task<SymbolGroundedFallbackModelReply> AnswerAsync(SymbolGroundedFallbackPrompt prompt, Guid tenantId, string correlationId, CancellationToken cancellationToken)
    {
        lock (_gate) _prompts.Add(prompt);
        return Task.FromResult(Reply(prompt));
    }

    private static SymbolGroundedFallbackModelReply Unexpected(SymbolGroundedFallbackPrompt prompt) =>
        throw new InvalidOperationException("The fallback model was not expected to be called.");
}
