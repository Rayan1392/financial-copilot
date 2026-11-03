using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;

internal sealed class BillingFunctions(IBillingFacadeHook billingHook)
{
    internal const string OperationCode = "AiQuery.Scanner";

    internal Task<BillingReservationHandle?> TryReserveAsync(
        AiQueryRequest request,
        CancellationToken cancellationToken,
        string? usageCorrelationId = null) =>
        billingHook.TryReserveAsync(
            new BillingReservationRequest(
                request.CorrelationId,
                request.TenantId,
                request.ActorId,
                OperationCode,
                request.UserId,
                request.ApiClientId,
                request.ExternalUserId,
                usageCorrelationId,
                request.BillingOperationId,
                AiQueryRequestFingerprint.Compute(request)),
            cancellationToken);

    internal Task<BillingReservationHandle> EnsureReservedCapacityAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken) =>
        billingHook.EnsureReservedCapacityAsync(handle, cancellationToken);

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
