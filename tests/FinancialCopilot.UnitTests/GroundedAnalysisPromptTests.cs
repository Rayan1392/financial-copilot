using System.Text.Json;
using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
using ReportGroundedAnalysis = FinancialCopilot.Application.AI.ReportDiscussion.GroundedAnalysis;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Change «sol-deep-analysis-web-search» T06: which prompt sections are present per depth and web switch, and the
/// structured-output schemas. No test asserts model wording beyond the presence of a section.
/// </summary>
public sealed class GroundedAnalysisPromptTests
{
    public static TheoryData<AnalysisDepth, bool> Choices() => new()
    {
        { AnalysisDepth.Simple, false }, { AnalysisDepth.Simple, true }, { AnalysisDepth.Deep, false }, { AnalysisDepth.Deep, true }
    };

    [Theory]
    [MemberData(nameof(Choices))]
    public void ReportDiscussionPrompt_HasTheSectionsOfItsProfile(AnalysisDepth depth, bool web)
    {
        var prompt = ReportDiscussionContextBuilder.AnalysisSystemPromptFor(
            ReportFamily.QuarterlyIncomeStatement, new AnalysisChoice(depth, web));

        AssertCommonSections(prompt, depth, web);
        Assert.Contains(ReportGroundedAnalysis.ShownReportRule, prompt);
        Assert.Contains(ReportGroundedAnalysis.ScoreRules, prompt);
        Assert.Contains("MissingInReport", prompt);
        Assert.DoesNotContain("NeedsDifferentData", prompt);
    }

    [Theory]
    [MemberData(nameof(Choices))]
    public void FallbackPrompt_HasTheSectionsOfItsProfile(AnalysisDepth depth, bool web)
    {
        var prompt = SymbolGroundedFallbackPromptBuilder.AnalysisSystemPromptFor(new AnalysisChoice(depth, web));

        AssertCommonSections(prompt, depth, web);
        Assert.Contains(ReportGroundedAnalysis.HiddenReportRule, prompt);
        Assert.Contains("closingOffer", prompt);
        Assert.Contains("NotAnswerable", prompt);
    }

    [Fact]
    public void BalanceSheetPrompt_IsNotScored()
    {
        var prompt = ReportDiscussionContextBuilder.AnalysisSystemPromptFor(
            ReportFamily.BalanceSheet, new AnalysisChoice(AnalysisDepth.Deep, true));

        Assert.Contains(ReportGroundedAnalysis.NoScoreRule, prompt);
        Assert.DoesNotContain(ReportGroundedAnalysis.ScoreRules, prompt);
    }

