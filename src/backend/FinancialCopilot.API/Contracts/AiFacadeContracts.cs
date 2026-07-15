using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.API.Contracts;

public sealed record AiQueryHttpRequest(
    string Message,
    Guid? ConversationId = null,
    int ScannerPage = 1,
    int ScannerPageSize = 20,
    AiQueryContextHttpRequest? Context = null);

public sealed record AiQueryContextHttpRequest(
    Guid? InsightEventId = null,
    Guid? AlertId = null);

public sealed record AiQueryHttpResponse(
    Guid ConversationId,
    Guid MessageId,
    Guid AssistantMessageId,
    string Intent,
    bool ClarificationRequired,
    string? ClarificationMessage,
    string? TextAnswer,
    ScannerPlanResponse? ScannerPlan,
    ScannerTableResponse? ScannerTable = null,
    ScannerTableResponse? SymbolLookupTable = null,
    ExplainableAnswerResponse? ExplainableAnswer = null,
    ConfidenceScoreResponse? ConfidenceScore = null,
    UsageAccountingResponse? Usage = null,
    IReadOnlyCollection<MemoryDisclosureResponse>? MemoryDisclosures = null,
    string? AiOrchestrationMode = null,
    string? WorkflowVersion = null,
    string? ProviderSelection = null,
    bool? ProviderFallbackOccurred = null,
    string? WorkflowCorrelationId = null,
    ComprehensiveAnalysisResultResponse? ComprehensiveAnalysisResult = null,
    FinancialStatementAnalysisHttpResponse? FinancialStatementAnalysisResult = null,
    FinancialStatementTableHttpResponse? FinancialStatementTableResult = null,
    MonthlyActivityTrendChartResponse? MonthlyActivityTrendResult = null,
    MonthlySalesQualityRankingHttpResponse? MonthlySalesQualityRankingResult = null);

public sealed record UsageAccountingResponse(
    string OperationCode,
    string CompletionStatus,
    decimal CreditsCharged,
    decimal RemainingSpendingCapacity,
    string PricingPolicyVersion,
    bool Cached,
    string? ProviderName = null,
    string? ModelName = null,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    int? TotalTokens = null,
    decimal? EstimatedCost = null);

public sealed record ScannerPlanResponse(
    Guid PlanId,
    int ConditionCount,
    bool ClarificationRequired,
    string? ClarificationMessage,
    IReadOnlyCollection<string> ColumnOverflowWarnings);

public sealed record ScannerTableColumnResponse(
    string Identifier,
    string DisplayName,
    string ColumnType,
    string? MetricCode);

public sealed record ScannerTableCellResponse(
    decimal? Value,
    string? FormattedValue,
    string FreshnessStatus,
    DateTimeOffset? SourceTimestamp,
    DateOnly? TradingDate = null,
    string? TradingDatePersian = null,
    string? SourceLabel = null);

public sealed record ScannerTableRowResponse(
    string SymbolCode,
    string? CompanyName,
    IReadOnlyDictionary<string, ScannerTableCellResponse> Cells,
    double Score,
    IReadOnlyCollection<string> MatchedConditionMetrics);

public sealed record ScannerExecutionFactsResponse(
    DateTimeOffset ExecutedAt,
    TimeSpan Duration,
    int TotalSymbolsEvaluated,
    int MatchingSymbolCount,
    bool FromCache,
    int Page,
    int PageSize,
    int TotalPages);

public sealed record ScannerTableResponse(
    Guid PlanId,
    IReadOnlyCollection<ScannerTableColumnResponse> Columns,
    IReadOnlyCollection<ScannerTableRowResponse> Rows,
    ScannerExecutionFactsResponse ExecutionFacts,
    IReadOnlyCollection<string> MissingDataWarnings);

public sealed record ConditionFilterChipResponse(
    string MetricCode,
    string MetricDisplayName,
    string OperatorSymbol,
    string OperatorLabel,
    decimal Threshold,
    string ThresholdFormatted,
    string FilterOrigin,
    bool IsInferred,
    string? InferredReason);

