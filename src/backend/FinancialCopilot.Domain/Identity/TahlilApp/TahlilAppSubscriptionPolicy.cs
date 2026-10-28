namespace FinancialCopilot.Domain.Identity.TahlilApp;

/// <summary>
/// Sapino plan decision derived from TahlilApp subscription data (Design §11, Revision 4 D6/D7).
/// <see cref="EffectiveTo"/> is null for Free; it is the TahlilApp expiry for an active paid plan.
/// </summary>
public sealed record TahlilAppSubscriptionDecision(
    string PlanCode,
    DateTimeOffset? EffectiveTo,
    string Reason,
    DateTimeOffset? EffectiveFrom = null,
    IReadOnlyList<TahlilAppPlanSubscription>? PlanSubscriptions = null);

/// <summary>One authoritative paid-plan window returned by TahlilApp.</summary>
public sealed record TahlilAppPlanSubscription(
    string Slug,
    string PlanCode,
    DateTimeOffset StartsAt,
    DateTimeOffset ExpiresAt,
    string IdentityKey);

/// <summary>
/// Maps a TahlilApp subscription to a Sapino plan. Fails closed: anything that cannot be proven
/// as an active, unexpired paid plan becomes <c>Free</c>. Never grants a higher plan than the mapping allows.
/// </summary>
public static class TahlilAppSubscriptionPolicy
{
    public const string FreePlanCode = "Free";

    public static TahlilAppSubscriptionDecision Decide(
        IReadOnlyList<(string Slug, DateTimeOffset StartsAt, DateTimeOffset ExpiresAt)> subscriptions,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string> planMap)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(planMap);

        var valid = subscriptions
            .Where(item => item.ExpiresAt > item.StartsAt)
            .Where(item => planMap.ContainsKey(item.Slug))
            .Select(item => new TahlilAppPlanSubscription(
                item.Slug,
                planMap[item.Slug],
                item.StartsAt.ToUniversalTime(),
                item.ExpiresAt.ToUniversalTime(),
                $"{item.Slug}:{item.StartsAt.ToUnixTimeMilliseconds()}:{item.ExpiresAt.ToUnixTimeMilliseconds()}"))
            .ToArray();

        var active = valid.Where(item => item.StartsAt <= now && item.ExpiresAt > now).ToArray();
        var plan = active.Any(item => item.PlanCode == "Pro") ? "Pro" :
            active.Any(item => item.PlanCode == "Plus") ? "Plus" : FreePlanCode;
        return new TahlilAppSubscriptionDecision(
            plan,
            active.Length == 0 ? null : active.Max(item => item.ExpiresAt),
            active.Length == 0 ? "free-or-no-active-paid-plan" : "active-paid-plan-set",
            active.Length == 1 ? active[0].StartsAt : null,
            valid);
    }

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
