using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.ValuationGauges;

namespace FinancialCopilot.UnitTests;

/// <summary>Change «valuation-gauge-per-card-credits» T1, T2: one credit per usable card (OQ-138, OQ-139).</summary>
public sealed class ValuationGaugeBillingPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(new ValuationGaugeCardState[0], 0)]
    [InlineData(new[] { ValuationGaugeCardState.Available }, 1)]
    // A range-only equilibrium card is Partial and counts (OQ-139).
    [InlineData(new[] { ValuationGaugeCardState.Available, ValuationGaugeCardState.Available, ValuationGaugeCardState.Partial }, 3)]
    // A card without an answer is free (OQ-138).
    [InlineData(new[] { ValuationGaugeCardState.NoData, ValuationGaugeCardState.Available, ValuationGaugeCardState.Partial }, 2)]
    [InlineData(new[] { ValuationGaugeCardState.Stale, ValuationGaugeCardState.TemporarilyUnavailable, ValuationGaugeCardState.Available }, 1)]
    [InlineData(new[] { ValuationGaugeCardState.NoData, ValuationGaugeCardState.Stale, ValuationGaugeCardState.TemporarilyUnavailable }, 0)]
    public void ChargedCards_CountsAvailableAndPartialCards(ValuationGaugeCardState[] states, int expected) =>
        Assert.Equal(expected, ValuationGaugeBillingPolicy.ChargedCards(Result(states)));

    [Theory]
    [InlineData(1, "ValuationGaugesAnswered.1", 1)]
    [InlineData(2, "ValuationGaugesAnswered.2", 2)]
    [InlineData(3, "ValuationGaugesAnswered.3", 3)]
    public void AnsweredStatus_RoundTripsToItsFixedAmount(int cards, string status, int credits)
    {
        Assert.Equal(status, ValuationGaugeBillingPolicy.AnsweredStatus(cards));
        Assert.Equal(credits, ValuationGaugeBillingPolicy.FixedCredits(status));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void AnsweredStatus_RejectsAnImpossibleCount(int cards) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ValuationGaugeBillingPolicy.AnsweredStatus(cards));

    [Theory]
    [InlineData(null)]
    [InlineData("Completed")]
    [InlineData("NoData")]
    [InlineData("GroundedAnalysisAnswered.DeepWeb")]
    [InlineData("ValuationGaugesAnswered.0")]
    [InlineData("ValuationGaugesAnswered.4")]
    [InlineData("ValuationGaugesAnswered.x")]
    [InlineData("ValuationGaugesAnswered.+2")]
    public void FixedCredits_IsNullForEveryOtherStatus(string? status) =>
        Assert.Null(ValuationGaugeBillingPolicy.FixedCredits(status));

    [Theory]
    [InlineData("pe", 1)]
    [InlineData("pe,ps", 2)]
    [InlineData("equilibrium,pe,pe", 2)]
    [InlineData("pe,ps,equilibrium", 3)]
    [InlineData("", 3)] // an empty kinds slot means all kinds
    public void RequiredCredits_IsTheRequestedCards(string kinds, int expected) =>
        Assert.Equal(expected, ValuationGaugeBillingPolicy.RequiredCredits(Frame(kinds)));

    [Fact]
    public void CompletionStatus_ForAGaugeAnswer_IsPerUsableCard()
    {
        var all = Result([ValuationGaugeCardState.Available, ValuationGaugeCardState.Available, ValuationGaugeCardState.Partial]);
        var partial = Result([ValuationGaugeCardState.NoData, ValuationGaugeCardState.Available, ValuationGaugeCardState.Partial]);

        Assert.Equal("ValuationGaugesAnswered.3", SemanticBillingCompletionStatus.For(Execution(CapabilityExecutionStatus.Executed, all)));
        Assert.Equal("ValuationGaugesAnswered.2", SemanticBillingCompletionStatus.For(Execution(CapabilityExecutionStatus.Partial, partial)));
    }

    [Fact]
    public void CompletionStatus_KeepsEveryOtherOutcome()
    {
        var none = Result([ValuationGaugeCardState.NoData]);

        Assert.Equal("NoData", SemanticBillingCompletionStatus.For(Execution(CapabilityExecutionStatus.NoData, none)));
        Assert.Equal("ProviderFailed", SemanticBillingCompletionStatus.For(Execution(CapabilityExecutionStatus.TemporarilyUnavailable, none)));
        Assert.Equal("Completed", SemanticBillingCompletionStatus.For(Execution(CapabilityExecutionStatus.Executed, "another payload")));
        Assert.Equal("Completed", SemanticBillingCompletionStatus.For(Execution(CapabilityExecutionStatus.Executed, null)));
    }

    private static CapabilityExecutionResult Execution(CapabilityExecutionStatus status, object? payload) =>
        new(ValuationGaugeCapability.Code, 1, status, DialogueOutcomeReasonCodes.None, payload);

    private static ValuationGaugeCardsResult Result(IReadOnlyList<ValuationGaugeCardState> states)
    {
        var kinds = Enum.GetValues<ValuationGaugeKind>().Take(states.Count).ToArray();
        var cards = states.Select((state, index) => new ValuationGaugeCard(kinds[index], state, "test", "CyclicalWavesV2", Now)).ToArray();
        return new ValuationGaugeCardsResult(1, "شاراک", null, "شاراک", kinds, cards, Now, 48);
    }

    private static ValidatedQueryFrame Frame(string kinds)
    {
        var interpretation = ValuationGaugeIntentRules.BuildInterpretation("گیج", new ValuationGaugeRequest([], "شاراک"), 1);
        var slots = new List<ResolvedQuerySlot>
        {
            new(QuerySlotType.MetricSet, kinds, QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, ValuationGaugeCapability.Code)
        };
        return new ValidatedQueryFrame(ValuationGaugeCapability.Code, 1, slots, interpretation);
    }
}