public sealed record MetricEvidenceSummaryResponse(
    string MetricCode,
    string MetricVersion,
    string CalculationPolicyVersion,
    string MetricDisplayName,
    string Unit,
    decimal? ActualValue,
    string? FormattedValue,
    string PeriodType,
    DateTimeOffset? ObservedAt);

public sealed record DataCitationResponse(
    string SymbolCode,
    string MetricCode,
    DateTimeOffset? ObservedAt,
    string FreshnessStatus,
    string? SourceProvider = null);

public sealed record ConfidenceFactorsResponse(
    double InterpretationCertainty,
    double EvidenceCompleteness,
    double SourceFreshness,
    double WarningPenalty);

public sealed record ConfidenceScoreResponse(
    double Score,
    ConfidenceFactorsResponse Factors,
    string PolicyVersion);

public sealed record ExplainableAnswerResponse(
    IReadOnlyCollection<ConditionFilterChipResponse> FilterChips,
    IReadOnlyCollection<MetricEvidenceSummaryResponse> MetricEvidence,
    IReadOnlyCollection<DataCitationResponse> DataCitations,
    ConfidenceScoreResponse Confidence,
    IReadOnlyCollection<string> SuggestedFollowUpQuestions,
    string? ExplanationText);

public sealed record ConversationSummaryResponse(
    Guid ConversationId,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount,
    string Title);

public sealed record ConversationDetailResponse(
    Guid ConversationId,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<MessageResponse> Messages);

public sealed record MessageResponse(
    Guid MessageId,
    string Role,
    string Content,
    bool HasScannerPlan,
    DateTimeOffset CreatedAt,
    AssistantMessageContentResponse? AssistantContent = null);

public sealed record AssistantMessageContentResponse(
    int Version,
    string Intent,
    bool ClarificationRequired,
    string? ClarificationMessage,
    string? TextAnswer,
    ScannerPlanResponse? ScannerPlan,
    ScannerTableResponse? ScannerTable,
    ScannerTableResponse? SymbolLookupTable,
    ExplainableAnswerResponse? ExplainableAnswer,
    ConfidenceScoreResponse? ConfidenceScore,
    UsageAccountingResponse? Usage,
    IReadOnlyCollection<MemoryDisclosureResponse>? MemoryDisclosures,
    ComprehensiveAnalysisResultResponse? ComprehensiveAnalysisResult = null,
    FinancialStatementAnalysisHttpResponse? FinancialStatementAnalysisResult = null,
    FinancialStatementTableHttpResponse? FinancialStatementTableResult = null,
    MonthlyActivityTrendChartResponse? MonthlyActivityTrendResult = null,
    MonthlySalesQualityRankingHttpResponse? MonthlySalesQualityRankingResult = null);

public sealed record ComprehensiveAnalysisResultResponse(
    IReadOnlyCollection<ComprehensiveAnalysisItemResponse> Items,
    IReadOnlyList<string> UnresolvedSymbols,
    bool HasResults);

public sealed record ComprehensiveAnalysisItemResponse(
    long AnalysisId,
    string Title,
    string PersianCreatedAt,
    string AuthorName,
    string PlainTextSummary,
    IReadOnlyList<string> TagNames,
    DateTimeOffset SyncedAt);

public sealed record FinancialStatementAnalysisHttpResponse(
    string CompanySymbol,
    string? CompanyName,
    int SelectedPeriodMonths,
    string SelectedPeriodType,
    string? JalaliPeriodEnd,
    string? JalaliFiscalYearEnd,
    string SelectedVariant,
    bool? SelectedAuditedStatus,
    IReadOnlyList<string> SummaryBullets,
    IReadOnlyList<FinancialStatementAnalysisSectionResponse> Sections,
    IReadOnlyList<FinancialStatementSourceReferenceResponse> SourceReferences,
    IReadOnlyList<string> Warnings,
    double ConfidenceScore,
    DateTimeOffset GeneratedAtUtc);

