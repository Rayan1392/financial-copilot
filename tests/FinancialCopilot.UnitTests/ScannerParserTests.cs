using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.UnitTests;

public sealed class ScannerParserTests
{
    private static readonly DateOnly AsOf = new(2026, 5, 27);
    private static readonly Guid TenantId = Guid.Parse("9a1b2c3d-4e5f-6789-abcd-ef0123456789");

    // --- ScannerQueryPlanValidator ---

    [Fact]
    public void Validator_AcceptsPlanWithAtLeastOneCondition()
    {
        var plan = MakePlan(conditions: [MakeCondition("PE_TTM")]);
        var validator = new ScannerQueryPlanValidator();

        Assert.Null(validator.Validate(plan));
    }

    [Fact]
    public void Validator_AcceptsPlanWithNoClarificationAndClarificationRequired()
    {
        var plan = new ScannerQueryPlan(
            Guid.NewGuid(), "test query", "en", [],
            [], true, "Needs clarification",
            [], [], DateTimeOffset.UtcNow, "v1");
        var validator = new ScannerQueryPlanValidator();

        Assert.Null(validator.Validate(plan));
    }

    [Fact]
    public void Validator_RejectsEmptyUserQuery()
    {
        var plan = new ScannerQueryPlan(
            Guid.NewGuid(), string.Empty, "en", [MakeCondition("PE_TTM")],
            [], false, null, [], [], DateTimeOffset.UtcNow, "v1");
        var validator = new ScannerQueryPlanValidator();

        Assert.NotNull(validator.Validate(plan));
    }

    [Fact]
    public void Validator_RejectsPlanWithNoConditionsAndNoClarification()
    {
        var plan = new ScannerQueryPlan(
            Guid.NewGuid(), "find stocks", "en", [],
            [], false, null, [], [], DateTimeOffset.UtcNow, "v1");
        var validator = new ScannerQueryPlanValidator();

        Assert.NotNull(validator.Validate(plan));
    }

    [Fact]
    public void Validator_RejectsExcessiveColumnCount()
    {
        var columns = Enumerable.Range(0, 11)
            .Select(i => new ScannerColumnRequest($"col{i}", IsUserRequested: true))
            .ToList();
        var plan = new ScannerQueryPlan(
            Guid.NewGuid(), "find stocks", "en", [MakeCondition("PE_TTM")],
            columns, false, null, [], [], DateTimeOffset.UtcNow, "v1");
        var validator = new ScannerQueryPlanValidator();

        Assert.NotNull(validator.Validate(plan));
    }

    // --- LlmScannerQueryParser ---

    [Fact]
    public async Task Parser_ResolvesEnglishPeRatio_ToCanonicalMetricCode()
    {
        var resolver = BuildAliasResolver();
        var json = BuildConditionsJson("P/E", "LessThan", 6.0m);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("P/E below 6", "en", "corr-1", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Plan.Conditions);
        Assert.Equal("PE_TTM", result.Plan.Conditions.First().MetricReference.MetricCode.Value);
    }

    [Fact]
    public async Task Parser_ResolvesBarePeTerm_ToCanonicalMetricCode()
    {
        var resolver = BuildAliasResolver();
        var json = BuildConditionsJson("PE", "LessThan", 6.0m);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("PE below 6", "en", "corr-pe", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Plan.Conditions);
        Assert.Equal("PE_TTM", result.Plan.Conditions.First().MetricReference.MetricCode.Value);
    }

    [Fact]
    public async Task Parser_ResolvesBarePsTerm_ToCanonicalMetricCode()
    {
        var resolver = BuildAliasResolver();
        var json = BuildConditionsJson("PS", "LessThan", 1.5m);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("PS below 1.5", "en", "corr-ps", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Plan.Conditions);
        Assert.Equal("PS_TTM", result.Plan.Conditions.First().MetricReference.MetricCode.Value);
    }

    [Fact]
    public async Task Parser_ResolvesPersianPeRatio_ToCanonicalMetricCode()
    {
        var resolver = BuildAliasResolver();
        // "نسبت پی به ای" is the registered Persian alias for PE_TTM
        var json = BuildConditionsJson("نسبت پی به ای", "LessThan", 6.0m, language: "fa");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("نسبت P/E زیر ۶", "fa", "corr-2", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Plan.Conditions);
        Assert.Equal("PE_TTM", result.Plan.Conditions.First().MetricReference.MetricCode.Value);
    }

