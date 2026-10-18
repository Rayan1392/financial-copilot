using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>
/// Parses the TahlilApp entitlement status response (Design §9.1 proposed contract, UNVERIFIED with TahlilApp).
/// Only <c>user.externalUserId</c>, <c>user.status</c> and <c>subscription</c> are read. Any profile field
/// (<c>nationalId</c>, <c>mobile</c>) makes the response invalid, because PII must not travel on this path (AC-75).
/// Pure: no I/O and no logging of values. A rejection reports a field name only.
/// </summary>
public static class TahlilAppStatusParser
{
    private const int MaximumDepth = 8;
    private static readonly TimeSpan MaximumExpiryHorizon = TimeSpan.FromDays(366 * 5);
    private static readonly string[] ForbiddenProfileFields = ["nationalId", "mobile"];

    /// <param name="rejectedField">Name of the field that made the response invalid, for diagnostics. Never a value.</param>
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
            return Parse(document.RootElement, expectedExternalUserId, now, out rejectedField);
        }
        catch (JsonException)
        {
            rejectedField = "json";
            return null;
        }
    }

    private static TahlilAppStatusSnapshot? Parse(
        JsonElement root,
        string expectedExternalUserId,
        DateTimeOffset now,
        out string? rejectedField)
    {
        rejectedField = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            rejectedField = "root";
            return null;
        }

        if (FindForbiddenField(root) is { } rootField)
        {
            rejectedField = rootField;
            return null;
        }

        if (!root.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object)
        {
            rejectedField = "user";
            return null;
        }

        if (FindForbiddenField(user) is { } userField)
        {
            rejectedField = userField;
            return null;
        }

        if (!root.TryGetProperty("subscription", out var subscription) || subscription.ValueKind != JsonValueKind.Object)
        {
            rejectedField = "subscription";
            return null;
        }

        if (!TryGetString(user, "externalUserId", out var externalUserId) ||
            !ExternalUserId.TryCreate(externalUserId, out var validatedExternalId) ||
            !string.Equals(validatedExternalId!.Value, expectedExternalUserId, StringComparison.Ordinal))
        {
            rejectedField = "user.externalUserId";
            return null;
        }

        if (!TryGetString(user, "status", out var status))
        {
            rejectedField = "user.status";
            return null;
        }

        bool isSuspended;
        if (string.Equals(status, TahlilAppIdentityStatus.Active, StringComparison.Ordinal))
        {
            isSuspended = false;
        }
        else if (string.Equals(status, TahlilAppIdentityStatus.Suspended, StringComparison.Ordinal))
        {
            isSuspended = true;
        }
        else
        {
            rejectedField = "user.status";
            return null;
        }

        // Fail closed: a missing or non-boolean isActive is treated as inactive.
        var isActive = subscription.TryGetProperty("isActive", out var activeElement) &&
            activeElement.ValueKind == JsonValueKind.True;

        DateTimeOffset? expiresAt = null;
        if (TryGetString(subscription, "expiresAt", out var expiresText) &&
            DateTimeOffset.TryParse(expiresText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedExpiry) &&
            parsedExpiry <= now + MaximumExpiryHorizon)
        {
            expiresAt = parsedExpiry;
        }

        long? revision = null;
        if (subscription.TryGetProperty("revision", out var revisionElement) &&
            revisionElement.ValueKind == JsonValueKind.Number &&
            revisionElement.TryGetInt64(out var parsedRevision))
        {
            revision = parsedRevision;
        }

        TryGetString(subscription, "plan", out var plan);

        return new TahlilAppStatusSnapshot(
            ExternalUserId: validatedExternalId!.Value,
            IsSuspended: isSuspended,
            ExternalPlan: plan,
            IsSubscriptionActive: isActive,
            SubscriptionExpiresAt: expiresAt,
            ExternalRevision: revision);
    }

    private static string? FindForbiddenField(JsonElement element)
    {
        foreach (var field in ForbiddenProfileFields)
        {
            if (element.TryGetProperty(field, out _))
            {
                return field;
            }
        }

        return null;
    }

    private static bool TryGetString(JsonElement element, string property, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(property, out var child) || child.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = child.GetString();
        return value is not null;
    }
}