    [Fact]
    public void DispositionPrompt_HasNoWebRules_AndCarriesNoValues()
    {
        var builder = new ReportDiscussionContextBuilder(new ReportDiscussionOptions());
        var data = new ReportDataSet(ReportFamily.QuarterlyIncomeStatement, "قمرو", "قند مرودشت", "consolidated", "Codal",
            ["1404-Q2", "1404-Q1"],
            [new ReportFact("F1", "درآمد عملیاتی", "1404-Q2", 123_456_789m, "Rial", 0)],
            [new ReportCalculation("C1", "حاشیه سود ناخالص", "1404-Q2", 17.3m, "Percent", "a/b", ["F1"], 0)]);

        var prompt = builder.BuildDisposition(data, [], "چرا سود کم شد؟");

        Assert.DoesNotContain("web", prompt.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("درآمد عملیاتی", prompt.UserContent);
        Assert.Contains("حاشیه سود ناخالص", prompt.UserContent);
        Assert.DoesNotContain("123456789", prompt.UserContent);
        Assert.DoesNotContain("17.3", prompt.UserContent);
    }

    [Theory]
    [InlineData(AnalysisDepth.Simple, "300 to 500 words")]
    [InlineData(AnalysisDepth.Deep, "600 to 1,000 words")]
    public void Layout_TargetsTheDepthsLength(AnalysisDepth depth, string length)
    {
        Assert.Contains(length, GroundedAnswerLayout.AnalysisPromptRules(depth));
    }

    public static TheoryData<string, string> Schemas() => new()
    {
        { "ReportDiscussionAnalysis_v1", LlmReportDiscussionAnalysisModel.AnalysisContract.JsonSchema! },
        { "ReportDiscussionDisposition_v1", LlmReportDiscussionAnalysisModel.DispositionContract.JsonSchema! },
        { "SymbolGroundedFallbackAnalysis_v1", LlmSymbolGroundedFallbackModel.AnalysisContract.JsonSchema! }
    };

    [Theory]
    [MemberData(nameof(Schemas))]
    public void Schemas_AreStrict_EveryPropertyIsRequired(string name, string schema)
    {
        using var document = JsonDocument.Parse(schema);
        AssertStrict(document.RootElement, name);
    }

    [Fact]
    public void AnalysisSchemas_DeclareExternalSources()
    {
        Assert.Contains("externalSources", LlmReportDiscussionAnalysisModel.AnalysisContract.RequiredRootProperties);
        Assert.Contains("externalSources", LlmSymbolGroundedFallbackModel.AnalysisContract.RequiredRootProperties);
    }

    [Fact]
    public void ParseAnalysis_ReadsSourcesAndDropsMalformedOnes()
    {
        const string json = """
            {"disposition":"Answered","answer":" متن ","usedEvidenceIds":["F1"],"reportScore":70,
             "externalSources":[
               {"id":"W1","url":"https://codal.ir/a","title":"اطلاعیه","publishedDate":"1404/08/10","claim":"x","value":12.5,"unit":"درصد"},
               {"id":"","url":"https://x","title":"","publishedDate":"","claim":"","value":null,"unit":null},
               "junk"]}
            """;

        var reply = LlmReportDiscussionAnalysisModel.ParseAnalysis(json, ["https://codal.ir/a"]);

        Assert.Equal(ReportDiscussionDisposition.Answered, reply.Disposition);
        Assert.Equal("متن", reply.Answer);
        Assert.Equal(70, reply.ReportScore);
        var source = Assert.Single(reply.DeclaredSources);
        Assert.Equal(("W1", 12.5m, "درصد"), (source.Id, source.Value, source.Unit));
        Assert.Single(reply.ConsultedSources);
    }

    [Theory]
    [InlineData("""{"disposition":"OutsideReport","answer":"","usedEvidenceIds":[],"reportScore":null,"externalSources":[]}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void ParseAnalysis_RejectsAnythingButAnsweredOrMissing(string json)
    {
        Assert.Throws<ReportDiscussionModelUnavailableException>(() => LlmReportDiscussionAnalysisModel.ParseAnalysis(json));
    }

    [Theory]
    [InlineData("Answered", ReportDiscussionDisposition.Answered)]
    [InlineData("NeedsDifferentData", ReportDiscussionDisposition.NeedsDifferentData)]
    [InlineData("OutsideReport", ReportDiscussionDisposition.OutsideReport)]
    [InlineData("MissingInReport", ReportDiscussionDisposition.MissingInReport)]
    public void ParseDisposition_ReadsTheFourDecisions(string text, ReportDiscussionDisposition expected)
    {
        Assert.Equal(expected, LlmReportDiscussionAnalysisModel.ParseDisposition($$"""{"disposition":"{{text}}"}"""));
    }

    private static void AssertCommonSections(string prompt, AnalysisDepth depth, bool web)
    {
        // Change «local-company-data-context» (FR-5, FR-6): every analysis prompt names <company_data>, never <report>.
        static string Data(string section) => ReportGroundedAnalysis.ForCompanyData(section);
        Assert.DoesNotContain("<report>", prompt);
        Assert.Contains("<company_data> is Sapio's own copy of this company's financial data", prompt);
        Assert.Contains(ReportGroundedAnalysis.AnalystRole, prompt);
        Assert.Contains(ReportGroundedAnalysis.ResearchMindset, prompt);
        Assert.Contains(ReportGroundedAnalysis.AnalysisDimensions, prompt);
        Assert.Contains(Data(ReportGroundedAnalysis.UnitRules), prompt);
        Assert.Contains(ReportGroundedAnalysis.NoValuationRules, prompt);
        Assert.Contains(Data(ReportGroundedAnalysis.EvidenceClasses(web)), prompt);
        Assert.Contains(GroundedAnswerLayout.AnalysisPromptRules(depth), prompt);
        if (web)
        {
            Assert.Contains(Data(ReportGroundedAnalysis.WebSearchUseRule(depth) + ReportGroundedAnalysis.WebRules), prompt);
            Assert.DoesNotContain(Data(ReportGroundedAnalysis.NoWebRules), prompt);
            // AC-6: no search for figures Sapio holds; the other search targets stay (G1, G2).
            Assert.Contains("Never search the web for this company's financial statements, its monthly activity report totals", prompt);
            Assert.Contains("Product-level detail (sales, quantities and rates of each product) is not in <company_data> and may be searched", prompt);
            Assert.Contains("the company's disclosures on codal.ir", prompt);
            if (depth == AnalysisDepth.Deep)
            {
                Assert.Contains("raw-material and product prices", prompt);
                Assert.Contains("and the company's own disclosures", prompt);
            }
            else
            {
                Assert.Contains("a price or rate change", prompt);
                Assert.Contains("the company's own disclosure about this period", prompt);
            }
        }
        else
        {
            Assert.Contains(Data(ReportGroundedAnalysis.NoWebRules), prompt);
            Assert.DoesNotContain("[W1]", prompt);
            Assert.DoesNotContain("codal.ir", prompt);
            Assert.DoesNotContain("Never search the web", prompt);
        }
    }

    private static void AssertStrict(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        if (element.TryGetProperty("properties", out var properties))
        {
            Assert.False(element.GetProperty("additionalProperties").GetBoolean(), path);
            var names = properties.EnumerateObject().Select(property => property.Name).Order().ToArray();
            var required = element.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).Order().ToArray();
            Assert.Equal(names, required);
            foreach (var property in properties.EnumerateObject()) AssertStrict(property.Value, $"{path}.{property.Name}");
        }
        if (element.TryGetProperty("items", out var items)) AssertStrict(items, $"{path}[]");
    }
}
