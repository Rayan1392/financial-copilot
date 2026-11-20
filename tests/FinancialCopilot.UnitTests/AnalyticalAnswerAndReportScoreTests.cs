using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// 162 change «analytical-answer-and-report-score» (applies to 163 too): display units (FR-1), prompt rules (FR-2 … FR-4),
/// score parsing (FR-4) and the score line (FR-5).
/// </summary>
public sealed class AnalyticalAnswerAndReportScoreTests
{
    // ── Display units (FR-1, AC-1, AC-2) ───────────────────────────────────────────────

    [Theory]
    [InlineData(12_345_678, "million_rial", 1.2, ReportDisplayUnits.Hemat)]
    [InlineData(9_876_543, "million_irr", 988, ReportDisplayUnits.BillionToman)]
    [InlineData(54_321, "million_rial", 5.4, ReportDisplayUnits.BillionToman)]
    [InlineData(5_000, "million_rial", 0.5, ReportDisplayUnits.BillionToman)]
    [InlineData(20, "million_rial", 0.002, ReportDisplayUnits.BillionToman)]
    [InlineData(-12_345_678, "million_rial", -1.2, ReportDisplayUnits.Hemat)]
    [InlineData(9_997_000, "million_rial", 1.0, ReportDisplayUnits.Hemat)] // 999.7 billion toman rounds to 1,000
    [InlineData(9_990_000, "million_rial", 999, ReportDisplayUnits.BillionToman)]
    [InlineData(2_500, ReportDisplayUnits.BillionTomanLabelFa, 2.5, ReportDisplayUnits.Hemat)]
    [InlineData(850, ReportDisplayUnits.BillionTomanLabelFa, 850, ReportDisplayUnits.BillionToman)]
    [InlineData(0, "million_rial", 0, ReportDisplayUnits.BillionToman)]
    public void MonetaryAmounts_AreShownInHematOrBillionToman(double value, string unit, double expected, string expectedUnit)
    {
        var display = ReportDisplayUnits.ToDisplay((decimal)value, unit);

        Assert.NotNull(display);
        Assert.Equal((decimal)expected, display.Value.Value);
        Assert.Equal(expectedUnit, display.Value.Unit);
    }

    [Fact]
    public void RialAmounts_AreConvertedToo() =>
        Assert.Equal((1234.6m, ReportDisplayUnits.Hemat), ReportDisplayUnits.ToDisplay(12_345_678_000_000_000m, "irr"));

    [Theory]
    [InlineData("percent")]
    [InlineData("percentage_point")]
    [InlineData("rial_per_share")]
    [InlineData("thousand_shares")]
    [InlineData("times")]
    public void OtherUnits_AreUnchanged(string unit) => Assert.Null(ReportDisplayUnits.ToDisplay(1_234_567m, unit));

    private static ReportDataSet Data() => new(
        ReportFamily.QuarterlyIncomeStatement, "فولاد", "فولاد مبارکه", "جداگانه", "NoavaranCurrentApi",
        ["فصل 2 سال مالی 1405", "فصل 1 سال مالی 1405"],
        [
            new ReportFact("F1", "فروش", "فصل 2 سال مالی 1405", 12_345_678m, "million_rial", 0),
            new ReportFact("F2", "فروش", "فصل 1 سال مالی 1405", 9_876_543m, "million_rial", 1),
            new ReportFact("F3", "سود هر سهم", "فصل 2 سال مالی 1405", 345m, "rial_per_share", 0),
        ],
        [new ReportCalculation("C1", "رشد فصلی فروش", "فصل 2 سال مالی 1405", 25m, "percent", "f", ["F1", "F2"], 0)]);

