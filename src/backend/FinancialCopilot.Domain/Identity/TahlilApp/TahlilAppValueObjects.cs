using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FinancialCopilot.Domain.Identity.TahlilApp;

/// <summary>
/// Stable TahlilApp internal user identifier. This is the only cross-system identity key (Design §10).
/// </summary>
public sealed class ExternalUserId
{
    public const string Provider = "TahlilApp";
    private static readonly Regex Allowed = new("^[A-Za-z0-9._:-]{1,128}$", RegexOptions.CultureInvariant);

    private ExternalUserId(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? raw, out ExternalUserId? result)
    {
        result = null;
        if (raw is null || !Allowed.IsMatch(raw)) return false;
        result = new ExternalUserId(raw);
        return true;
    }

    public override string ToString() => "[external-user-id]";
}

/// <summary>
/// Iranian national identifier: exactly ten ASCII digits after Persian/Arabic digit normalisation.
/// Never logged; <see cref="ToString"/> is redacted (Design §13.3).
/// </summary>
public sealed class IranianNationalId
{
    private IranianNationalId(string value) => Value = value;

    public string Value { get; }

    public string Last4 => Value[^4..];

    public static bool TryCreate(string? raw, out IranianNationalId? result)
    {
        result = null;
        var normalized = PersianDigits.ToAscii(raw?.Trim());
        if (normalized is null || normalized.Length != 10 || !normalized.All(char.IsAsciiDigit)) return false;
        result = new IranianNationalId(normalized);
        return true;
    }

    public override string ToString() => "[redacted]";
}

/// <summary>
/// Iranian mobile number normalised to E.164 (+989xxxxxxxxx). Accepts 09xxxxxxxxx, 989xxxxxxxxx and +989xxxxxxxxx.
/// Never logged; <see cref="ToString"/> is redacted.
/// </summary>
public sealed class IranianMobile
{
    private static readonly Regex E164 = new("^\\+989[0-9]{9}$", RegexOptions.CultureInvariant);

    private IranianMobile(string value) => Value = value;

    public string Value { get; }

    public string Last4 => Value[^4..];

    public static bool TryCreate(string? raw, out IranianMobile? result)
    {
        result = null;
        var digits = PersianDigits.ToAscii(raw?.Replace(" ", string.Empty).Replace("-", string.Empty).Trim());
        if (digits is null) return false;
        if (digits.StartsWith('+')) digits = digits[1..];
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];

        string candidate;
        if (digits.Length == 11 && digits.StartsWith("09", StringComparison.Ordinal))
        {
            candidate = "+98" + digits[1..];
        }
        else if (digits.Length == 12 && digits.StartsWith("989", StringComparison.Ordinal))
        {
            candidate = "+" + digits;
        }
        else
        {
            return false;
        }

        if (!E164.IsMatch(candidate)) return false;
        result = new IranianMobile(candidate);
        return true;
    }

    public override string ToString() => "[redacted]";
}

/// <summary>
/// Display name fragment: trimmed, 1–100 characters, no control characters. Treated as PII; never logged.
/// </summary>
public sealed class PersonName
{
    public const int MaximumLength = 100;

    private PersonName(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? raw, out PersonName? result)
    {
        result = null;
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaximumLength) return false;
        if (trimmed.Any(char.IsControl)) return false;
        result = new PersonName(trimmed);
        return true;
    }

    public override string ToString() => "[redacted]";
}

/// <summary>
/// Converts Persian (U+06F0–U+06F9) and Arabic-Indic (U+0660–U+0669) digits to ASCII digits.
/// Returns null for null input. Other characters are preserved.
/// </summary>
internal static class PersianDigits
{
    public static string? ToAscii(string? value)
    {
        if (value is null) return null;
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is >= '۰' and <= '۹')
            {
                builder.Append((char)('0' + (character - '۰')));
            }
            else if (character is >= '٠' and <= '٩')
            {
                builder.Append((char)('0' + (character - '٠')));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// Stable parse result for a field received from TahlilApp. Only <see cref="Valid"/> carries a value.
/// </summary>
public enum IncomingProfileFieldKind
{
    Valid,
    Absent,
    Null,
    Invalid
}

public sealed record IncomingProfileField(IncomingProfileFieldKind Kind, string? NormalizedValue)
{
    public static IncomingProfileField Absent { get; } = new(IncomingProfileFieldKind.Absent, null);
    public static IncomingProfileField Null { get; } = new(IncomingProfileFieldKind.Null, null);
    public static IncomingProfileField Invalid { get; } = new(IncomingProfileFieldKind.Invalid, null);
    public static IncomingProfileField Valid(string normalizedValue) => new(IncomingProfileFieldKind.Valid, normalizedValue);

    /// <summary>Redacted: the value is PII and must never reach logs (Design §13.3).</summary>
    public override string ToString() => $"IncomingProfileField({Kind})";
}

public enum ProfileFieldAction
{
    /// <summary>Valid, changed value: store it (TahlilApp is authoritative).</summary>
    Overwrite,

    /// <summary>Valid, identical value: no value write.</summary>
    Unchanged,

    /// <summary>Absent, null or invalid: keep whatever is stored. Absence is never deletion.</summary>
    KeepStored
}

/// <summary>
/// Per-field profile synchronisation rule (Design §10.2). Pure and side-effect free.
/// </summary>
public static class ProfileFieldMergePolicy
{
    public static ProfileFieldAction Decide(string? storedValue, IncomingProfileField incoming)
    {
        if (incoming.Kind != IncomingProfileFieldKind.Valid || incoming.NormalizedValue is null)
        {
            return ProfileFieldAction.KeepStored;
        }

        return string.Equals(storedValue, incoming.NormalizedValue, StringComparison.Ordinal)
            ? ProfileFieldAction.Unchanged
            : ProfileFieldAction.Overwrite;
    }
}
