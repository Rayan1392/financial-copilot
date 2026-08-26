using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using System.Globalization;
using static FinancialCopilot.Application.AI.Orchestration.SemanticExecutionResultFactory;

namespace FinancialCopilot.Application.AI.Orchestration;

public sealed record SemanticScannerPayload(ScannerQueryPlan Plan, ScannerTableResult Table);
public sealed record SemanticComprehensiveAnalysisPayload(
    ComprehensiveAnalysisQueryResponse Analysis,
    SymbolLookupTableResult Lookup);

public sealed record IndustryRelativeValuationPayload(
    IndustryRelativeValuationReadModel ReadModel,
    string PresentationText);

public sealed class IndustryRelativeValuationCapabilityExecutor(
    IIndustryRelativeValuationReadRepository repository,
    IndustryRelativeValuationReadOptions readOptions,
    string? registeredCapabilityCode = null) : IConversationalCapabilityExecutor
{
    public IndustryRelativeValuationCapabilityExecutor(
        IIndustryRelativeValuationReadRepository repository,
        string? registeredCapabilityCode = null)
        : this(repository, new IndustryRelativeValuationReadOptions(), registeredCapabilityCode)
    {
    }

    private static readonly IReadOnlySet<string> Codes = new HashSet<string>(StringComparer.Ordinal)
    {
        "symbol_vs_industry_relative_valuation", "industry_relative_valuation_ranking",
        "industry_relative_valuation_summary", "symbol_pair_within_industry"
    };

    public string CapabilityCode => registeredCapabilityCode ?? "symbol_vs_industry_relative_valuation";

    public async Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        if (!Codes.Contains(frame.CapabilityCode)) return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Unsupported, "unsupported_feature_125_capability");
        var group = ParseId(frame.Value(QuerySlotType.IndustryGroup) ?? frame.Value(QuerySlotType.Industry));
        var ids = ParseIds(frame.Value(QuerySlotType.CompaniesOrSymbols) ?? frame.Value(QuerySlotType.CompanyOrSymbol));
        var options = readOptions;
        options.Validate();
        var requestedLimit = int.TryParse(frame.Value(QuerySlotType.ResultLimit), out var parsed)
            ? parsed
            : options.DefaultResultLimit;
        if (requestedLimit < 1 || requestedLimit > options.MaximumResultLimit)
        {
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.ClarificationRequired, DialogueOutcomeReasonCodes.ResultLimitExceeded);
        }
        var limit = requestedLimit;
        try
        {
            var read = await repository.ReadAsync(new IndustryRelativeValuationReadRequest(group, ids, frame.CapabilityCode, limit), cancellationToken);
            if (read is null)
            {
                return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.NoData, DialogueOutcomeReasonCodes.SupportedButNoRows);
            }
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Executed, DialogueOutcomeReasonCodes.None,
                new IndustryRelativeValuationPayload(read, IndustryRelativeValuationPresentation.Explain(read, context.ReplyLanguage)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Failed, DialogueOutcomeReasonCodes.ProviderOrToolFailure);
        }
    }

    private static Guid? ParseId(string? value) => Guid.TryParse(value, out var id) ? id : null;
    private static IReadOnlyList<Guid> ParseIds(string? value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ParseId).Where(x => x.HasValue).Select(x => x!.Value).Take(2).ToArray();
}

public sealed class StockScreeningCapabilityExecutor(
    IScannerQueryParser parser,
    IScannerExecutionService executionService,
    IScannerCache scannerCache,
    TimeProvider timeProvider) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "stock_screening";
    public async Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);
        var cacheScope = new ScannerCacheScope(context.TenantId, context.ActorId, context.ApiClientId);
        var dataVersion = await scannerCache.GetDataVersionAsync(cancellationToken);
        var parseRequest = new ScannerParseRequest(
            frame.Interpretation.OriginalText,
            frame.Interpretation.ReplyLanguage,
            context.CorrelationId,
            context.TenantId,
            asOf);
        var parsed = await scannerCache.GetPlanAsync(cacheScope, dataVersion, parseRequest, cancellationToken)
            ?? await parser.ParseAsync(parseRequest, cancellationToken);
        if (!parsed.Succeeded || parsed.Plan.ClarificationRequired)
            return Missing(frame, DialogueOutcomeReasonCodes.RequiredInputMissing);
        await scannerCache.SetPlanAsync(cacheScope, dataVersion, parseRequest, parsed, cancellationToken);
        var executionRequest = new ScannerExecutionRequest(
            parsed.Plan,
            asOf,
            Page: context.Page,
            PageSize: context.PageSize,
            ActorId: context.ActorId.ToString(),
            QueryText: frame.Interpretation.OriginalText);
        var cached = await scannerCache.GetResultAsync(cacheScope, dataVersion, executionRequest, cancellationToken);
        var table = cached is null
            ? await executionService.ExecuteAsync(executionRequest, cancellationToken)
            : cached with { ExecutionFacts = cached.ExecutionFacts with { FromCache = true } };
        if (cached is null)
            await scannerCache.SetResultAsync(cacheScope, dataVersion, executionRequest, table, cancellationToken);
        var payload = new SemanticScannerPayload(parsed.Plan, table);
        return table.Rows.Count == 0 ? NoData(frame, payload) : Success(frame, payload);
    }
}

