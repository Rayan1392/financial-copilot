using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.UnitTests;

public sealed class ConfidenceScoreCalculatorTests
{
    private readonly ConfidenceScoreCalculator _sut = new();

    [Fact]
    public void Calculate_AllOptimal_ReturnsMaxScore()
    {
        var plan = MakePlan(clarification: false, [D.Condition("PE_TTM")]);
        var result = D.Result(plan.PlanId, [D.Row("A", "PE_TTM", 3.5m, live: true)]);

        var score = _sut.Calculate(plan, result);

        Assert.Equal(1.0, score.Score);
        Assert.Equal(1.0, score.Factors.InterpretationCertainty);
        Assert.Equal(1.0, score.Factors.EvidenceCompleteness);
        Assert.Equal(1.0, score.Factors.SourceFreshness);
        Assert.Equal(0.0, score.Factors.WarningPenalty);
    }

    [Fact]
    public void Calculate_ClarificationRequired_ZeroInterpretation()
    {
        var plan = MakePlan(clarification: true, []);

        var score = _sut.Calculate(plan, null);

        Assert.Equal(0.0, score.Factors.InterpretationCertainty);
        Assert.True(score.Score < 0.2);
    }

    [Fact]
    public void Calculate_OneInferredCondition_ReducesInterpretationByTenPercent()
    {
        var condition = new ScannerCondition(
            D.MetricRef("PE_TTM"),
            ConditionOperator.LessThan, 6m, FilterOrigin.InferredDefault);
        var plan = MakePlan(clarification: false, [condition]);

        var score = _sut.Calculate(plan, null);

        Assert.Equal(0.9, score.Factors.InterpretationCertainty);
    }

    [Fact]
    public void Calculate_MissingMetricForOneRow_HalfEvidenceCompleteness()
    {
        var plan = MakePlan(clarification: false, [D.Condition("PE_TTM")]);
        var result = D.Result(plan.PlanId, [
            D.Row("A", "PE_TTM", 3.5m, live: true),
            D.RowMissing("B", "PE_TTM")
        ]);

        var score = _sut.Calculate(plan, result);

        Assert.Equal(0.5, score.Factors.EvidenceCompleteness);
    }

    [Fact]
    public void Calculate_TwoWarnings_WarningPenaltyIsTwentyPercent()
    {
        var plan = MakePlan(clarification: false, [D.Condition("PE_TTM")],
            overflowWarnings: ["w1", "w2"]);
        var result = D.Result(plan.PlanId, [D.Row("A", "PE_TTM", 3.5m, live: true)]);

        var score = _sut.Calculate(plan, result);

        Assert.Equal(0.2, score.Factors.WarningPenalty);
        Assert.Equal(Math.Round(1.0 * 0.8, 2), score.Score);
    }

    [Fact]
    public void Calculate_NoRows_ZeroEvidenceAndNeutralFreshness()
    {
        var plan = MakePlan(clarification: false, [D.Condition("PE_TTM")]);
        var result = D.Result(plan.PlanId, []);

        var score = _sut.Calculate(plan, result);

        Assert.Equal(0.0, score.Factors.EvidenceCompleteness);
        Assert.Equal(0.5, score.Factors.SourceFreshness);
    }

    [Fact]
    public void Calculate_PolicyVersionIsV1()
    {
        var score = _sut.Calculate(MakePlan(clarification: false, []), null);
        Assert.Equal("v1", score.PolicyVersion);
    }

    private static ScannerQueryPlan MakePlan(
        bool clarification,
        IReadOnlyCollection<ScannerCondition> conditions,
        IReadOnlyCollection<string>? overflowWarnings = null) =>
        new(Guid.NewGuid(), "test", "en", conditions, [], clarification,
            clarification ? "needs clarification" : null,
            [], overflowWarnings ?? [], DateTimeOffset.UtcNow, "v1");
}

public sealed class ConfidenceScoringServiceTests
{
    private readonly ConfidenceScoringService _sut = new();

