using FinancialCopilot.Application.AI.ReportDiscussion;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Change «sol-deep-analysis-web-search» T07 (FR-10, AC-13): a number may come from the web only with a valid [Wn]
/// marker whose source the search consulted, with a date and the same value; everything else is checked against the
/// report as before.
/// </summary>
public sealed class ExternalEvidenceValidatorTests
{
    private const string Url = "https://www.codal.ir/Reports/Decision.aspx?LetterSerial=abc";
    private static readonly string[] Consulted = ["https://codal.ir/Reports/Decision.aspx?LetterSerial=abc&utm_source=openai"];

    private static readonly ReportDataSet Data = new(ReportFamily.QuarterlyIncomeStatement, "قمرو", null, "consolidated", "Codal",
        ["1404-Q2"],
        [new ReportFact("F1", "درآمد عملیاتی", "1404-Q2", 1250m, "همت", 0)],
        [new ReportCalculation("C1", "حاشیه سود ناخالص", "1404-Q2", 17.3m, "Percent", "a/b", ["F1"], 0)]);

    private static DeclaredExternalSource Source(string id = "W1", string url = Url, string date = "1404/08/12", decimal? value = 12.5m) =>
        new(id, url, "افزایش نرخ خوراک", date, "نرخ خوراک افزایش یافت", value, "درصد");

    private static string Answer(string item) =>
        "**سودآوری فصل دوم ۱۴۰۴ زیر فشار هزینه بود.**\n\n" +
        "- **فروش:** درآمد عملیاتی **۱,۲۵۰ همت** بود.\n" +
        item + "\n" +
        "- **حاشیه:** حاشیه سود ناخالص ۱۷.۳ درصد بود.\n\n" +
        "این تحلیل ارزش‌گذاری یا توصیهٔ سرمایه‌گذاری نیست.";

    [Fact]
    public void ValidExternalNumber_IsKept_AndExcludedFromTheReportCheck()
    {
        var answer = Answer("- **خوراک:** نرخ خوراک از آبان ۱۲.۵ درصد افزایش یافت [W1].");

        var result = ExternalEvidenceValidator.Validate(answer, [Source()], Consulted);

        Assert.True(result.Passed);
        Assert.Equal(answer, result.Text);
        Assert.Equal(1, result.MarkersChecked);
        Assert.DoesNotContain("12.5", result.GroundingText);
        Assert.DoesNotContain("[W1]", result.GroundingText);
        Assert.True(NumericGroundingValidator.Validate(result.GroundingText, Data, "سود چرا کم شد؟").Passed);
        var source = Assert.Single(result.Sources);
        Assert.Equal(("W1", "1404/08/12"), (source.Id, source.PublishedAt));
    }

    [Fact]
    public void DateNumbersOfTheCitedSource_AreCovered()
    {
        var answer = Answer("- **خوراک:** طبق اطلاعیهٔ ۱۴۰۴/۰۸/۲۷ نرخ خوراک بالا رفت [W1].");

        var result = ExternalEvidenceValidator.Validate(answer, [Source(date: "1404/08/27", value: null)], Consulted);

        Assert.True(result.Passed);
        Assert.True(NumericGroundingValidator.Validate(result.GroundingText, Data, "سود چرا کم شد؟").Passed);
    }

    [Theory]
    [InlineData("https://example.com/other", "1404/08/12", 12.5)] // not consulted
    [InlineData(Url, "", 12.5)]                                   // no date
    [InlineData(Url, "1404/08/12", 14)]                           // value mismatch
    public void InvalidSource_RemovesItsListItem(string url, string date, double value)
    {
        var answer = Answer("- **خوراک:** نرخ خوراک ۱۲.۵ درصد افزایش یافت [W1].");

        var result = ExternalEvidenceValidator.Validate(answer, [Source(url: url, date: date, value: (decimal)value)], Consulted);

        Assert.True(result.Passed);
        Assert.Equal(1, result.ItemsRemoved);
        Assert.DoesNotContain("خوراک", result.Text);
        Assert.Empty(result.Sources);
        Assert.True(NumericGroundingValidator.Validate(result.GroundingText, Data, "سود چرا کم شد؟").Passed);
    }

