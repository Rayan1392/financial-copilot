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
