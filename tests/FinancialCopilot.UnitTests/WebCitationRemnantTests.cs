using FinancialCopilot.Application.AI.ReportDiscussion;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// BUG-036: the model writes OpenAI's inline citation «([domain](url))» before the <c>[Wn]</c> marker. The layout
/// safety net removes the whole citation, so no «(domain)» remnant is left; the marker and the server-written source
/// list carry the source. Pure tests, no model call.
/// </summary>
public sealed class WebCitationRemnantTests
{
    private const string Url = "https://irrena.ir/news/12345/steel?utm_source=openai";

    [Theory]
    [InlineData("لحاظ کرده است. ([irrena.ir](" + Url + ")) [W1]", "لحاظ کرده است. [W1]")]
    [InlineData("لحاظ کرده است ([irrena.ir](" + Url + "))[W1]", "لحاظ کرده است [W1]")]
    [InlineData("محدودیت گاز ادامه داشت ([a.ir](https://a.ir/x), [b.com](https://b.com/y)) [W1]", "محدودیت گاز ادامه داشت [W1]")]
    [InlineData("محدودیت گاز ادامه داشت ([a.ir](https://a.ir/x)؛ [b.com](https://b.com/y)) [W1]", "محدودیت گاز ادامه داشت [W1]")]
    [InlineData("محدودیت گاز ادامه داشت [irrena.ir](" + Url + ") [W1]", "محدودیت گاز ادامه داشت [W1]")]
    [InlineData("محدودیت برق ادامه داشت ([khabar24.ir](https://khabar24.ir/n/7)).", "محدودیت برق ادامه داشت.")]
    public void Normalize_RemovesTheWholeWebCitation(string input, string expected) =>
        Assert.Equal(expected, GroundedAnswerLayout.Normalize(input));

    [Theory]
    [InlineData("طبق [اطلاعیه کدال](https://codal.ir/x) فروش ثبت شد.", "طبق اطلاعیه کدال فروش ثبت شد.")]
    [InlineData("سود (پس از کسر مالیات) بیشتر شد.", "سود (پس از کسر مالیات) بیشتر شد.")]
    [InlineData("گزارش ([پیوست](attachment.pdf)) را ببینید.", "گزارش (پیوست) را ببینید.")]
    [InlineData("طبق گزارش [W1](https://a.ir/x) و ([W2](https://b.ir/y))", "طبق گزارش W1 و (W2)")]
    public void Normalize_LeavesOtherLinksAndParenthesesAsBefore(string input, string expected) =>
        Assert.Equal(expected, GroundedAnswerLayout.Normalize(input));

    [Fact]
    public void Normalize_WithACitation_IsIdempotent()
    {
        var once = GroundedAnswerLayout.Normalize("محدودیت گاز ادامه داشت. ([irrena.ir](" + Url + ")) [W1]");
        Assert.Equal(once, GroundedAnswerLayout.Normalize(once));
    }

    [Fact]
    public void CitedAnswer_KeepsTheMarker_ListsTheSourceOnce_AndHasNoRemnant()
    {
        var answer = GroundedAnswerLayout.Normalize(
            "**جمع‌بندی:** سودآوری بهتر شد.\n\n" +
            "- **انرژی:** محدودیت گاز در زمستان ادامه داشت. ([irrena.ir](" + Url + ")) [W1]");
        var source = new DeclaredExternalSource("W1", Url, "محدودیت گاز فولادسازان", "1405/09/10",
            "محدودیت گاز ادامه داشت", null, null);

        var evidence = ExternalEvidenceValidator.Validate(answer, [source], ["https://irrena.ir/news/12345/steel"]);
        var text = ExternalEvidenceValidator.AppendSourcesBlock(evidence.Text, evidence.Sources);

        Assert.True(evidence.Passed);
        Assert.Equal(1, evidence.MarkersChecked);
        Assert.Contains("ادامه داشت. [W1]", text);
        Assert.DoesNotContain("(irrena.ir)", text);
        Assert.Single(evidence.Sources);
        Assert.Equal(1, text.Split("https://irrena.ir").Length - 1);
    }
}
