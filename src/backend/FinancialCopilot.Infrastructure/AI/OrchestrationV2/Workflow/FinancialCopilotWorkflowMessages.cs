using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;

// Typed message records passed between workflow steps. Each record is the output of one step
// and the input of the next, flowing along the declared workflow graph edges.

internal sealed record WorkflowStartMessage(
    AiQueryRequest Request,
    Guid ConversationId,
    bool CreateConversation,
    DateTimeOffset Now,
    BillingReservationHandle? Reservation = null,
    // Feature 162: a Report Discussion reply decided before the dialogue gate; null for every other turn.
    ReportDiscussionTurn? ReportDiscussion = null,
    // Change «sol-deep-analysis-web-search»: the resolved 163 offer action (confirm or cancel); null for every other turn.
    GroundedAnalysisOfferTurn? OfferTurn = null);

/// <summary>
/// Change «sol-deep-analysis-web-search» (FR-8): the reply to a 163 offer action, composed and settled before the dialogue
/// gate. Its own reservation (keyed by the offer) is already settled, so <see cref="Usage"/> is final.
/// </summary>
internal sealed record GroundedAnalysisOfferTurn(
    string Text,
    DialogueOutcome Outcome,
    string ReasonCode,
    string CompletionStatus,
    UsageAccountingResult? Usage = null,
    SymbolGroundedFallbackAnswer? Answer = null,
    FinancialCopilot.Application.AI.GroundedAnalysis.GroundedAnalysisOffer? NewOffer = null,
    SuggestedAction? Suggestion = null);

internal sealed record MemoryRetrievedMessage(
    AiQueryRequest Request,
    Guid ConversationId,
    bool CreateConversation,
    DateTimeOffset Now,
    AuthorizedMemoryContext MemoryContext,
    string EnrichedMessage,
    BillingReservationHandle? Reservation = null,
    ReportDiscussionTurn? ReportDiscussion = null,
    GroundedAnalysisOfferTurn? OfferTurn = null);

internal sealed record BillingReservedMessage(
    AiQueryRequest Request,
    Guid ConversationId,
    bool CreateConversation,
    DateTimeOffset Now,
    AuthorizedMemoryContext MemoryContext,
    string EnrichedMessage,
    BillingReservationHandle? Reservation,
    ReportDiscussionTurn? ReportDiscussion = null,
    GroundedAnalysisOfferTurn? OfferTurn = null);

internal sealed record AgentExecutedMessage(
    AiQueryRequest Request,
    Guid ConversationId,
    bool CreateConversation,
    DateTimeOffset Now,
    AuthorizedMemoryContext MemoryContext,
    BillingReservationHandle? Reservation,
    string AgentResponseText,
    ScannerToolResult? ScannerResult,
    SymbolLookupToolResult? LookupResult,
    ComprehensiveAnalysisToolResult? ComprehensiveAnalysisResult,
    FinancialStatementAnalysisResponse? FinancialStatementAnalysisResult,
    FinancialStatementTableResult? FinancialStatementTableResult,
    ProductRevenueMixResponse? ProductRevenueMixResult,
    MonthlyActivityTrendResponse? MonthlyActivityTrendResult,
    MonthlySalesQualityRankingResponse? MonthlySalesQualityRankingResult,
    string CompletionStatus,
    bool FromCache,
    IAiModelClient? ModelClient,
    UsageAccountingResult? Usage,
    DisclosureListingResult? DisclosureListingResult = null,
    PsVisualizationResult? PsVisualizationResult = null,
    DialogueOutcome? SemanticOutcome = null,
    string? SemanticOutcomeReasonCode = null,
    string? SemanticReplyLanguage = null,
    MonthlyProductComparisonResponse? MonthlyProductComparisonResult = null,
    FinancialStatementValueSearchResult? FinancialStatementValueSearchResult = null,
     MonthlyProductTrendResult? MonthlyProductTrendResult = null,
     ValidatedQueryFrame? ExecutedFrame = null,
     SymbolSnapshotResult? SymbolSnapshotResult = null,
     ReportDiscussionTurn? ReportDiscussion = null,
     GroundedAnalysisOfferTurn? OfferTurn = null,
     IndustryRelativeValuationReadModel? IndustryComparison = null);

