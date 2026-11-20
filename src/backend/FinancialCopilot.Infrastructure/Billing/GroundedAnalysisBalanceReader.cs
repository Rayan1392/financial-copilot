using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;

namespace FinancialCopilot.Infrastructure.Billing;

/// <summary>
/// Change «sol-deep-analysis-web-search» (FR-7, PO-11): the caller's spendable balance and unlimited status for the
/// analysis hint and the 163 offer. Read only; it never reserves. The same account resolution and plan lookup the
/// billing hook uses, and the expiry-aware spendable wallet the usage endpoint shows.
/// </summary>
public sealed class GroundedAnalysisBalanceReader(
    IBillableAccountResolver accountResolver,
    IExpiryAwareWalletReadService spendableWallets,
    ISubscriptionPlanRepository? subscriptionPlans = null) : IGroundedAnalysisBalanceReader
{
    public async Task<GroundedAnalysisBalance?> ReadAsync(
        Guid tenantId,
        Guid actorId,
        Guid? userId,
        Guid? apiClientId,
        string? externalUserId,
        CancellationToken cancellationToken)
    {
        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(actorId, tenantId, userId, apiClientId, externalUserId), cancellationToken);
        var plan = subscriptionPlans is null
            ? null
            : await subscriptionPlans.FindForCustomerAsync(account.Id, cancellationToken);
        var wallet = await spendableWallets.GetSpendableSnapshotAsync(account.Id, cancellationToken);
        return new GroundedAnalysisBalance(
            Math.Max(0m, wallet.Balance - wallet.ReservedAmount),
            plan?.QuotaMode == CreditQuotaMode.Unlimited);
    }
}
