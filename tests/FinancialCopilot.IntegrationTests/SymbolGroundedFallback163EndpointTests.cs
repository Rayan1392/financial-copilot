using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Infrastructure.Billing.Persistence;
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
/// Feature 154's seeded quarterly income statements. Change «sol-deep-analysis-web-search» (T09, T10): the fallback is an
/// offer; the analysis runs only after a <c>ga1:</c> confirmation, once, at the fixed tier. All models are scripted.
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

    private static async Task<JsonElement> Send(HttpClient client, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", body, CancellationToken.None);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"{response.StatusCode}: {content}");
        if (content.Length == 0) return default;
        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private static Task<JsonElement> Post(HttpClient client, string message, Guid? conversationId = null) =>
        Send(client, conversationId is null ? new { message } : new { message, conversationId });

    /// <summary>A click on an offer button: the action id, and the chosen options on confirm (no amount).</summary>
    private static Task<JsonElement> Act(
        HttpClient client, Guid conversationId, string actionId, object? options = null, Guid? idempotencyKey = null,
        HttpStatusCode expected = HttpStatusCode.OK) =>
        Send(client, new
        {
            message = "تحلیل کن",
            conversationId,
            suggestedActionId = actionId,
            analysisOptions = options,
            idempotencyKey
        }, expected);

    private static string Intent(JsonElement root) => root.GetProperty("intent").GetString() ?? string.Empty;
    private static string Text(JsonElement root) => root.GetProperty("textAnswer").GetString() ?? string.Empty;
    private static string Outcome(JsonElement root) => root.GetProperty("outcome").GetString() ?? string.Empty;
    private static decimal Credits(JsonElement root) =>
        root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            ? usage.GetProperty("creditsCharged").GetDecimal()
            : 0m;

    private static JsonElement? OfferOf(JsonElement root) =>
        root.TryGetProperty("groundedAnalysis", out var hint) && hint.ValueKind == JsonValueKind.Object &&
        hint.TryGetProperty("offer", out var offer) && offer.ValueKind == JsonValueKind.Object
            ? offer
            : null;

    private AssistantMessagePayload Payload(JsonElement answer)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConversationDbContext>();
        var id = answer.GetProperty("assistantMessageId").GetGuid();
        var json = db.Messages.AsNoTracking().Single(message => message.Id == id).AssistantPayloadJson;
        return JsonSerializer.Deserialize<AssistantMessagePayload>(json!)!;
    }

    /// <summary>Rewrites the stored offer message (simulates time passing or a price change since the offer).</summary>
    private void RewriteOfferMessage(JsonElement offerTurn, string oldText, string newText)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConversationDbContext>();
        var id = offerTurn.GetProperty("assistantMessageId").GetGuid();
        var row = db.Messages.Single(message => message.Id == id);
        Assert.Contains(oldText, row.AssistantPayloadJson!, StringComparison.Ordinal);
        row.AssistantPayloadJson = row.AssistantPayloadJson!.Replace(oldText, newText, StringComparison.Ordinal);
        db.SaveChanges();
    }

    /// <summary>The ledger entries that charged credits on this offer's derived key (FR-8: at most one).</summary>
    private int ChargesOf(Guid offerId)
    {
        var operationId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{offerId:N}:grounded_analysis_offer")).AsSpan(0, 16));
        return _factory.ReadUsageEntries().Count(entry =>
            entry.IdempotencyKey.StartsWith(operationId.ToString("N"), StringComparison.Ordinal) && entry.CreditsCharged > 0);
    }

    private static SymbolGroundedFallbackModelReply EchoFirstFact(SymbolGroundedFallbackPrompt prompt, string closing = Closing)
    {
        var fact = prompt.Data.Facts[0];
        return new SymbolGroundedFallbackModelReply(SymbolGroundedFallbackDisposition.Answered,
            $"بر اساس صورت سود و زیان فصلی، {fact.Label} در {fact.Period} برابر {fact.Value.ToString("0.##", CultureInfo.InvariantCulture)} است. این پاسخ ارزش‌گذاری یا توصیه سرمایه‌گذاری نیست.",
            closing, [fact.EvidenceId]);
    }

    private async Task<(Guid Conversation, JsonElement Turn, Guid OfferId, string Confirm, string Cancel)> OfferAsync(
        HttpClient client, string question = AdviceQuestion)
    {
        var turn = await Post(client, question);
        var offer = OfferOf(turn) ?? throw new Xunit.Sdk.XunitException($"No analysis offer: {turn}");
        return (turn.GetProperty("conversationId").GetGuid(), turn, offer.GetProperty("offerId").GetGuid(),
            offer.GetProperty("confirmActionId").GetString()!, offer.GetProperty("cancelActionId").GetString()!);
    }

    // ── Offer (T09, FR-7, AC-8) ─────────────────────────────────────────────────────────

    [Fact] // AC-8: the non-answer stays, the offer is added; no model call, no reservation, no charge.
    public async Task UnsupportedQuestionWithOneSymbol_GetsAnOffer_WithoutAModelCall()
    {
        using var client = Client();

        var turn = await Post(client, AdviceQuestion);

        Assert.Equal("Unsupported", Outcome(turn));
        Assert.Equal(0m, Credits(turn));
        Assert.Equal(0, _factory.Fallback.Calls);
        Assert.StartsWith(ChatCapabilityAvailability.InvestmentAdviceUnavailableMessageFa, Text(turn), StringComparison.Ordinal);
        Assert.Contains("صورت سود و زیان فصلی وبملت", Text(turn), StringComparison.Ordinal);
        Assert.Contains("عمیق با وب ۲۵ اعتبار", Text(turn), StringComparison.Ordinal);
        Assert.Contains("ساده بدون وب ۸ اعتبار", Text(turn), StringComparison.Ordinal);

        var hint = turn.GetProperty("groundedAnalysis");
        Assert.False(hint.GetProperty("available").GetBoolean());
        Assert.Equal(4, hint.GetProperty("prices").GetArrayLength());
        var offer = OfferOf(turn)!.Value;
        var offerId = offer.GetProperty("offerId").GetGuid();
        Assert.Equal("وبملت", offer.GetProperty("symbol").GetString());
        Assert.True(offer.GetProperty("sufficientBalance").GetBoolean());
        Assert.Equal(GroundedAnalysisOfferActions.Confirm(offerId), offer.GetProperty("confirmActionId").GetString());
        Assert.Equal(GroundedAnalysisOfferActions.Cancel(offerId), offer.GetProperty("cancelActionId").GetString());

        var payload = Payload(turn);
        Assert.Equal(offerId, payload.GroundedAnalysisOffer!.OfferId);
        Assert.Equal(_factory.Services.GetRequiredService<GroundedAnalysisPricing>().PricingVersion, payload.GroundedAnalysisOffer.PricingVersion);
        Assert.Null(payload.GroundingReport);
        Assert.Null(payload.FallbackOf);
        Assert.Equal(0, ChargesOf(offerId));
    }

    [Fact] // AC-8, AC-19: typed text is never a confirmation.
    public async Task TypedYes_NeverConfirms()
    {
        using var client = Client();
        var (conversation, _, offerId, _, _) = await OfferAsync(client);

        await Post(client, "بله", conversation);

        Assert.Equal(0, _factory.Fallback.Calls);
        Assert.Equal(0, ChargesOf(offerId));
    }

    [Fact] // AC-5 (163): a valuation question gets the offer too.
    public async Task ValuationQuestion_GetsTheOffer()
    {
        using var client = Client();

        var turn = await Post(client, "ارزش ذاتی وبملت چقدر است؟");

        Assert.NotNull(OfferOf(turn));
        Assert.Equal(0, _factory.Fallback.Calls);
    }

    [Fact] // AC-16: the technical-analysis refusal stays; no offer, no model call.
    public async Task TechnicalAnalysis_KeepsItsFixedRefusal()
    {
        using var client = Client();

        var answer = await Post(client, "تحلیل تکنیکال وبملت");

        Assert.Equal("Unsupported", Outcome(answer));
        Assert.Equal(ChatCapabilityAvailability.TechnicalAnalysisUnavailableMessageFa, Text(answer));
        Assert.Null(OfferOf(answer));
        Assert.Equal(0, _factory.Fallback.Calls);
    }

    [Theory] // FR-2: no symbol or two symbols get no offer.
    [InlineData("این سهم ارزش خرید دارد؟")]
    [InlineData("وبملت یا شگل ارزش خرید دارد؟")]
    public async Task NoSingleCompany_GetsNoOffer(string question)
    {
        using var client = Client();

        var answer = await Post(client, question);

        Assert.NotEqual("Answered", Outcome(answer));
        Assert.Null(OfferOf(answer));
        Assert.Null(Payload(answer).GroundedAnalysisOffer);
    }

    [Fact] // AC-16: an answered capability is never replaced.
    public async Task AnsweredCapability_IsNotTouched()
    {
        using var client = Client();

        var report = await Post(client, "صورت سود و زیان وبملت");

        Assert.Equal("Answered", Outcome(report));
        Assert.Null(OfferOf(report));
        Assert.Equal(0, _factory.Fallback.Calls);
    }

    // ── Confirm (T10, FR-8, FR-9) ──────────────────────────────────────────────────────

    [Fact] // AC-1, FR-14: the default confirmation runs Deep with web once and charges 25; the hidden statement is stored.
    public async Task Confirm_RunsTheAnalysisOnce_AtTheDefaultTier()
    {
        using var client = Client();
        var (conversation, offerTurn, offerId, confirm, _) = await OfferAsync(client);
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt);

        var answer = await Act(client, conversation, confirm);

        Assert.Equal("ReportDiscussion", Intent(answer));
        Assert.Equal("Answered", Outcome(answer));
        Assert.StartsWith("بر اساس صورت سود و زیان فصلی", Text(answer), StringComparison.Ordinal);
        Assert.EndsWith(Closing, Text(answer), StringComparison.Ordinal);
        Assert.Equal(25m, Credits(answer));
        Assert.Equal(1, ChargesOf(offerId));
        Assert.Equal(new AnalysisChoice(AnalysisDepth.Deep, true), _factory.Fallback.Choices.Single());
        Assert.True(!answer.TryGetProperty("financialStatementTableResult", out var shown) || shown.ValueKind == JsonValueKind.Null);
        Assert.True(answer.GetProperty("groundedAnalysis").GetProperty("available").GetBoolean());

        var prompt = _factory.Fallback.Prompts.Single();
        Assert.Equal(ReportFamily.QuarterlyIncomeStatement, prompt.Data.Family);
        Assert.Equal("وبملت", prompt.Data.CompanySymbol);
        Assert.True(prompt.EstimatedInputTokens <= 10000);
        // The question is the user message of the offer's exchange, not the button text.
        Assert.Contains($"<question>\n{AdviceQuestion}\n</question>", prompt.UserContent, StringComparison.Ordinal);
        Assert.Contains("<offers>\nروند فروش ماهانه وبملت\nصورت وضعیت مالی وبملت\nصورت جریان وجوه نقد وبملت\n</offers>", prompt.UserContent, StringComparison.Ordinal);

        var payload = Payload(answer);
        Assert.NotNull(payload.GroundingReport?.QuarterlyResult);
        Assert.Null(payload.GroundingReport!.RenderedAnswer);
        Assert.Null(payload.FinancialStatementTableResult);
        Assert.Equal(DialogueOutcome.Unsupported, payload.FallbackOf!.Outcome);
        Assert.Equal(UnavailableTopicCatalog.InvestmentAdviceReasonCode, payload.FallbackOf.ReasonCode);
        Assert.Equal(offerId, payload.FallbackOf.ConfirmedOfferId);
        Assert.Equal("DeepWeb", payload.GroundedAnalysisRun!.Tier);
        Assert.Equal(Payload(offerTurn).GroundedAnalysisOffer!.OfferId, offerId);
    }

    [Theory] // AC-1: the chosen tier sets the amount; nothing else does.
    [InlineData("simple", false, 8)]
    [InlineData("simple", true, 12)]
    [InlineData("deep", false, 16)]
    public async Task Confirm_ChargesTheChosenTier(string depth, bool webSearch, int credits)
    {
        using var client = Client();
        var (conversation, _, _, confirm, _) = await OfferAsync(client);
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt);

        var answer = await Act(client, conversation, confirm, new { depth, webSearch });

        Assert.Equal((decimal)credits, Credits(answer));
        Assert.Equal(webSearch, _factory.Fallback.Choices.Single().WebSearch);
    }

    [Fact] // AC-15: after a confirmed answer the next question is answered by 162 from the hidden statement.
    public async Task NextQuestion_IsAnsweredByReportDiscussionFromTheHiddenStatement()
    {
        using var client = Client();
        var (conversation, _, _, confirm, _) = await OfferAsync(client);
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt);
        var answer = await Act(client, conversation, confirm);

        _factory.Discussion.Analysis = prompt => new ReportDiscussionAnalysisReply(ReportDiscussionDisposition.Answered,
            $"**{prompt.Data.Facts[0].Label} در {prompt.Data.Facts[0].Period} برابر {prompt.Data.Facts[0].Value.ToString("0.##", CultureInfo.InvariantCulture)} است.**",
            [prompt.Data.Facts[0].EvidenceId], null, [], []);
        var next = await Post(client, "این عدد را بیشتر توضیح بده", conversation);

        Assert.Equal("ReportDiscussion", Intent(next));
        Assert.Equal("Answered", Outcome(next));
        Assert.Equal(1, _factory.Fallback.Calls);
        var discussion = _factory.Discussion.Prompts.Single();
        Assert.Equal(ReportFamily.QuarterlyIncomeStatement, discussion.Data.Family);
        Assert.Equal("وبملت", discussion.Data.CompanySymbol);
        Assert.Equal(answer.GetProperty("assistantMessageId").GetGuid(), Payload(next).ReportDiscussion!.SourceAssistantMessageId);
    }

    [Fact] // 162 change «analytical-answer-and-report-score» AC-7: the score goes before the closing offer, which stays last.
    public async Task OverallAnalysis_ShowsTheScoreBeforeTheClosingOffer()
    {
        using var client = Client();
        var (conversation, _, _, confirm, _) = await OfferAsync(client);
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt) with { ReportScore = 64 };

        var text = Text(await Act(client, conversation, confirm));

        var score = text.IndexOf(": ۶۴ از ۱۰۰**", StringComparison.Ordinal);
        Assert.True(score > 0);
        Assert.True(score < text.IndexOf("توصیهٔ خرید یا فروش نیست", StringComparison.Ordinal));
        Assert.EndsWith(Closing, text, StringComparison.Ordinal);
    }

    [Fact] // Amendment A2 (owner, 2026-11-20): the one-sentence reason sits right after the score line, before the disclaimer.
    public async Task OverallAnalysis_ShowsTheScoreReason_RightAfterTheScore()
    {
        const string reason = "سودآوری عملیاتی از نقاط قوت گزارش است؛ در مقابل، اتکا به درآمدهای غیرعملیاتی امتیاز را تعدیل کرده است.";
        using var client = Client();
        var (conversation, _, _, confirm, _) = await OfferAsync(client);
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt) with { ReportScore = 64, ScoreReason = reason };

        var text = Text(await Act(client, conversation, confirm));

        Assert.Contains($": ۶۴ از ۱۰۰**\n\n{reason}\n\nاین امتیاز ارزیابی", text, StringComparison.Ordinal);
        Assert.EndsWith(Closing, text, StringComparison.Ordinal);
    }

    [Fact] // FR-7: an empty closing offer is replaced by the fixed sentence.
    public async Task EmptyClosingOffer_UsesTheFixedSentence()
    {
        using var client = Client();
        var (conversation, _, _, confirm, _) = await OfferAsync(client);
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt, closing: "");

        Assert.EndsWith(Closing, Text(await Act(client, conversation, confirm)), StringComparison.Ordinal);
    }

    [Fact] // FR-9: not answerable is free and offers the income statement itself.
    public async Task NotAnswerable_IsFree_AndOffersTheIncomeStatement()
    {
        using var client = Client();
        var (conversation, _, offerId, confirm, _) = await OfferAsync(client);
        _factory.Fallback.Reply = _ => new SymbolGroundedFallbackModelReply(SymbolGroundedFallbackDisposition.NotAnswerable, "", "", []);

        var answer = await Act(client, conversation, confirm);

        Assert.Equal("NoData", Outcome(answer));
        Assert.Equal(0m, Credits(answer));
        Assert.Equal(0, ChargesOf(offerId));
        Assert.Equal("می‌توانم صورت سود و زیان وبملت را تحلیل کنم.", Text(answer));
        Assert.Null(Payload(answer).GroundingReport);
        var chip = Assert.Single(answer.GetProperty("suggestedActions").EnumerateArray());
        Assert.Equal("گزارش فصلی وبملت", chip.GetProperty("message").GetString());
    }

    [Fact] // AC-6, FR-9: an ungrounded number is free and returns a fresh offer.
    public async Task UngroundedNumber_IsFree_AndReturnsAFreshOffer()
    {
        using var client = Client();
        var (conversation, _, offerId, confirm, _) = await OfferAsync(client);
        _factory.Fallback.Reply = _ => new SymbolGroundedFallbackModelReply(
            SymbolGroundedFallbackDisposition.Answered, "سود خالص ۹۸۷٫۶۵ میلیون ریال است.", Closing, []);

        var answer = await Act(client, conversation, confirm);

        Assert.Equal("NoData", Outcome(answer));
        Assert.Equal(0m, Credits(answer));
        Assert.Equal(0, ChargesOf(offerId));
        Assert.StartsWith(SymbolGroundedFallbackMessages.AnalysisFailed, Text(answer), StringComparison.Ordinal);
        var fresh = OfferOf(answer)!.Value.GetProperty("offerId").GetGuid();
        Assert.NotEqual(offerId, fresh);
        Assert.Null(Payload(answer).GroundingReport);
    }

    [Fact] // AC-6: a provider failure is free and returns a fresh offer that can be confirmed.
    public async Task ProviderFailure_IsFree_AndTheFreshOfferCanBeConfirmed()
    {
        using var client = Client();
        var (conversation, _, offerId, confirm, _) = await OfferAsync(client);
        _factory.Fallback.Reply = _ => throw new ReportDiscussionModelUnavailableException("timeout");

        var failed = await Act(client, conversation, confirm);

        Assert.Equal("TemporarilyUnavailable", Outcome(failed));
        Assert.Equal(0m, Credits(failed));
        Assert.Equal(0, ChargesOf(offerId));
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt);
        var retry = await Act(client, conversation, OfferOf(failed)!.Value.GetProperty("confirmActionId").GetString()!);
        Assert.Equal("Answered", Outcome(retry));
        Assert.Equal(25m, Credits(retry));
    }

    // ── One outcome per offer (AC-9, AC-10) ─────────────────────────────────────────────

    [Fact] // AC-9: cancel is free and makes a later confirm fail without a model call.
    public async Task Cancel_IsFree_AndALaterConfirmDoesNotRun()
    {
        using var client = Client();
        var (conversation, _, offerId, confirm, cancel) = await OfferAsync(client);

        var cancelled = await Act(client, conversation, cancel);
        Assert.Equal(SymbolGroundedFallbackMessages.OfferCancelled, Text(cancelled));
        Assert.Equal(0m, Credits(cancelled));

        var late = await Act(client, conversation, confirm);
        Assert.Equal(SymbolGroundedFallbackMessages.OfferAlreadyResolved, Text(late));
        Assert.Equal(0m, Credits(late));
        Assert.Equal(0, _factory.Fallback.Calls);
        Assert.Equal(0, ChargesOf(offerId));
    }

    [Fact] // AC-9: cancel after confirm changes nothing; a repeated confirm does not run again.
    public async Task CancelOrRepeat_AfterConfirm_DoesNotRunOrChargeAgain()
    {
        using var client = Client();
        var (conversation, _, offerId, confirm, cancel) = await OfferAsync(client);
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt);
        await Act(client, conversation, confirm);

        var cancelled = await Act(client, conversation, cancel);
        var repeated = await Act(client, conversation, confirm);

        Assert.Equal(SymbolGroundedFallbackMessages.OfferAlreadyResolved, Text(cancelled));
        Assert.Equal(SymbolGroundedFallbackMessages.OfferAlreadyResolved, Text(repeated));
        Assert.Equal(0m, Credits(repeated));
        Assert.Equal(1, _factory.Fallback.Calls);
        Assert.Equal(1, ChargesOf(offerId));
    }

    [Fact] // AC-9, AC-11: a replay with the same idempotency key never runs the model twice or charges twice.
    public async Task Replay_WithTheSameIdempotencyKey_RunsOnce()
    {
        using var client = Client();
        var (conversation, _, offerId, confirm, _) = await OfferAsync(client);
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt);
        var key = Guid.NewGuid();

        var first = await Act(client, conversation, confirm, idempotencyKey: key);
        var replay = await Act(client, conversation, confirm, idempotencyKey: key);

        Assert.Equal("Answered", Outcome(first));
        Assert.Equal(SymbolGroundedFallbackMessages.OfferAlreadyResolved, Text(replay));
        Assert.Equal(1, _factory.Fallback.Calls);
        Assert.Equal(1, ChargesOf(offerId));
    }

    [Fact] // AC-9: two concurrent confirms run the model once and charge once.
    public async Task ConcurrentConfirms_RunOnce_AndChargeOnce()
    {
        using var client = Client();
        var (conversation, _, offerId, confirm, _) = await OfferAsync(client);
        _factory.Fallback.Delay = TimeSpan.FromMilliseconds(300);
        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt);

        using var second = Client();
        var results = await Task.WhenAll(Act(client, conversation, confirm), Act(second, conversation, confirm));

        Assert.Equal(1, _factory.Fallback.Calls);
        Assert.Equal(1, ChargesOf(offerId));
        Assert.Single(results, result => Outcome(result) == "Answered");
        var other = Assert.Single(results, result => Outcome(result) != "Answered");
        Assert.Contains(Text(other), new[] { SymbolGroundedFallbackMessages.OfferInProgress, SymbolGroundedFallbackMessages.OfferAlreadyResolved });
        Assert.Equal(0m, Credits(other));
    }

    private static async Task<JsonElement[]> AssistantMessagesAsync(HttpClient client, Guid conversationId)
    {
        using var response = await client.GetAsync($"/api/ai/v1/conversations/{conversationId}/messages", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("messages").EnumerateArray()
            .Where(message => message.GetProperty("role").GetString() == "Assistant")
            .Select(message => message.GetProperty("assistantContent").Clone())
            .ToArray();
    }

    [Fact] // OQ-113 (a): a reloaded conversation shows the pending offer, with the same action ids, until it is resolved.
    public async Task ReloadedConversation_ShowsThePendingOffer_UntilItIsResolved()
    {
        using var client = Client();
        var (conversation, _, offerId, confirm, cancel) = await OfferAsync(client);

        var saved = OfferOf((await AssistantMessagesAsync(client, conversation))[^1]);
        Assert.NotNull(saved);
        Assert.Equal(offerId, saved.Value.GetProperty("offerId").GetGuid());
        Assert.Equal(confirm, saved.Value.GetProperty("confirmActionId").GetString());
        Assert.Equal(cancel, saved.Value.GetProperty("cancelActionId").GetString());
        Assert.True(saved.Value.GetProperty("sufficientBalance").GetBoolean());

        await Act(client, conversation, cancel);

        var afterCancel = await AssistantMessagesAsync(client, conversation);
        Assert.All(afterCancel, content => Assert.Null(OfferOf(content)));
        Assert.Equal(0, _factory.Fallback.Calls);
    }

    [Fact] // OQ-113 (a): an expired offer is not shown again after a reload.
    public async Task ReloadedConversation_HidesAnExpiredOffer()
    {
        using var client = Client();
        var (conversation, offerTurn, _, _, _) = await OfferAsync(client);
        RewriteOfferMessage(offerTurn,
            JsonSerializer.Serialize(Payload(offerTurn).GroundedAnalysisOffer!.ExpiresAt),
            JsonSerializer.Serialize(DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Null(OfferOf((await AssistantMessagesAsync(client, conversation))[^1]));
    }

    [Fact] // AC-9: an expired offer never runs.
    public async Task ExpiredOffer_NeverRuns()
    {
        using var client = Client();
        var (conversation, offerTurn, offerId, confirm, _) = await OfferAsync(client);
        RewriteOfferMessage(offerTurn,
            JsonSerializer.Serialize(Payload(offerTurn).GroundedAnalysisOffer!.ExpiresAt),
            JsonSerializer.Serialize(DateTimeOffset.UtcNow.AddMinutes(-1)));

        var answer = await Act(client, conversation, confirm);

        Assert.Equal(SymbolGroundedFallbackMessages.OfferExpired, Text(answer));
        Assert.Equal(0, _factory.Fallback.Calls);
        Assert.Equal(0, ChargesOf(offerId));
    }

    [Fact] // AC-9: an unknown offer, or one from another conversation, never runs; a deleted conversation is not found.
    public async Task UnknownOfferOrConversation_NeverRuns()
    {
        using var client = Client();
        var (conversation, _, _, confirm, _) = await OfferAsync(client);
        var (otherConversation, _, _, _, _) = await OfferAsync(client);

        var unknown = await Act(client, conversation, GroundedAnalysisOfferActions.Confirm(Guid.NewGuid()));
        var foreign = await Act(client, otherConversation, confirm);
        await Act(client, Guid.NewGuid(), confirm, expected: HttpStatusCode.NotFound);

        Assert.Equal(SymbolGroundedFallbackMessages.OfferExpired, Text(unknown));
        Assert.Equal(SymbolGroundedFallbackMessages.OfferExpired, Text(foreign));
        Assert.Equal(0, _factory.Fallback.Calls);
    }

    [Fact] // AC-5, AC-9: a price change since the offer returns a new offer at 0 credits, without a model call.
    public async Task PriceChange_ReturnsANewOffer_WithoutRunning()
    {
        using var client = Client();
        var (conversation, offerTurn, offerId, confirm, _) = await OfferAsync(client);
        RewriteOfferMessage(offerTurn, Payload(offerTurn).GroundedAnalysisOffer!.PricingVersion, "000000000000");

        var answer = await Act(client, conversation, confirm);

        Assert.Equal("ClarificationNeeded", Outcome(answer));
        Assert.StartsWith(SymbolGroundedFallbackMessages.OfferChanged, Text(answer), StringComparison.Ordinal);
        Assert.Equal(0m, Credits(answer));
        Assert.Equal(0, _factory.Fallback.Calls);
        Assert.Equal(0, ChargesOf(offerId));
        Assert.NotEqual(offerId, OfferOf(answer)!.Value.GetProperty("offerId").GetGuid());
    }

    [Fact] // AC-10: a short balance gets the standard insufficient-credit response, and the offer stays confirmable.
    public async Task InsufficientCredit_IsRefused_AndTheOfferStays()
    {
        using var client = Client();
        var (conversation, _, offerId, confirm, _) = await OfferAsync(client);
        var balances = await _factory.SetWalletBalancesAsync(10m);
        try
        {
            await Act(client, conversation, confirm, expected: HttpStatusCode.PaymentRequired);
            Assert.Equal(0, _factory.Fallback.Calls);
        }
        finally
        {
            await _factory.RestoreWalletBalancesAsync(balances);
        }

        _factory.Fallback.Reply = prompt => EchoFirstFact(prompt);
        var answer = await Act(client, conversation, confirm);
        Assert.Equal("Answered", Outcome(answer));
        Assert.Equal(1, ChargesOf(offerId));
    }
}

public sealed class SymbolGroundedFallback163ApiFactory : BalanceSheetReport154ApiFactory
{
    public ScriptedSymbolGroundedFallbackModel Fallback { get; } = new();
    public ScriptedReportDiscussionAnalysisModel Discussion { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReportDiscussion:Enabled"] = "true",
                ["SymbolGroundedFallback:Enabled"] = "true",
                ["GroundedAnalysis:Enabled"] = "true"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReportDiscussionAnalysisModel>();
            services.AddSingleton<IReportDiscussionAnalysisModel>(Discussion);
            services.RemoveAll<IReportDiscussionModel>();
            services.AddSingleton<IReportDiscussionModel>(new ScriptedReportDiscussionModel());
            services.RemoveAll<ISymbolGroundedFallbackModel>();
            services.AddSingleton<ISymbolGroundedFallbackModel>(Fallback);
        });
    }

    /// <summary>Sets every wallet's balance and returns the previous balances.</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> SetWalletBalancesAsync(decimal balance)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var wallets = await db.WalletProjections.ToListAsync();
        var previous = wallets.ToDictionary(wallet => wallet.CustomerAccountId, wallet => wallet.Balance);
        foreach (var wallet in wallets)
        {
            wallet.Balance = balance;
            wallet.Revision++;
        }
        await db.SaveChangesAsync();
        return previous;
    }

    public async Task RestoreWalletBalancesAsync(IReadOnlyDictionary<Guid, decimal> balances)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        foreach (var wallet in await db.WalletProjections.ToListAsync())
        {
            if (!balances.TryGetValue(wallet.CustomerAccountId, out var balance)) continue;
            wallet.Balance = balance;
            wallet.Revision++;
        }
        await db.SaveChangesAsync();
    }
}

