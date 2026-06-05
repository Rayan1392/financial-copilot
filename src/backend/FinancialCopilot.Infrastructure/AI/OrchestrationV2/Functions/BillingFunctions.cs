using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;

internal sealed class BillingFunctions(IBillingFacadeHook billingHook)
{
    internal const string OperationCode = "AiQuery.Scanner";

    internal Task<BillingReservationHandle?> TryReserveAsync(
        AiQueryRequest request,
        CancellationToken cancellationToken) =>
        billingHook.TryReserveAsync(
            new BillingReservationRequest(
                request.CorrelationId,
                request.TenantId,
                request.ActorId,
                OperationCode,
                request.UserId,
                request.ApiClientId,
                request.ExternalUserId),
            cancellationToken);

    internal Task<UsageAccountingResult?> FinalizeAsync(
        BillingReservationHandle handle,
        string completionStatus,
        bool fromCache,
        CancellationToken cancellationToken) =>
        billingHook.FinalizeAsync(
            handle,
            new BillingFinalizationRequest(completionStatus, fromCache),
            cancellationToken);
}