    [Fact]
    public void Calculate_PreCalculatedPeTtmAnswerMatchesTable_ReturnsAtLeastNinetyFivePercent()
    {
        var table = LookupTable("SHAPNA", "PE_TTM", 5.17m);

        var score = _sut.Calculate(new ConfidenceScoringRequest(
            "نسبت P/E نماد شپنا برابر است با 5.17",
            null,
            table,
            ConfidenceSourceType.PreCalculatedMetric,
            "corr"));

        Assert.True(score.Score >= 0.95);
        Assert.NotEqual(0.0, score.Score);
    }

    [Fact]
    public void Calculate_DerivedMetricCalculatedSuccessfully_ReturnsAtLeastEightyFivePercent()
    {
        var table = LookupTable("FOLAD", "NET_PROFIT_GROWTH_QOQ", 42.25m);

        var score = _sut.Calculate(new ConfidenceScoringRequest(
            "Net profit growth is 42.25",
            null,
            table,
            ConfidenceSourceType.DerivedMetric,
            "corr"));

        Assert.True(score.Score >= 0.85);
    }

    [Fact]
    public void Calculate_PartialDataUsed_ReturnsBetweenFiftyAndEightyPercent()
    {
        var table = LookupTable(
            [
                Row("FOLAD", "PE_TTM", 5.17m),
                RowMissing("SHAPNA", "PE_TTM")
            ],
            ["One requested symbol has no current PE_TTM value."]);

        var score = _sut.Calculate(new ConfidenceScoringRequest(
            "The available P/E value is 5.17",
            null,
            table,
            ConfidenceSourceType.LlmInference,
            "corr"));

        Assert.InRange(score.Score, 0.50, 0.80);
    }

    [Fact]
    public void Calculate_NoSupportingData_ReturnsAtMostThirtyPercent()
    {
        var table = LookupTable([RowMissing("FOLAD", "PE_TTM")], ["PE_TTM is unavailable."]);

        var score = _sut.Calculate(new ConfidenceScoringRequest(
            "No reliable P/E value is available.",
            null,
            table,
            ConfidenceSourceType.MissingDataFallback,
            "corr"));

        Assert.True(score.Score <= 0.30);
    }

    private static SymbolLookupTableResult LookupTable(string symbol, string metricCode, decimal value) =>
        LookupTable([Row(symbol, metricCode, value)], []);

    private static SymbolLookupTableResult LookupTable(
        IReadOnlyCollection<ScannerTableRow> rows,
        IReadOnlyCollection<string> warnings)
    {
        var metricCode = rows
            .SelectMany(r => r.Cells.Keys)
            .First(k => k != "SYMBOL");

        return new SymbolLookupTableResult(
            Guid.NewGuid(),
            [
                new ScannerTableColumn("SYMBOL", "Symbol", ScannerColumnType.Symbol),
                new ScannerTableColumn(metricCode, metricCode, ScannerColumnType.Metric, metricCode)
            ],
            rows,
            new ScannerExecutionFacts(DateTimeOffset.UtcNow, TimeSpan.Zero, rows.Count, rows.Count, false),
            warnings,
            []);
    }

    private static ScannerTableRow Row(string symbol, string metricCode, decimal value)
    {
        var now = DateTimeOffset.UtcNow;
        return new ScannerTableRow(
            symbol,
            null,
            new Dictionary<string, ScannerTableCell>
            {
                ["SYMBOL"] = new(null, symbol, CellFreshnessStatus.Persisted, null),
                [metricCode] = new(value, value.ToString("N2"), CellFreshnessStatus.Persisted, now)
            },
            1.0,
            []);
    }

    private static ScannerTableRow RowMissing(string symbol, string metricCode) =>
        new(
            symbol,
            null,
            new Dictionary<string, ScannerTableCell>
            {
                ["SYMBOL"] = new(null, symbol, CellFreshnessStatus.Persisted, null),
                [metricCode] = new(null, null, CellFreshnessStatus.Missing, null)
            },
            0.0,
            []);
}

