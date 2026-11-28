using System.Security.Cryptography;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.FinancialData;

namespace FinancialCopilot.Application.AI.Orchestration;

public enum DetectedIntent
{
    Scanner,
    SymbolLookup,
    ComprehensiveAnalysis,
    FinancialStatementPeriodAnalysis,
    FinancialStatementTableLookup,
    ProductRevenueMix,
    MonthlyActivityTrend,
    MonthlyProductComparison,
    MonthlyProductTrend,
    DisclosureListing,
    MonthlySalesQualityRanking,
    PsGaugeVisualization,
    PersonalizedInsightExplanation,
    FinancialStatementValueSearch,
    Clarification,
    Unknown,
    // Persisted as an integer: new members go last.
    SymbolSnapshot,
    // Feature 162: a turn answered by Report Discussion from the conversation's active report.
    ReportDiscussion,
    // Feature 159 change «standalone-valuation-gauge-cards»: standalone valuation gauge cards (V2).
    ValuationGauges,
    // Feature 168: option comparison answers (V2).
    OptionComparison
}

public sealed record IntentDetectionInput(
    string UserQuery,
    string Language,
    string CorrelationId,
    Guid TenantId);

public sealed record IntentDetectionResult(
    DetectedIntent Intent,
    double Confidence,
    string? Reasoning = null);

// Raw JSON structure returned by the LLM for intent detection.
public sealed record LlmIntentOutput(
    string Intent,
    double Confidence,
    string? Reasoning = null);

public sealed record AiQueryRequest(
    string Message,
    Guid TenantId,
    Guid ActorId,
    string CorrelationId,
    Guid? ConversationId = null,
    Guid? UserId = null,
    Guid? ApiClientId = null,
    string? ExternalUserId = null,
    int ScannerPage = 1,
    int ScannerPageSize = 20,
    int DisclosurePage = 1,
    int DisclosurePageSize = 20,
    ActorType ActorType = ActorType.User,
    AuthenticationMode AuthenticationMode = AuthenticationMode.WebAppUser,
    AiQueryContext? Context = null,
    string? OriginalUserMessage = null,
    ValidatedQueryFrame? SemanticFrame = null,
    ValidatedQueryFrame? SemanticShadowFrame = null,
    string? SuggestedActionId = null,
    string? SemanticRoutingDisposition = null,
    Guid? BillingOperationId = null,
    // Feature 162: set when Report Discussion handed this turn to the normal route; persisted with the answer.
    // Server-derived, so it is excluded from the request fingerprint.
    FinancialCopilot.Application.AI.ReportDiscussion.ReportDiscussionMarker? ReportDiscussionHandBack = null,
    // Change «sol-deep-analysis-web-search» (FR-4): the caller's depth and web choice. No amount is ever accepted.
    FinancialCopilot.Application.AI.GroundedAnalysis.AnalysisOptionsRequest? AnalysisOptions = null);

public static class AiQueryRequestFingerprint
{
    private sealed record Payload(
        int Version,
        string Message,
        Guid? ConversationId,
        int ScannerPage,
        int ScannerPageSize,
        int DisclosurePage,
        int DisclosurePageSize,
        Guid? InsightEventId,
        Guid? AlertId,
        int? MonthlyActivityTrendReportYear,
        int? MonthlyActivityTrendReportMonth,
        string? SuggestedActionId);

    // Version 2 adds the analysis choice, which changes the price. A request without options keeps the Version 1
    // payload byte for byte, so existing reservations and replays keep their identity.
    private sealed record PayloadV2(
        int Version,
        Payload Base,
        string? AnalysisDepth,
        bool? AnalysisWebSearch,
        string? AnalysisPricingVersion);

