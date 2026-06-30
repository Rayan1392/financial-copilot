using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
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
    DateTimeOffset Now);

internal sealed record MemoryRetrievedMessage(
    AiQueryRequest Request,
    Guid ConversationId,
    bool CreateConversation,
    DateTimeOffset Now,
    AuthorizedMemoryContext MemoryContext,
    string EnrichedMessage);

internal sealed record BillingReservedMessage(
    AiQueryRequest Request,
    Guid ConversationId,
    bool CreateConversation,
    DateTimeOffset Now,
    AuthorizedMemoryContext MemoryContext,
    string EnrichedMessage,
    BillingReservationHandle? Reservation);

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
    ProductRevenueMixResponse? ProductRevenueMixResult,
    MonthlyActivityTrendResponse? MonthlyActivityTrendResult,
    MonthlySalesQualityRankingResponse? MonthlySalesQualityRankingResult,
    string CompletionStatus,
    bool FromCache,
    IAiModelClient ModelClient,
    UsageAccountingResult? Usage);

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
    ProductRevenueMixResponse? ProductRevenueMixResult,
    MonthlyActivityTrendResponse? MonthlyActivityTrendResult,
    MonthlySalesQualityRankingResponse? MonthlySalesQualityRankingResult,
    string CompletionStatus,
    bool FromCache,
    IAiModelClient ModelClient,
    DetectedIntent DetectedIntent,
    bool ClarificationRequired,
    string? ClarificationMessage,
    ExplainableAnswer? ExplainableAnswer,
    ConfidenceScoreResult? ConfidenceScore,
    string? GroundedAnswer,
    UsageAccountingResult? Usage);

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
    ProductRevenueMixResponse? ProductRevenueMixResult,
    MonthlyActivityTrendResponse? MonthlyActivityTrendResult,
    MonthlySalesQualityRankingResponse? MonthlySalesQualityRankingResult,
    ExplainableAnswer? ExplainableAnswer,
    ConfidenceScoreResult? ConfidenceScore,
    string? TextAnswer,
    UsageAccountingResult? Usage,
    IReadOnlyCollection<MemoryUseDisclosure>? Disclosures,
    IAiModelClient ModelClient,
    string WorkflowCorrelationId);