public sealed class ExplainableAnswerBuilderTests
{
    [Fact]
    public async Task Build_FilterChips_ReflectPlanConditions()
    {
        var plan = MakePlan([D.Condition("PE_TTM", ConditionOperator.LessThan, 6m)]);
        var builder = MakeBuilder();

        var answer = await builder.BuildAsync(
            new ExplainableAnswerRequest(plan, null, Guid.NewGuid(), "corr"),
            CancellationToken.None);

        Assert.Single(answer.FilterChips);
        var chip = answer.FilterChips.Single();
        Assert.Equal("PE_TTM", chip.MetricCode);
        Assert.Equal("<", chip.OperatorSymbol);
        Assert.Equal("below", chip.OperatorLabel);
        Assert.Equal(6m, chip.Threshold);
        Assert.Equal("6", chip.ThresholdFormatted);
    }

    [Fact]
    public async Task Build_FilterChips_ExplicitOriginNotMarkedInferred()
    {
        var plan = MakePlan([D.Condition("PE_TTM", ConditionOperator.LessThan, 6m, FilterOrigin.Explicit)]);

        var answer = await MakeBuilder().BuildAsync(
            new ExplainableAnswerRequest(plan, null, Guid.NewGuid(), "corr"),
            CancellationToken.None);

        Assert.False(answer.FilterChips.Single().IsInferred);
    }

    [Fact]
    public async Task Build_FilterChips_InferredOriginIsMarked()
    {
        var condition = new ScannerCondition(
            D.MetricRef("PE_TTM"), ConditionOperator.LessThan, 6m,
            FilterOrigin.InferredDefault, "inferred reason");
        var plan = MakePlan([condition]);

        var answer = await MakeBuilder().BuildAsync(
            new ExplainableAnswerRequest(plan, null, Guid.NewGuid(), "corr"),
            CancellationToken.None);

        var chip = answer.FilterChips.Single();
        Assert.True(chip.IsInferred);
        Assert.Equal("InferredDefault", chip.FilterOrigin);
        Assert.Equal("inferred reason", chip.InferredReason);
    }

    [Fact]
    public async Task Build_MetricEvidence_HasActualValueFromRow()
    {
        var plan = MakePlan([D.Condition("PE_TTM", ConditionOperator.LessThan, 6m)]);
        var result = D.Result(plan.PlanId, [D.Row("A", "PE_TTM", 3.5m, live: false)]);

        var answer = await MakeBuilder().BuildAsync(
            new ExplainableAnswerRequest(plan, result, Guid.NewGuid(), "corr"),
            CancellationToken.None);

        var ev = answer.MetricEvidence.Single();
        Assert.Equal("PE_TTM", ev.MetricCode);
        Assert.Equal(3.5m, ev.ActualValue);
        Assert.Equal("v1", ev.MetricVersion);
        Assert.Equal("PE_TTM_v1", ev.CalculationPolicyVersion);
    }

    [Fact]
    public async Task Build_MetricEvidence_NullValueWhenAllCellsMissing()
    {
        var plan = MakePlan([D.Condition("PE_TTM", ConditionOperator.LessThan, 6m)]);
        var result = D.Result(plan.PlanId, [D.RowMissing("A", "PE_TTM")]);

        var answer = await MakeBuilder().BuildAsync(
            new ExplainableAnswerRequest(plan, result, Guid.NewGuid(), "corr"),
            CancellationToken.None);

        Assert.Null(answer.MetricEvidence.Single().ActualValue);
    }

