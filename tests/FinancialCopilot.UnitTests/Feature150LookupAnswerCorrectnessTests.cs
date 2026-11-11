using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.UnitTests;

// Feature 150 change «unavailable-capability-refusals-and-answer-correctness», R4 (BUG-006):
// the «واحد: میلیون ریال» note belongs to monthly-sales amounts only, not to a per-share price
// that happens to sit next to a monthly-sales context column.
public sealed class Feature150LookupAnswerCorrectnessTests
{
    private const string UnitNote = "واحد: میلیون ریال";

    [Fact]
    public void RequestedLatestPrice_WithMonthlySalesContextColumn_HasNoMillionRialNote_AndStatesRial()
    {
        var table = PriceTable(requested: ["LATEST_PRICE"]);

        var text = Builder().Build(table);

        Assert.DoesNotContain(UnitNote, text);
        Assert.Contains("3,450 ریال", text);
    }

    [Fact]
    public void RequestedMonthlySales_KeepsMillionRialNote()
    {
        var table = PriceTable(requested: ["MONTHLY_SALES"]);

        var text = Builder().Build(table);

        Assert.StartsWith(UnitNote, text);
    }

    [Fact]
    public void RequestedPeTtm_WithMonthlySalesContextColumn_HasNoMillionRialNote()
    {
        var table = PriceTable(requested: ["PE_TTM"], includePe: true);

        var text = Builder().Build(table);

        Assert.DoesNotContain(UnitNote, text);
    }

    [Fact]
    public void TableWithoutRequestedMetrics_KeepsTodaysText()
    {
        // V1 never sets RequestedMetricCodes; its text must not change.
        var table = PriceTable(requested: null);

        var text = Builder().Build(table);

        Assert.Equal("آخرین فروش ماهانه فولاد برابر با 90,879,722 میلیون ریال است.", text);
    }

    // R5 (BUG-007): a lookup where no row has a value for any requested metric is NoData, not Answered.
    [Fact]
    public async Task LookupExecutor_RequestedMetricMissingInEveryRow_IsNoData_AndKeepsTable()
    {
        var table = PriceTable(requested: null, includePe: true);
        var executor = new SymbolMetricLookupCapabilityExecutor(new FixedLookupService(table), TimeProvider.System);

        var result = await executor.ExecuteAsync(LookupFrame("PE_TTM"), Context(), default);

        Assert.Equal(CapabilityExecutionStatus.NoData, result.Status);
        var payload = Assert.IsType<SymbolLookupTableResult>(result.Payload);
        Assert.Single(payload.Rows);
        Assert.Equal("NoData", SemanticBillingCompletionStatus.For(result.Status));
    }

    [Fact]
    public async Task LookupExecutor_RequestedMetricPresent_IsExecuted()
    {
        var table = PriceTable(requested: null);
        var executor = new SymbolMetricLookupCapabilityExecutor(new FixedLookupService(table), TimeProvider.System);

        var result = await executor.ExecuteAsync(LookupFrame("LATEST_PRICE"), Context(), default);

        Assert.Equal(CapabilityExecutionStatus.Executed, result.Status);
    }

    [Fact]
    public async Task LookupExecutor_OneOfTwoRequestedMetricsPresent_IsExecuted()
    {
        var table = PriceTable(requested: null, includePe: true);
        var executor = new SymbolMetricLookupCapabilityExecutor(new FixedLookupService(table), TimeProvider.System);

        var result = await executor.ExecuteAsync(LookupFrame("PE_TTM,LATEST_PRICE"), Context(), default);

        Assert.Equal(CapabilityExecutionStatus.Executed, result.Status);
    }

    [Fact]
    public void NoDataLookupProse_IsTheDeterministicPersianSentence_WithoutUnitNote()
    {
        var table = PriceTable(requested: ["PE_TTM"], includePe: true);

        var text = Builder().Build(table);

        Assert.Equal("برای نماد فولاد مقدار قابل اتکایی برای P/E در داده‌های فعلی موجود نیست.", text);
    }

    // R5 on Telegram: the one-row stock card hides TextAnswer, so a NoData lookup must print the sentence too.
    [Fact]
    public void Telegram_NoDataSingleSymbolLookup_PrintsTheSentenceAboveTheCard()
    {
        const string sentence = "برای نماد فولاد مقدار قابل اتکایی برای P/E در داده‌های فعلی موجود نیست.";
        var response = LookupResponse(sentence, DialogueOutcome.NoData);

        var text = string.Join("\n", TelegramRenderer().Render(response, "fa-IR").Select(message => message.Text));

        Assert.Contains("مقدار قابل اتکایی برای P/E", text);
        Assert.Contains("فولاد", text);
    }

