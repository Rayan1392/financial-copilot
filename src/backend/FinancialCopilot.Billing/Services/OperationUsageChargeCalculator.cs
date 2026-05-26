using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;

namespace FinancialCopilot.Billing.Services;

public sealed class OperationUsageChargeCalculator(IPricingPolicyProvider policyProvider) : IUsageChargeCalculator
{
    public UsageChargeResult Calculate(UsageChargeRequest request)
    {
        var policy = policyProvider.GetPolicy(request.PricingPolicyVersion);

        if (policy.ZeroChargeStatuses.Contains(request.CompletionStatus))
        {
            return new UsageChargeResult(0, policy.Version, request.Cached);
        }

        if (!policy.OperationCredits.TryGetValue(request.OperationCode, out var credits))
        {
            throw new InvalidOperationException(
                $"Operation '{request.OperationCode}' is not priced by policy '{policy.Version}'.");
        }

        var multiplier = request.Cached ? policy.CachedMultiplier : 1m;

        if (multiplier < 0 || multiplier > 1)
        {
            throw new InvalidOperationException("Cached multiplier must be between zero and one.");
        }

        return new UsageChargeResult(decimal.Round(credits * multiplier, 4), policy.Version, request.Cached);
    }
}