    [Fact]
    public async Task Build_ConfidenceScore_InRange()
    {
        var plan = MakePlan([D.Condition("PE_TTM", ConditionOperator.LessThan, 6m)]);
        var result = D.Result(plan.PlanId, [D.Row("A", "PE_TTM", 3.5m, live: true)]);

        var answer = await MakeBuilder().BuildAsync(
            new ExplainableAnswerRequest(plan, result, Guid.NewGuid(), "corr"),
            CancellationToken.None);

        Assert.InRange(answer.Confidence.Score, 0.0, 1.0);
        Assert.Equal("v1", answer.Confidence.PolicyVersion);
    }

    [Fact]
    public async Task Build_SuggestedQuestions_ReturnedFromGenerator()
    {
        var plan = MakePlan([D.Condition("PE_TTM", ConditionOperator.LessThan, 6m)]);
        var result = D.Result(plan.PlanId, [D.Row("A", "PE_TTM", 3.5m, live: true)]);

        var answer = await MakeBuilder(suggestions: ["Q1", "Q2"]).BuildAsync(
            new ExplainableAnswerRequest(plan, result, Guid.NewGuid(), "corr"),
            CancellationToken.None);

        Assert.Equal(2, answer.SuggestedFollowUpQuestions.Count);
        Assert.Contains("Q1", answer.SuggestedFollowUpQuestions);
    }

    [Fact]
    public async Task Build_GeneratorThrows_DeterministicEvidenceStillReturned()
    {
        var plan = MakePlan([D.Condition("PE_TTM", ConditionOperator.LessThan, 6m)]);
        var result = D.Result(plan.PlanId, [D.Row("A", "PE_TTM", 3.5m, live: true)]);

        var answer = await MakeBuilder(throwOnGenerate: true).BuildAsync(
            new ExplainableAnswerRequest(plan, result, Guid.NewGuid(), "corr"),
            CancellationToken.None);

        Assert.NotNull(answer.Confidence);
        Assert.NotEmpty(answer.FilterChips);
        Assert.Empty(answer.SuggestedFollowUpQuestions);
        Assert.Null(answer.ExplanationText);
    }

    [Fact]
    public async Task Build_SalesGrowth_UsesDeterministicPersianFramingAndDefaultDisclosure()
    {
        var salesPlan = new SalesGrowthScannerPlan(
            new SalesGrowthScannerSemantics(
                SalesGrowthComparisonBaseline.SameMonthPreviousYear,
                SalesGrowthThresholdKind.Percent,
                ConditionOperator.GreaterThan,
                30m,
                FilterOrigin.InferredDefault,
                SalesGrowthPolicyVersions.V1));
        var plan = new ScannerQueryPlan(
            Guid.NewGuid(),
            "رشد فروش نمادها",
            "fa",
            [new ScannerCondition(
                D.MetricRef("MONTHLY_SALES_GROWTH_YOY"),
                ConditionOperator.GreaterThan,
                30m,
                FilterOrigin.InferredDefault)],
            [],
            false,
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            "v1",
            salesPlan);
        var result = D.Result(plan.PlanId, [D.Row("A", "MONTHLY_SALES_GROWTH_PERCENT", 42m, live: false)]) with
        {
            SalesGrowthMetadata = new SalesGrowthTableMetadata(
                new DateOnly(2026, 6, 1),
                8,
                10,
                80m,
                SalesGrowthCommonPeriodSelectionStatus.Available,
                SalesGrowthPolicyVersions.V1.TargetPeriod,
                SalesGrowthPolicyVersions.V1.Calculation,
                false,
                null)
        };

        var answer = await MakeBuilder(throwOnGenerate: true).BuildAsync(
            new ExplainableAnswerRequest(plan, result, Guid.NewGuid(), "corr"),
            CancellationToken.None);

        Assert.Contains("ماه مشابه سال قبل", answer.ExplanationText);
        Assert.Contains("۳۰٪", answer.ExplanationText);
        Assert.Contains("مبنای مقایسه مشخص نشده بود", answer.ExplanationText);
        Assert.Contains("پوشش دوره مشترک", answer.ExplanationText);
        Assert.NotNull(answer.ExplanationText);
        Assert.True(answer.FilterChips.Single().IsInferred);
        Assert.Equal(SalesGrowthSymbolScanner.Intent, answer.FilterChips.Single().MetricCode);
    }