public sealed class MonthlyActivityTrendCapabilityExecutor(
    IMonthlyActivityTrendQueryUseCase useCase) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "monthly_activity_trend";

    public async Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        var symbol = frame.Value(QuerySlotType.CompanyOrSymbol);
        if (symbol is null) return Missing(frame, DialogueOutcomeReasonCodes.RequiredInputMissing);
        var presentation = frame.Value(QuerySlotType.Presentation);
        var result = await useCase.ExecuteAsync(new MonthlyActivityTrendQuery(
            frame.Interpretation.OriginalText,
            symbol,
            IncludeChartPayload: !string.Equals(presentation, nameof(PresentationKind.Summary), StringComparison.OrdinalIgnoreCase)), cancellationToken);
        return result is null
            ? NoData(frame)
            : Success(frame, result);
    }
}

public sealed class SymbolMetricLookupCapabilityExecutor(
    ISymbolMetricLookupService lookupService,
    TimeProvider timeProvider) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "symbol_metric_lookup";

    public async Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        var symbols = SplitValues(frame.Value(QuerySlotType.CompaniesOrSymbols));
        if (symbols.Count == 0 && frame.Value(QuerySlotType.CompanyOrSymbol) is { } symbol)
            symbols = [symbol];
        var metrics = SplitValues(frame.Value(QuerySlotType.Metrics));
        if (metrics.Count == 0 && frame.Value(QuerySlotType.Metric) is { } metric)
            metrics = [metric];
        if (symbols.Count == 0 || metrics.Count == 0) return Missing(frame, DialogueOutcomeReasonCodes.RequiredInputMissing);
        var periodText = frame.Value(QuerySlotType.Period);
        var period = Enum.TryParse<SymbolLookupPeriodSelector>(periodText, true, out var parsed) ? parsed : (SymbolLookupPeriodSelector?)null;
        var lookupRequestedMetrics = period == SymbolLookupPeriodSelector.SameMonthLastYear &&
                                     string.Equals(metrics[0], "MONTHLY_SALES", StringComparison.OrdinalIgnoreCase)
            ? new[] { "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH" }.Concat(metrics.Skip(1))
            : metrics;
        var metricCodes = lookupRequestedMetrics.Concat(SemanticLookupMetricPolicy.ContextMetricCodes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var table = await lookupService.LookupAsync(new SymbolLookupRequest(
            symbols.SelectMany(symbolName => metricCodes.Select(code => new SymbolLookupRequestPair(
                symbolName,
                new MetricCode(code),
                period is not null && period != SymbolLookupPeriodSelector.SameMonthLastYear &&
                string.Equals(code, metrics[0], StringComparison.OrdinalIgnoreCase) ? period : null))).ToArray(),
            DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime),
            context.ActorId.ToString(),
            frame.Interpretation.OriginalText), cancellationToken);
        var effectiveRequestedMetrics = lookupRequestedMetrics.ToArray();
        table = table with { RequestedMetricCodes = effectiveRequestedMetrics };
        if (SemanticLookupMetricPolicy.ProjectionMetricCodes(metrics, period) is { } projection)
            table = SemanticLookupMetricPolicy.ProjectMetrics(table, projection, effectiveRequestedMetrics);
        return table.Rows.Count == 0 ? NoData(frame, table) : Success(frame, table);
    }
}

public sealed class ProductRevenueMixCapabilityExecutor(IProductRevenueMixQueryUseCase useCase) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "product_revenue_mix";
    public async Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        var symbol = frame.Value(QuerySlotType.CompanyOrSymbol);
        if (symbol is null) return Missing(frame, DialogueOutcomeReasonCodes.RequiredInputMissing);
        var result = await useCase.ExecuteAsync(new ProductRevenueMixQuery(symbol), cancellationToken);
        return result is null ? NoData(frame) : Success(frame, result);
    }
}