    [Fact]
    public async Task Parser_UnrecognizedTerminology_SetsClarificationRequired()
    {
        var resolver = BuildAliasResolver();
        var json = BuildConditionsJson("supersecret_metric_xyz", "GreaterThan", 100m);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("supersecret_metric_xyz above 100", "en", "corr-3", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Plan.ClarificationRequired);
        Assert.Empty(result.Plan.Conditions);
        Assert.NotEmpty(result.Plan.ClarificationItems);
    }

    [Fact]
    public async Task Parser_EnforcesMaxTenColumnLimit()
    {
        var resolver = BuildAliasResolver();
        var columns = Enumerable.Range(0, 13).Select(i => $"col{i}").ToList();
        var json = BuildConditionsJsonWithColumns("P/E", "LessThan", 6.0m, columns);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("P/E below 6 show all columns", "en", "corr-4", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Plan.RequestedColumns.Count <= ScannerQueryPlan.MaxDisplayColumns);
        Assert.NotEmpty(result.Plan.ColumnOverflowWarnings);
    }

    [Fact]
    public async Task Parser_NeverAcceptsHardcodedPropertyGuess_ForUnknownTerm()
    {
        var resolver = BuildAliasResolver();
        // Completely made-up term that should NOT resolve
        var json = BuildConditionsJson("profit_magic_v99", "GreaterThan", 50m);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("profit_magic_v99 above 50", "en", "corr-5", TenantId, AsOf),
            CancellationToken.None);

        // The LLM candidate was NOT resolved — no condition may carry a made-up code
        Assert.Empty(result.Plan.Conditions);
        Assert.True(result.Plan.ClarificationRequired);
    }

    [Fact]
    public async Task Parser_ExplicitConditionOrigin_WhenUserStatedTerm()
    {
        var resolver = BuildAliasResolver();
        var json = BuildConditionsJson("P/E", "LessThan", 6.0m);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("P/E below 6", "en", "corr-6", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(FilterOrigin.Explicit, result.Plan.Conditions.First().Origin);
    }

    [Fact]
    public async Task Parser_EmptyLlmOutput_ReturnsClarificationRequired()
    {
        var resolver = BuildAliasResolver();
        var parser = BuildParser(string.Empty, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("find stocks", "en", "corr-7", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Plan.ClarificationRequired);
    }

    // --- helpers ---

    private static LlmScannerQueryParser BuildParser(string llmJson, IMetricAliasResolver resolver)
    {
        var execution = new StubAiModelExecutionService(llmJson);
        var validator = new ScannerQueryPlanValidator();
        return new LlmScannerQueryParser(execution, resolver, validator, TimeProvider.System);
    }

    private static IMetricAliasResolver BuildAliasResolver() =>
        new MetricAliasResolver(new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []));

    private static string BuildConditionsJson(
        string terminology,
        string @operator,
        decimal threshold,
        string language = "en",
        string? periodHint = null) =>
        JsonSerializer.Serialize(new
        {
            detectedLanguage = language,
            conditions = new[]
            {
                new
                {
                    userTerminology = terminology,
                    language,
                    @operator,
                    threshold,
                    periodHint,
                    growthComparison = (string?)null,
                    inferredDefault = false,
                    inferredReason = (string?)null
                }
            },
            requestedColumns = Array.Empty<string>(),
            clarificationRequired = false,
            clarificationMessage = (string?)null
        });

    private static string BuildConditionsJsonWithColumns(
        string terminology,
        string @operator,
        decimal threshold,
        IEnumerable<string> columns) =>
        JsonSerializer.Serialize(new
        {
            detectedLanguage = "en",
            conditions = new[]
            {
                new
                {
                    userTerminology = terminology,
                    language = "en",
                    @operator,
                    threshold,
                    periodHint = (string?)null,
                    growthComparison = (string?)null,
                    inferredDefault = false,
                    inferredReason = (string?)null
                }
            },
            requestedColumns = columns,
            clarificationRequired = false,
            clarificationMessage = (string?)null
        });

    private static ScannerCondition MakeCondition(string metricCode) =>
        new(
            new ScannerMetricReference(
                metricCode,
                new MetricCode(metricCode),
                new MetricVersion("v1"),
                new CalculationPolicyVersion($"{metricCode}_v1"),
                FiscalPeriodType.TrailingTwelveMonths,
                null),
            ConditionOperator.LessThan,
            6m,
            FilterOrigin.Explicit);

    private static ScannerQueryPlan MakePlan(IReadOnlyCollection<ScannerCondition> conditions) =>
        new(
            Guid.NewGuid(),
            "test query",
            "en",
            conditions,
            [],
            false,
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            "v1");

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
