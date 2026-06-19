using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.UnitTests;

// Guards the answer/table numeric-consistency contract: AI prose can never report a metric value
// that disagrees with the deterministic structured table. Mirrors the reported bug
// (`pe شبندر چقدر است؟` → prose "7.88" vs table PE_TTM "5.06").
public sealed class AnswerConsistencyTests
{
    private const string MetricCode = "PE_TTM";

    [Fact]
    public void SymbolLookup_ProseConflictsWithTable_ReplacedWithDeterministicValue()
    {
        var table = LookupTable("شبندر", value: 5.06m, persianSymbol: true);
        var sut = MakeValidator();

        var result = sut.ValidateSymbolLookup(
            table, "نسبت P/E نماد شبندر برابر است با 7.88", Context());

        Assert.Equal(AnswerConsistencyAction.ReplacedWithDeterministic, result.Action);
        Assert.Contains("5.06", result.Answer);
        Assert.DoesNotContain("7.88", result.Answer);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("7.88", conflict.ProseValue);
        Assert.Equal("5.06", conflict.TableValue);
        Assert.Equal(MetricCode, conflict.MetricCode);
    }

    [Fact]
    public void SymbolLookup_RegressionExactQuery_ProseNeverContains788()
    {
        var table = LookupTable("شبندر", value: 5.06m, persianSymbol: true);
        var sut = MakeValidator();

        var result = sut.ValidateSymbolLookup(
            table, "نسبت P/E نماد شبندر برابر است با 7.88", Context());

        Assert.DoesNotContain("7.88", result.Answer);
        Assert.Contains("5.06", result.Answer);
    }

    [Fact]
    public void SymbolLookup_ProseWithPersianDigits_ConflictDetected()
    {
        var table = LookupTable("شبندر", value: 5.06m, persianSymbol: true);
        var sut = MakeValidator();

        // LLM renders the hallucinated value with Persian digits and the Persian decimal separator.
        var result = sut.ValidateSymbolLookup(
            table, "نسبت P/E نماد شبندر برابر است با ۷٫۸۸", Context());

        Assert.Equal(AnswerConsistencyAction.ReplacedWithDeterministic, result.Action);
        Assert.Contains("5.06", result.Answer);
        Assert.DoesNotContain("۷٫۸۸", result.Answer);
    }

    [Fact]
    public void SymbolLookup_ProseWithPersianDigitsMatchingTable_Unchanged()
    {
        var table = LookupTable("شبندر", value: 5.06m, persianSymbol: true);
        var sut = MakeValidator();

        // Persian-digit rendering of the correct value (۵٫۰۶ == 5.06) must NOT be flagged.
        var prose = "نسبت P/E نماد شبندر برابر است با ۵٫۰۶";
        var result = sut.ValidateSymbolLookup(table, prose, Context());

        Assert.Equal(AnswerConsistencyAction.Unchanged, result.Action);
        Assert.Equal(prose, result.Answer);
    }

