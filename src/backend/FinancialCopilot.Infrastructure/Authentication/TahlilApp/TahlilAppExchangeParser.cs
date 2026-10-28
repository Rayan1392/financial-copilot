using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>Parses the official TahlilApp partner exchange response. No response values are logged.</summary>
public static class TahlilAppExchangeParser
{
    private const int MaximumDepth = 8;
    private static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(366 * 5);

    public static TahlilAppExchangeSnapshot? TryParse(ReadOnlySpan<byte> utf8Json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = MaximumDepth });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryString(root, "user_id", out var userId) ||
                !ExternalUserId.TryCreate(userId, out var externalId) ||
                !root.TryGetProperty("subscription", out var subscription) ||
                subscription.ValueKind != JsonValueKind.Object ||
                !TryReadPlans(subscription, now, out var plans))
            {
                return null;
            }

            string? name = null;
            if (root.TryGetProperty("name", out var nameElement))
            {
                if (nameElement.ValueKind == JsonValueKind.String) name = nameElement.GetString();
                else if (nameElement.ValueKind != JsonValueKind.Null) return null;
            }

            var decision = TahlilAppSubscriptionPolicy.Decide(
                plans.Select(plan => (plan.Slug, plan.StartsAt, plan.ExpiresAt)).ToArray(),
                now,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sapio_plus"] = "Plus",
                    ["sapio_pro"] = "Pro"
                });

            return new TahlilAppExchangeSnapshot(
                ExternalUserId: externalId!.Value,
                IsSuspended: false,
                ExternalPlan: decision.PlanCode,
                IsSubscriptionActive: decision.PlanCode != "Free",
                SubscriptionExpiresAt: decision.EffectiveTo,
                ExternalRevision: null,
                ExternalPermissionCount: 0,
                FirstName: IncomingProfileField.Absent,
                LastName: IncomingProfileField.Absent,
                NationalId: IncomingProfileField.Absent,
                Mobile: IncomingProfileField.Absent,
                SubscriptionEffectiveFrom: decision.EffectiveFrom,
                PlanSubscriptions: plans,
                Name: name);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static bool TryReadPlans(
        JsonElement subscription,
        DateTimeOffset now,
        out IReadOnlyList<TahlilAppPlanSubscription> plans)
    {
        plans = Array.Empty<TahlilAppPlanSubscription>();
        if (!subscription.TryGetProperty("plans", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<TahlilAppPlanSubscription>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !TryString(entry, "slug", out var slug) ||
                !TryString(entry, "starts_at", out var startsText) ||
                !TryString(entry, "expires_at", out var expiresText) ||
                !TryUtcInstant(startsText, out var startsAt) ||
                !TryUtcInstant(expiresText, out var expiresAt) ||
                startsAt > now + MaximumWindow ||
                expiresAt > now + MaximumWindow ||
                expiresAt <= startsAt)
            {
                return false;
            }

            var planCode = slug switch
            {
                "sapio_plus" => "Plus",
                "sapio_pro" => "Pro",
                _ => null
            };
            if (planCode is null) continue;

            var startUtc = startsAt.ToUniversalTime();
            var expiryUtc = expiresAt.ToUniversalTime();
            parsed.Add(new TahlilAppPlanSubscription(
                slug,
                planCode,
                startUtc,
                expiryUtc,
                $"{slug}:{startUtc.ToUnixTimeMilliseconds()}:{expiryUtc.ToUnixTimeMilliseconds()}"));
        }

        plans = parsed
            .GroupBy(item => item.IdentityKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        return true;
    }

    private static bool TryUtcInstant(string text, out DateTimeOffset value)
    {
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value) &&
            value.Offset == TimeSpan.Zero;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var child) || child.ValueKind != JsonValueKind.String) return false;
        value = child.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
