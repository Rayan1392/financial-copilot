using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;

namespace FinancialCopilot.Billing.Services;

public sealed class WalletEntitlementService(
    IWalletService wallets,
    IPricingPolicyProvider pricingPolicies,
    ICreditLinePolicyService creditLinePolicy,
    string defaultPricingPolicyVersion) : IEntitlementService
{
    public async Task ValidateCanExecuteAsync(
        CustomerAccount account,
        string operationCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationCode);

        var policy = pricingPolicies.GetPolicy(defaultPricingPolicyVersion);

        if (!policy.OperationCredits.TryGetValue(operationCode, out var maximumCredits))
        {
            throw new InvalidOperationException("The requested operation is not entitled under the active policy.");
        }

        var wallet = await wallets.GetSnapshotAsync(account.Id, cancellationToken);

        if (!creditLinePolicy.CanReserve(account, wallet, maximumCredits))
        {
            throw new InvalidOperationException("Available spending capacity is insufficient.");
        }
    }
}
