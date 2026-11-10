using System.Reflection;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 162: per-capability source map, the comprehensive-analysis card, and its Telegram rendering.</summary>
public sealed class Feature162CapabilitySourceMapTests
{
    private const string Url = "https://tahlilapp.com/app/analysis";

    private static readonly IReadOnlyList<string> AllRoutes = typeof(CapabilityExecutionRoutes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToList();

    // --- Source map ---

    [Fact]
    public void OnlyComprehensiveAnalysisMayBeOverridden() =>
        Assert.Equal([CapabilityExecutionRoutes.ComprehensiveAnalysis], ChatCapabilitySourceMap.OverridableRoutes);

    [Theory]
    [InlineData(FinancialDataChatProvider.NoavaranAmin)]
    [InlineData(FinancialDataChatProvider.CyclicalWaves)]
    public void EmptyMapKeepsTheFeature148MatrixForEveryRoute(FinancialDataChatProvider mode)
    {
        var legacy = new ChatCapabilityAvailability(mode);
        var withEmptyMap = new ChatCapabilityAvailability(mode, ChatCapabilitySourceMap.Create(new FinancialDataOptions { ChatProvider = mode }));

        Assert.NotEmpty(AllRoutes);
        foreach (var route in AllRoutes)
        {
            Assert.Equal(legacy.Evaluate(route), withEmptyMap.Evaluate(route));
            Assert.Equal(mode, withEmptyMap.SourceFor(route));
        }

        Assert.Equal(
            mode == FinancialDataChatProvider.CyclicalWaves,
            withEmptyMap.Evaluate(CapabilityExecutionRoutes.ComprehensiveAnalysis).IsAvailable);
    }

    [Fact]
    public void MappedAnalysisChangesOnlyTheAnalysisRoute()
    {
        var unmapped = new ChatCapabilityAvailability(FinancialDataChatProvider.NoavaranAmin);
        var mapped = new ChatCapabilityAvailability(FinancialDataChatProvider.NoavaranAmin, Map("CyclicalWaves"));

        foreach (var route in AllRoutes.Where(route => route != CapabilityExecutionRoutes.ComprehensiveAnalysis))
        {
            Assert.Equal(unmapped.Evaluate(route), mapped.Evaluate(route));
            Assert.Equal(FinancialDataChatProvider.NoavaranAmin, mapped.SourceFor(route));
        }

        Assert.False(unmapped.Evaluate(CapabilityExecutionRoutes.ComprehensiveAnalysis).IsAvailable);
        Assert.True(mapped.Evaluate(CapabilityExecutionRoutes.ComprehensiveAnalysis).IsAvailable);
        Assert.Equal(FinancialDataChatProvider.CyclicalWaves, mapped.SourceFor(CapabilityExecutionRoutes.ComprehensiveAnalysis));
    }

    [Fact]
    public void MappingAnalysisToTheBaseProviderKeepsC2()
    {
        var mapped = new ChatCapabilityAvailability(FinancialDataChatProvider.NoavaranAmin, Map("noavaranamin"));

        Assert.False(mapped.Evaluate(CapabilityExecutionRoutes.ComprehensiveAnalysis).IsAvailable);
    }

    [Fact]
    public void KeysAndProviderNamesAreCaseInsensitive()
    {
        var options = new FinancialDataOptions();
        options.CapabilitySources["Comprehensive_Analysis"] = "cyclicalwaves";

        Assert.Equal(FinancialDataChatProvider.CyclicalWaves,
            Assert.Single(ChatCapabilitySourceMap.Create(options).Overrides).Value);
    }

    [Fact]
    public void ValidationReportsEveryInvalidEntry()
    {
        var options = new FinancialDataOptions();
        options.CapabilitySources["scanner"] = "CyclicalWaves";
        options.CapabilitySources["comprehensive_analysis"] = "Codal";

        var errors = ChatCapabilitySourceMap.Validate(options, out var map);

        Assert.Equal(2, errors.Count);
        Assert.Same(ChatCapabilitySourceMap.Empty, map);
    }

    // --- Card ---

    [Fact]
    public void MarkdownCardShowsTitleAndWholeSummaryThenOneLink()
    {
        var card = ComprehensiveAnalysisCardComposer.ComposeMarkdown(
            [Item(1, "شگل، بخش دوم- تعادلی", "خلاصهٔ اول.\nخط دوم."), Item(2, "شگل", "خلاصهٔ دوم")],
            [],
            Url);

        Assert.Equal(
            "### شگل، بخش دوم- تعادلی\nخلاصهٔ اول.\nخط دوم.\n\n### شگل\nخلاصهٔ دوم\n\n[ادامهٔ تحلیل‌ها در تحلیل‌اپ](https://tahlilapp.com/app/analysis)",
            card);
        Assert.DoesNotContain("تحلیلگر", card);
        Assert.DoesNotContain("تاریخ", card);
    }

    [Fact]
    public void CardWithEmptySummaryShowsTheTitleOnly() =>
        Assert.Equal(
            "### خزامیا\n\n[ادامهٔ تحلیل‌ها در تحلیل‌اپ](https://tahlilapp.com/app/analysis)",
            ComprehensiveAnalysisCardComposer.ComposeMarkdown([Item(1, "خزامیا", "  ")], [], Url));

    [Theory]
    [InlineData(new[] { "فولاد" }, "برای نماد فولاد تحلیلی در تحلیل‌اپ پیدا نشد.")]
    [InlineData(new[] { "فولاد", "شگل" }, "برای نماد فولاد، شگل تحلیلی در تحلیل‌اپ پیدا نشد.")]
    [InlineData(new string[0], "تحلیلی در تحلیل‌اپ پیدا نشد.")]
    public void NoAnalysisSaysSoAndStillLinks(string[] symbols, string sentence) =>
        Assert.Equal(
            $"{sentence}\n\n[ادامهٔ تحلیل‌ها در تحلیل‌اپ]({Url})",
            ComprehensiveAnalysisCardComposer.ComposeMarkdown([], symbols, Url));

    [Fact]
    public void PlainCardUsesTheBareUrl() =>
        Assert.Equal(
            $"شگل\nخلاصه\n\nادامهٔ تحلیل‌ها در تحلیل‌اپ: {Url}",
            ComprehensiveAnalysisCardComposer.ComposePlainText([Item(1, "شگل", "خلاصه")], [], Url));

    [Theory]
    [InlineData("https://tahlilapp.com/app/analysis", true)]
    [InlineData("http://tahlilapp.com/app/analysis", false)]
    [InlineData("tahlilapp.com/app/analysis", false)]
    [InlineData("", false)]
    public void AnalysesLinkMustBeAbsoluteHttps(string url, bool valid) =>
        Assert.Equal(valid, new ComprehensiveAnalysisPresentationOptions { TahlilAppAnalysesUrl = url }.IsValid(out _));

    // --- Telegram ---

    [Fact]
    public void Telegram_AnalysisTurnWithOneLookupRowShowsTheStockCardAndTheAnalysisOnce()
    {
        var analysis = new ComprehensiveAnalysisQueryResponse([Item(1, "فولاد- تکنیکال", "روند صعودی است")], []);
        var response = AnalysisResponse(analysis, Lookup("فولاد"), "ignored markdown card");

        var text = RenderText(response);

        Assert.Equal(1, Count(text, "فولاد- تکنیکال"));
        Assert.Equal(1, Count(text, "روند صعودی است"));
        Assert.Contains($"ادامهٔ تحلیل‌ها در تحلیل‌اپ: {Url}", text);
        Assert.DoesNotContain("ignored markdown card", text);
    }

    [Fact]
    public void Telegram_AnalysisTurnWithoutLookupRowsShowsTheAnalysisOnceWithoutMarkdown()
    {
        var analysis = new ComprehensiveAnalysisQueryResponse([Item(1, "شگل", "خلاصه")], []);
        var markdown = ComprehensiveAnalysisCardComposer.ComposeMarkdown(analysis.Items, [], Url);

        var text = RenderText(AnalysisResponse(analysis, null, markdown));

        Assert.Equal(1, Count(text, "خلاصه"));
        Assert.DoesNotContain("###", text);
        Assert.DoesNotContain("](", text);
        Assert.Contains(Url, text);
    }

    [Fact]
    public void Telegram_AnalysisTurnWithNoAnalysisSaysSo()
    {
        var text = RenderText(AnalysisResponse(new ComprehensiveAnalysisQueryResponse([], ["فولاد"]), Lookup("فولاد"), "x"));

        Assert.Contains("برای نماد فولاد تحلیلی در تحلیل‌اپ پیدا نشد.", text);
        Assert.Contains(Url, text);
    }

    [Fact]
    public void Telegram_UsesTheConfiguredLink()
    {
        var renderer = new TelegramAssistantResponseRenderer(
            new UnusedChartRenderer(),
            NullLogger<TelegramAssistantResponseRenderer>.Instance,
            Options.Create(new ComprehensiveAnalysisPresentationOptions { TahlilAppAnalysesUrl = "https://example.test/analysis" }));
        var response = AnalysisResponse(new ComprehensiveAnalysisQueryResponse([Item(1, "شگل", "خلاصه")], []), null, "x");

        var text = string.Concat(renderer.Render(response, "fa-IR").Select(message => message.Text)).Replace("\\", string.Empty);

        Assert.Contains("https://example.test/analysis", text);
    }

    [Fact]
    public void Telegram_OtherTurnsKeepTheirAnalysisAppendix()
    {
        var analysis = new ComprehensiveAnalysisQueryResponse([Item(1, "شگل", "خلاصه")], []);
        var response = AnalysisResponse(analysis, null, "پاسخ عامل") with { SemanticCapabilityCode = null };

        var text = RenderText(response);

        Assert.Contains("پاسخ عامل", text);
        Assert.Contains("تحلیل‌های یافت‌شده:", text);
    }

    private static ChatCapabilitySourceMap Map(string provider)
    {
        var options = new FinancialDataOptions();
        options.CapabilitySources[CapabilityExecutionRoutes.ComprehensiveAnalysis] = provider;
        return ChatCapabilitySourceMap.Create(options);
    }

    private static ComprehensiveAnalysisSummaryItem Item(long id, string title, string summary) =>
        new(id, title, "1405/08/18", "نیما آزادی", summary, [], DateTimeOffset.UtcNow);

    private static AiQueryResponse AnalysisResponse(
        ComprehensiveAnalysisQueryResponse analysis,
        SymbolLookupTableResult? lookup,
        string textAnswer) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.ComprehensiveAnalysis,
            null, null, lookup, null, null, textAnswer, false, null, null,
            ComprehensiveAnalysisResult: analysis,
            SemanticCapabilityCode: CapabilityExecutionRoutes.ComprehensiveAnalysis);