internal sealed record ResultsComputedMessage(
    AiQueryRequest Request,
    Guid ConversationId,
    bool CreateConversation,
    DateTimeOffset Now,
    AuthorizedMemoryContext MemoryContext,
    BillingReservationHandle? Reservation,
    string AgentResponseText,
    ScannerToolResult? ScannerResult,
    SymbolLookupToolResult? LookupResult,
    ComprehensiveAnalysisToolResult? ComprehensiveAnalysisResult,
    FinancialStatementAnalysisResponse? FinancialStatementAnalysisResult,
    FinancialStatementTableResult? FinancialStatementTableResult,
    ProductRevenueMixResponse? ProductRevenueMixResult,
    MonthlyActivityTrendResponse? MonthlyActivityTrendResult,
    MonthlySalesQualityRankingResponse? MonthlySalesQualityRankingResult,
    string CompletionStatus,
    bool FromCache,
    IAiModelClient? ModelClient,
    DetectedIntent DetectedIntent,
    bool ClarificationRequired,
    string? ClarificationMessage,
    ExplainableAnswer? ExplainableAnswer,
    ConfidenceScoreResult? ConfidenceScore,
    string? GroundedAnswer,
    UsageAccountingResult? Usage,
    DialogueOutcome Outcome = DialogueOutcome.Answered,
    string OutcomeReasonCode = DialogueOutcomeReasonCodes.None,
    string ReplyLanguage = "en",
    bool LanguageGuardApplied = false,
    DisclosureListingResult? DisclosureListingResult = null,
    PsVisualizationResult? PsVisualizationResult = null,
    IReadOnlyCollection<SuggestedAction>? SuggestedActions = null,
    MonthlyProductComparisonResponse? MonthlyProductComparisonResult = null,
    FinancialStatementValueSearchResult? FinancialStatementValueSearchResult = null,
    MonthlyProductTrendResult? MonthlyProductTrendResult = null,
    bool DeterministicSuggestionsApplied = false,
    SymbolSnapshotResult? SymbolSnapshotResult = null,
    ReportDiscussionTurn? ReportDiscussion = null,
    SymbolGroundedFallbackAnswer? SymbolGroundedFallback = null,
    // Change «sol-deep-analysis-web-search»: a 163 analysis offer made on this turn (no model call, no reservation).
    FinancialCopilot.Application.AI.GroundedAnalysis.GroundedAnalysisOffer? GroundedAnalysisOffer = null,
    // Change «sol-deep-analysis-web-search»: set when this turn resolved a 163 offer action (the dialogue gate did not run).
    GroundedAnalysisOfferTurn? OfferTurn = null);

/// <summary>Feature 163: what an answered symbol-grounded fallback adds to the persisted turn.</summary>
internal sealed record SymbolGroundedFallbackAnswer(
    FinancialStatementTableResult GroundingReport,
    FallbackOrigin Origin,
    // Change «sol-deep-analysis-web-search»: what the confirmed analysis ran and the sources it cited.
    FinancialCopilot.Application.AI.GroundedAnalysis.GroundedAnalysisRun? AnalysisRun = null,
    IReadOnlyList<FinancialCopilot.Application.AI.GroundedAnalysis.ExternalSource>? Sources = null);

internal sealed record PersistenceCompletedMessage(
    AiQueryRequest Request,
    Guid ConversationId,
    Guid UserMessageId,
    Guid AssistantMessageId,
    DetectedIntent DetectedIntent,
    bool ClarificationRequired,
    string? ClarificationMessage,
    ScannerToolResult? ScannerResult,
    SymbolLookupToolResult? LookupResult,
    ComprehensiveAnalysisToolResult? ComprehensiveAnalysisResult,
    FinancialStatementAnalysisResponse? FinancialStatementAnalysisResult,
    FinancialStatementTableResult? FinancialStatementTableResult,
    ProductRevenueMixResponse? ProductRevenueMixResult,
    MonthlyActivityTrendResponse? MonthlyActivityTrendResult,
    MonthlySalesQualityRankingResponse? MonthlySalesQualityRankingResult,
    ExplainableAnswer? ExplainableAnswer,
    ConfidenceScoreResult? ConfidenceScore,
    string? TextAnswer,
    UsageAccountingResult? Usage,
    IReadOnlyCollection<MemoryUseDisclosure>? Disclosures,
    IAiModelClient? ModelClient,
    string WorkflowCorrelationId,
    DialogueOutcome Outcome = DialogueOutcome.Answered,
    string OutcomeReasonCode = DialogueOutcomeReasonCodes.None,
    string ReplyLanguage = "en",
    bool LanguageGuardApplied = false,
    DisclosureListingResult? DisclosureListingResult = null,
    PsVisualizationResult? PsVisualizationResult = null,
    IReadOnlyCollection<SuggestedAction>? SuggestedActions = null,
    MonthlyProductComparisonResponse? MonthlyProductComparisonResult = null,
    FinancialStatementValueSearchResult? FinancialStatementValueSearchResult = null,
    MonthlyProductTrendResult? MonthlyProductTrendResult = null,
    bool DeterministicSuggestionsApplied = false,
    SymbolSnapshotResult? SymbolSnapshotResult = null,
    // Change «sol-deep-analysis-web-search»: the response hint (prices, defaults, offer) and an answer's cited sources.
    FinancialCopilot.Application.AI.GroundedAnalysis.GroundedAnalysisHint? GroundedAnalysis = null,
    IReadOnlyList<FinancialCopilot.Application.AI.GroundedAnalysis.ExternalSource>? Sources = null);