    [Fact]
    public async Task Build_SalesGrowth_EmptyPartialResultDisclosesUnavailableStatus()
    {
        var salesPlan = SalesGrowthScannerPlan.CreateInferredDefault();
        var plan = new ScannerQueryPlan(
            Guid.NewGuid(),
            "sales growth",
            "fa",
            [],
            [],
            false,
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            "v1",
            salesPlan);
        var result = D.Result(plan.PlanId, []) with
        {
            MissingDataWarnings = ["No common evaluation period is available."],
            SalesGrowthMetadata = new SalesGrowthTableMetadata(
                new DateOnly(2026, 6, 1),
                2,
                10,
                20m,
                SalesGrowthCommonPeriodSelectionStatus.Unavailable,
                SalesGrowthPolicyVersions.V1.TargetPeriod,
                SalesGrowthPolicyVersions.V1.Calculation,
                false,
                "No period met the minimum coverage policy.")
        };

        var answer = await MakeBuilder(throwOnGenerate: true).BuildAsync(
            new ExplainableAnswerRequest(plan, result, Guid.NewGuid(), "corr"),
            CancellationToken.None);

        Assert.Contains("نماد منطبقی یافت نشد", answer.ExplanationText);
        Assert.Contains("رتبه‌بندی انجام نشد", answer.ExplanationText);
        Assert.Contains("در دسترس نیست", answer.ExplanationText);
    }

    // --- helpers ---

    private static ExplainableAnswerBuilder MakeBuilder(
        IReadOnlyCollection<string>? suggestions = null,
        bool throwOnGenerate = false) =>
        new(new ConfidenceScoreCalculator(),
            new FakeGenerator("Explanation.", suggestions ?? ["Q1"], throwOnGenerate),
            new ThrowingRegistry(),
            TimeProvider.System);

    private static ScannerQueryPlan MakePlan(IReadOnlyCollection<ScannerCondition> conditions) =>
        new(Guid.NewGuid(), "test query", "en", conditions, [], false, null, [], [],
            DateTimeOffset.UtcNow, "v1");

    private sealed class FakeGenerator(string? text, IReadOnlyCollection<string> questions, bool shouldThrow)
        : IScannerExplanationGenerator
    {
        public Task<ScannerExplanationOutput> GenerateAsync(
            ScannerExplanationRequest request, CancellationToken cancellationToken)
        {
            if (shouldThrow) throw new InvalidOperationException("AI unavailable");
            return Task.FromResult(new ScannerExplanationOutput(text, questions));
        }
    }

    private sealed class ThrowingRegistry : IFinancialMetricRegistry
    {
        public FinancialMetricDefinition ResolveDefinition(MetricCode code, DateOnly asOf) =>
            throw new InvalidOperationException($"Not found: {code.Value}");
        public IFinancialMetricCalculator ResolveCalculator(MetricCode code) => throw new NotImplementedException();
        public IReadOnlyCollection<FinancialMetricDefinition> GetSupportedMetrics(DateOnly asOf) => [];
    }
}

public sealed class LlmScannerExplanationGeneratorBuildUserContentTests
{
    private static ScannerExplanationRequest MakeRequest(
        int totalCount,
        IReadOnlyCollection<string> pageSymbols,
        string query = "pe < 5") =>
        new(query, totalCount, pageSymbols, [], Guid.Empty, "corr");