    // Hash only the stable caller payload. Correlation IDs, workflow frames, and derived server
    // context are excluded so retries can have a different trace ID without changing billing
    // identity. The raw prompt is never persisted with the reservation.
    public static string Compute(AiQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payload = new Payload(
            1,
            request.OriginalUserMessage ?? request.Message,
            request.ConversationId,
            request.ScannerPage,
            request.ScannerPageSize,
            request.DisclosurePage,
            request.DisclosurePageSize,
            request.Context?.InsightEventId,
            request.Context?.AlertId,
            request.Context?.MonthlyActivityTrendReportYear,
            request.Context?.MonthlyActivityTrendReportMonth,
            request.SuggestedActionId);
        var bytes = request.AnalysisOptions is { } options
            ? JsonSerializer.SerializeToUtf8Bytes(new PayloadV2(
                2,
                payload,
                options.Depth?.ToString(),
                options.WebSearch,
                options.PricingVersion))
            : JsonSerializer.SerializeToUtf8Bytes(payload);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

public sealed record AiQueryContext(
    Guid? InsightEventId = null,
    Guid? AlertId = null,
    int? MonthlyActivityTrendReportYear = null,
    int? MonthlyActivityTrendReportMonth = null);

public sealed record AiQueryResponse(
    Guid ConversationId,
    Guid MessageId,
    Guid AssistantMessageId,
    DetectedIntent Intent,
    ScannerQueryPlan? ScannerPlan,
    ScannerTableResult? ScannerTable,
    SymbolLookupTableResult? SymbolLookupTable,
    ExplainableAnswer? ExplainableAnswer,
    ConfidenceScoreResult? ConfidenceScore,
    string? TextAnswer,
    bool ClarificationRequired,
    string? ClarificationMessage,
    UsageAccountingResult? Usage,
    IReadOnlyCollection<MemoryUseDisclosure>? MemoryDisclosures = null,
    string? AiOrchestrationMode = null,
    string? WorkflowVersion = null,
    string? ProviderSelection = null,
    bool? ProviderFallbackOccurred = null,
    string? WorkflowCorrelationId = null,
    ComprehensiveAnalysisQueryResponse? ComprehensiveAnalysisResult = null,
    FinancialStatementAnalysisResponse? FinancialStatementAnalysisResult = null,
    FinancialStatementTableResult? FinancialStatementTableResult = null,
    ProductRevenueMixResponse? ProductRevenueMixResult = null,
    MonthlyActivityTrendResponse? MonthlyActivityTrendResult = null,
    MonthlySalesQualityRankingResponse? MonthlySalesQualityRankingResult = null,
    DisclosureListingResult? DisclosureListingResult = null,
    PsVisualizationResult? PsVisualizationResult = null,
    DialogueOutcome Outcome = DialogueOutcome.Answered,
    string OutcomeReasonCode = DialogueOutcomeReasonCodes.None,
    string ReplyLanguage = "en",
    bool LanguageGuardApplied = false,
    IReadOnlyCollection<SuggestedAction>? SuggestedActions = null,
    string? SemanticCapabilityCode = null,
    int? SemanticRegistryVersion = null,
    MonthlyProductComparisonResponse? MonthlyProductComparisonResult = null,
    FinancialStatementValueSearchResult? FinancialStatementValueSearchResult = null,
    MonthlyProductTrendResult? MonthlyProductTrendResult = null,
    SymbolSnapshotResult? SymbolSnapshotResult = null,
    // Change «sol-deep-analysis-web-search»: prices, defaults and a pending offer (FR-4, FR-7), and the external
    // sources an answer cited (FR-10).
    FinancialCopilot.Application.AI.GroundedAnalysis.GroundedAnalysisHint? GroundedAnalysis = null,
    IReadOnlyList<FinancialCopilot.Application.AI.GroundedAnalysis.ExternalSource>? Sources = null,
    FinancialCopilot.Application.FinancialData.ValuationGauges.ValuationGaugeCardsResult? ValuationGaugeCards = null,
    FinancialCopilot.Application.FinancialData.Options.OptionComparison? OptionComparison = null);

public sealed record UsageAccountingResult(
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
    decimal? EstimatedCost = null,
    string? ExecutionRoute = null);

// Integration point for mandatory Billing reservation/finalization.
// Story 007 defines this boundary; Story 010 provides the real implementation.
// The no-op implementation allows the orchestrator to compile and be tested
// without billing policy being active.
public sealed record BillingReservationRequest(
    string CorrelationId,
    Guid TenantId,
    Guid ActorId,
    string OperationCode,
    Guid? UserId,
    Guid? ApiClientId,
    string? ExternalUserId = null,
    // The correlation id the turn's model calls are recorded under, when the reservation
    // uses a derived correlation id (for example the Feature 141 fallback reservation).
    string? UsageCorrelationId = null,
    Guid? BillingOperationId = null,
    string? RequestFingerprint = null);

public sealed record BillingReservationHandle(
    string ReservationId,
    string CorrelationId,
    Guid CustomerAccountId,
    Guid TenantId,
    Guid ActorId,
    Guid? ApiClientId,
    string? ExternalUserId,
    string OperationCode,
    string? AllocationSource = null,
    string? AllowanceDateKey = null,
    // The credits held for this turn: the most its finalization may charge.
    decimal ReservedCredits = 0,
    string? UsageCorrelationId = null,
    Guid? BillingOperationId = null,
    string QuotaMode = "Finite",
    Guid? SourceSubscriptionId = null,
    DateTimeOffset? SourceSubscriptionExpiresAtUtc = null);

public sealed record BillingFinalizationRequest(
    string CompletionStatus,
    bool Cached = false,
    string? ProviderName = null,
    string? ModelName = null,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    int? TotalTokens = null,
    decimal? EstimatedCost = null,
    string? ExecutionRoute = null);

public interface IBillingFacadeHook
{
    // Attempts to reserve billing credit before executing the AI workflow.
    // Returns null when billing is not active (no-op phase) or entitlement fails.
    Task<BillingReservationHandle?> TryReserveAsync(
        BillingReservationRequest request,
        CancellationToken cancellationToken);

    // In Enforced mode, increase the existing hold before entering an expensive path.
    // Legacy and Shadow retain their operation-based reservation.
    Task<BillingReservationHandle> EnsureReservedCapacityAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken) => Task.FromResult(handle);

    // Reserves a fixed minimum amount before a path whose price is outcome-based.
    // Implementations that do not support targeted holds retain their existing behavior.
    Task<BillingReservationHandle> EnsureReservedCapacityAsync(
        BillingReservationHandle handle,
        decimal requiredCredits,
        CancellationToken cancellationToken) => EnsureReservedCapacityAsync(handle, cancellationToken);

    Task<BillingReservationHandle> EnsureCurrentUsageCapacityAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken) => Task.FromResult(handle);

    // Finalizes the reservation after the workflow completes.
    Task<UsageAccountingResult?> FinalizeAsync(
        BillingReservationHandle handle,
        BillingFinalizationRequest request,
        CancellationToken cancellationToken);

    // Releases the reservation if the workflow was abandoned or failed early.
    Task ReleaseAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken);

    // Change «sol-deep-analysis-web-search» (FR-8): the status of the reservation keyed by this billing operation id
    // (Reserved, Committed, Released, Expired), or null when none exists. A 163 offer's derived key is its single
    // arbiter: once claimed by a confirm or a cancel, no later request runs the analysis.
    Task<string?> FindReservationStatusAsync(
        Guid billingOperationId,
        CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}

public interface IAiIntentDetector
{
    Task<IntentDetectionResult> DetectAsync(
        IntentDetectionInput input,
        CancellationToken cancellationToken);
}

public interface IAiQueryOrchestrationService
{
    Task<AiQueryResponse> ExecuteAsync(
        AiQueryRequest request,
        CancellationToken cancellationToken);
}
