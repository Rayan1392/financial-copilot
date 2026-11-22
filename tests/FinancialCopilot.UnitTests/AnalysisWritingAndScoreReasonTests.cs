using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
using ReportGroundedAnalysis = FinancialCopilot.Application.AI.ReportDiscussion.GroundedAnalysis;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Change «sol-deep-analysis-web-search», amendment A1 and A2 (owner, 2026-11-20): Persian writing rules in both analysis
/// prompts (instruction only, no rewriting) and a one-sentence score reason under the server-written score line. The
/// legacy (GroundedAnalysis off) prompt and score block stay as they were. No test asserts model wording.
/// </summary>
public sealed class AnalysisWritingAndScoreReasonTests
{
    public static TheoryData<AnalysisDepth, bool> Choices() => new()
    {
        { AnalysisDepth.Simple, false }, { AnalysisDepth.Simple, true }, { AnalysisDepth.Deep, false }, { AnalysisDepth.Deep, true }
    };

    private const string Disclaimer = "این امتیاز ارزیابی عملکرد مالی همین گزارش است و توصیهٔ خرید یا فروش نیست.";
    private const string Reason =
        "بهبود سودآوری عملیاتی از نقاط قوت گزارش است؛ در مقابل، سهم درآمدهای غیرعملیاتی امتیاز را تعدیل کرده است.";

    // ── A1: writing rules ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Choices))]
    public void BothAnalysisPrompts_HaveTheWritingRules(AnalysisDepth depth, bool web)
    {
        var choice = new AnalysisChoice(depth, web);
        Assert.Contains(ReportGroundedAnalysis.ForCompanyData(ReportGroundedAnalysis.PersianWritingRules),
            ReportDiscussionContextBuilder.AnalysisSystemPromptFor(ReportFamily.QuarterlyIncomeStatement, choice));
        Assert.Contains(ReportGroundedAnalysis.ForCompanyData(ReportGroundedAnalysis.PersianWritingRules),
            ReportDiscussionContextBuilder.AnalysisSystemPromptFor(ReportFamily.BalanceSheet, choice));
        Assert.Contains(ReportGroundedAnalysis.ForCompanyData(ReportGroundedAnalysis.PersianWritingRules), SymbolGroundedFallbackPromptBuilder.AnalysisSystemPromptFor(choice));
    }

    [Fact]
    public void LegacyPrompt_IsUnchanged()
    {
        foreach (var family in new[] { ReportFamily.QuarterlyIncomeStatement, ReportFamily.BalanceSheet, ReportFamily.MonthlySales })
        {
            var prompt = ReportDiscussionContextBuilder.SystemPromptFor(family);
            Assert.DoesNotContain(ReportGroundedAnalysis.PersianWritingRules, prompt);
            Assert.DoesNotContain("scoreReason", prompt);
        }
    }

    [Fact]
    public void WritingRules_MarkTheExampleFiguresAsNotData()
    {
        Assert.Contains("never be copied", ReportGroundedAnalysis.PersianWritingRules);
        Assert.Contains("never changes the meaning, the numbers or the conclusions", ReportGroundedAnalysis.PersianWritingRules);
    }

    [Fact]
    public void WebRules_AskForAShortLinkTitle() =>
        Assert.Contains("shown as the link text", ReportGroundedAnalysis.WebRules);

    // ── A2: score reason ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Choices))]
    public void ScoredAnalysisPrompts_AskForTheReason_OthersForNull(AnalysisDepth depth, bool web)
    {
        var choice = new AnalysisChoice(depth, web);
        Assert.Contains(ReportGroundedAnalysis.AnalysisScoreRules,
            ReportDiscussionContextBuilder.AnalysisSystemPromptFor(ReportFamily.QuarterlyIncomeStatement, choice));
        Assert.Contains(ReportGroundedAnalysis.AnalysisScoreRules, SymbolGroundedFallbackPromptBuilder.AnalysisSystemPromptFor(choice));
        var balanceSheet = ReportDiscussionContextBuilder.AnalysisSystemPromptFor(ReportFamily.BalanceSheet, choice);
        Assert.Contains(ReportGroundedAnalysis.AnalysisNoScoreRule, balanceSheet);
        Assert.DoesNotContain(ReportGroundedAnalysis.AnalysisScoreRules, balanceSheet);
    }

    [Fact]
    public void AnalysisSchemas_RequireTheReason()
    {
        Assert.Contains("scoreReason", LlmReportDiscussionAnalysisModel.AnalysisContract.RequiredRootProperties);
        Assert.Contains("scoreReason", LlmSymbolGroundedFallbackModel.AnalysisContract.RequiredRootProperties);
        Assert.Contains("\"scoreReason\"", LlmReportDiscussionAnalysisModel.AnalysisContract.JsonSchema!);
        Assert.Contains("\"scoreReason\"", LlmSymbolGroundedFallbackModel.AnalysisContract.JsonSchema!);
    }

    [Fact]
    public void Parsers_ReadTheReason_AndAcceptOutputWithoutIt()
    {
        var with = LlmReportDiscussionAnalysisModel.ParseAnalysis(
            $$"""{"disposition":"Answered","answer":"متن","usedEvidenceIds":[],"reportScore":82,"scoreReason":" {{Reason}} ","externalSources":[]}""");
        var without = LlmReportDiscussionAnalysisModel.ParseAnalysis(
            """{"disposition":"Answered","answer":"متن","usedEvidenceIds":[],"reportScore":82,"externalSources":[]}""");
        var fallback = LlmSymbolGroundedFallbackModel.Parse(
            $$"""{"disposition":"Answered","answer":"متن","closingOffer":"","usedEvidenceIds":[],"reportScore":82,"scoreReason":"{{Reason}}","externalSources":[]}""");

        Assert.Equal(Reason, with.ScoreReason);
        Assert.Null(without.ScoreReason);
        Assert.Equal(Reason, fallback.ScoreReason);
    }

    [Fact]
    public void ScoreBlock_PutsTheReasonRightAfterTheScore_AndBeforeTheDisclaimer() =>
        Assert.Equal(
            $"**امتیاز گزارش فصل ۲ سال مالی ۱۴۰۵: ۸۲ از ۱۰۰**\n\n{Reason}\n\n{Disclaimer}",
            ReportGroundedAnalysis.ScoreBlock(82, "فصل 2 سال مالی 1405", Reason));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("رشد ۲۵ درصدی فروش از نقاط قوت گزارش است.")]
    [InlineData("Sales grew 25 percent.")]
    public void ScoreBlock_LeavesOutAnEmptyOrNumericReason_AndKeepsTheScore(string? reason) =>
        Assert.Equal(
            $"**امتیاز گزارش فصل ۲ سال مالی ۱۴۰۵: ۸۲ از ۱۰۰**\n\n{Disclaimer}",
            ReportGroundedAnalysis.ScoreBlock(82, "فصل 2 سال مالی 1405", reason));

    [Fact]
    public void ScoreReason_IsOneLine() =>
        Assert.Equal("نقطه قوت؛ نقطه ضعف.", ReportGroundedAnalysis.ShownScoreReason("نقطه قوت؛\n نقطه ضعف."));
}
