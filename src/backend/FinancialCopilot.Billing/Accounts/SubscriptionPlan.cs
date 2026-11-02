namespace FinancialCopilot.Billing.Accounts;

public sealed record SubscriptionPlan(
    string Code,
    string Name,
    decimal IncludedCredits,
    string PricingPolicyVersion,
    CreditQuotaMode QuotaMode = CreditQuotaMode.Finite,
    DateTimeOffset? SubscriptionExpiresAtUtc = null)
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

        if (!Enum.IsDefined(QuotaMode) || QuotaMode == CreditQuotaMode.Unlimited && IncludedCredits != 0)
            throw new ArgumentException("Unlimited plans must use explicit Unlimited mode and a zero finite allowance.");

        return this;
    }
}
