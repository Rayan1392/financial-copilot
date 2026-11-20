using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;

internal sealed class BillingFunctions(IBillingFacadeHook billingHook)
{
    internal Task<BillingReservationHandle?> TryReserveAsync(
        AiQueryRequest request,
        CancellationToken cancellationToken,
        string? usageCorrelationId = null) =>
        billingHook.TryReserveAsync(
            new BillingReservationRequest(
                request.CorrelationId,
                request.TenantId,
                request.ActorId,
                AiQueryBillingOperationSelector.Select(request.SemanticFrame?.CapabilityCode, request.Message),
                request.UserId,
                request.ApiClientId,
                request.ExternalUserId,
                usageCorrelationId,
                request.BillingOperationId,
                AiQueryRequestFingerprint.Compute(request)),
            cancellationToken);

    /// <summary>
    /// Change «sol-deep-analysis-web-search» (FR-8): claims a 163 offer's derived key with the confirm or the cancel
    /// operation. The fingerprint is unique per HTTP request (its correlation id), so a second request for the same
    /// offer — concurrent, repeated or replayed — never reuses the claim: it fails with
    /// <see cref="FinancialCopilot.Billing.BillingIdempotencyConflictException"/> instead of running the model again.
    /// </summary>
    internal Task<BillingReservationHandle?> TryClaimOfferAsync(
        AiQueryRequest request,
        Guid offerBillingOperationId,
        string operationCode,
        CancellationToken cancellationToken) =>
        billingHook.TryReserveAsync(
            new BillingReservationRequest(
                $"{request.CorrelationId}:analysis_offer",
                request.TenantId,
                request.ActorId,
                operationCode,
                request.UserId,
                request.ApiClientId,
                request.ExternalUserId,
                request.CorrelationId,
                offerBillingOperationId,
                // 64 hex characters: the fingerprint column length.
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"analysis_offer:{request.CorrelationId}"))).ToLowerInvariant()),
            cancellationToken);

    internal Task<string?> FindReservationStatusAsync(Guid billingOperationId, CancellationToken cancellationToken) =>
        billingHook.FindReservationStatusAsync(billingOperationId, cancellationToken);

    internal Task<BillingReservationHandle> EnsureReservedCapacityAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken) =>
        billingHook.EnsureReservedCapacityAsync(handle, cancellationToken);

    internal Task<BillingReservationHandle> EnsureReservedCapacityAsync(
        BillingReservationHandle handle,
        decimal requiredCredits,
        CancellationToken cancellationToken) =>
        billingHook.EnsureReservedCapacityAsync(handle, requiredCredits, cancellationToken);

    internal Task<BillingReservationHandle> EnsureCurrentUsageCapacityAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken) =>
        billingHook.EnsureCurrentUsageCapacityAsync(handle, cancellationToken);

    internal Task<UsageAccountingResult?> FinalizeAsync(
        BillingReservationHandle handle,
        string completionStatus,
        bool fromCache,
        CancellationToken cancellationToken,
        string? executionRoute = null) =>
        billingHook.FinalizeAsync(
            handle,
            new BillingFinalizationRequest(completionStatus, fromCache, ExecutionRoute: executionRoute),
            cancellationToken);
}
