namespace FinancialCopilot.Billing.Accounts;

public sealed record SubscriptionPlan(
    string Code,
    string Name,
    decimal IncludedCredits,
    string PricingPolicyVersion)
{
    public SubscriptionPlan Validate()
    {
        if (string.IsNullOrWhiteSpace(Code) ||
            string.IsNullOrWhiteSpace(Name) ||
            string.IsNullOrWhiteSpace(PricingPolicyVersion))
        {
            throw new ArgumentException("Plan code, name, and pricing policy version are required.");
        }

        if (IncludedCredits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(IncludedCredits));
        }

        return this;
    }
}
