using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;

namespace FinancialCopilot.Billing.Services;

public sealed class WalletEntitlementService : IEntitlementService
{
    private readonly IWalletService _wallets;
    private readonly IPricingPolicyProvider _pricingPolicies;
    private readonly ICreditLinePolicyService _creditLinePolicy;
    private readonly string _defaultPricingPolicyVersion;
    private readonly IPlanCapabilityService? _planCapabilities;

    public WalletEntitlementService(
        IWalletService wallets,
        IPricingPolicyProvider pricingPolicies,
        ICreditLinePolicyService creditLinePolicy,
        string defaultPricingPolicyVersion,
        IPlanCapabilityService? planCapabilities = null)
    {
        _wallets = wallets;
        _pricingPolicies = pricingPolicies;
        _creditLinePolicy = creditLinePolicy;
        _defaultPricingPolicyVersion = defaultPricingPolicyVersion;
        _planCapabilities = planCapabilities;
    }

    public async Task ValidateCanExecuteAsync(
        CustomerAccount account,
        string operationCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationCode);

        if (_planCapabilities is not null)
        {
            await _planCapabilities.ValidateCanExecuteAsync(account, operationCode, cancellationToken);
        }

        var policy = _pricingPolicies.GetPolicy(_defaultPricingPolicyVersion);

        if (!policy.OperationCredits.TryGetValue(operationCode, out var maximumCredits))
        {
            throw new InvalidOperationException("The requested operation is not entitled under the active policy.");
        }

        var wallet = await _wallets.GetSnapshotAsync(account.Id, cancellationToken);

        if (!_creditLinePolicy.CanReserve(account, wallet, maximumCredits))
        {
            throw new InvalidOperationException("Available spending capacity is insufficient.");
        }
    }
}
