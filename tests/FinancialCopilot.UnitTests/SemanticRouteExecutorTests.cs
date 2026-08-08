using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.UnitTests;

public sealed class SemanticRouteExecutorTests
{
    [Fact]
    public void FrameEnricher_MaterializesStatementTableParametersBeforeExecution()
    {
        var slots = Enrich(
            "financial_statement_table",
            "ترازنامه ۱۲ ماهه حسابرسی شده تلفیقی کگل را نشان بده",
            Symbol("کگل"));

        Assert.Equal("BalanceSheet", Value(slots, QuerySlotType.StatementType));
        Assert.Equal("12", Value(slots, QuerySlotType.Period));
        Assert.Equal("True", Value(slots, QuerySlotType.AuditStatus));
        Assert.Equal("True", Value(slots, QuerySlotType.ConsolidationScope));
    }

    [Fact]
    public void FrameEnricher_MaterializesAnalysisDisclosureAndRankingParameters()
    {
        var analysis = Enrich(
            "financial_statement_period_analysis",
            "نسبت جاری تلفیقی غالبر چقدر است؟",
            Symbol("غالبر"));
        Assert.Equal("BalanceSheet", Value(analysis, QuerySlotType.StatementType));
        Assert.Equal("ConsolidatedOnly", Value(analysis, QuerySlotType.ConsolidationScope));
        Assert.Contains("CURRENT_RATIO", Value(analysis, QuerySlotType.MetricSet));

        var now = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.FromHours(3.5));
        var disclosure = Enrich(
            "disclosure_listing",
            "فهرست صورت های مالی تلفیقی این هفته شرکت فولاد",
            now,
            Symbol("فولاد"));
        Assert.Contains("IncomeStatement", Value(disclosure, QuerySlotType.DisclosureTypes));
        Assert.Equal("Consolidated", Value(disclosure, QuerySlotType.ConsolidationScope));
        Assert.Equal("2026-08-01", Value(disclosure, QuerySlotType.PublishedFrom));

