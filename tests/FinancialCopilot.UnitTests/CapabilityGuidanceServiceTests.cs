using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

public sealed class CapabilityGuidanceServiceTests
{
    [Fact]
    public void Clarification_PrefersTheRelevantEnabledCapabilityExample()
    {
        var service = Create();
        var interpretation = new DeterministicCapabilityInterpreter(new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create())).Interpret("chart monthly sales");

        var action = Assert.Single(service.Suggest(new("chart monthly sales", "en", DialogueOutcome.ClarificationNeeded, DialogueOutcomeReasonCodes.RequiredInputMissing, interpretation)));

        Assert.Equal(SuggestedActionKind.FillSlot, action.Kind);
        Assert.Equal("monthly_activity_trend", action.CapabilityCode);
    }

    [Fact]
    public void Ambiguity_UsesConcreteCandidatesBeforeGenericHelp()
    {
        var service = Create();
        var candidate = new EntityResolutionCandidate(new(Guid.NewGuid(), "Foolad", "Foolad Co", "Company", "Companies"), .9m, "alias");

        var action = Assert.Single(service.Suggest(new("foolad", "en", DialogueOutcome.DisambiguationNeeded, DialogueOutcomeReasonCodes.EntityAmbiguous, EntityCandidates: [candidate])));

        Assert.Equal(SuggestedActionKind.ChooseEntity, action.Kind);
        Assert.Equal("Foolad", action.Message);
    }

    [Fact]
    public void StarterPrompts_ContainOnlyActorAvailableEnabledCapabilities()
    {
        var prompts = Create().StarterPrompts("en", ["monthly_activity_trend"]);
        var prompt = Assert.Single(prompts);
        Assert.Equal("monthly_activity_trend", prompt.CapabilityCode);
    }

    [Theory]
    [InlineData(DialogueOutcome.PartialAnswer, DialogueOutcomeReasonCodes.PartialEvidence)]
    [InlineData(DialogueOutcome.ClarificationNeeded, DialogueOutcomeReasonCodes.RequiredInputMissing)]
    [InlineData(DialogueOutcome.DisambiguationNeeded, DialogueOutcomeReasonCodes.EntityAmbiguous)]
    [InlineData(DialogueOutcome.NoData, DialogueOutcomeReasonCodes.SupportedButNoRows)]
    [InlineData(DialogueOutcome.Unsupported, DialogueOutcomeReasonCodes.CapabilityNotRecognized)]
    [InlineData(DialogueOutcome.TemporarilyUnavailable, DialogueOutcomeReasonCodes.ProviderOrToolTimeout)]
    [InlineData(DialogueOutcome.Failed, DialogueOutcomeReasonCodes.ProviderOrToolFailure)]
    public void EveryNonSuccessOutcome_ReturnsBoundedEnabledGuidance(
        DialogueOutcome outcome,
        string reason)
    {
        var service = Create();

        foreach (var language in new[] { "fa", "en" })
        {
            var actions = service.Suggest(new(
                language == "fa" ? "راهنمایی کن" : "help me",
                language,
                outcome,
                reason,
                ActorAvailableCapabilities: ["monthly_activity_trend"]));

            var action = Assert.Single(actions);
            Assert.Equal("monthly_activity_trend", action.CapabilityCode);
            Assert.InRange(action.Id.Length, 1, 160);
            Assert.InRange(action.LocalizedLabel.Length, 1, 160);
            Assert.InRange(action.Message.Length, 1, 500);
            Assert.StartsWith(language == "fa" ? "مثال" : "Try", action.LocalizedLabel, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VagueHelp_DoesNotAdvertiseDisabledOrUnavailableCapabilities()
    {
        var actions = Create().Suggest(new(
            "what can you do?", "en", DialogueOutcome.Unsupported,
            DialogueOutcomeReasonCodes.CapabilityNotRecognized,
            ActorAvailableCapabilities: ["symbol_metric_lookup", "not_registered"]));

        var action = Assert.Single(actions);
        Assert.Equal("symbol_metric_lookup", action.CapabilityCode);
        Assert.DoesNotContain(actions, item => item.CapabilityCode == "not_registered");
    }

    private static CapabilityGuidanceService Create() => new(new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create()));
}
