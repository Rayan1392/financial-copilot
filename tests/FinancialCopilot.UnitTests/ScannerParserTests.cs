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
    public async Task Parser_ConditionMetricNotCopiedIntoRequestedColumns()
    {
        var resolver = BuildAliasResolver();
        var json = BuildConditionsJsonWithColumns("P/E", "LessThan", 6.0m, ["P/E"]);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("P/E below 6", "en", "corr-condition-column", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("PE_TTM", result.Plan.Conditions.Single().MetricReference.MetricCode.Value);
        Assert.Empty(result.Plan.RequestedColumns);
    }

    [Fact]
    public async Task Parser_ExplicitExtraRequestedMetric_IsCanonicalized()
    {
        var resolver = BuildAliasResolver();
        var json = BuildConditionsJsonWithColumns(
            "P/E",
            "LessThan",
            6.0m,
            ["NetProfitMargin"]);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("P/E below 6 and show net profit margin", "en", "corr-extra-column", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var column = Assert.Single(result.Plan.RequestedColumns);
        Assert.Equal("NET_PROFIT_MARGIN", column.Identifier);
    }

    [Fact]
    public async Task Parser_PersianStandardColumnSynonyms_AreIgnoredFromRequestedColumns()
    {
        var resolver = BuildAliasResolver();
        var json = BuildConditionsJsonWithColumns(
            "نسبت پی به ای",
            "LessThan",
            6.0m,
            ["نماد", "نام نماد", "شرکت", "نام شرکت", "قیمت", "آخرین قیمت", "درصد تغییر", "تغییر قیمت", "درصد تغییر آخرین قیمت", "ارزش بازار"],
            language: "fa");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("نسبت P/E زیر ۶", "fa", "corr-persian-standard-columns", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Plan.RequestedColumns);
    }

    [Fact]
    public async Task Parser_DuplicateRequestedColumns_AppearOnlyOnce()
    {
        var resolver = BuildAliasResolver();
        var json = BuildConditionsJsonWithColumns(
            "P/E",
            "LessThan",
            6.0m,
            ["net profit margin", "NET_PROFIT_MARGIN", "Net Profit Margin"]);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("P/E below 6 show net profit margin", "en", "corr-duplicate-columns", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var column = Assert.Single(result.Plan.RequestedColumns);
        Assert.Equal("NET_PROFIT_MARGIN", column.Identifier);
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

    [Fact]
    public async Task Parser_Feature116SalesGrowthQuery_UsesDeterministicMonthlyYoyPlan()
    {
        var resolver = BuildAliasResolver();
        var parser = BuildParser(string.Empty, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("سهام با رشد فروش بالای 100 درصد؟", "fa", "corr-feature-116-bug", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Plan.ClarificationRequired);
        Assert.Equal("MONTHLY_SALES_GROWTH_YOY", result.Plan.Conditions.Single().MetricReference.MetricCode.Value);
        Assert.Equal(ConditionOperator.GreaterThan, result.Plan.Conditions.Single().Operator);
        Assert.Equal(100m, result.Plan.Conditions.Single().Threshold);
        Assert.NotNull(result.Plan.SalesGrowth);
        Assert.Equal(
            SalesGrowthComparisonBaseline.SameMonthPreviousYear,
            result.Plan.SalesGrowth.Semantics.Baseline);
        Assert.Equal(SalesGrowthThresholdKind.Percent, result.Plan.SalesGrowth.Semantics.ThresholdKind);
    }

    [Fact]
    public async Task Parser_Feature116SalesGrowthQuery_RecognizesSalesBeforeGrowthPhraseForMom()
    {
        var resolver = BuildAliasResolver();
        var parser = BuildParser(string.Empty, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest(
                "\u0644\u06cc\u0633\u062a \u0633\u0647\u0645\u200c\u0647\u0627\u06cc\u06cc \u06a9\u0647 \u0641\u0631\u0648\u0634 \u0627\u06cc\u0646 \u0645\u0627\u0647\u0634\u0627\u0646 \u0646\u0633\u0628\u062a \u0628\u0647 \u0645\u0627\u0647 \u0642\u0628\u0644 \u0628\u06cc\u0634 \u0627\u0632 100 \u062f\u0631\u0635\u062f \u0631\u0634\u062f \u06a9\u0631\u062f\u0647",
                "fa",
                "corr-feature-116-mom-phrase",
                TenantId,
                AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Plan.ClarificationRequired);
        Assert.Equal("MONTHLY_SALES_GROWTH_MOM", result.Plan.Conditions.Single().MetricReference.MetricCode.Value);
        Assert.Equal(ConditionOperator.GreaterThan, result.Plan.Conditions.Single().Operator);
        Assert.Equal(100m, result.Plan.Conditions.Single().Threshold);
        Assert.Equal(SalesGrowthComparisonBaseline.PreviousMonth, result.Plan.SalesGrowth!.Semantics.Baseline);
    }

    [Fact]
    public async Task Parser_Feature116SalesMultiple_RecognizesSameMonthPreviousYearWithoutGrowthWord()
    {
        var parser = BuildParser(string.Empty, BuildAliasResolver());

        var result = await parser.ParseAsync(
            new ScannerParseRequest(
                "\u0646\u0645\u0627\u062f\u0647\u0627\u06cc\u06cc \u06a9\u0647 \u0641\u0631\u0648\u0634\u0634\u0627\u0646 \u062d\u062f\u0627\u0642\u0644 \u06f2 \u0628\u0631\u0627\u0628\u0631 \u0645\u0627\u0647 \u0645\u0634\u0627\u0628\u0647 \u0633\u0627\u0644 \u0642\u0628\u0644 \u0634\u062f\u0647",
                "fa",
                "corr-feature-116-yoy-multiple",
                TenantId,
                AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Plan.ClarificationRequired);
        Assert.Equal("MONTHLY_SALES_GROWTH_YOY", result.Plan.Conditions.Single().MetricReference.MetricCode.Value);
        Assert.Equal(ConditionOperator.GreaterThanOrEqual, result.Plan.Conditions.Single().Operator);
        Assert.Equal(2m, result.Plan.Conditions.Single().Threshold);
        Assert.Equal(SalesGrowthComparisonBaseline.SameMonthPreviousYear, result.Plan.SalesGrowth!.Semantics.Baseline);
        Assert.Equal(SalesGrowthThresholdKind.Multiple, result.Plan.SalesGrowth.Semantics.ThresholdKind);
    }

    [Fact]
    public async Task Parser_Feature116_RealPersianQueries_BypassLlmAliasRewrites()
    {
        var cases = new[]
        {
            (
                Query: "\u0633\u0647\u0627\u0645 \u0628\u0627 \u0631\u0634\u062f \u0641\u0631\u0648\u0634 \u0628\u0627\u0644\u0627\u06cc 100 \u062f\u0631\u0635\u062f\u061f",
                Baseline: SalesGrowthComparisonBaseline.SameMonthPreviousYear,
                Kind: SalesGrowthThresholdKind.Percent,
                Operator: ConditionOperator.GreaterThan,
                Threshold: 100m),
            (
                Query: "\u0633\u0647\u0627\u0645 \u0628\u0627 \u0631\u0634\u062f \u0641\u0631\u0648\u0634 \u0628\u0627\u0644\u0627\u06cc 40 \u062f\u0631\u0635\u062f\u061f",
                Baseline: SalesGrowthComparisonBaseline.SameMonthPreviousYear,
                Kind: SalesGrowthThresholdKind.Percent,
                Operator: ConditionOperator.GreaterThan,
                Threshold: 40m),
            (
                Query: "\u0644\u06cc\u0633\u062a \u0646\u0645\u0627\u062f\u0647\u0627\u06cc \u0628\u0627 \u0631\u0634\u062f \u0641\u0631\u0648\u0634 \u0628\u0627\u0644\u0627\u06cc \u06f3\u06f0 \u062f\u0631\u0635\u062f \u0646\u0633\u0628\u062a \u0628\u0647 \u0633\u0627\u0644 \u06af\u0630\u0634\u062a\u0647",
                Baseline: SalesGrowthComparisonBaseline.SameMonthPreviousYear,
                Kind: SalesGrowthThresholdKind.Percent,
                Operator: ConditionOperator.GreaterThan,
                Threshold: 30m),
            (
                Query: "\u0634\u0631\u06a9\u062a\u200c\u0647\u0627\u06cc\u06cc \u06a9\u0647 \u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647\u200c\u0634\u0627\u0646 \u06f1.\u06f5 \u0628\u0631\u0627\u0628\u0631 \u0645\u06cc\u0627\u0646\u06af\u06cc\u0646 \u06f1\u06f2 \u0645\u0627\u0647\u0647 \u0627\u0633\u062a",
                Baseline: SalesGrowthComparisonBaseline.AveragePrevious12Months,
                Kind: SalesGrowthThresholdKind.Multiple,
                Operator: ConditionOperator.GreaterThanOrEqual,
                Threshold: 1.5m)
        };

        foreach (var testCase in cases)
        {
            var parser = BuildParser(string.Empty, BuildAliasResolver());
            var result = await parser.ParseAsync(
                new ScannerParseRequest(testCase.Query, "fa", "corr-feature-116-real-fa", TenantId, AsOf),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.False(result.Plan.ClarificationRequired);
            Assert.Equal("fa", result.Plan.Language);
            Assert.NotNull(result.Plan.SalesGrowth);
            Assert.Equal(testCase.Baseline, result.Plan.SalesGrowth.Semantics.Baseline);
            Assert.Equal(testCase.Kind, result.Plan.SalesGrowth.Semantics.ThresholdKind);
            Assert.Equal(testCase.Operator, result.Plan.SalesGrowth.Semantics.ComparisonOperator);
            Assert.Equal(testCase.Threshold, result.Plan.SalesGrowth.Semantics.ThresholdValue);
        }
    }

    // --- Bug 1 regression: market-scope clarification hallucination ---

    [Fact]
    public async Task Parser_PeAndPsConditions_WhenLlmReturnsClarificationForMarketScope_OverridestoScanner()
    {
        // Simulates the bug: LLM returns clarificationRequired=true with a market-scope message
        // even though both PE_TTM and PS_TTM resolved cleanly. The parser must override this
        // and return a successful Scanner plan.
        var resolver = BuildAliasResolver();
        var json = BuildClarificationJsonWithConditions(
            conditions: [("pe", "LessThan", 5m), ("ps", "LessThan", 2m)],
            language: "fa",
            clarificationMessage: "در کدام بازار؟");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("لیست نمادهای با pe کمتر از 5 و ps کمتر از 2", "fa", "corr-bug1-1", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Plan.ClarificationRequired);
        Assert.Equal(2, result.Plan.Conditions.Count);
        Assert.Contains(result.Plan.Conditions, c => c.MetricReference.MetricCode.Value == "PE_TTM");
        Assert.Contains(result.Plan.Conditions, c => c.MetricReference.MetricCode.Value == "PS_TTM");
    }

    [Fact]
    public async Task Parser_PeAndPsConditionsPersian_WhenLlmReturnsFalse_ProducesScanner()
    {
        // LLM correctly returns clarificationRequired=false — parser must produce a valid Scanner plan.
        var resolver = BuildAliasResolver();
        var json = BuildConditionsJson("نسبت پی به ای", "LessThan", 4m, language: "fa");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("نمادهای با پی به ای زیر 4", "fa", "corr-bug1-2", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Plan.ClarificationRequired);
        Assert.Single(result.Plan.Conditions);
        Assert.Equal("PE_TTM", result.Plan.Conditions.First().MetricReference.MetricCode.Value);
    }

    [Fact]
    public async Task Parser_PeOnlyCondition_WhenLlmReturnsFalse_ProducesScanner()
    {
        // لیست نمادهای با پی بر ای زیر 4 — single PE condition, no market scope
        var resolver = BuildAliasResolver();
        var json = BuildConditionsJson("P/E", "LessThan", 4m);
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("لیست نمادهای با پی بر ای زیر 4", "fa", "corr-bug1-3", TenantId, AsOf),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Plan.ClarificationRequired);
        Assert.Single(result.Plan.Conditions);
        Assert.Equal("PE_TTM", result.Plan.Conditions.First().MetricReference.MetricCode.Value);
    }

    [Fact]
    public async Task Parser_WhenLlmClarificationMessageMentionsPhraseMissingFromQuery_ClarificationIsOverridden()
    {
        // The hallucinated phrase "در بازار" was not in the original query. The parser guard
        // must suppress this clarification because all metric conditions resolved cleanly.
        var resolver = BuildAliasResolver();
        var json = BuildClarificationJsonWithConditions(
            conditions: [("P/E", "LessThan", 5m)],
            language: "en",
            clarificationMessage: "عبارت «در بازار» مبهم است و مشخص نمی‌کند کدام بازار مدنظر است.");
        var parser = BuildParser(json, resolver);

        var result = await parser.ParseAsync(
            new ScannerParseRequest("symbols with PE below 5", "en", "corr-bug1-4", TenantId, AsOf),
            CancellationToken.None);

        Assert.False(result.Plan.ClarificationRequired);
        Assert.Null(result.Plan.ClarificationMessage);
        Assert.Single(result.Plan.Conditions);
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

    private static string BuildClarificationJsonWithConditions(
        IEnumerable<(string terminology, string @operator, decimal threshold)> conditions,
        string language,
        string clarificationMessage) =>
        JsonSerializer.Serialize(new
        {
            detectedLanguage = language,
            conditions = conditions.Select(c => new
            {
                userTerminology = c.terminology,
                language,
                @operator = c.@operator,
                threshold = c.threshold,
                periodHint = (string?)null,
                growthComparison = (string?)null,
                inferredDefault = false,
                inferredReason = (string?)null
            }).ToArray(),
            requestedColumns = Array.Empty<string>(),
            clarificationRequired = true,
            clarificationMessage
        });

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
        IEnumerable<string> columns,
        string language = "en") =>
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
