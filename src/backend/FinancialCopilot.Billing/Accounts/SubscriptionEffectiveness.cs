namespace FinancialCopilot.Billing.Accounts;

/// <summary>
/// Read-side subscription expiry rule (Design §11, AC-35, AC-87). A paid window whose end is at or before
/// <paramref name="now"/> grants no paid access: the effective plan is the canonical Free plan.
/// Accounts without a window end are not affected.
/// </summary>
public static class SubscriptionEffectiveness
{
    public const string FreePlanCode = "Free";

    public static string? ResolveEffectivePlanCode(string? storedPlanCode, DateTimeOffset? effectiveTo, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(storedPlanCode))
        {
            return storedPlanCode;
        }

        return effectiveTo is { } end && end <= now ? FreePlanCode : storedPlanCode;
    }
}