public sealed class FinancialStatementTableCapabilityExecutor(IFinancialStatementTableQueryUseCase useCase) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "financial_statement_table";
    public async Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        var query = new FinancialStatementTableQuery(
            frame.Interpretation.OriginalText,
            frame.Value(QuerySlotType.CompanyOrSymbol),
            ParseEnum<FinancialStatementType>(frame.Value(QuerySlotType.StatementType)),
            ParseInt(frame.Value(QuerySlotType.Period)),
            ParseBool(frame.Value(QuerySlotType.AuditStatus)),
            ParseBool(frame.Value(QuerySlotType.RestatementStatus)),
            ParseBool(frame.Value(QuerySlotType.ConsolidationScope)));
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return result is null ? NoData(frame) : Success(frame, result);
    }
}

public sealed class FinancialStatementAnalysisCapabilityExecutor(IFinancialStatementAnalysisUseCase useCase) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "financial_statement_period_analysis";
    public async Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        var statementType = ParseEnum<FinancialStatementType>(frame.Value(QuerySlotType.StatementType));
        var metricFocus = SplitValues(frame.Value(QuerySlotType.MetricSet));
        var query = new FinancialStatementAnalysisQuery(
            frame.Interpretation.OriginalText,
            frame.Value(QuerySlotType.CompanyOrSymbol),
            ParseInt(frame.Value(QuerySlotType.Period)),
            statementType,
            ParseEnum<FinancialStatementVariantPreference>(frame.Value(QuerySlotType.ConsolidationScope))
                ?? FinancialStatementVariantPreference.DefaultNonConsolidated,
            ParseBool(frame.Value(QuerySlotType.AuditStatus)),
            metricFocus,
            IncludeBalanceSheetSummary: statementType == FinancialStatementType.BalanceSheet || metricFocus.Any(code =>
                code is "TOTAL_ASSETS" or "TOTAL_LIABILITIES" or "TOTAL_EQUITY" or "CURRENT_RATIO" or "DEBT_RATIO"),
            IncludeReturnMetrics: metricFocus.Any(code => code is "ROA" or "ROE"),
            IncludeSourceDetails: true);
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return result is null ? NoData(frame) : Success(frame, result);
    }
}

public sealed class DisclosureListingCapabilityExecutor(IDisclosureListingUseCase useCase) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "disclosure_listing";
    public async Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        var query = new DisclosureListingQuery(
            Types: SplitValues(frame.Value(QuerySlotType.DisclosureTypes))
                .Select(ParseEnum<CompanyDisclosureType>)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray(),
            SymbolOrCompany: frame.Value(QuerySlotType.CompanyOrSymbol),
            PublishedFrom: ParseDate(frame.Value(QuerySlotType.PublishedFrom)),
            PublishedTo: ParseDate(frame.Value(QuerySlotType.PublishedTo)),
            ConsolidationScope: ParseEnum<DisclosureConsolidationScope>(frame.Value(QuerySlotType.ConsolidationScope))
                ?? DisclosureConsolidationScope.NonConsolidated,
            Page: Math.Max(1, context.Page),
            PageSize: Math.Clamp(context.PageSize, 1, 100),
            Channel: context.Channel);
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return result.Items.Count == 0 ? NoData(frame, result) : Success(frame, result);
    }
}

public sealed class MonthlySalesQualityCapabilityExecutor(IMonthlySalesQualityRankingQueryUseCase useCase) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "monthly_sales_quality_ranking";
    public async Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        var industry = frame.Value(QuerySlotType.Industry);
        var query = new MonthlySalesQualityRankingQuery(
            IndustryTitle: industry,
            Scope: string.IsNullOrWhiteSpace(industry) ? MonthlySalesQualityScope.Market : MonthlySalesQualityScope.Industry,
            Direction: ParseEnum<MonthlySalesQualityDirection>(frame.Value(QuerySlotType.Sort)) ?? MonthlySalesQualityDirection.Top,
            Limit: ParseInt(frame.Value(QuerySlotType.ResultLimit)) ?? 0,
            IncludeExplanation: true,
            IncludeDimensionScores: false,
            OnlyEligibleRows: true);
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return result.Items.Count == 0 ? NoData(frame, result) : Success(frame, result);
    }
}

