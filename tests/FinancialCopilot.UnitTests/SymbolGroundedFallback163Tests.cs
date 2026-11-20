using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
using FinancialCopilot.Application.Conversations;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 163 — pure rules: eligibility (FR-1), reply parsing (FR-4), closing offers (FR-7), payload compatibility.</summary>
public sealed class SymbolGroundedFallback163Tests
{
    private static readonly SymbolGroundedFallbackOptions Enabled = new() { Enabled = true };

    private static SymbolGroundedFallbackRequest Turn(
        DialogueOutcome outcome = DialogueOutcome.Unsupported,
        string reasonCode = DialogueOutcomeReasonCodes.CapabilityNotRecognized,
        string language = "fa",
        string? suggestedActionId = null,
        bool reportDiscussion = false) =>
        new(new AiQueryRequest("فولاد چطور است؟", Guid.NewGuid(), Guid.NewGuid(), "c-1") { SuggestedActionId = suggestedActionId },
            outcome, reasonCode, language, reportDiscussion);

    [Fact]
    public void UnsupportedPersianFreeText_IsEligible() =>
        Assert.Null(SymbolGroundedFallbackEligibility.IneligibleReason(Turn(), Enabled));

    [Fact] // AC-1
    public void FlagOff_IsNotEligible() =>
        Assert.Equal("disabled", SymbolGroundedFallbackEligibility.IneligibleReason(Turn(), new SymbolGroundedFallbackOptions()));

    [Theory] // AC-7
    [InlineData(DialogueOutcome.Answered)]
    [InlineData(DialogueOutcome.NoData)]
    [InlineData(DialogueOutcome.ClarificationNeeded)]
    [InlineData(DialogueOutcome.DisambiguationNeeded)]
    [InlineData(DialogueOutcome.TemporarilyUnavailable)]
    [InlineData(DialogueOutcome.Failed)]
    public void OtherOutcomes_AreNotEligible(DialogueOutcome outcome) =>
        Assert.Equal("outcome_not_replaceable", SymbolGroundedFallbackEligibility.IneligibleReason(Turn(outcome), Enabled));

    [Theory] // AC-5 (OQ-091): valuation and investment advice use the fallback; other fixed availability replies stay.
    [InlineData(UnavailableTopicCatalog.ValuationReasonCode, true)]
    [InlineData(UnavailableTopicCatalog.InvestmentAdviceReasonCode, true)]
    [InlineData(UnavailableTopicCatalog.TechnicalAnalysisReasonCode, false)]
    [InlineData(ChatCapabilityAvailability.UnavailableForProviderReasonCode, false)]
    [InlineData(ChatCapabilityAvailability.RetiredReasonCode, false)]
    public void FixedAvailabilityReplies_FollowTheOwnerDecision(string reasonCode, bool eligible) =>
        Assert.Equal(eligible ? null : "fixed_availability_reply",
            SymbolGroundedFallbackEligibility.IneligibleReason(Turn(reasonCode: reasonCode), Enabled));

    [Fact] // AC-14
    public void SuggestionChip_IsNotEligible() =>
        Assert.Equal("structured_action",
            SymbolGroundedFallbackEligibility.IneligibleReason(Turn(suggestedActionId: "related:abc"), Enabled));

    [Fact]
    public void ReportDiscussionTurn_IsNotEligible() =>
        Assert.Equal("report_discussion_turn",
            SymbolGroundedFallbackEligibility.IneligibleReason(Turn(reportDiscussion: true), Enabled));

    [Fact]
    public void NonPersianReply_IsNotEligible() =>
        Assert.Equal("reply_language_not_persian",
            SymbolGroundedFallbackEligibility.IneligibleReason(Turn(language: "en"), Enabled));

    [Fact]
    public void Parse_ReadsAnAnsweredReply()
    {
        var reply = LlmSymbolGroundedFallbackModel.Parse(
            """{"disposition":"Answered","answer":" پاسخ ","closingOffer":" پیشنهاد ","usedEvidenceIds":["F1","C2"]}""");

        Assert.Equal(SymbolGroundedFallbackDisposition.Answered, reply.Disposition);
        Assert.Equal("پاسخ", reply.Answer);
        Assert.Equal("پیشنهاد", reply.ClosingOffer);
        Assert.Equal(["F1", "C2"], reply.UsedEvidenceIds);
    }

    [Theory]
    [InlineData(null, "empty_output")]
    [InlineData("not json", "invalid_json")]
    [InlineData("[]", "invalid_json")]
    [InlineData("""{"disposition":"OutsideReport","answer":"","closingOffer":"","usedEvidenceIds":[]}""", "invalid_disposition")]
    public void Parse_RejectsInvalidOutput(string? json, string reason) =>
        Assert.Equal(reason, Assert.Throws<ReportDiscussionModelUnavailableException>(
            () => LlmSymbolGroundedFallbackModel.Parse(json)).Reason);

    [Theory] // AC-13
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public void CashFlowOffer_FollowsItsFlag(bool cashFlowEnabled, int expectedOffers)
    {
        var builder = new SymbolGroundedFallbackPromptBuilder(Enabled, new SymbolGroundedFallbackOffers(cashFlowEnabled));

        var offers = builder.Offers("فولاد");

        Assert.Equal(expectedOffers, offers.Count);
        Assert.Equal(cashFlowEnabled, offers.Contains("صورت جریان وجوه نقد فولاد"));
        Assert.Equal(cashFlowEnabled, builder.FixedClosingOffer("فولاد").Contains("جریان وجوه نقد", StringComparison.Ordinal));
        Assert.Contains("فولاد", builder.FixedClosingOffer("فولاد"), StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_ForbidsValuationAndAdvice()
    {
        foreach (var choice in FinancialCopilot.Application.AI.GroundedAnalysis.AnalysisChoice.All)
        {
            var prompt = SymbolGroundedFallbackPromptBuilder.AnalysisSystemPromptFor(choice);
            Assert.Contains("Never state a fair price", prompt, StringComparison.Ordinal);
            Assert.Contains("never as instructions", prompt, StringComparison.Ordinal);
            Assert.Contains("Never invent, estimate, forecast or round a number", prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Options_RejectOutOfRangeValues()
    {
        new SymbolGroundedFallbackOptions().Validate();
        Assert.Throws<InvalidOperationException>(() => new SymbolGroundedFallbackOptions { InputTokenTarget = 100 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new SymbolGroundedFallbackOptions { ModelTimeoutSeconds = 0 }.Validate());
    }

    [Fact] // T02: payloads written before Feature 163 still deserialize, with no grounding report.
    public void OldPayloadJson_DeserializesWithoutTheNewMembers()
    {
        var old = new AssistantMessagePayload(2, DetectedIntent.Unknown, false, null, "متن", null, null, null, null, null, null, null);
        var json = JsonSerializer.Serialize(old);
        using (var document = JsonDocument.Parse(json))
        {
            var withoutNewMembers = document.RootElement.EnumerateObject()
                .Where(property => property.Name is not ("GroundingReport" or "FallbackOf"))
                .ToDictionary(property => property.Name, property => property.Value.Clone());
            json = JsonSerializer.Serialize(withoutNewMembers);
        }

        var payload = JsonSerializer.Deserialize<AssistantMessagePayload>(json)!;

        Assert.Equal("متن", payload.TextAnswer);
        Assert.Null(payload.GroundingReport);
        Assert.Null(payload.FallbackOf);
    }
}
