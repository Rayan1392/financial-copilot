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

    private static LlmSymbolLookupParser BuildParser(string llmJson, IMetricAliasResolver resolver)
    {
        var execution = new StubAiModelExecutionService(llmJson);
        return new LlmSymbolLookupParser(execution, resolver);
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
