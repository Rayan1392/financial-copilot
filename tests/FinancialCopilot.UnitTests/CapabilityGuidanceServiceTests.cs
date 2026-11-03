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

    [Fact]
    public void StarterPrompts_ExcludeCapabilitiesDisabledForGuidance()
    {
        var prompts = Create().StarterPrompts("fa");

        Assert.DoesNotContain(prompts, prompt => prompt.CapabilityCode is
            "financial_statement_value_search" or
            "ps_gauge_visualization" or
            "personalized_insight_explanation" or
            "symbol_vs_industry_relative_valuation" or
            "industry_relative_valuation_ranking" or
            "industry_relative_valuation_summary" or
            "symbol_pair_within_industry");
        Assert.Contains(prompts, prompt => prompt.CapabilityCode == "monthly_sales_quality_ranking");
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

    [Fact]
    public void NoMonthlyData_OffersTheQuarterlyReportForTheSameSymbolInsteadOfGenericExamples()
    {
        var actions = Create().Suggest(new(
            "روند فروش ماهانه فاراک", "fa", DialogueOutcome.NoData, DialogueOutcomeReasonCodes.SupportedButNoRows,
            Symbol: "فاراک", AttemptedCapability: "monthly_activity_trend"));

        var action = Assert.Single(actions);
        Assert.Equal("گزارش فصلی فاراک", action.Message);
        Assert.Equal("گزارش فصلی فاراک", action.LocalizedLabel);
        Assert.Equal(SuggestedActionKind.RunRelatedCapability, action.Kind);
        Assert.Equal("financial_statement_table", action.CapabilityCode);
        Assert.Equal("فاراک", action.PresetSlots["symbol"]);
    }

    [Fact]
    public void UnsupportedQuestionAboutASymbol_OffersSalesTrendAndQuarterlyReportForIt()
    {
        var actions = Create().Suggest(new(
            "سهامداران عمده فاراک", "fa", DialogueOutcome.Unsupported, DialogueOutcomeReasonCodes.CapabilityNotRecognized,
            Symbol: "فاراک"));

        Assert.Equal(["روند فروش فاراک", "گزارش فصلی فاراک"], actions.Select(action => action.Message));
    }

    [Fact]
    public void UnansweredQuestionWithoutASymbol_ShowsTheTwoExamplesOnly()
    {
        var actions = Create().Suggest(new(
            "سهام با P/E زیر ۵", "fa", DialogueOutcome.NoData, DialogueOutcomeReasonCodes.SupportedButNoRows,
            AttemptedCapability: "stock_screening"));

        Assert.Equal(["مثال: روند فروش فولاد", "مثال: گزارش فصلی فولاد"], actions.Select(action => action.LocalizedLabel));
        Assert.DoesNotContain(actions, action => action.CapabilityCode == "stock_screening");
    }

    [Theory]
    [InlineData("روند فروش فاراک", "monthly_activity_trend")]
    [InlineData("گزارش فصلی فاراک", "financial_statement_table")]
    public void SuggestedFollowUps_RouteToTheirCapability(string message, string capability)
    {
        var interpretation = new DeterministicCapabilityInterpreter(
            new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create())).Interpret(message);

        Assert.Equal(capability, interpretation.CapabilityCandidates.First().CapabilityCode);
    }

    [Fact]
    public void FollowUpTexts_MatchTheLegacyIntentRules()
    {
        Assert.True(MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery("روند فروش فاراک"));
        Assert.Equal("فاراک", MonthlyActivityTrendIntentRules.ExtractCompanySymbol("روند فروش فاراک"));
        Assert.True(FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery("گزارش فصلی فاراک"));
        Assert.Equal("فاراک", FinancialStatementTableIntentRules.BuildQuarterlyQuery("گزارش فصلی فاراک").CompanyQuery);
    }

    [Fact]
    public void Subject_UsesTheSymbolTheRoutedIntentExtracted()
    {
        var (symbol, attempted) = CapabilityGuidanceSubject.From(null, DetectedIntent.MonthlyActivityTrend, "روند فروش ماهانه فاراک را نشان بده");

        Assert.Equal("فاراک", symbol);
        Assert.Equal("monthly_activity_trend", attempted);
        Assert.Equal((null, null), CapabilityGuidanceSubject.From(null, DetectedIntent.Unknown, "سهام با P/E زیر ۵"));
    }

    private static CapabilityGuidanceService Create() => new(new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create()));
}