public sealed record FinancialStatementAnalysisSectionResponse(
    string TitleFa,
    IReadOnlyList<string> SummaryBullets,
    IReadOnlyList<FinancialStatementMetricComparisonResponse> Metrics);

public sealed record FinancialStatementMetricComparisonResponse(
    string MetricCode,
    string LabelFa,
    decimal? CurrentValue,
    string? CurrentFormattedValue,
    decimal? PreviousValue,
    string? PreviousFormattedValue,
    decimal? ChangePercent,
    string? ChangeDirectionFa,
    string? Indicator,
    bool IsUnavailable,
    string? Warning);

public sealed record FinancialStatementSourceReferenceResponse(
    string StatementType,
    Guid StatementId,
    string ExternalStatementId,
    string ProviderName,
    string PeriodType,
    int PeriodMonths,
    string? JalaliPeriodEnd,
    string? JalaliFiscalYearEnd,
    string? JalaliAnnouncementDate,
    bool IsAudited,
    bool IsRepresented,
    bool IsComposing);

public sealed record FinancialStatementTableHttpResponse(
    FinancialStatementTableSourceHttpResponse Source,
    IReadOnlyList<FinancialStatementTableLineItemHttpResponse> LineItems,
    IReadOnlyList<BalanceSheetTableRowHttpResponse> BalanceSheetRows,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAtUtc);

public sealed record FinancialStatementTableSourceHttpResponse(
    Guid StatementId,
    string ExternalStatementId,
    string ProviderName,
    string ExternalCompanyId,
    string CompanySymbol,
    string? CompanyName,
    string StatementType,
    string PeriodType,
    int PeriodMonths,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset? AnnouncementDate,
    string? JalaliPeriodEnd,
    string? JalaliFiscalYearEnd,
    string? JalaliAnnouncementDate,
    bool IsAudited,
    bool IsRepresented,
    bool IsComposing,
    string? Unit);

public sealed record FinancialStatementTableLineItemHttpResponse(
    int RowNumber,
    int? SourceItemId,
    string? TitleFa,
    string? TitleEn,
    string? MetricCode,
    decimal? Value,
    string? FormattedValue,
    string? Unit,
    string Side);

public sealed record BalanceSheetTableRowHttpResponse(
    FinancialStatementTableLineItemHttpResponse? Asset,
    FinancialStatementTableLineItemHttpResponse? LiabilityOrEquity);

// ---------------------------------------------------------------------------
// Monthly Activity Trend chart response (spec 077)
// ---------------------------------------------------------------------------

public sealed record MonthlyActivityTrendChartResponse(
    string CompanySymbol,
    string? CompanyName,
    int LatestReportYear,
    int LatestReportMonth,
    string UnitLabelFa,
    decimal? LatestMonthlySalesAmount,
    decimal? SameMonthPreviousYearSalesAmount,
    decimal? Average12MonthSalesAmount,
    decimal? SalesAmountYoYGrowthPercent,
    decimal? SalesVsAverage12MonthPercent,
    decimal? YtdSalesAmount,
    decimal? YtdPreviousMonthSalesAmount,
    IReadOnlyList<MonthlyActivityTrendChartPointResponse> ChartPoints,
    IReadOnlyList<MonthlyActivityTrendInsightResponse> Insights,
    IReadOnlyList<MonthlyActivityTrendMissingDataPointResponse> MissingDataPoints,
    string SourceProviderName,
    DateTimeOffset CalculatedAtUtc);

public sealed record MonthlyActivityTrendChartPointResponse(
    int FiscalMonthIndex,
    string FiscalMonthNameFa,
    int? PreviousFiscalYear,
    decimal? PreviousFiscalYearSalesAmount,
    int? CurrentFiscalYear,
    decimal? CurrentFiscalYearSalesAmount,
    decimal? Average12MonthSalesAmount,
    bool IsCurrentYearReported,
    bool IsPreviousYearReported);

public sealed record MonthlyActivityTrendInsightResponse(
    string Kind,
    string TextFa);

public sealed record MonthlyActivityTrendMissingDataPointResponse(
    int Year,
    int Month,
    string ReasonFa);