    [Fact]
    public void BuildUserContent_AllSymbolsFitOnPage_ListsAllWithoutSampleLabel()
    {
        var request = MakeRequest(3, ["A", "B", "C"]);

        var content = LlmScannerExplanationGenerator.BuildUserContent(request);

        Assert.Contains("Found 3 symbol(s): A, B, C", content);
        Assert.DoesNotContain("page", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do not name", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildUserContent_TotalExceedsPage_FramesAsPageSample()
    {
        var request = MakeRequest(247, ["خچرخش", "خزر", "دتولید", "شخارک", "غدام"]);

        var content = LlmScannerExplanationGenerator.BuildUserContent(request);

        Assert.Contains("247", content);
        Assert.Contains("5 symbol(s)", content);
        Assert.Contains("خچرخش", content);
        Assert.Contains("do not name", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Take", content);
    }

    [Fact]
    public void BuildUserContent_TotalExceedsPage_ContainsAllPageSymbols()
    {
        // Regression: .Take(5) was dropping symbols beyond index 4.
        // With 8 page symbols, all 8 must appear in the content.
        var pageSymbols = Enumerable.Range(1, 8).Select(i => $"SYM{i}").ToList();
        var request = MakeRequest(500, pageSymbols);

        var content = LlmScannerExplanationGenerator.BuildUserContent(request);

        foreach (var sym in pageSymbols)
            Assert.Contains(sym, content);
    }

    [Fact]
    public void BuildUserContent_NoSymbolsOnPage_SaysNoSymbols()
    {
        var request = MakeRequest(0, []);

        var content = LlmScannerExplanationGenerator.BuildUserContent(request);

        Assert.Contains("No symbols on this page", content);
    }

    [Fact]
    public void BuildUserContent_FiltersAppearInContent()
    {
        var chips = new[]
        {
            new ConditionFilterChip("PE_TTM", "P/E", "<", "below", 5m, "5", "Explicit", false, null)
        };
        var request = new ScannerExplanationRequest("pe < 5", 2, ["A", "B"], chips, Guid.Empty, "corr");

        var content = LlmScannerExplanationGenerator.BuildUserContent(request);

        Assert.Contains("P/E", content);
        Assert.Contains("below", content);
        Assert.Contains("5", content);
    }
}

// D = shared test data factory used across both test classes in this file
internal static class D
{
    public static ScannerMetricReference MetricRef(string code) =>
        new(code, new MetricCode(code), new MetricVersion("v1"),
            new CalculationPolicyVersion($"{code}_v1"),
            FiscalPeriodType.TrailingTwelveMonths, null);

    public static ScannerCondition Condition(
        string code,
        ConditionOperator op = ConditionOperator.LessThan,
        decimal threshold = 6m,
        FilterOrigin origin = FilterOrigin.Explicit) =>
        new(MetricRef(code), op, threshold, origin);

    public static ScannerTableResult Result(
        Guid planId,
        IReadOnlyCollection<ScannerTableRow> rows,
        IReadOnlyCollection<string>? warnings = null) =>
        new(planId, [], rows,
            new ScannerExecutionFacts(DateTimeOffset.UtcNow, TimeSpan.Zero, rows.Count, rows.Count, false),
            warnings ?? []);

    public static ScannerTableRow Row(string symbol, string metricCode, decimal value, bool live)
    {
        var priceFreshness = live ? CellFreshnessStatus.Live : CellFreshnessStatus.Persisted;
        var now = DateTimeOffset.UtcNow;
        return new ScannerTableRow(symbol, null,
            new Dictionary<string, ScannerTableCell>
            {
                [metricCode] = new(value, value.ToString("N2"), CellFreshnessStatus.Persisted, now),
                ["LATEST_PRICE"] = new(100m, "100.00", priceFreshness, now)
            },
            0.0, [metricCode]);
    }

    public static ScannerTableRow RowMissing(string symbol, string metricCode)
    {
        var now = DateTimeOffset.UtcNow;
        return new ScannerTableRow(symbol, null,
            new Dictionary<string, ScannerTableCell>
            {
                [metricCode] = new(null, null, CellFreshnessStatus.Missing, null),
                ["LATEST_PRICE"] = new(100m, "100.00", CellFreshnessStatus.Persisted, now)
            },
            0.0, []);
    }
}
