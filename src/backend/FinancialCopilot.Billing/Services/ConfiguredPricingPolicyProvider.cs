using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;

namespace FinancialCopilot.Billing.Services;

public sealed class ConfiguredPricingPolicyProvider : IPricingPolicyProvider
{
    private readonly IReadOnlyDictionary<string, PricingPolicy> _policies;

    public ConfiguredPricingPolicyProvider(IEnumerable<PricingPolicy> policies)
    {
        _policies = policies.ToDictionary(policy => policy.Version, StringComparer.OrdinalIgnoreCase);
    }

    public PricingPolicy GetPolicy(string policyVersion)
    {
        if (!_policies.TryGetValue(policyVersion, out var policy))
        {
            throw new KeyNotFoundException($"Pricing policy '{policyVersion}' is not configured.");
        }

        return policy;
    }
}