public sealed class ScriptedSymbolGroundedFallbackModel : ISymbolGroundedFallbackModel
{
    private readonly object _gate = new();
    private readonly List<SymbolGroundedFallbackPrompt> _prompts = [];
    private readonly List<AnalysisChoice> _choices = [];

    public Func<SymbolGroundedFallbackPrompt, SymbolGroundedFallbackModelReply> Reply { get; set; } = Unexpected;

    /// <summary>Holds the call open, so concurrent confirmations overlap.</summary>
    public TimeSpan Delay { get; set; }

    public int Calls { get { lock (_gate) return _prompts.Count; } }

    public IReadOnlyList<SymbolGroundedFallbackPrompt> Prompts { get { lock (_gate) return _prompts.ToArray(); } }

    public IReadOnlyList<AnalysisChoice> Choices { get { lock (_gate) return _choices.ToArray(); } }

    public void Reset()
    {
        lock (_gate)
        {
            _prompts.Clear();
            _choices.Clear();
        }
        Reply = Unexpected;
        Delay = TimeSpan.Zero;
    }

    public async Task<SymbolGroundedFallbackModelReply> AnalyzeAsync(
        SymbolGroundedFallbackPrompt prompt, AnalysisChoice choice, Guid tenantId, string correlationId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _prompts.Add(prompt);
            _choices.Add(choice);
        }
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
        return Reply(prompt);
    }

    private static SymbolGroundedFallbackModelReply Unexpected(SymbolGroundedFallbackPrompt prompt) =>
        throw new InvalidOperationException("The fallback model was not expected to be called.");
}