    [Fact]
    public void Telegram_AnsweredSingleSymbolLookup_StillShowsOnlyTheCard()
    {
        var response = LookupResponse("آخرین قیمت نماد فولاد برابر است با 3,450 ریال.", DialogueOutcome.Answered);

        var text = string.Join("\n", TelegramRenderer().Render(response, "fa-IR").Select(message => message.Text));

        Assert.DoesNotContain("برابر است با", text);
    }

    private static AiQueryResponse LookupResponse(string textAnswer, DialogueOutcome outcome) =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.SymbolLookup,
            ScannerPlan: null,
            ScannerTable: null,
            SymbolLookupTable: PriceTable(requested: ["PE_TTM"], includePe: true),
            ExplainableAnswer: null,
            ConfidenceScore: null,
            TextAnswer: textAnswer,
            ClarificationRequired: false,
            ClarificationMessage: null,
            Usage: null,
            Outcome: outcome);

    private static FinancialCopilot.Infrastructure.Authentication.TelegramAssistantResponseRenderer TelegramRenderer() =>
        new(
            new FinancialCopilot.Infrastructure.Authentication.TelegramMonthlyTrendChartRenderer(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FinancialCopilot.Infrastructure.Authentication.TelegramAssistantResponseRenderer>.Instance);

    private static ValidatedQueryFrame LookupFrame(string metrics) => new(
        "symbol_metric_lookup", 1,
        [
            new(QuerySlotType.CompanyOrSymbol, "فولاد", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid),
            metrics.Contains(',')
                ? new(QuerySlotType.Metrics, metrics, QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)
                : new(QuerySlotType.Metric, metrics, QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)
        ],
        new("P/E فولاد", "p/e فولاد", "fa", [], [], [], null, null, null, [], [], 1m, [], 1));

    private static QueryExecutionContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "fa");

    private sealed class FixedLookupService(SymbolLookupTableResult table) : ISymbolMetricLookupService
    {
        public Task<SymbolLookupTableResult> LookupAsync(SymbolLookupRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(table);
    }

    private static SymbolLookupProseBuilder Builder() =>
        new(new MetricDisplayNameResolver(new EmptyRegistry(), TimeProvider.System));

    internal static SymbolLookupTableResult PriceTable(IReadOnlyCollection<string>? requested, bool includePe = false)
    {
        var now = DateTimeOffset.UtcNow;
        var cells = new Dictionary<string, ScannerTableCell>
        {
            ["SYMBOL"] = new(null, "فولاد", CellFreshnessStatus.Persisted, null),
            ["LATEST_PRICE"] = new(3450m, "3,450", CellFreshnessStatus.Persisted, now),
            ["MONTHLY_SALES"] = new(90_879_722m, "90,879,722", CellFreshnessStatus.Persisted, now)
        };
        var columns = new List<ScannerTableColumn>
        {
            new("SYMBOL", "نماد", ScannerColumnType.Symbol),
            new("LATEST_PRICE", "آخرین قیمت", ScannerColumnType.LatestPrice, "LATEST_PRICE"),
            new("MONTHLY_SALES", "فروش ماهانه", ScannerColumnType.Metric, "MONTHLY_SALES")
        };
        if (includePe)
        {
            cells["PE_TTM"] = new(null, null, CellFreshnessStatus.Missing, null);
            columns.Add(new("PE_TTM", "P/E", ScannerColumnType.Metric, "PE_TTM"));
        }

        return new SymbolLookupTableResult(
            Guid.NewGuid(),
            columns,
            [new ScannerTableRow("فولاد", "فولاد مبارکه اصفهان", cells, 1.0, [])],
            new ScannerExecutionFacts(now, TimeSpan.Zero, 1, 1, false, 1, 1, 1),
            [],
            [],
            RequestedMetricCodes: requested);
    }

    private sealed class EmptyRegistry : IFinancialMetricRegistry
    {
        public FinancialMetricDefinition ResolveDefinition(MetricCode code, DateOnly asOf) =>
            throw new KeyNotFoundException(code.Value);

        public IFinancialMetricCalculator ResolveCalculator(MetricCode code) =>
            throw new NotImplementedException();

        public IReadOnlyCollection<FinancialMetricDefinition> GetSupportedMetrics(DateOnly asOf) => [];
    }
}
