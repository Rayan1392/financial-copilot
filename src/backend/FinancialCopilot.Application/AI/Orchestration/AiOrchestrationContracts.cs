using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Application.AI.Orchestration;

public enum DetectedIntent
{
    Scanner,
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
    Guid? ConversationId = null);

public sealed record AiQueryResponse(
    Guid ConversationId,
    Guid MessageId,
    Guid AssistantMessageId,
    DetectedIntent Intent,
    ScannerQueryPlan? ScannerPlan,
    string? TextAnswer,
    bool ClarificationRequired,
    string? ClarificationMessage);

// Integration point for mandatory Billing reservation/finalization.
// Story 007 defines this boundary; Story 010 provides the real implementation.
// The no-op implementation allows the orchestrator to compile and be tested
// without billing policy being active.
public sealed record BillingReservationRequest(
    string CorrelationId,
    Guid TenantId,
    Guid ActorId,
    string OperationCode);

public sealed record BillingReservationHandle(
    string ReservationId,
    string CorrelationId);

public sealed record BillingFinalizationRequest(
    bool Succeeded,
    string? ErrorCategory = null);

public interface IBillingFacadeHook
{
    // Attempts to reserve billing credit before executing the AI workflow.
    // Returns null when billing is not active (no-op phase) or entitlement fails.
    Task<BillingReservationHandle?> TryReserveAsync(
        BillingReservationRequest request,
        CancellationToken cancellationToken);

    // Finalizes the reservation after the workflow completes.
    Task FinalizeAsync(
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
