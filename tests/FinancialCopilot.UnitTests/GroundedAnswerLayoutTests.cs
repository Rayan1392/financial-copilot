using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 163 change «answer-readability»: the layout rules and the deterministic safety net.</summary>
public sealed class GroundedAnswerLayoutTests
{
    // ── Thousands separators ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("فروش ۱۶۷۵۲۵۵۳۸۵ میلیون ریال", "فروش ۱٬۶۷۵٬۲۵۵٬۳۸۵ میلیون ریال")]
    [InlineData("Revenue 1675255385 million", "Revenue 1,675,255,385 million")]
    [InlineData("از -۵۸۱۲۷۶۸ به ۱۲۴۵۱۹۴۷۹", "از -۵٬۸۱۲٬۷۶۸ به ۱۲۴٬۵۱۹٬۴۷۹")]
    [InlineData("۱۲۳۴۵ و ۱۲۳۴۵۶", "۱۲٬۳۴۵ و ۱۲۳٬۴۵۶")]
    [InlineData("فصل ۲ سال مالی ۱۴۰۵", "فصل ۲ سال مالی ۱۴۰۵")]
    [InlineData("۱۰۰٫۸۳ درصد و 291.56%", "۱۰۰٫۸۳ درصد و 291.56%")]
    [InlineData("۰٫۱۲۳۴۵۶ و 0.123456", "۰٫۱۲۳۴۵۶ و 0.123456")]
    [InlineData("۱٬۶۷۵٬۲۵۵٬۳۸۵ و 1,675,255,385", "۱٬۶۷۵٬۲۵۵٬۳۸۵ و 1,675,255,385")]
    [InlineData("۱۲۳۴۵۶٫۷۸ میلیون", "۱۲۳٬۴۵۶٫۷۸ میلیون")]
    public void GroupThousands_SeparatesOnlyUngroupedIntegerRunsOfFiveOrMoreDigits(string input, string expected) =>
        Assert.Equal(expected, GroundedAnswerLayout.GroupThousands(input));

    // ── Markdown subset ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_KeepsTheLayout_AndSeparatesAParagraphAfterTheList()
    {
        var answer = "بر اساس صورت سود و زیان فصلی فولاد، **سودآوری بهتر شد.**\n\n" +
                     "- **فروش:** ۱۶۷۵۲۵۵۳۸۵ میلیون ریال\n" +
                     "- **سود خالص:** **رشد ۲۹۱٫۶ درصد**\n" +
                     "این بررسی، ارزش‌گذاری یا توصیه سرمایه‌گذاری نیست.";

        Assert.Equal(
            "بر اساس صورت سود و زیان فصلی فولاد، **سودآوری بهتر شد.**\n\n" +
            "- **فروش:** ۱٬۶۷۵٬۲۵۵٬۳۸۵ میلیون ریال\n" +
            "- **سود خالص:** **رشد ۲۹۱٫۶ درصد**\n\n" +
            "این بررسی، ارزش‌گذاری یا توصیه سرمایه‌گذاری نیست.",
            GroundedAnswerLayout.Normalize(answer));
    }

    [Theory]
    [InlineData("## خلاصه", "خلاصه")]
    [InlineData("* فروش بالا رفت", "- فروش بالا رفت")]
    [InlineData("• فروش بالا رفت", "- فروش بالا رفت")]
    [InlineData("**فروش** بالا رفت", "**فروش** بالا رفت")]
    [InlineData("[کدال](https://codal.ir) را ببینید", "کدال را ببینید")]
    [InlineData("** فروش ** بالا رفت", "**فروش** بالا رفت")]
    [InlineData("**فروش:**بالا رفت", "**فروش:** بالا رفت")]
    [InlineData("حاشیه:**۳۰ درصد**", "حاشیه:**۳۰ درصد**")]
    [InlineData("الف\n\n\n\nب", "الف\n\nب")]
    [InlineData("الف\r\nب", "الف\nب")]
    public void Normalize_KeepsOnlyTheSupportedMarkdown(string input, string expected) =>
        Assert.Equal(expected, GroundedAnswerLayout.Normalize(input));