public sealed class PsGaugeCapabilityExecutor(IPsVisualizationExperienceUseCase useCase) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "ps_gauge_visualization";
    public async Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        var symbol = frame.Value(QuerySlotType.CompanyOrSymbol);
        if (symbol is null) return Missing(frame, DialogueOutcomeReasonCodes.RequiredInputMissing);
        var result = await useCase.ExecuteAsync(new PsVisualizationQuery(symbol, true), cancellationToken);
        return result is null ? NoData(frame) : Success(frame, result);
    }
}

public sealed class ComprehensiveAnalysisCapabilityExecutor(
    IComprehensiveAnalysisQueryUseCase useCase,
    ISymbolMetricLookupService lookupService,
    TimeProvider timeProvider) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "comprehensive_analysis";
    public async Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        var symbols = frame.Value(QuerySlotType.CompanyOrSymbol) is { } symbol ? new[] { symbol } : [];
        if (symbols.Length == 0) return Missing(frame, DialogueOutcomeReasonCodes.RequiredInputMissing);
        var topics = SplitValues(frame.Value(QuerySlotType.AnalysisTopic));
        var fromDate = ParseDateTimeOffset(frame.Value(QuerySlotType.Period));
        var limit = Math.Clamp(ParseInt(frame.Value(QuerySlotType.ResultLimit)) ?? 3, 1, 5);

        var analysisTask = TryExecuteAsync(
            () => useCase.ExecuteAsync(
                new ComprehensiveAnalysisQueryRequest(symbols, topics, fromDate, limit),
                cancellationToken),
            cancellationToken);
        var lookupTask = TryExecuteAsync(
            () => lookupService.LookupAsync(new SymbolLookupRequest(
                symbols.SelectMany(symbolName => SemanticLookupMetricPolicy.ContextMetricCodes.Select(metricCode =>
                    new SymbolLookupRequestPair(symbolName, new MetricCode(metricCode)))).ToArray(),
                DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime),
                context.ActorId.ToString(),
                frame.Interpretation.OriginalText), cancellationToken),
            cancellationToken);
        await Task.WhenAll(analysisTask, lookupTask);

        var analysisAttempt = await analysisTask;
        var lookupAttempt = await lookupTask;
        if (analysisAttempt.Failed && lookupAttempt.Failed)
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Failed,
                DialogueOutcomeReasonCodes.ProviderOrToolFailure);

        var analysis = analysisAttempt.Value ?? new ComprehensiveAnalysisQueryResponse([], symbols);
        var lookup = lookupAttempt.Value ?? EmptyLookup(symbols, context.Now);
        var payload = new SemanticComprehensiveAnalysisPayload(analysis, lookup);
        var hasAnalysis = analysis.HasResults;
        var hasMetrics = lookup.Rows.Count > 0;
        var warnings = new List<string>();
        if (analysisAttempt.Failed) warnings.Add("analysis_posts_unavailable");
        else if (!hasAnalysis) warnings.Add("analysis_posts_unavailable");
        if (lookupAttempt.Failed) warnings.Add("live_metrics_unavailable");
        else if (!hasMetrics) warnings.Add("live_metrics_unavailable");

        if ((analysisAttempt.Failed && !hasMetrics) || (lookupAttempt.Failed && !hasAnalysis))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.TemporarilyUnavailable,
                DialogueOutcomeReasonCodes.ProviderOrToolFailure, payload, warnings);
        if (!hasAnalysis && !hasMetrics) return NoData(frame, payload);
        return hasAnalysis && hasMetrics
            ? Success(frame, payload)
            : new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Partial,
                DialogueOutcomeReasonCodes.PartialEvidence, payload, warnings);
    }

    private async Task<ExecutionAttempt<T>> TryExecuteAsync<T>(
        Func<Task<T>> execute,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            return new(await execute(), false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(null, true);
        }
    }

    private static SymbolLookupTableResult EmptyLookup(IReadOnlyCollection<string> symbols, DateTimeOffset now) =>
        new(Guid.NewGuid(), [], [], new(now, TimeSpan.Zero, symbols.Count, 0, false), [], symbols);

    private sealed record ExecutionAttempt<T>(T? Value, bool Failed) where T : class;
}

public sealed class PersonalizedInsightExplanationCapabilityExecutor(
    IExplainInsightUseCase useCase) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "personalized_insight_explanation";

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        ValidatedQueryFrame frame,
        QueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(frame.Value(QuerySlotType.Insight), out var insightEventId))
            return Missing(frame, DialogueOutcomeReasonCodes.RequiredInputMissing);
        var explanation = await useCase.ExecuteAsync(new ExplainInsightQuery(
            new CurrentActor(
                context.ActorType,
                context.ActorId,
                context.TenantId,
                context.AuthenticationMode,
                context.UserId,
                context.ApiClientId),
            insightEventId), cancellationToken);
        return Success(frame, explanation);
    }
}

