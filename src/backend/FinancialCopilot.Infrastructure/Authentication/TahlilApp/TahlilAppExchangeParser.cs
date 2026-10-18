using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>
/// Parses and validates the TahlilApp exchange response (Design §9.1, §13.2). Pure: no I/O, no logging of values.
/// Structural failures return null (the launch fails as unavailable). Invalid optional profile fields are dropped
/// to <see cref="IncomingProfileFieldKind.Invalid"/> and never block the launch.
/// </summary>
public static class TahlilAppExchangeParser
{
    private const int MaximumDepth = 8;
    private static readonly TimeSpan MaximumExpiryHorizon = TimeSpan.FromDays(366 * 5);

    public static TahlilAppExchangeSnapshot? TryParse(ReadOnlySpan<byte> utf8Json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = MaximumDepth });
            return Parse(document.RootElement, now);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TahlilAppExchangeSnapshot? Parse(JsonElement root, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("subscription", out var subscription) || subscription.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetString(user, "externalUserId", out var externalUserId) ||
            !ExternalUserId.TryCreate(externalUserId, out var validatedExternalId))
        {
            return null;
        }

        if (!TryGetString(user, "status", out var status))
        {
            return null;
        }

        bool isSuspended;
        if (string.Equals(status, "Active", StringComparison.Ordinal))
        {
            isSuspended = false;
        }
        else if (string.Equals(status, "Suspended", StringComparison.Ordinal))
        {
            isSuspended = true;
        }
        else
        {
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

        var permissionCount = root.TryGetProperty("permissions", out var permissions) && permissions.ValueKind == JsonValueKind.Array
            ? permissions.GetArrayLength()
            : 0;

        return new TahlilAppExchangeSnapshot(
            ExternalUserId: validatedExternalId!.Value,
            IsSuspended: isSuspended,
            ExternalPlan: plan,
            IsSubscriptionActive: isActive,
            SubscriptionExpiresAt: expiresAt,
            ExternalRevision: revision,
            ExternalPermissionCount: permissionCount,
            FirstName: ReadPersonName(user, "firstName"),
            LastName: ReadPersonName(user, "lastName"),
            NationalId: ReadNationalId(user, "nationalId"),
            Mobile: ReadMobile(user, "mobile"));
    }

    private static IncomingProfileField ReadPersonName(JsonElement user, string property) =>
        ReadField(user, property, raw => PersonName.TryCreate(raw, out var name) ? name!.Value : null);

    private static IncomingProfileField ReadNationalId(JsonElement user, string property) =>
        ReadField(user, property, raw => IranianNationalId.TryCreate(raw, out var id) ? id!.Value : null);

    private static IncomingProfileField ReadMobile(JsonElement user, string property) =>
        ReadField(user, property, raw => IranianMobile.TryCreate(raw, out var mobile) ? mobile!.Value : null);

    private static IncomingProfileField ReadField(JsonElement user, string property, Func<string, string?> normalize)
    {
        if (!user.TryGetProperty(property, out var element))
        {
            return IncomingProfileField.Absent;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return IncomingProfileField.Null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return IncomingProfileField.Invalid;
        }

        var normalized = normalize(element.GetString() ?? string.Empty);
        return normalized is null ? IncomingProfileField.Invalid : IncomingProfileField.Valid(normalized);
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
