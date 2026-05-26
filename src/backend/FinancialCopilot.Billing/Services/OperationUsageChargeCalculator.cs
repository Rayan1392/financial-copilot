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

        var cachedMultiplier = request.Cached ? policy.CachedMultiplier : 1m;

        if (cachedMultiplier < 0 || cachedMultiplier > 1)
        {
            throw new InvalidOperationException("Cached multiplier must be between zero and one.");
        }

        var outcomeMultiplier = 1m;

        if (policy.CompletionMultipliers is not null &&
            policy.CompletionMultipliers.TryGetValue(request.CompletionStatus, out var configuredMultiplier))
        {
            outcomeMultiplier = configuredMultiplier;
        }

        if (outcomeMultiplier < 0 || outcomeMultiplier > 1)
        {
            throw new InvalidOperationException("Completion multiplier must be between zero and one.");
        }

        return new UsageChargeResult(
            decimal.Round(credits * cachedMultiplier * outcomeMultiplier, 4),
            policy.Version,
            request.Cached);
    }
}