    [Fact]
    public void Normalize_TurnsATableIntoListItems()
    {
        var table = "| دوره | فروش |\n|---|---|\n| فصل ۲ | ۱۲۳۴۵ |";

        Assert.Equal("- دوره · فروش\n- فصل ۲ · ۱۲٬۳۴۵", GroundedAnswerLayout.Normalize(table));
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var once = GroundedAnswerLayout.Normalize("## عنوان\n* **فروش:**۱۲۳۴۵۶ میلیون\nپایان");

        Assert.Equal(once, GroundedAnswerLayout.Normalize(once));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  \n ")]
    public void Normalize_EmptyAnswer_IsEmpty(string? input) => Assert.Equal(string.Empty, GroundedAnswerLayout.Normalize(input));

    // ── Grounding still passes on the formatted answer ───────────────────────────────────

    [Fact]
    public void Grounding_AcceptsBoldListAndSeparators()
    {
        var data = new ReportDataSet(
            ReportFamily.QuarterlyIncomeStatement, "فولاد", "فولاد مبارکه", "quarterly", "Noavaran",
            ["فصل ۲ سال مالی ۱۴۰۵", "فصل ۱ سال مالی ۱۴۰۵"],
            [
                new ReportFact("F1", "فروش", "فصل ۲ سال مالی ۱۴۰۵", 1675255385m, "میلیون ریال", 0),
                new ReportFact("F2", "سود خالص", "فصل ۲ سال مالی ۱۴۰۵", 471272793m, "میلیون ریال", 0),
                new ReportFact("F3", "سایر درآمدها و هزینه‌های غیرعملیاتی", "فصل ۱ سال مالی ۱۴۰۵", -5812768m, "میلیون ریال", 1)
            ],
            [new ReportCalculation("C1", "رشد فروش", "فصل ۲ سال مالی ۱۴۰۵", 100.83m, "%", "growth", ["F1"], 0)]);
        var answer = GroundedAnswerLayout.Normalize(
            "بر اساس صورت سود و زیان فصلی فولاد، **فصل ۲ ۱۴۰۵ در مقایسه با فصل ۱ ۱۴۰۵ بهتر بود.**\n\n" +
            "- **فروش:** ۱۶۷۵۲۵۵۳۸۵ میلیون ریال؛ **رشد ۱۰۰٫۸ درصد**\n" +
            "- **سود خالص:** **۴۷۱۲۷۲۷۹۳ میلیون ریال**\n" +
            "- **غیرعملیاتی:** از منفی ۵۸۱۲۷۶۸ میلیون ریال بهتر شد");

        var result = NumericGroundingValidator.Validate(answer, data, "سهم فولاد را تحلیل کن");

        Assert.True(result.Passed, $"{result.UngroundedCount} of {result.NumbersChecked} ungrounded: {answer}");
        Assert.Contains("۱٬۶۷۵٬۲۵۵٬۳۸۵", answer);
    }

    // ── Prompts carry the layout ─────────────────────────────────────────────────────────

    [Fact]
    public void BothPrompts_CarryTheSharedLayoutRules_AndNoLongerThe250WordLimit()
    {
        // Change «sol-deep-analysis-web-search»: 163 runs only as the confirmed analysis, with the per-depth layout.
        foreach (var depth in new[] { FinancialCopilot.Application.AI.GroundedAnalysis.AnalysisDepth.Simple, FinancialCopilot.Application.AI.GroundedAnalysis.AnalysisDepth.Deep })
        {
            var fallback = SymbolGroundedFallbackPromptBuilder.AnalysisSystemPromptFor(
                new FinancialCopilot.Application.AI.GroundedAnalysis.AnalysisChoice(depth, false));
            Assert.Contains(GroundedAnswerLayout.AnalysisPromptRules(depth), fallback);
            Assert.DoesNotContain("250 words", fallback);
        }
        Assert.Contains(GroundedAnswerLayout.PromptRules, ReportDiscussionContextBuilder.SystemPrompt);
        Assert.DoesNotContain("250 words", ReportDiscussionContextBuilder.SystemPrompt);
        Assert.Contains("thousands separators", GroundedAnswerLayout.PromptRules);
        Assert.Contains("**bold**", GroundedAnswerLayout.PromptRules);
    }
}
