using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.UnitTests;

public sealed class AiDialogueOutcomePolicyTests
{
    [Theory]
    [InlineData("P/E فولاد", "fa")]
    [InlineData("چارت روند فروش فولاد", "fa")]
    [InlineData("P/E فولاد را نشان بده", "fa")]
    [InlineData("show me فولاد metrics", "fa")]
    [InlineData("show me the P/E for steel", "en")]
    public void DetectReplyLanguage_UsesScriptOfUserMessage(string message, string expected)
    {
        Assert.Equal(expected, AiDialogueOutcomePolicy.DetectReplyLanguage(message));
    }

    [Fact]
    public void UnknownRequest_BecomesLocalizedUnsupportedOutcome()
    {
        var outcome = AiDialogueOutcomePolicy.Determine(
            "این قابلیت ناشناخته را انجام بده",
            DetectedIntent.Unknown,
            clarificationRequired: false,
            clarificationMessage: null,
            hasStructuredResult: false,
            hasData: false);

        Assert.Equal(DialogueOutcome.Unsupported, outcome.Outcome);
        Assert.Equal(DialogueOutcomeReasonCodes.CapabilityNotRecognized, outcome.ReasonCode);
        Assert.Equal("fa", outcome.ReplyLanguage);
        Assert.Contains("نمی‌توانم", AiDialogueOutcomePolicy.ComposeSystemMessage(outcome));
    }

    [Fact]
    public void EnglishUnknownRequest_DoesNotUsePersianFallback()
    {
        var outcome = AiDialogueOutcomePolicy.Determine(
            "do something unsupported",
            DetectedIntent.Unknown,
            clarificationRequired: false,
            clarificationMessage: null,
            hasStructuredResult: false,
            hasData: false);

        var message = AiDialogueOutcomePolicy.ComposeSystemMessage(outcome);

        Assert.Equal("en", outcome.ReplyLanguage);
        Assert.Contains("cannot answer", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("نمی‌توانم", message);
    }

    [Fact]
    public void Clarification_UsesCompatibleDetailAndSetsRequiredOutcome()
    {
        var outcome = AiDialogueOutcomePolicy.Determine(
            "روند فروش ماهانه را نشان بده",
            DetectedIntent.MonthlyActivityTrend,
            clarificationRequired: true,
            clarificationMessage: "لطفاً نام نماد را مشخص کنید.",
            hasStructuredResult: true,
            hasData: false);

        Assert.Equal(DialogueOutcome.ClarificationNeeded, outcome.Outcome);
        Assert.Equal(DialogueOutcomeReasonCodes.RequiredInputMissing, outcome.ReasonCode);
        Assert.Equal("لطفاً نام نماد را مشخص کنید.", AiDialogueOutcomePolicy.ComposeSystemMessage(outcome, outcome.SafeDetail));
    }

    [Fact]
    public void IncompatibleModelDetail_IsDiscardedForLocalizedSystemMessage()
    {
        var outcome = AiDialogueOutcomePolicy.Determine(
            "این قابلیت را انجام بده",
            DetectedIntent.Unknown,
            clarificationRequired: false,
            clarificationMessage: null,
            hasStructuredResult: false,
            hasData: false);

        var message = AiDialogueOutcomePolicy.ComposeSystemMessage(outcome, "This is an unsafe guessed answer.");

        Assert.DoesNotContain("unsafe guessed answer", message);
        Assert.Contains("نمی‌توانم", message);
    }

    [Fact]
    public void SupportedStructuredRouteWithoutRows_IsNoData()
    {
        var outcome = AiDialogueOutcomePolicy.Determine(
            "monthly sales for فولاد",
            DetectedIntent.MonthlyActivityTrend,
            clarificationRequired: false,
            clarificationMessage: null,
            hasStructuredResult: true,
            hasData: false);

        Assert.Equal(DialogueOutcome.NoData, outcome.Outcome);
        Assert.Equal(DialogueOutcomeReasonCodes.SupportedButNoRows, outcome.ReasonCode);
    }

    [Fact]
    public void AmbiguousEntity_IsDisambiguationAndNotMissingInput()
    {
        var outcome = AiDialogueOutcomePolicy.Determine(
            "P/E فولاد یا فولاژ",
            DetectedIntent.SymbolLookup,
            clarificationRequired: true,
            clarificationMessage: "یک نماد را انتخاب کنید.",
            hasStructuredResult: true,
            hasData: false,
            hasUnresolvedEntity: true);

        Assert.Equal(DialogueOutcome.DisambiguationNeeded, outcome.Outcome);
        Assert.Equal(DialogueOutcomeReasonCodes.EntityAmbiguous, outcome.ReasonCode);
        Assert.False(outcome.Outcome == DialogueOutcome.ClarificationNeeded);
    }

    [Fact]
    public void StaleData_IsReportedSeparatelyFromEmptyRows()
    {
        var outcome = AiDialogueOutcomePolicy.Determine(
            "monthly sales for فولاد",
            DetectedIntent.MonthlyActivityTrend,
            clarificationRequired: false,
            clarificationMessage: null,
            hasStructuredResult: true,
            hasData: false,
            hasStaleOrIneligibleData: true);

        Assert.Equal(DialogueOutcome.NoData, outcome.Outcome);
        Assert.Equal(DialogueOutcomeReasonCodes.DataStaleOrIneligible, outcome.ReasonCode);
    }

    [Theory]
    [InlineData(AiExecutionStatus.TimedOut, DialogueOutcome.TemporarilyUnavailable, DialogueOutcomeReasonCodes.ProviderOrToolTimeout)]
    [InlineData(AiExecutionStatus.InvalidStructuredOutput, DialogueOutcome.Failed, DialogueOutcomeReasonCodes.ResponseValidationFailed)]
    [InlineData(AiExecutionStatus.CapabilityUnavailable, DialogueOutcome.TemporarilyUnavailable, DialogueOutcomeReasonCodes.ProviderOrToolFailure)]
    [InlineData(AiExecutionStatus.Failed, DialogueOutcome.Failed, DialogueOutcomeReasonCodes.ProviderOrToolFailure)]
    public void ProviderFailures_MapToSafeTypedOutcomes(
        AiExecutionStatus status,
        DialogueOutcome expectedOutcome,
        string expectedReason)
    {
        var exception = new AiModelProviderException(status, "internal-code", "secret provider detail");

        var outcome = AiDialogueOutcomePolicy.FromException("show فولاد metrics", exception);

        Assert.Equal(expectedOutcome, outcome.Outcome);
        Assert.Equal(expectedReason, outcome.ReasonCode);
        Assert.Null(outcome.SafeDetail);
        Assert.DoesNotContain("secret provider detail", AiDialogueOutcomePolicy.ComposeSystemMessage(outcome));
    }
}
