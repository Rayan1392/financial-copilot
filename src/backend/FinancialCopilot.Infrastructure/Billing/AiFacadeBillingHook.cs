using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;

namespace FinancialCopilot.Infrastructure.Billing;

public sealed class AiFacadeBillingHook(
    IBillableAccountResolver accountResolver,
    ICustomerAccountRepository accounts,
    IWalletService wallets,
    ICreditReservationService reservationService,
    IUsageChargeCalculator chargeCalculator,
    IUsageFinalizationService finalizationService,
    IDailyFreeAllowanceService dailyFreeAllowanceService,
    FinancialCopilot.Application.AI.ModelProviders.IAiExecutionUsageAccumulator usageAccumulator) : IBillingFacadeHook
{
    private const string PricingPolicyVersion = "v1";

    public async Task<BillingReservationHandle?> TryReserveAsync(
        BillingReservationRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(
                request.ActorId,
                request.TenantId,
                request.UserId,
                request.ApiClientId,
                request.ExternalUserId),
            cancellationToken);
        var wallet = await wallets.GetSnapshotAsync(account.Id, cancellationToken);
        var actorContext = new BillableActorContext(
            request.ActorId,
            request.TenantId,
            request.UserId,
            request.ApiClientId,
            request.ExternalUserId);
        var allowance = await dailyFreeAllowanceService.EnsureAsync(
            actorContext,
            account,
            request.CorrelationId,
            cancellationToken);
        if (allowance.Granted)
        {
            wallet = await wallets.GetSnapshotAsync(account.Id, cancellationToken);
        }
        var maximumCharge = chargeCalculator.Calculate(
            CreateChargeRequest(request.OperationCode, "Completed", cached: false));
        var reservationKey = $"{request.CorrelationId}:reservation";
        var reservation = await reservationService.ReserveAsync(
            account,
            wallet,
            request.OperationCode,
            maximumCharge.CreditsCharged,
            reservationKey,
            cancellationToken);

        return new BillingReservationHandle(
            reservation.IdempotencyKey,
            request.CorrelationId,
            account.Id,
            request.TenantId,
            request.ActorId,
            request.ApiClientId,
            request.ExternalUserId,
            request.OperationCode,
            allowance.RemainingCredits > 0 ? "TelegramDailyFreeAllowance" : null,
            string.IsNullOrWhiteSpace(allowance.AllowanceDateKey) ? null : allowance.AllowanceDateKey);
    }

    public async Task<UsageAccountingResult?> FinalizeAsync(
        BillingReservationHandle handle,
        BillingFinalizationRequest request,
        CancellationToken cancellationToken)
    {
        var usage = usageAccumulator.GetSummary(handle.CorrelationId);
        var charge = chargeCalculator.Calculate(
            CreateChargeRequest(handle.OperationCode, request.CompletionStatus, request.Cached));
        var finalized = await finalizationService.CommitAsync(
            new UsageCommitCommand(
                handle.CustomerAccountId,
                handle.ActorId,
                handle.TenantId,
                handle.ApiClientId,
                handle.ExternalUserId,
                handle.ReservationId,
                $"{handle.CorrelationId}:charge",
                charge,
                request.CompletionStatus,
                request.ProviderName ?? usage?.ProviderKey,
                request.ModelName ?? usage?.ModelKey,
                request.PromptTokens ?? usage?.InputTokens,
                request.CompletionTokens ?? usage?.OutputTokens,
                request.TotalTokens ?? usage?.TotalTokens,
                request.EstimatedCost ?? usage?.ProviderReportedCost,
                handle.AllocationSource,
                handle.AllowanceDateKey),
            cancellationToken);
        var account = await accounts.FindAsync(handle.CustomerAccountId, cancellationToken) ??
            throw new KeyNotFoundException("Billing account is not configured.");

        return new UsageAccountingResult(
            handle.OperationCode,
            request.CompletionStatus,
            charge.CreditsCharged,
            account.GetAvailableSpendingCapacity(finalized.Wallet),
            charge.PricingPolicyVersion,
            charge.Cached,
            usage?.ProviderKey,
            usage?.ModelKey,
            usage?.InputTokens,
            usage?.OutputTokens,
            usage?.TotalTokens,
            usage?.ProviderReportedCost);
    }

    public async Task ReleaseAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken)
    {
        await finalizationService.ReleaseAsync(
            new UsageReleaseCommand(
                handle.CustomerAccountId,
                handle.TenantId,
                handle.ReservationId,
                "Workflow released without a chargeable outcome."),
            cancellationToken);
    }

    private static UsageChargeRequest CreateChargeRequest(
        string operationCode,
        string completionStatus,
        bool cached) =>
        new(
            operationCode,
            PricingPolicyVersion,
            cached,
            completionStatus,
            UsageUnits: [],
            ProviderCosts: []);
}