    [Fact]
    public void UnknownMarker_RemovesItsListItem()
    {
        var result = ExternalEvidenceValidator.Validate(Answer("- **خوراک:** نرخ خوراک بالا رفت [W3]."), [Source()], Consulted);

        Assert.Equal(1, result.ItemsRemoved);
    }

    [Fact]
    public void UnmarkedExternalNumber_FailsTheReportCheck()
    {
        var answer = Answer("- **تورم:** تورم سالانه ۴۵ درصد بود.");

        var result = ExternalEvidenceValidator.Validate(answer, [], []);

        Assert.True(result.Passed);
        Assert.False(NumericGroundingValidator.Validate(result.GroundingText, Data, "سود چرا کم شد؟").Passed);
    }

    [Fact]
    public void InvalidMarkerOnTheTakeaway_FailsTheAnswer()
    {
        var answer = "**نرخ خوراک ۱۲.۵ درصد بالا رفت [W1].**\n\n- **فروش:** درآمد عملیاتی ۱,۲۵۰ همت بود.";

        var result = ExternalEvidenceValidator.Validate(answer, [Source(url: "https://example.com/x")], Consulted);

        Assert.False(result.Passed);
        Assert.Equal("invalid_takeaway", result.FailureReason);
    }

    [Fact]
    public void ValidMarkerOnTheTakeaway_Passes()
    {
        var answer = "**نرخ خوراک ۱۲.۵ درصد بالا رفت [W1] و سود کاهش یافت.**\n\n- **فروش:** درآمد عملیاتی ۱,۲۵۰ همت بود.";

        Assert.True(ExternalEvidenceValidator.Validate(answer, [Source()], Consulted).Passed);
    }

    [Fact]
    public void ExternalNumber_CannotCoverAReportNumberInAnotherSentence()
    {
        // 45 is in the sentence before the marker's sentence, so the marker does not cover it.
        var answer = Answer("- **تورم:** تورم ۴۵ درصد بود. نرخ خوراک بالا رفت [W1].");

        var result = ExternalEvidenceValidator.Validate(answer, [Source(value: 45m)], Consulted);

        Assert.Equal(1, result.ItemsRemoved);
    }

    [Theory]
    [InlineData("**سود فصل دوم ۱۴۰۴ کاهش یافت.**\n\n- **فروش:** درآمد ۱,۲۵۰ همت بود.\n\nاین توصیه نیست.")]
    [InlineData("پاسخ ساده با عدد 17.3 درصد")]
    [InlineData("")]
    public void AnswerWithoutMarkers_IsReturnedUnchanged(string answer)
    {
        var result = ExternalEvidenceValidator.Validate(answer, [Source()], Consulted);

        Assert.True(result.Passed);
        Assert.Same(answer, result.Text);
        Assert.Same(answer, result.GroundingText);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void SourcesBlock_IsWrittenByTheServer()
    {
        var text = ExternalEvidenceValidator.AppendSourcesBlock("متن",
            [new FinancialCopilot.Application.AI.GroundedAnalysis.ExternalSource("W1", "اطلاعیه", "https://codal.ir/a", "1404/08/12")]);

        Assert.Equal("متن\n\n**منابع**\n- [W1] اطلاعیه (1404/08/12): https://codal.ir/a", text);
        Assert.Equal("متن", ExternalEvidenceValidator.AppendSourcesBlock("متن", []));
    }

    [Theory]
    [InlineData("https://www.Codal.ir/a/?utm_source=openai", "codal.ir/a")]
    [InlineData("https://codal.ir/a?x=1&utm_medium=y#top", "codal.ir/a?x=1")]
    [InlineData("ftp://codal.ir/a", null)]
    [InlineData("codal.ir/a", null)]
    public void NormalizeUrl_IgnoresWwwTrackingAndFragments(string url, string? expected)
    {
        Assert.Equal(expected, ExternalEvidenceValidator.NormalizeUrl(url));
    }
}
