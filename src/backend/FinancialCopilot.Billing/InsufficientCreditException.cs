namespace FinancialCopilot.Billing;

public sealed class InsufficientCreditException : InvalidOperationException
{
    public const string InsufficientCreditsCode = "INSUFFICIENT_CREDITS";
    public const string SubscriptionCooldownActiveCode = "SUBSCRIPTION_COOLDOWN_ACTIVE";

    public InsufficientCreditException()
        : this(InsufficientCreditsCode, null, null)
    {
    }

    public InsufficientCreditException(string errorCode, DateTimeOffset? cooldownUntilUtc, decimal? freePlanCredits)
        : base("Available spending capacity is insufficient.")
    {
        ErrorCode = errorCode;
        CooldownUntilUtc = cooldownUntilUtc;
        FreePlanCredits = freePlanCredits;
    }

    public string ErrorCode { get; }
    public DateTimeOffset? CooldownUntilUtc { get; }
    public decimal? FreePlanCredits { get; }
}
