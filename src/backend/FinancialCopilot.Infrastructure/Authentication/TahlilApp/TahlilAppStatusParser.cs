using System.Text.Json;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>Parses an official TahlilApp subscription-status response without accepting or logging profile data.</summary>
public static class TahlilAppStatusParser
{
    private const int MaximumDepth = 8;

    public static TahlilAppStatusSnapshot? TryParse(
        ReadOnlySpan<byte> utf8Json,
        string expectedExternalUserId,
        DateTimeOffset now,
        out string? rejectedField)
    {
        rejectedField = null;
        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = MaximumDepth });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                rejectedField = "root";
                return null;
            }

            if (!root.TryGetProperty("user_id", out var idElement) || idElement.ValueKind != JsonValueKind.String ||
                !ExternalUserId.TryCreate(idElement.GetString(), out var externalId) ||
                !string.Equals(externalId!.Value, expectedExternalUserId, StringComparison.Ordinal))
            {
                rejectedField = "user_id";
                return null;
            }

            if (!root.TryGetProperty("subscription", out var subscription) || subscription.ValueKind != JsonValueKind.Object)
            {
                rejectedField = "subscription";
                return null;
            }

            if (!TahlilAppExchangeParser.TryReadPlans(subscription, now, out var plans))
            {
                rejectedField = "subscription.plans";
                return null;
            }

            return new TahlilAppStatusSnapshot(
                externalId.Value,
                IsSuspended: false,
                ExternalPlan: null,
                IsSubscriptionActive: plans.Any(plan => plan.StartsAt <= now && plan.ExpiresAt > now),
                SubscriptionExpiresAt: plans.Count == 0 ? null : plans.Max(plan => plan.ExpiresAt),
                ExternalRevision: null,
                SubscriptionEffectiveFrom: null,
                PlanSubscriptions: plans);
        }
        catch (JsonException)
        {
            rejectedField = "json";
            return null;
        }
    }
}
