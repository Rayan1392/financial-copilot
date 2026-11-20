using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
using ReportGroundedAnalysis = FinancialCopilot.Application.AI.ReportDiscussion.GroundedAnalysis;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Change «sol-deep-analysis-web-search», amendment A4 (owner, 2026-11-20): less conservative analysis prompts. Deep
/// with web treats research as part of the analysis; the number rule limits written numbers, not reasoning; inferences
/// and hypotheses are expected; without web, industry knowledge may frame hypotheses but no outside fact or figure is
/// added and no web check is claimed (D1 a). The legacy (GroundedAnalysis off) prompt is unchanged. No test asserts
/// model wording beyond the presence of a rule.
/// </summary>
public sealed class AnalysisResearchPromptTests
{
    private const string OldSkipRule = "do not search when the report alone answers the question";

    public static TheoryData<AnalysisDepth, bool> Choices() => new()
    {
        { AnalysisDepth.Simple, false }, { AnalysisDepth.Simple, true }, { AnalysisDepth.Deep, false }, { AnalysisDepth.Deep, true }
    };

    private static IEnumerable<string> PromptsFor(AnalysisChoice choice) =>
    [
        ReportDiscussionContextBuilder.AnalysisSystemPromptFor(ReportFamily.QuarterlyIncomeStatement, choice),
        ReportDiscussionContextBuilder.AnalysisSystemPromptFor(ReportFamily.BalanceSheet, choice),
        ReportDiscussionContextBuilder.AnalysisSystemPromptFor(ReportFamily.MonthlySales, choice),
        SymbolGroundedFallbackPromptBuilder.AnalysisSystemPromptFor(choice)
    ];

    [Theory]
    [MemberData(nameof(Choices))]
    public void EveryAnalysisPrompt_HasTheResearchMindset_AndKeepsTheNumberBan(AnalysisDepth depth, bool web)
    {
        foreach (var prompt in PromptsFor(new AnalysisChoice(depth, web)))
        {
            Assert.Contains(ReportGroundedAnalysis.ResearchMindset, prompt);
            Assert.Contains("Which indicators should an investor watch in the next report?", prompt);
            Assert.Contains("Never invent, estimate, forecast or round a number", prompt);
            Assert.Contains("This rule limits the numbers you write, not your reasoning", prompt);
            Assert.Contains("never replace a report number", prompt);
            Assert.Contains("Inferences and hypotheses are expected, not merely allowed", prompt);
            Assert.Contains("never give a forecast number", prompt);
            Assert.Contains(ReportGroundedAnalysis.NoValuationRules, prompt);
            Assert.DoesNotContain(OldSkipRule, prompt);
        }
    }

    [Fact]
    public void DeepWithWeb_MakesResearchPartOfTheAnalysis()
    {
        foreach (var prompt in PromptsFor(new AnalysisChoice(AnalysisDepth.Deep, true)))
        {
            Assert.Contains("Web research is part of a deep analysis", prompt);
            Assert.Contains("That the report has enough numbers is not a reason to skip this research", prompt);
            Assert.Contains("never repeat one", prompt);
            Assert.Contains("never fill the gap with invented information", prompt);
            Assert.Contains("Cite only pages you actually consulted", prompt);
            Assert.DoesNotContain("It is optional.", prompt);
        }
    }

    [Fact]
    public void SimpleWithWeb_KeepsTheSearchOptional_WithoutTheSkipSentence()
    {
        foreach (var prompt in PromptsFor(new AnalysisChoice(AnalysisDepth.Simple, true)))
        {
            Assert.Contains("It is optional.", prompt);
            Assert.DoesNotContain("Web research is part of a deep analysis", prompt);
            Assert.Contains("never fill the gap with invented information", prompt);
        }
    }

    [Theory]
    [InlineData(AnalysisDepth.Simple)]
    [InlineData(AnalysisDepth.Deep)]
    public void WithoutWeb_IndustryKnowledgeFramesHypotheses_ButNoWebCheckIsClaimed(AnalysisDepth depth)
    {
        foreach (var prompt in PromptsFor(new AnalysisChoice(depth, false)))
        {
            Assert.Contains(ReportGroundedAnalysis.NoWebRules, prompt);
            Assert.Contains("well-established knowledge of how the company's industry works", prompt);
            Assert.Contains("Do not add news, events, dates or figures from outside the report", prompt);
            Assert.Contains("never say or imply that you checked online or current information", prompt);
            Assert.DoesNotContain(ReportGroundedAnalysis.WebRules, prompt);
            Assert.DoesNotContain("Web research is part of a deep analysis", prompt);
            Assert.DoesNotContain("[W", prompt);
        }
    }

    [Fact]
    public void HypothesisExample_CarriesNoNumbers()
    {
        // The owner's example wording sits in every prompt; it must not offer a figure the model could copy.
        foreach (var web in new[] { false, true })
        {
            var classes = ReportGroundedAnalysis.EvidenceClasses(web);
            var start = classes.IndexOf('«');
            var end = classes.IndexOf('»');
            Assert.True(start >= 0 && end > start);
            Assert.DoesNotContain(classes[start..end], char.IsDigit);
        }
        Assert.DoesNotContain(ReportGroundedAnalysis.ResearchMindset, char.IsDigit);
    }

    [Fact]
    public void LegacyPrompt_HasNoneOfTheNewRules()
    {
        foreach (var family in new[] { ReportFamily.QuarterlyIncomeStatement, ReportFamily.BalanceSheet, ReportFamily.MonthlySales })
        {
            var prompt = ReportDiscussionContextBuilder.SystemPromptFor(family);
            Assert.DoesNotContain(ReportGroundedAnalysis.ResearchMindset, prompt);
            Assert.DoesNotContain("not merely allowed", prompt);
            Assert.DoesNotContain("not your reasoning", prompt);
            Assert.DoesNotContain("Web research", prompt);
        }
    }

    [Fact]
    public void DeepLayout_TargetsALongerOverallAnalysis_AndKeepsNarrowQuestionsShort()
    {
        var deep = GroundedAnswerLayout.AnalysisPromptRules(AnalysisDepth.Deep);
        Assert.Contains("5 to 8 items", deep);
        Assert.Contains("at most five sentences", deep);
        Assert.Contains("For a narrow question about one item, use 2 to 4 items.", deep);
        Assert.Contains("300 to 500 words", GroundedAnswerLayout.AnalysisPromptRules(AnalysisDepth.Simple));
    }
}