    [Fact]
    public void Apply_ConvertsOnlyAmounts_AndRendersTheNewUnits()
    {
        var projected = ReportDisplayUnits.Apply(Data());

        Assert.Equal([1.2m, 988m, 345m], projected.Facts.Select(fact => fact.Value));
        Assert.Equal(25m, projected.Calculations.Single().Value);
        var rendered = ReportDiscussionContextBuilder.RenderReport(projected);
        Assert.Contains("F1 | فروش | فصل 2 سال مالی 1405 | 1.2 | همت", rendered, StringComparison.Ordinal);
        Assert.Contains("F2 | فروش | فصل 1 سال مالی 1405 | 988 | میلیارد تومان", rendered, StringComparison.Ordinal);
        Assert.Contains("| 345 | ریال به ازای هر سهم", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("میلیون ریال", rendered, StringComparison.Ordinal);
    }

    [Fact] // AC-3
    public void Grounding_UsesTheConvertedValues()
    {
        var projected = ReportDisplayUnits.Apply(Data());

        Assert.True(NumericGroundingValidator.Validate("فروش به **۱٫۲ همت** رسید و از ۹۸۸ میلیارد تومان بالاتر رفت.", projected, "تحلیل کن").Passed);
        Assert.False(NumericGroundingValidator.Validate("فروش ۱۲٬۳۴۵٬۶۷۸ میلیون ریال بود.", projected, "تحلیل کن").Passed);
    }

    [Fact]
    public void PromptBuilders_SendConvertedAmounts()
    {
        var discussion = new ReportDiscussionContextBuilder(new ReportDiscussionOptions()).Build(Data(), [], "تحلیل کن")!;
        var fallback = new SymbolGroundedFallbackPromptBuilder(new SymbolGroundedFallbackOptions(), new SymbolGroundedFallbackOffers(false))
            .BuildAnalysis(Data(), "تحلیل کن",
                new FinancialCopilot.Application.AI.GroundedAnalysis.AnalysisChoice(
                    FinancialCopilot.Application.AI.GroundedAnalysis.AnalysisDepth.Deep, false), 10000)!;

        foreach (var (content, data) in new[] { (discussion.UserContent, discussion.Data), (fallback.UserContent, fallback.Data) })
        {
            Assert.Contains("| 1.2 | همت", content, StringComparison.Ordinal);
            Assert.Equal(ReportDisplayUnits.Hemat, data.Facts[0].Unit);
        }
    }

    // ── Prompts (FR-2 … FR-4, AC-8) ────────────────────────────────────────────────────

    [Fact]
    public void ReportDiscussionPrompt_IsAnalytical_AndScoresOnlyQuarterlyStatements()
    {
        var quarterly = ReportDiscussionContextBuilder.SystemPromptFor(ReportFamily.QuarterlyIncomeStatement);
        Assert.Equal(ReportDiscussionContextBuilder.SystemPrompt, quarterly);
        Assert.DoesNotContain("Do not convert units", quarterly, StringComparison.Ordinal);
        Assert.Contains(GroundedAnalysis.UnitRules, quarterly, StringComparison.Ordinal);
        Assert.Contains(GroundedAnalysis.StyleRules, quarterly, StringComparison.Ordinal);
        Assert.Contains(GroundedAnalysis.ShownReportRule, quarterly, StringComparison.Ordinal);
        Assert.Contains(GroundedAnalysis.ScoreRules, quarterly, StringComparison.Ordinal);

        foreach (var family in new[] { ReportFamily.BalanceSheet, ReportFamily.MonthlySales })
        {
            var prompt = ReportDiscussionContextBuilder.SystemPromptFor(family);
            Assert.DoesNotContain(GroundedAnalysis.ScoreRules, prompt, StringComparison.Ordinal);
            Assert.Contains(GroundedAnalysis.NoScoreRule, prompt, StringComparison.Ordinal);
            Assert.Contains(GroundedAnalysis.StyleRules, prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FallbackPrompt_IsAnalytical_KeepsKeyFigures_AndScores()
    {
        // Change «sol-deep-analysis-web-search»: 163 runs only as the confirmed analysis; its prompt keeps these rules.
        var prompt = SymbolGroundedFallbackPromptBuilder.AnalysisSystemPromptFor(
            new FinancialCopilot.Application.AI.GroundedAnalysis.AnalysisChoice(
                FinancialCopilot.Application.AI.GroundedAnalysis.AnalysisDepth.Deep, false));
        Assert.DoesNotContain("Do not convert units", prompt, StringComparison.Ordinal);
        Assert.Contains(GroundedAnalysis.UnitRules, prompt, StringComparison.Ordinal);
        Assert.Contains(GroundedAnalysis.HiddenReportRule, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(GroundedAnalysis.ShownReportRule, prompt, StringComparison.Ordinal);
        Assert.Contains(GroundedAnalysis.ScoreRules, prompt, StringComparison.Ordinal);
    }

    // ── Score parsing (FR-4, AC-5) ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("72", 72)]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    [InlineData("null", null)]
    [InlineData("101", null)]
    [InlineData("-1", null)]
    [InlineData("72.5", null)]
    [InlineData("\"72\"", null)]
    public void Score_IsAnIntegerFromZeroToHundredOrNothing(string json, int? expected)
    {
        var discussion = LlmReportDiscussionModel.Parse(
            $$"""{"disposition":"Answered","answer":"پاسخ","usedEvidenceIds":[],"reportScore":{{json}}}""");
        var fallback = LlmSymbolGroundedFallbackModel.Parse(
            $$"""{"disposition":"Answered","answer":"پاسخ","closingOffer":"","usedEvidenceIds":[],"reportScore":{{json}}}""");

        Assert.Equal(expected, discussion.ReportScore);
        Assert.Equal(expected, fallback.ReportScore);
    }

    [Fact]
    public void MissingScore_IsNothing() =>
        Assert.Null(LlmReportDiscussionModel.Parse("""{"disposition":"Answered","answer":"پاسخ","usedEvidenceIds":[]}""").ReportScore);

    // ── Score line (FR-5) ──────────────────────────────────────────────────────────────

    [Fact]
    public void ScoreBlock_IsWrittenByTheServerInPersian()
    {
        Assert.Equal(
            "**امتیاز گزارش فصل ۲ سال مالی ۱۴۰۵: ۷۲ از ۱۰۰**\n\nاین امتیاز ارزیابی عملکرد مالی همین گزارش است و توصیهٔ خرید یا فروش نیست.",
            GroundedAnalysis.ScoreBlock(72, "فصل 2 سال مالی 1405"));
        Assert.Equal([72m, 100m], GroundedAnalysis.ScoreNumbers(72));
        Assert.Empty(GroundedAnalysis.ScoreNumbers(null));
        Assert.Null(GroundedAnalysis.ShownScore(ReportFamily.BalanceSheet, 72));
        Assert.Equal(72, GroundedAnalysis.ShownScore(ReportFamily.QuarterlyIncomeStatement, 72));
    }
}