internal static class SemanticExecutionResultFactory
{
    public static string? Value(this ValidatedQueryFrame frame, QuerySlotType type) => frame.Slots.FirstOrDefault(slot => slot.Type == type && slot.ValidationState == QuerySlotValidationState.Valid)?.Value;
    public static CapabilityExecutionResult Success(ValidatedQueryFrame frame, object payload) => new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Executed, DialogueOutcomeReasonCodes.None, payload);
    public static CapabilityExecutionResult NoData(ValidatedQueryFrame frame, object? payload = null) => new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.NoData, DialogueOutcomeReasonCodes.SupportedButNoRows, payload);
    public static CapabilityExecutionResult Missing(ValidatedQueryFrame frame, string reason) => new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.ClarificationRequired, reason);
    public static TEnum? ParseEnum<TEnum>(string? value) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : null;
    public static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    public static bool? ParseBool(string? value) =>
        bool.TryParse(value, out var parsed) ? parsed : null;
    public static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    public static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
    public static IReadOnlyList<string> SplitValues(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal static class SemanticLookupMetricPolicy
{
    public static readonly IReadOnlyList<string> ContextMetricCodes =
        ["LATEST_PRICE", "DAILY_CHANGE_PCT", "MONTHLY_SALES", "PE_TTM", "PS_TTM", "EPS"];

    private static readonly IReadOnlySet<string> ContextSuppressedMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AVG_12M_MONTHLY_SALES", "MONTHLY_SALES_YTD", "MONTHLY_SALES_YTD_PREVIOUS_MONTH",
        "MONTHLY_SALES_QUANTITY", "MONTHLY_PRODUCTION_QUANTITY", "MONTHLY_SALES_RATE",
        "MONTHLY_SALES_GROWTH_YOY", "MONTHLY_SALES_GROWTH_MOM", "MONTHLY_PRODUCTION_GROWTH_YOY",
        "MONTHLY_SALES_QUANTITY_GROWTH_YOY", "MONTHLY_SALES_TO_PRODUCTION_RATIO"
    };

    private static readonly IReadOnlyList<string> LatestMonthlySalesFamily =
    [
        "MONTHLY_SALES",
        "AVG_12M_MONTHLY_SALES",
        "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH",
        "MONTHLY_SALES_YTD",
        "MONTHLY_SALES_YTD_PREVIOUS_MONTH"
    ];

    public static IReadOnlyList<string>? ProjectionMetricCodes(
        IReadOnlyList<string> requestedMetrics,
        SymbolLookupPeriodSelector? period)
    {
        if (period == SymbolLookupPeriodSelector.SameMonthLastYear &&
            requestedMetrics.Count == 1 &&
            string.Equals(requestedMetrics[0], "MONTHLY_SALES", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "MONTHLY_SALES",
                "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH",
                "MONTHLY_SALES_YTD",
                "MONTHLY_SALES_YTD_PREVIOUS_MONTH"
            ];
        }
        if (period is not null) return requestedMetrics;
        if (requestedMetrics.Count == 1 && string.Equals(requestedMetrics[0], "MONTHLY_SALES", StringComparison.OrdinalIgnoreCase))
            return LatestMonthlySalesFamily;
        return requestedMetrics.All(ContextSuppressedMetrics.Contains) ? requestedMetrics : null;
    }

    public static SymbolLookupTableResult ProjectMetrics(
        SymbolLookupTableResult table,
        IReadOnlyCollection<string> retainedMetricCodes,
        IReadOnlyList<string> requestedMetricCodes)
    {
        var retainedMetrics = retainedMetricCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedColumns = table.Columns.Where(column =>
            column.ColumnType is ScannerColumnType.Symbol or ScannerColumnType.CompanyName ||
            retainedMetrics.Contains(column.MetricCode ?? column.Identifier)).ToArray();
        var retainedIds = retainedColumns.Select(column => column.Identifier).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = table.Rows.Select(row => row with
        {
            Cells = row.Cells.Where(cell => retainedIds.Contains(cell.Key))
                .ToDictionary(cell => cell.Key, cell => cell.Value, StringComparer.OrdinalIgnoreCase)
        }).ToArray();
        return table with { Columns = retainedColumns, Rows = rows, RequestedMetricCodes = requestedMetricCodes };
    }
}
