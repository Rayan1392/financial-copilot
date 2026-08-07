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

    private static CapabilityGuidanceService Create() => new(new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create()));
}
