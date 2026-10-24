namespace FinancialCopilot.Domain.Identity.TahlilApp;

/// <summary>
/// Sapino plan decision derived from TahlilApp subscription data (Design §11, Revision 4 D6/D7).
/// <see cref="EffectiveTo"/> is null for Free; it is the TahlilApp expiry for an active paid plan.
/// </summary>
public sealed record TahlilAppSubscriptionDecision(
    string PlanCode,
    DateTimeOffset? EffectiveTo,
    string Reason,
    DateTimeOffset? EffectiveFrom = null);

/// <summary>
/// Maps a TahlilApp subscription to a Sapino plan. Fails closed: anything that cannot be proven
/// as an active, unexpired paid plan becomes <c>Free</c>. Never grants a higher plan than the mapping allows.
/// </summary>
public static class TahlilAppSubscriptionPolicy
{
    public const string FreePlanCode = "Free";

    public static TahlilAppSubscriptionDecision Decide(
        string? externalPlan,
        bool isActive,
        DateTimeOffset? expiresAt,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string> planMap) =>
        Decide(externalPlan, isActive, null, expiresAt, now, planMap);

    public static TahlilAppSubscriptionDecision Decide(
        string? externalPlan,
        bool isActive,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? expiresAt,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string> planMap)
    {
        ArgumentNullException.ThrowIfNull(planMap);

        if (string.IsNullOrWhiteSpace(externalPlan) || !planMap.TryGetValue(externalPlan.Trim(), out var planCode))
        {
            return new TahlilAppSubscriptionDecision(FreePlanCode, null, "unknown-plan");
        }

        if (string.Equals(planCode, FreePlanCode, StringComparison.Ordinal))
        {
            return new TahlilAppSubscriptionDecision(FreePlanCode, null, "free");
        }

        if (!isActive)
        {
            return new TahlilAppSubscriptionDecision(FreePlanCode, null, "inactive");
        }

        if (expiresAt is null)
        {
            return new TahlilAppSubscriptionDecision(FreePlanCode, null, "missing-expiry");
        }

        if (expiresAt.Value <= now)
        {
            return new TahlilAppSubscriptionDecision(FreePlanCode, null, "expired");
        }

        // Old contract payloads remain readable for eligibility, but never become a credit anchor.
        if (effectiveFrom is null)
        {
            return new TahlilAppSubscriptionDecision(planCode, expiresAt, "active-paid-missing-effective-from");
        }

        var normalizedEffectiveFrom = effectiveFrom.Value.ToUniversalTime();
        if (normalizedEffectiveFrom > now || normalizedEffectiveFrom >= expiresAt.Value)
        {
            return new TahlilAppSubscriptionDecision(FreePlanCode, null, "invalid-effective-from");
        }

        return new TahlilAppSubscriptionDecision(planCode, expiresAt, "active-paid", normalizedEffectiveFrom);
    }
}
