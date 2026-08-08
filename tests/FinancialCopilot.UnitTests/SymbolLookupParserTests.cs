using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.UnitTests;

public sealed class SymbolLookupParserTests
{
    private static readonly DateOnly AsOf = new(2026, 6, 5);
    private static readonly Guid TenantId = Guid.Parse("9a1b2c3d-4e5f-6789-abcd-ef0123456789");

    [Fact]
    public async Task Parser_PersianSymbol_PersianMetricTerm_ReturnsResolvedPair()
    {
        var resolver = BuildAliasResolver();
        var json = BuildPairsJson([("حفاری", "نسبت پی به ای")], language: "fa");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest("PE حفاری چقدر است؟", "fa", "corr-1", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        Assert.Single(result.Pairs);
        Assert.Equal("حفاری", result.Pairs.First().RawSymbolName);
        Assert.Equal("PE_TTM", result.Pairs.First().ResolvedMetricCode?.Value);
    }

    [Fact]
    public async Task Parser_EnglishSymbol_EnglishMetricTerm_ReturnsResolvedPair()
    {
        var resolver = BuildAliasResolver();
        var json = BuildPairsJson([("AAPL", "P/E")], language: "en");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest("What is the P/E of AAPL?", "en", "corr-2", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        Assert.Single(result.Pairs);
        Assert.Equal("AAPL", result.Pairs.First().RawSymbolName);
        Assert.Equal("PE_TTM", result.Pairs.First().ResolvedMetricCode?.Value);
    }

    [Theory]
    [InlineData("PE", "PE_TTM")]
    [InlineData("PS", "PS_TTM")]
    public async Task Parser_BarePeAndPsMetricTerms_ReturnResolvedPairs(
        string metricTerm,
        string expectedMetricCode)
    {
        var resolver = BuildAliasResolver();
        var json = BuildPairsJson([("AAPL", metricTerm)], language: "en");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest($"{metricTerm} AAPL?", "en", $"corr-{metricTerm}", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        Assert.Single(result.Pairs);
        Assert.Equal(expectedMetricCode, result.Pairs.First().ResolvedMetricCode?.Value);
    }

    [Fact]
    public async Task Parser_CompositeMetricExpression_PrefersUserWrittenMonthlySalesSegment()
    {
        var resolver = BuildAliasResolver();
        var json = BuildPairsJson([("GGLPA", "latest monthly sales / sales / revenue")], language: "en");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest("latest monthly sales GGLPA?", "en", "corr-composite-sales", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        Assert.Single(result.Pairs);
        Assert.Equal("MONTHLY_SALES", result.Pairs.First().ResolvedMetricCode?.Value);
        Assert.Equal("latest monthly sales / sales / revenue", result.Pairs.First().OriginalMetricTerm);
    }

    [Theory]
    [InlineData("آخرین فروش کچاد چقدر بوده؟", "فروش")]
    [InlineData("آخرین فروش کچاد چقدر بوده؟", "REVENUE کچاد")]
    [InlineData("فروش ماهانه کچاد چقدر است؟", "REVENUE")]
    [InlineData("فروش این ماه کچاد چقدر است؟", "revenue")]
    [InlineData("فروش کچاد", "فروش")]
    public async Task Parser_LatestSalesQuestionWithGenericSalesTerm_ForcesMonthlySalesSnapshot(
        string userMessage,
        string llmMetricTerm)
    {
        var resolver = BuildAliasResolver();
        var json = BuildPairsJson([("کچاد", llmMetricTerm)], language: "fa");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest(userMessage, "fa", $"corr-{llmMetricTerm}", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        Assert.Single(result.Pairs);
        Assert.Equal("MONTHLY_SALES", result.Pairs.First().ResolvedMetricCode?.Value);
        Assert.Equal(llmMetricTerm, result.Pairs.First().OriginalMetricTerm);
    }

    [Theory]
    [InlineData("متوسط فروش 12 ماهه کچاد چقدر است؟", "AVG_12M_MONTHLY_SALES", "AVG_12M_MONTHLY_SALES")]
    [InlineData("متوسط فروش 12 ماهه کچاد چقدر است؟", "sales", "AVG_12M_MONTHLY_SALES")]
    [InlineData("فروش YTD کچاد چقدر است؟", "sales", "MONTHLY_SALES_YTD")]
    [InlineData("فروش YTD کچاد چقدر است؟", "فروش", "MONTHLY_SALES_YTD")]
    [InlineData("فروش YTD تا ماه قبل کچاد؟", "sales", "MONTHLY_SALES_YTD_PREVIOUS_MONTH")]
    [InlineData("فروش YTD تا ماه قبل کچاد؟", "فروش YTD", "MONTHLY_SALES_YTD_PREVIOUS_MONTH")]
    public async Task Parser_ExplicitMonthlySalesCompanionQuestion_PreservesRequestedMetric(
        string userMessage,
        string llmMetricTerm,
        string expectedMetricCode)
    {
        var resolver = BuildAliasResolver();
        var json = BuildPairsJson([("کچاد", llmMetricTerm)], language: "fa");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest(userMessage, "fa", $"corr-{expectedMetricCode}", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        Assert.Single(result.Pairs);
        Assert.Equal(expectedMetricCode, result.Pairs.First().ResolvedMetricCode?.Value);
        Assert.Equal(llmMetricTerm, result.Pairs.First().OriginalMetricTerm);
    }

    [Theory]
    [InlineData("درآمد فصلی کچاد", "درآمد فصلی")]
    [InlineData("فروش فصلی کچاد", "فروش فصلی")]
    public async Task Parser_ExplicitQuarterlyRevenue_DoesNotForceMonthlySales(
        string userMessage,
        string llmMetricTerm)
    {
        var resolver = BuildAliasResolver();
        var json = BuildPairsJson([("کچاد", llmMetricTerm)], language: "fa");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest(userMessage, "fa", $"corr-{llmMetricTerm}", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        Assert.Single(result.Pairs);
        Assert.Equal("REVENUE", result.Pairs.First().ResolvedMetricCode?.Value);
    }

    [Theory]
    [InlineData("PE")]
    [InlineData("P/E")]
    [InlineData("پی به ای")]
    [InlineData("پی‌ای")]
    [InlineData("نسبت قیمت به سود")]
    [InlineData("price-to-earnings")]
    public async Task Parser_PeAliases_ReturnResolvedPeTtmPair(string metricTerm)
    {
        var resolver = BuildAliasResolver();
        var language = metricTerm.Any(ch => ch is >= 'ا' and <= 'ی') ? "fa" : "en";
        var json = BuildPairsJson([("کگل", metricTerm)], language);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest($"{metricTerm} کگل", language, $"corr-{metricTerm}", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        Assert.Equal("PE_TTM", result.Pairs.First().ResolvedMetricCode?.Value);
    }

    [Theory]
    [InlineData("آخرین قیمت کچاد؟", "کچاد", "LATEST_PRICE", "آخرین قیمت")]
    [InlineData("قیمت کگل؟", "کگل", "LATEST_PRICE", "قیمت")]
    [InlineData("قیمت امروز کچاد؟", "کچاد", "LATEST_PRICE", "قیمت امروز")]
    [InlineData("قیمت پایانی کگل؟", "کگل", "LATEST_PRICE", "قیمت پایانی")]
    [InlineData("تغییر قیمت کگل؟", "کگل", "DAILY_CHANGE_PCT", "تغییر قیمت")]
    [InlineData("درصد تغییر قیمت کگل؟", "کگل", "DAILY_CHANGE_PCT", "درصد تغییر قیمت")]
    [InlineData("درصد تغییر روزانه کگل؟", "کگل", "DAILY_CHANGE_PCT", "درصد تغییر روزانه")]
    public async Task Parser_DirectQuoteQuestion_WhenLlmReturnsNoPairs_UsesDeterministicFallback(
        string userMessage,
        string expectedSymbolName,
        string expectedMetricCode,
        string expectedMetricTerm)
    {
        var resolver = BuildAliasResolver();
        var json = BuildClarificationJson("I could not extract symbol/metric pairs.");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest(userMessage, "fa", "corr-direct-quote-fallback", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        var pair = Assert.Single(result.Pairs);
        Assert.Equal(expectedSymbolName, pair.RawSymbolName);
        Assert.Equal(expectedMetricCode, pair.ResolvedMetricCode?.Value);
        Assert.Equal(expectedMetricTerm, pair.OriginalMetricTerm, ignoreCase: true);
    }

    [Theory]
    [InlineData("نسبت قیمت به سود کگل؟")]
    [InlineData("قیمت به سود کگل؟")]
    public async Task Parser_PePhraseContainingPrice_DoesNotResolveAsDirectPrice(string userMessage)
    {
        var resolver = BuildAliasResolver();
        var json = BuildClarificationJson("I could not extract symbol/metric pairs.");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest(userMessage, "fa", "corr-direct-pe-protection", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        var pair = Assert.Single(result.Pairs);
        Assert.Equal("کگل", pair.RawSymbolName);
        Assert.Equal("PE_TTM", pair.ResolvedMetricCode?.Value);
    }

    [Fact]
    public async Task Parser_MultipleSymbolsOnePair_ReturnsBothPairs()
    {
        var resolver = BuildAliasResolver();
        var json = BuildPairsJson([("فملی", "P/E"), ("کگل", "P/E")], language: "fa");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest("P/E فملی و کگل", "fa", "corr-3", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        Assert.Equal(2, result.Pairs.Count);
        Assert.All(result.Pairs, p => Assert.Equal("PE_TTM", p.ResolvedMetricCode?.Value));
    }

    [Fact]
    public async Task Parser_UnresolvableMetricTerm_SetsClarificationRequired()
    {
        var resolver = BuildAliasResolver();
        var json = BuildPairsJson([("حفاری", "metric_xyz_unknown")], language: "fa");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest("metric_xyz_unknown حفاری", "fa", "corr-4", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.ClarificationRequired, result.Status);
        Assert.NotNull(result.ClarificationMessage);
    }

    [Fact]
    public async Task Parser_EmptyLlmOutput_ReturnsClarificationRequired()
    {
        var resolver = BuildAliasResolver();
        var parser = BuildParser(string.Empty, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest("something", "fa", "corr-5", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.ClarificationRequired, result.Status);
    }

    [Fact]
    public async Task Parser_NoPairsInLlmOutput_ReturnsClarificationRequired()
    {
        var resolver = BuildAliasResolver();
        var json = BuildClarificationJson("I could not extract symbol/metric pairs.");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest("what time is it?", "en", "corr-6", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.ClarificationRequired, result.Status);
    }

    [Theory]
    [InlineData("نسبت پی به ای چادرملو؟", "چادرملو", "نسبت پی به ای")]
    [InlineData("P/E چادرملو", "چادرملو", "p/e")]
    [InlineData("نسبت قیمت به سود پالایش نفت اصفهان؟", "پالایش نفت اصفهان", "نسبت قیمت به سود")]
    public async Task Parser_DirectPeCompanyName_WhenLlmReturnsNoPairs_UsesDeterministicFallback(
        string userMessage,
        string expectedSymbolName,
        string expectedMetricTerm)
    {
        var resolver = BuildAliasResolver();
        var json = BuildClarificationJson("I could not extract symbol/metric pairs.");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest(userMessage, "fa", "corr-direct-pe-fallback", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        var pair = Assert.Single(result.Pairs);
        Assert.Equal(expectedSymbolName, pair.RawSymbolName);
        Assert.Equal("PE_TTM", pair.ResolvedMetricCode?.Value);
        Assert.Equal(expectedMetricTerm, pair.OriginalMetricTerm, ignoreCase: true);
    }

    [Theory]
    [InlineData("آخرین فروش چادرملو؟", "چادرملو", "MONTHLY_SALES", "آخرین فروش")]
    [InlineData("فروش ماهانه چادرملو؟", "چادرملو", "MONTHLY_SALES", "فروش ماهانه")]
    [InlineData("متوسط فروش 12 ماهه چادرملو؟", "چادرملو", "AVG_12M_MONTHLY_SALES", "متوسط فروش 12 ماهه")]
    [InlineData("فروش YTD چادرملو؟", "چادرملو", "MONTHLY_SALES_YTD", "فروش YTD")]
    [InlineData("فروش YTD تا ماه قبل چادرملو؟", "چادرملو", "MONTHLY_SALES_YTD_PREVIOUS_MONTH", "فروش YTD تا ماه قبل")]
    [InlineData("متوسط فروش ۱۲ ماهه چادرملو؟", "چادرملو", "AVG_12M_MONTHLY_SALES", "متوسط فروش 12 ماهه")]
    [InlineData("میانگین فروش 12 ماهه چادرملو؟", "چادرملو", "AVG_12M_MONTHLY_SALES", "میانگین فروش 12 ماهه")]
    [InlineData("میانگین فروش ۱۲ ماهه چادرملو؟", "چادرملو", "AVG_12M_MONTHLY_SALES", "میانگین فروش 12 ماهه")]
    [InlineData("فروش YTD تا ماه گذشته چادرملو؟", "چادرملو", "MONTHLY_SALES_YTD_PREVIOUS_MONTH", "فروش YTD تا ماه گذشته")]
    public async Task Parser_DirectMonthlySalesCompanyName_WhenLlmReturnsNoPairs_UsesDeterministicFallback(
        string userMessage,
        string expectedSymbolName,
        string expectedMetricCode,
        string expectedMetricTerm)
    {
        var resolver = BuildAliasResolver();
        var json = BuildClarificationJson("I could not extract symbol/metric pairs.");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest(userMessage, "fa", "corr-direct-monthly-sales-fallback", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        var pair = Assert.Single(result.Pairs);
        Assert.Equal(expectedSymbolName, pair.RawSymbolName);
        Assert.Equal(expectedMetricCode, pair.ResolvedMetricCode?.Value);
        Assert.Equal(expectedMetricTerm, pair.OriginalMetricTerm, ignoreCase: true);
    }

    [Theory]
    [InlineData("حاشیه سود خالص آخرین فصل کگل", "حاشیه سود خالص", "NET_PROFIT_MARGIN", SymbolLookupPeriodSelector.LatestQuarter)]
    [InlineData("حاشیه سود خالص فصل قبل کگل", "حاشیه سود خالص", "NET_PROFIT_MARGIN", SymbolLookupPeriodSelector.PreviousQuarter)]
    [InlineData("حاشیه سود ناخالص فصل مشابه سال قبل کچاد", "حاشیه سود ناخالص", "GROSS_PROFIT_MARGIN", SymbolLookupPeriodSelector.SameQuarterLastYear)]
    [InlineData("فروش ماه قبل کچاد", "فروش", "MONTHLY_SALES", SymbolLookupPeriodSelector.PreviousMonth)]
    [InlineData("فروش ماه مشابه سال قبل کچاد", "فروش", "MONTHLY_SALES", SymbolLookupPeriodSelector.SameMonthLastYear)]
    [InlineData("متوسط فروش 12 ماهه سال قبل کچاد", "متوسط فروش 12 ماهه", "AVG_12M_MONTHLY_SALES", SymbolLookupPeriodSelector.LastYearAverage12Month)]
    public async Task Parser_PeriodAwareDirectMetricQuestions_ReturnMetricAndSelector(
        string userMessage,
        string llmMetricTerm,
        string expectedMetricCode,
        SymbolLookupPeriodSelector expectedSelector)
    {
        var resolver = BuildAliasResolver();
        var json = BuildPairsJson([("کچاد", llmMetricTerm)], language: "fa");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest(userMessage, "fa", "corr-period-aware", TenantId, AsOf),
            CancellationToken.None);

        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        var pair = Assert.Single(result.Pairs);
        Assert.Equal(expectedMetricCode, pair.ResolvedMetricCode?.Value);
        Assert.Equal(expectedSelector, pair.PeriodSelector);
    }

    [Fact]
    public async Task Parser_MixedValidAndInvalidMetrics_ReturnsPartialResolution()
    {
        var resolver = BuildAliasResolver();
        var json = BuildPairsJson([("فملی", "P/E"), ("فملی", "metric_does_not_exist")], language: "fa");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest("فملی P/E و metric_does_not_exist", "fa", "corr-7", TenantId, AsOf),
            CancellationToken.None);

        // At least one valid pair → Parsed status; unresolved pair has null MetricCode
        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        Assert.Contains(result.Pairs, p => p.ResolvedMetricCode is not null);
        Assert.Contains(result.Pairs, p => p.ResolvedMetricCode is null);
    }

    // --- helpers ---

    [Fact]
    public void DirectMetricRegistry_ResolvesYtdBeforeGenericSalesAlias()
    {
        var resolver = BuildAliasResolver();
        var registry = new DirectMetricRoutingRegistry(
            resolver,
            new FinancialCopilot.Infrastructure.Financial.Semantics.DefaultMetricAliasExpressionNormalizer());

        var result = registry.TryResolve("فروش YTD چقدر بوده؟", AsOf);

        Assert.Equal("MONTHLY_SALES_YTD", result?.MetricCode.Value);
    }

    [Fact]
    public void DirectMetricRegistry_ResolvesGenericMonthlySalesAndAllCanonicalMetrics()
    {
        var resolver = BuildAliasResolver();
        var registry = new DirectMetricRoutingRegistry(
            resolver,
            new FinancialCopilot.Infrastructure.Financial.Semantics.DefaultMetricAliasExpressionNormalizer());

        var monthlySales = registry.TryResolve("فروش ماه قبل کچاد", AsOf);
        var all = registry.ResolveAll("PE و ROE فملی و حفاری", AsOf);
        var peLongForm = registry.ResolveAll("نسبت قیمت به سود کگل", AsOf);
        var dailyChange = registry.ResolveAll("درصد تغییر قیمت کگل", AsOf);

        Assert.Equal("MONTHLY_SALES", monthlySales?.MetricCode.Value);
        Assert.Equal(SymbolLookupPeriodSelector.PreviousMonth, monthlySales?.PeriodSelector);
        Assert.Equal(["PE_TTM", "RETURN_ON_EQUITY"], all.Select(match => match.MetricCode.Value));
        Assert.Equal(["PE_TTM"], peLongForm.Select(match => match.MetricCode.Value));
        Assert.Equal(["DAILY_CHANGE_PCT"], dailyChange.Select(match => match.MetricCode.Value));
    }

    private static LlmSymbolLookupParser BuildParser(string llmJson, IMetricAliasResolver resolver)
    {
        var execution = new StubAiModelExecutionService(llmJson);
        return new LlmSymbolLookupParser(
            execution,
            resolver,
            new DirectMetricRoutingRegistry(
                resolver,
                new FinancialCopilot.Infrastructure.Financial.Semantics.DefaultMetricAliasExpressionNormalizer()));
    }

    private static IMetricAliasResolver BuildAliasResolver() =>
        new MetricAliasResolver(new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []));

    private static string BuildPairsJson(
        IEnumerable<(string SymbolName, string MetricTerm)> pairs,
        string language = "fa") =>
        JsonSerializer.Serialize(new
        {
            detectedLanguage = language,
            pairs = pairs.Select(p => new { symbolName = p.SymbolName, metricTerm = p.MetricTerm }).ToArray(),
            clarificationRequired = false,
            clarificationMessage = (string?)null
        });

    private static string BuildClarificationJson(string message) =>
        JsonSerializer.Serialize(new
        {
            detectedLanguage = "fa",
            pairs = Array.Empty<object>(),
            clarificationRequired = true,
            clarificationMessage = message
        });

    private sealed class StubAiModelExecutionService(string json) : IAiModelExecutionService
    {
        public Task<AiModelResult> ExecuteAsync(
            AiModelSelectionRequest selection,
            AiModelRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiModelResult(
                Text: null,
                StructuredJson: json,
                ToolCalls: [],
                Usage: new AiExecutionUsageFacts(
                    request.CorrelationId,
                    "StubProvider",
                    "stub-model",
                    AiExecutionStatus.Completed,
                    TimeSpan.Zero,
                    AttemptNumber: 0)));

        public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
            AiModelSelectionRequest selection,
            AiModelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