    private static string RenderText(AiQueryResponse response) =>
        string.Concat(new TelegramAssistantResponseRenderer(new UnusedChartRenderer(), NullLogger<TelegramAssistantResponseRenderer>.Instance)
                .Render(response, "fa-IR")
                .Select(message => message.Text))
            .Replace("\\", string.Empty);

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static SymbolLookupTableResult Lookup(string symbol)
    {
        var cells = new Dictionary<string, ScannerTableCell>
        {
            ["SYMBOL"] = new(null, symbol, CellFreshnessStatus.Persisted, null),
            ["PE_TTM"] = new(5.2m, "5.2", CellFreshnessStatus.Persisted, DateTimeOffset.UtcNow)
        };
        return new SymbolLookupTableResult(
            Guid.NewGuid(),
            [
                new ScannerTableColumn("SYMBOL", "Symbol", ScannerColumnType.Symbol),
                new ScannerTableColumn("PE_TTM", "P/E", ScannerColumnType.Metric, "PE_TTM")
            ],
            [new ScannerTableRow(symbol, null, cells, 1.0, [])],
            new ScannerExecutionFacts(DateTimeOffset.UtcNow, TimeSpan.Zero, 1, 1, false, 1, 1, 1),
            [],
            []);
    }

    private sealed class UnusedChartRenderer : ITelegramMonthlyTrendChartRenderer
    {
        public TelegramAssistantMediaAttachment Render(MonthlyActivityTrendResponse trend) =>
            throw new InvalidOperationException("must not be called");
    }
}