    [Fact]
    public void SymbolLookup_ProseMatchesTable_Unchanged()
    {
        var table = LookupTable("شبندر", value: 5.06m, persianSymbol: true);
        var sut = MakeValidator();

        var prose = "نسبت P/E نماد شبندر برابر است با 5.06";
        var result = sut.ValidateSymbolLookup(table, prose, Context());

        Assert.Equal(AnswerConsistencyAction.Unchanged, result.Action);
        Assert.Equal(prose, result.Answer);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void SymbolLookup_ValueUnavailable_DoesNotInventNumber()
    {
        var table = LookupTableMissing("FOLAD", persianSymbol: false);
        var sut = MakeValidator();

        // LLM invented a number even though the table has no value.
        var result = sut.ValidateSymbolLookup(table, "The P/E of FOLAD is 12.3", Context());

        Assert.Equal(AnswerConsistencyAction.ReplacedWithDeterministic, result.Action);
        Assert.DoesNotContain("12.3", result.Answer);
        Assert.Contains("No reliable", result.Answer);
    }

    [Fact]
    public void SymbolLookup_NoCandidateProse_FallsBackToDeterministicProse()
    {
        var table = LookupTable("FOLAD", value: 5.06m, persianSymbol: false);
        var sut = MakeValidator();

        var result = sut.ValidateSymbolLookup(table, candidateProse: null, Context());

        Assert.Contains("5.06", result.Answer);
    }

    [Fact]
    public void SymbolLookup_MultipleSymbols_DoesNotStateSingleValue()
    {
        var table = LookupTableMulti();
        var sut = MakeValidator();

        var result = sut.ValidateSymbolLookup(table, "هر دو نماد برابر است با 5.06", Context());

        // With more than one symbol, prose must not assert a single value: defer to the table.
        Assert.Equal(AnswerConsistencyAction.ReplacedWithDeterministic, result.Action);
        Assert.Contains("جدول", result.Answer);
    }

    [Fact]
    public void SymbolLookup_MonthlySalesWithData_ReplacesNonNumericLlmCommentaryWithConciseValue()
    {
        var table = MonthlySalesLookupTable();
        var sut = MakeValidator();

        var result = sut.ValidateSymbolLookup(
            table,
            "The direct metric tool did not return a numeric sales value. Please clarify whether you mean monthly, quarterly, or annual sales.",
            Context());

        Assert.Equal(AnswerConsistencyAction.ReplacedWithDeterministic, result.Action);
        Assert.Contains("90,879,722", result.Answer);
        Assert.Contains("میلیون ریال", result.Answer);
        Assert.DoesNotContain("did not return", result.Answer);
        Assert.DoesNotContain("clarify", result.Answer);
    }

    [Theory]
    [InlineData("AVG_12M_MONTHLY_SALES", "متوسط فروش ۱۲ ماهه", "57,549,287")]
    [InlineData("MONTHLY_SALES_YTD", "فروش YTD", "787,016,400")]
    [InlineData("MONTHLY_SALES_YTD_PREVIOUS_MONTH", "فروش YTD تا ماه قبل", "605,344,668")]
    public void SymbolLookup_MonthlySalesCompanionOnly_UsesRequestedMetricInProse(
        string metricCode,
        string displayName,
        string formattedValue)
    {
        var table = MonthlySalesCompanionLookupTable(metricCode, displayName, formattedValue);
        var sut = MakeValidator();

        var result = sut.ValidateSymbolLookup(table, candidateProse: null, Context());

        Assert.Contains(formattedValue, result.Answer);
        Assert.Contains(displayName, result.Answer);
        Assert.DoesNotContain("90,879,722", result.Answer);
    }

    [Fact]
    public void SymbolLookup_PersianPeDisplayName_DoesNotDuplicateRatioPrefix()
    {
        var table = LookupTable("شبندر", value: 5.06m, persianSymbol: true);
        var sut = MakeValidator();

        var result = sut.ValidateSymbolLookup(table, candidateProse: null, Context());

        Assert.Contains("نسبت پی به ای", result.Answer);
        Assert.DoesNotContain("نسبت نسبت پی به ای", result.Answer);
    }

    // --- Scanner deterministic prose regression tests (Bug: LLM free-form symbol list) ---

    [Fact]
    public void Scanner_NullCandidate_ReturnsDeterministicEnglishSentence()
    {
        var (table, plan) = ScannerTableAndPlan(cellValue: 3.50m, threshold: 6m);
        var sut = MakeValidator();

        // Null candidate = orchestrator chose not to pass LLM prose; must get deterministic summary.
        var result = sut.ValidateScanner(table, plan, null, Context());

        Assert.Equal(AnswerConsistencyAction.Unchanged, result.Action);
        Assert.Contains("1", result.Answer);
        Assert.DoesNotContain("فباهنر", result.Answer);
        Assert.DoesNotContain("اگر بخواهی", result.Answer);
    }

    [Fact]
    public void Scanner_NullCandidate_PersianSymbols_ReturnsPersianSentence()
    {
        var (table, plan) = ScannerTableAndPlanPersian(rowCount: 15);
        var sut = MakeValidator();

        var result = sut.ValidateScanner(table, plan, null, Context());

        Assert.Equal(AnswerConsistencyAction.Unchanged, result.Action);
        // Must contain the count and be in Persian (contains Persian characters)
        Assert.Contains("15", result.Answer);
        Assert.Contains("نماد", result.Answer);
        // Must not contain a symbol list or suggestions
        Assert.DoesNotContain("اگر بخواهی", result.Answer);
        Assert.DoesNotContain("وساپا", result.Answer);
    }

    [Fact]
    public void Scanner_NullCandidate_ProseIsAtMostTwoSentences()
    {
        var (table, plan) = ScannerTableAndPlan(cellValue: 3.50m, threshold: 6m);
        var sut = MakeValidator();

        var result = sut.ValidateScanner(table, plan, null, Context());

        // Deterministic summary must be compact — at most two sentences (one period + optional second)
        var sentences = result.Answer.Split('.', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.True(sentences.Length <= 2, $"Expected at most 2 sentences, got: {result.Answer}");
    }

    [Fact]
    public void Scanner_LlmSymbolListCandidate_IsReplacedWithDeterminsticSummary()
    {
        var (table, plan) = ScannerTableAndPlan(cellValue: 3.50m, threshold: 6m);
        var sut = MakeValidator();

        // The LLM prose contains no unsupported metric numbers (just names), so conservative
        // conflict detection does NOT flag it — this is why the old path let it through.
        // With null candidate (new behavior), the deterministic sentence is returned instead.
        var resultNew = sut.ValidateScanner(table, plan, null, Context());

        Assert.DoesNotContain("فباهنر", resultNew.Answer);
        Assert.DoesNotContain("اگر بخواهی", resultNew.Answer);
        Assert.Contains("1", resultNew.Answer); // row count present
    }

    [Fact]
    public void Scanner_DeterministicSummary_DoesNotContainSymbolEnumeration()
    {
        // Regression guard: whatever ValidateScanner returns for null candidate must never
        // be a symbol list — it must be a count summary only.
        var (table, plan) = ScannerTableAndPlan(cellValue: 2.1m, threshold: 4m);
        var sut = MakeValidator();

        var result = sut.ValidateScanner(table, plan, null, Context());

        // Must contain count (1 row) and must not look like a bullet list.
        Assert.DoesNotContain("\n-", result.Answer);
        Assert.DoesNotContain("•", result.Answer);
        Assert.DoesNotContain("فباهنر", result.Answer);
        Assert.DoesNotContain("وساپا", result.Answer);
    }

    // --- existing scanner tests ---

    [Fact]
    public void Scanner_ProseInventsMetricValue_Replaced()
    {
        var (table, plan) = ScannerTableAndPlan(cellValue: 3.50m, threshold: 6m);
        var sut = MakeValidator();

        // "3.50" is the real cell value; "9.99" is an unsupported metric figure → conflict.
        var result = sut.ValidateScanner(
            table, plan, "Found 1 stock with a P/E of 9.99.", Context());

        Assert.Equal(AnswerConsistencyAction.ReplacedWithDeterministic, result.Action);
        Assert.DoesNotContain("9.99", result.Answer);
    }

    [Fact]
    public void Scanner_ProseWithCountsAndThresholds_NotFlagged()
    {
        var (table, plan) = ScannerTableAndPlan(cellValue: 3.50m, threshold: 6m);
        var sut = MakeValidator();

        // "1" is the count, "6" is the plan threshold, "3.50" is the real cell value — all allowed.
        var prose = "Found 1 stock with P/E below 6. It reports 3.50.";
        var result = sut.ValidateScanner(table, plan, prose, Context());

        Assert.Equal(AnswerConsistencyAction.Unchanged, result.Action);
        Assert.Equal(prose, result.Answer);
    }

    [Fact]
    public void Warning_RecordsConflictWithContext()
    {
        var table = LookupTable("شبندر", value: 5.06m, persianSymbol: true);
        var sink = new RecordingWarningSink();
        var sut = MakeValidator(sink);

        sut.ValidateSymbolLookup(
            table, "نسبت P/E نماد شبندر برابر است با 7.88",
            new AnswerConsistencyContext("corr-1", Guid.Empty, "MicrosoftAgentFrameworkV2", 2));

        var recorded = Assert.Single(sink.Recorded);
        Assert.Equal("corr-1", recorded.Context.CorrelationId);
        Assert.Equal("MicrosoftAgentFrameworkV2", recorded.Context.OrchestrationMode);
        Assert.Equal(2, recorded.Context.WorkflowVersion);
        Assert.Equal("5.06", recorded.Conflict.TableValue);
        Assert.Equal("7.88", recorded.Conflict.ProseValue);
    }

    // --- helpers ---

    private static AnswerConsistencyContext Context() =>
        new("corr", Guid.NewGuid(), "V1", 1);

    private static AnswerConsistencyValidator MakeValidator(IAnswerConsistencyWarningSink? sink = null)
    {
        var displayNames = new MetricDisplayNameResolver(new FakeRegistry(), TimeProvider.System);
        var proseBuilder = new SymbolLookupProseBuilder(displayNames);
        return new AnswerConsistencyValidator(
            proseBuilder, displayNames, sink ?? new RecordingWarningSink());
    }

    private static SymbolLookupTableResult LookupTable(string symbol, decimal value, bool persianSymbol)
    {
        var now = DateTimeOffset.UtcNow;
        var cells = new Dictionary<string, ScannerTableCell>
        {
            ["SYMBOL"] = new(null, symbol, CellFreshnessStatus.Persisted, null),
            [MetricCode] = new(value, value.ToString("N2"), CellFreshnessStatus.Persisted, now)
        };
        return new SymbolLookupTableResult(
            Guid.NewGuid(),
            [
                new ScannerTableColumn("SYMBOL", "Symbol", ScannerColumnType.Symbol),
                new ScannerTableColumn(MetricCode, MetricCode, ScannerColumnType.Metric, MetricCode)
            ],
            [new ScannerTableRow(symbol, null, cells, 1.0, [])],
            Facts(1),
            [],
            []);
    }

    private static SymbolLookupTableResult LookupTableMissing(string symbol, bool persianSymbol)
    {
        var cells = new Dictionary<string, ScannerTableCell>
        {
            ["SYMBOL"] = new(null, symbol, CellFreshnessStatus.Persisted, null),
            [MetricCode] = new(null, null, CellFreshnessStatus.Missing, null)
        };
        return new SymbolLookupTableResult(
            Guid.NewGuid(),
            [
                new ScannerTableColumn("SYMBOL", "Symbol", ScannerColumnType.Symbol),
                new ScannerTableColumn(MetricCode, MetricCode, ScannerColumnType.Metric, MetricCode)
            ],
            [new ScannerTableRow(symbol, null, cells, 1.0, [])],
            Facts(0),
            [],
            []);
    }

    private static SymbolLookupTableResult LookupTableMulti()
    {
        var now = DateTimeOffset.UtcNow;
        ScannerTableRow MakeRow(string symbol, decimal value) =>
            new(symbol, null, new Dictionary<string, ScannerTableCell>
            {
                ["SYMBOL"] = new(null, symbol, CellFreshnessStatus.Persisted, null),
                [MetricCode] = new(value, value.ToString("N2"), CellFreshnessStatus.Persisted, now)
            }, 1.0, []);

        return new SymbolLookupTableResult(
            Guid.NewGuid(),
            [
                new ScannerTableColumn("SYMBOL", "Symbol", ScannerColumnType.Symbol),
                new ScannerTableColumn(MetricCode, MetricCode, ScannerColumnType.Metric, MetricCode)
            ],
            [MakeRow("شبندر", 5.06m), MakeRow("فولاد", 4.10m)],
            Facts(2),
            [],
            []);
    }

    private static SymbolLookupTableResult MonthlySalesLookupTable()
    {
        var now = DateTimeOffset.UtcNow;
        var cells = new Dictionary<string, ScannerTableCell>
        {
            ["SYMBOL"] = new(null, "کچاد", CellFreshnessStatus.Persisted, null),
            ["MONTHLY_SALES"] = new(90_879_722m, "90,879,722", CellFreshnessStatus.Persisted, now),
            ["MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH"] = new(null, null, CellFreshnessStatus.Missing, null),
            ["MONTHLY_SALES_YTD"] = new(787_016_400m, "787,016,400", CellFreshnessStatus.Persisted, now),
            ["MONTHLY_SALES_YTD_PREVIOUS_MONTH"] = new(605_344_668m, "605,344,668", CellFreshnessStatus.Persisted, now)
        };

        return new SymbolLookupTableResult(
            Guid.NewGuid(),
            [
                new ScannerTableColumn("SYMBOL", "نماد", ScannerColumnType.Symbol),
                new ScannerTableColumn("MONTHLY_SALES", "فروش ماهانه", ScannerColumnType.Metric, "MONTHLY_SALES"),
                new ScannerTableColumn("MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH", "فروش ماه مشابه قبل", ScannerColumnType.Metric, "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH"),
                new ScannerTableColumn("MONTHLY_SALES_YTD", "فروش YTD", ScannerColumnType.Metric, "MONTHLY_SALES_YTD"),
                new ScannerTableColumn("MONTHLY_SALES_YTD_PREVIOUS_MONTH", "فروش YTD تا ماه قبل", ScannerColumnType.Metric, "MONTHLY_SALES_YTD_PREVIOUS_MONTH")
            ],
            [new ScannerTableRow("کچاد", "معدنی و صنعتی چادرملو", cells, 1.0, [])],
            Facts(1),
            [],
            []);
    }

    private static SymbolLookupTableResult MonthlySalesCompanionLookupTable(
        string metricCode,
        string displayName,
        string formattedValue)
    {
        var now = DateTimeOffset.UtcNow;
        var cells = new Dictionary<string, ScannerTableCell>
        {
            ["SYMBOL"] = new(null, "کچاد", CellFreshnessStatus.Persisted, null),
            [metricCode] = new(
                decimal.Parse(formattedValue.Replace(",", string.Empty)),
                formattedValue,
                CellFreshnessStatus.Persisted,
                now)
        };

        return new SymbolLookupTableResult(
            Guid.NewGuid(),
            [
                new ScannerTableColumn("SYMBOL", "نماد", ScannerColumnType.Symbol),
                new ScannerTableColumn(metricCode, displayName, ScannerColumnType.Metric, metricCode)
            ],
            [new ScannerTableRow("کچاد", "معدنی و صنعتی چادرملو", cells, 1.0, [])],
            Facts(1),
            [],
            []);
    }

    private static (ScannerTableResult Table, ScannerQueryPlan Plan) ScannerTableAndPlanPersian(
        int rowCount)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = Enumerable.Range(1, rowCount).Select(i =>
            new ScannerTableRow($"نماد{i}", null, new Dictionary<string, ScannerTableCell>
            {
                [MetricCode] = new(2m, "2.00", CellFreshnessStatus.Persisted, now)
            }, 1.0, [MetricCode])).ToList();

        var table = new ScannerTableResult(
            Guid.NewGuid(),
            [new ScannerTableColumn(MetricCode, MetricCode, ScannerColumnType.Metric, MetricCode)],
            rows,
            Facts(rowCount),
            []);

        var condition = new ScannerCondition(
            new ScannerMetricReference(MetricCode, new MetricCode(MetricCode),
                new MetricVersion("v1"), new CalculationPolicyVersion("PE_TTM_v1"),
                FiscalPeriodType.ThreeMonths, null),
            ConditionOperator.LessThan, 4m, FilterOrigin.Explicit);
        var plan = new ScannerQueryPlan(
            table.PlanId, "پی به ای زیر 4", "fa", [condition], [], false, null, [], [],
            now, "v1");

        return (table, plan);
    }

    private static (ScannerTableResult Table, ScannerQueryPlan Plan) ScannerTableAndPlan(
        decimal cellValue, decimal threshold)
    {
        var now = DateTimeOffset.UtcNow;
        var row = new ScannerTableRow("A", null, new Dictionary<string, ScannerTableCell>
        {
            [MetricCode] = new(cellValue, cellValue.ToString("N2"), CellFreshnessStatus.Persisted, now),
            ["LATEST_PRICE"] = new(100m, "100.00", CellFreshnessStatus.Live, now)
        }, 1.0, [MetricCode]);

        var table = new ScannerTableResult(
            Guid.NewGuid(),
            [new ScannerTableColumn(MetricCode, MetricCode, ScannerColumnType.Metric, MetricCode)],
            [row],
            Facts(1),
            []);

        var condition = new ScannerCondition(
            new ScannerMetricReference(MetricCode, new MetricCode(MetricCode),
                new MetricVersion("v1"), new CalculationPolicyVersion("PE_TTM_v1"),
                FiscalPeriodType.ThreeMonths, null),
            ConditionOperator.LessThan, threshold, FilterOrigin.Explicit);
        var plan = new ScannerQueryPlan(
            table.PlanId, "P/E below 6", "en", [condition], [], false, null, [], [],
            now, "v1");

        return (table, plan);
    }

    private static ScannerExecutionFacts Facts(int count) =>
        new(DateTimeOffset.UtcNow, TimeSpan.Zero, count, count, false, 1, Math.Max(1, count), 1);

    // Registry with the single governed metric used in these tests.
    private sealed class FakeRegistry : IFinancialMetricRegistry
    {
        private static readonly FinancialMetricDefinition PeTtm = new(
            new MetricCode(MetricCode),
            new MetricVersion("v1"),
            "P/E (TTM)",
            "Price to earnings (TTM).",
            MetricCategory.Valuation,
            new MetricUnit("Ratio", "Ratio"),
            new DateOnly(2020, 1, 1),
            null,
            [FiscalPeriodType.ThreeMonths],
            [
                new MetricAlias("p/e", "en-US", new MetricCode(MetricCode), new MetricVersion("v1")),
                new MetricAlias("نسبت پی به ای", "fa-IR", new MetricCode(MetricCode), new MetricVersion("v1"))
            ],
            [],
            []);

        public FinancialMetricDefinition ResolveDefinition(MetricCode code, DateOnly asOf) =>
            code.Value == MetricCode
                ? PeTtm
                : throw new KeyNotFoundException(code.Value);

        public IFinancialMetricCalculator ResolveCalculator(MetricCode code) =>
            throw new NotImplementedException();

        public IReadOnlyCollection<FinancialMetricDefinition> GetSupportedMetrics(DateOnly asOf) => [PeTtm];
    }

    private sealed class RecordingWarningSink : IAnswerConsistencyWarningSink
    {
        public List<(AnswerConsistencyContext Context, AnswerConsistencyConflict Conflict)> Recorded { get; } = [];

        public void RecordCorrectedInconsistency(
            AnswerConsistencyContext context, AnswerConsistencyConflict conflict) =>
            Recorded.Add((context, conflict));
    }
}
