using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Application.AI.Orchestration;

public enum DetectedIntent
{
    Scanner,
    SymbolLookup,
    Clarification,
    Unknown
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
    int ScannerPageSize = 20);

public sealed record AiQueryResponse(
    Guid ConversationId,
    Guid MessageId,
    Guid AssistantMessageId,
    DetectedIntent Intent,
    ScannerQueryPlan? ScannerPlan,
    ScannerTableResult? ScannerTable,
    SymbolLookupTableResult? SymbolLookupTable,
    ExplainableAnswer? ExplainableAnswer,
    string? TextAnswer,
    bool ClarificationRequired,
    string? ClarificationMessage,
    UsageAccountingResult? Usage,
    IReadOnlyCollection<MemoryUseDisclosure>? MemoryDisclosures = null);

public sealed record UsageAccountingResult(
    string OperationCode,
    string CompletionStatus,
    decimal CreditsCharged,
    decimal RemainingSpendingCapacity,
    string PricingPolicyVersion,
    bool Cached);

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
    string? ExternalUserId = null);

public sealed record BillingReservationHandle(
    string ReservationId,
    string CorrelationId,
    Guid CustomerAccountId,
    Guid TenantId,
    Guid ActorId,
    Guid? ApiClientId,
    string? ExternalUserId,
    string OperationCode);

public sealed record BillingFinalizationRequest(
    string CompletionStatus,
    bool Cached = false);

public interface IBillingFacadeHook
{
    // Attempts to reserve billing credit before executing the AI workflow.
    // Returns null when billing is not active (no-op phase) or entitlement fails.
    Task<BillingReservationHandle?> TryReserveAsync(
        BillingReservationRequest request,
        CancellationToken cancellationToken);

    // Finalizes the reservation after the workflow completes.
    Task<UsageAccountingResult?> FinalizeAsync(
        BillingReservationHandle handle,
        BillingFinalizationRequest request,
        CancellationToken cancellationToken);

    // Releases the reservation if the workflow was abandoned or failed early.
    Task ReleaseAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken);
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