        var ranking = Enrich(
            "monthly_sales_quality_ranking",
            "۵ گزارش‌های فروش ضعیف در صنعت فلزات اساسی را بگو");
        Assert.Equal("فلزات اساسی", Value(ranking, QuerySlotType.Industry));
        Assert.Equal("Bottom", Value(ranking, QuerySlotType.Sort));
        Assert.Equal("5", Value(ranking, QuerySlotType.ResultLimit));
    }

    [Fact]
    public void FrameEnricher_MaterializesComprehensiveAnalysisTopicDateAndLimit()
    {
        var now = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.FromHours(3.5));
        var slots = Enrich(
            "comprehensive_analysis",
            "آخرین 2 تحلیل تکنیکال فولاد در این ماه",
            now,
            Symbol("فولاد"));

        Assert.Equal("تحلیل_تکنیکال", Value(slots, QuerySlotType.AnalysisTopic));
        Assert.Equal("2", Value(slots, QuerySlotType.ResultLimit));
        Assert.StartsWith("2026-08-01T00:00:00", Value(slots, QuerySlotType.Period));
    }

    [Fact]
    public async Task MonthlyTrendExecutor_UsesCanonicalFrameSymbolInsteadOfRawTextExtraction()
    {
        var useCase = new RecordingTrendUseCase();
        var executor = new MonthlyActivityTrendCapabilityExecutor(useCase);

        var result = await executor.ExecuteAsync(Frame("monthly_activity_trend", Symbol("فولاد")), Context(), default);

        Assert.Equal("فولاد", useCase.Query!.SymbolOrCompanyName);
        Assert.Equal(CapabilityExecutionStatus.NoData, result.Status);
    }

    [Fact]
    public async Task DirectLookupExecutor_UsesCanonicalSymbolAndGovernedMetricCode()
    {
        var lookup = new RecordingLookupService();
        var executor = new SymbolMetricLookupCapabilityExecutor(lookup, TimeProvider.System);

        await executor.ExecuteAsync(Frame("symbol_metric_lookup", Symbol("فولاد"),
            new(QuerySlotType.Metric, "PE_TTM", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)), Context(), default);

        var pair = Assert.Single(lookup.Request!.Pairs, item => item.MetricCode.Value == "PE_TTM");
        Assert.Equal("فولاد", pair.SymbolName);
        Assert.Equal("PE_TTM", pair.MetricCode.Value);
        Assert.Contains(lookup.Request.Pairs, item => item.MetricCode.Value == "LATEST_PRICE");
        Assert.Contains(lookup.Request.Pairs, item => item.MetricCode.Value == "MONTHLY_SALES");
    }

    [Fact]
    public async Task DirectLookupExecutor_ExecutesPluralCanonicalSymbolsAndMetricsInOneOperation()
    {
        var lookup = new RecordingLookupService();
        var executor = new SymbolMetricLookupCapabilityExecutor(lookup, TimeProvider.System);

        await executor.ExecuteAsync(Frame("symbol_metric_lookup",
            Symbol("فملی"),
            new(QuerySlotType.CompaniesOrSymbols, "فملی,حفاری", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid),
            new(QuerySlotType.Metric, "PE_TTM", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid),
            new(QuerySlotType.Metrics, "PE_TTM,RETURN_ON_EQUITY", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)), Context(), default);

        Assert.Equal(2, lookup.Request!.Pairs.Where(pair => pair.MetricCode.Value == "PE_TTM").Count());
        Assert.Equal(2, lookup.Request.Pairs.Where(pair => pair.MetricCode.Value == "RETURN_ON_EQUITY").Count());
        Assert.Equal(["فملی", "حفاری"], lookup.Request.Pairs.Select(pair => pair.SymbolName).Distinct());
    }

    [Fact]
    public async Task ComprehensiveAnalysisExecutor_ReturnsReportsAndLiveMetricsFromOneSemanticExecution()
    {
        var lookup = new RecordingLookupService(rows: true);
        var analysis = new AnalysisUseCase(hasRows: true);
        var executor = new ComprehensiveAnalysisCapabilityExecutor(
            analysis, lookup, TimeProvider.System);

        var result = await executor.ExecuteAsync(
            Frame("comprehensive_analysis", Symbol("FOLD"),
                new(QuerySlotType.AnalysisTopic, "تحلیل_تکنیکال", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid),
                new(QuerySlotType.ResultLimit, "2", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)), Context(), default);

        var payload = Assert.IsType<SemanticComprehensiveAnalysisPayload>(result.Payload);
        Assert.Equal(CapabilityExecutionStatus.Executed, result.Status);
        Assert.True(payload.Analysis.HasResults);
        Assert.NotEmpty(payload.Lookup.Rows);
        Assert.Equal(["تحلیل_تکنیکال"], analysis.Request!.TopicTags);
        Assert.Equal(2, analysis.Request.Limit);
        Assert.All(lookup.Request!.Pairs, pair => Assert.Equal("FOLD", pair.SymbolName));
    }

    [Fact]
    public async Task PersonalizedInsightExecutor_UsesValidatedInsightAndCurrentActor()
    {
        var insightId = Guid.NewGuid();
        var useCase = new RecordingExplainInsightUseCase();
        var executor = new PersonalizedInsightExplanationCapabilityExecutor(useCase);
        var context = Context() with
        {
            ActorType = ActorType.ApiClient,
            AuthenticationMode = AuthenticationMode.ApiClient,
            ApiClientId = Guid.NewGuid()
        };

        var result = await executor.ExecuteAsync(Frame("personalized_insight_explanation",
            new ResolvedQuerySlot(QuerySlotType.Insight, insightId.ToString("D"), QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)), context, default);

        Assert.Equal(CapabilityExecutionStatus.Executed, result.Status);
        Assert.Equal("verified explanation", result.Payload);
        Assert.Equal(insightId, useCase.Query!.InsightEventId);
        Assert.Equal(context.ActorId, useCase.Query.Actor.ActorId);
        Assert.Equal(context.ApiClientId, useCase.Query.Actor.ApiClientId);
    }

    private static ResolvedQuerySlot Symbol(string value) => new(QuerySlotType.CompanyOrSymbol, value, QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid);
    private static IReadOnlyCollection<ResolvedQuerySlot> Enrich(string capability, string message, params ResolvedQuerySlot[] slots) =>
        Enrich(capability, message, DateTimeOffset.UtcNow, slots);
    private static IReadOnlyCollection<ResolvedQuerySlot> Enrich(string capability, string message, DateTimeOffset now, params ResolvedQuerySlot[] slots) =>
        new SemanticQueryFrameEnricher().Enrich(capability,
            new(message, QueryNormalization.Normalize(message), AiDialogueOutcomePolicy.DetectReplyLanguage(message), [], [], [], null, null, null, [], [], 1m, [], 1),
            slots,
            now);
    private static string Value(IReadOnlyCollection<ResolvedQuerySlot> slots, QuerySlotType type) =>
        Assert.Single(slots, slot => slot.Type == type).Value!;
    private static ValidatedQueryFrame Frame(string capability, params ResolvedQuerySlot[] slots) => new(
        capability, 1, slots,
        new("raw text without a usable symbol", "raw text", "en", [], [], [], null, null, null, [], [], 1m, [], 1));
    private static QueryExecutionContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "en");

    private sealed class RecordingTrendUseCase : IMonthlyActivityTrendQueryUseCase
    {
        public MonthlyActivityTrendQuery? Query { get; private set; }
        public Task<MonthlyActivityTrendResponse?> ExecuteAsync(MonthlyActivityTrendQuery query, CancellationToken ct = default) { Query = query; return Task.FromResult<MonthlyActivityTrendResponse?>(null); }
    }

    private sealed class RecordingLookupService(bool rows = false) : ISymbolMetricLookupService
    {
        public SymbolLookupRequest? Request { get; private set; }
        public Task<SymbolLookupTableResult> LookupAsync(SymbolLookupRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            var resultRows = rows
                ? new[] { new ScannerTableRow("FOLD", "FOLD", new Dictionary<string, ScannerTableCell>(), 1d, []) }
                : [];
            return Task.FromResult(new SymbolLookupTableResult(Guid.NewGuid(), [], resultRows, new(DateTimeOffset.UtcNow, TimeSpan.Zero, resultRows.Length, resultRows.Length, false), [], []));
        }
    }

    private sealed class AnalysisUseCase(bool hasRows) : IComprehensiveAnalysisQueryUseCase
    {
        public ComprehensiveAnalysisQueryRequest? Request { get; private set; }
        public Task<ComprehensiveAnalysisQueryResponse> ExecuteAsync(ComprehensiveAnalysisQueryRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            IReadOnlyList<ComprehensiveAnalysisSummaryItem> items = hasRows
                ? [new(1, "title", "1405/05/16", "author", "P/E 5.4", [], DateTimeOffset.UtcNow)]
                : [];
            return Task.FromResult(new ComprehensiveAnalysisQueryResponse(items, []));
        }
    }

    private sealed class RecordingExplainInsightUseCase : IExplainInsightUseCase
    {
        public ExplainInsightQuery? Query { get; private set; }
        public Task<string> ExecuteAsync(ExplainInsightQuery query, CancellationToken cancellationToken = default)
        {
            Query = query;
            return Task.FromResult("verified explanation");
        }
    }
}
