using System.Globalization;
using System.Numerics;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinancialCopilot.Domain.Financial.CyclicalWavesV2;

/// <summary>
/// An exact base-10 number whose scale is not limited by System.Decimal. PostgreSQL numeric supports
/// up to 131072 integer digits and 16383 fractional digits; values outside those database limits are
/// rejected before persistence instead of being rounded.
/// </summary>
[JsonConverter(typeof(ExactDecimalJsonConverter))]
public readonly struct ExactDecimal : IComparable<ExactDecimal>, IEquatable<ExactDecimal>, IFormattable
{
    public const int MaximumScale = 16_383;
    public const int MaximumIntegerDigits = 131_072;

    public ExactDecimal(BigInteger unscaledValue, int scale)
    {
        if (scale is < 0 or > MaximumScale)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), $"Scale must be between 0 and {MaximumScale}.");
        }

        if (DigitCount(unscaledValue) - scale > MaximumIntegerDigits)
        {
            throw new ArgumentOutOfRangeException(nameof(unscaledValue), "Integer digits exceed PostgreSQL numeric limits.");
        }

        UnscaledValue = unscaledValue;
        Scale = scale;
    }

    public BigInteger UnscaledValue { get; }

    public int Scale { get; }

    public static ExactDecimal Zero => new(BigInteger.Zero, 0);

    public static ExactDecimal Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!TryParse(value.AsSpan(), out var result))
        {
            throw new FormatException("Value is not a finite base-10 number supported by PostgreSQL numeric.");
        }

        return result;
    }

    public static bool TryParse(ReadOnlySpan<char> value, out ExactDecimal result)
    {
        result = default;
        if (value.IsEmpty)
        {
            return false;
        }

        var exponentIndex = value.IndexOfAny('e', 'E');
        var mantissa = exponentIndex >= 0 ? value[..exponentIndex] : value;
        var exponent = 0;
        if (exponentIndex >= 0 &&
            !int.TryParse(value[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
        {
            return false;
        }

        var negative = mantissa[0] == '-';
        if (negative || mantissa[0] == '+')
        {
            mantissa = mantissa[1..];
        }

        var pointIndex = mantissa.IndexOf('.');
        var fractionDigits = pointIndex < 0 ? 0 : mantissa.Length - pointIndex - 1;
        var digitBuilder = new System.Text.StringBuilder(mantissa.Length);
        for (var index = 0; index < mantissa.Length; index++)
        {
            var character = mantissa[index];
            if (character == '.')
            {
                if (pointIndex != index || pointIndex == 0 || pointIndex == mantissa.Length - 1)
                {
                    return false;
                }

                continue;
            }

            if (character is < '0' or > '9')
            {
                return false;
            }

            digitBuilder.Append(character);
        }

        if (digitBuilder.Length == 0 || digitBuilder.Length > MaximumIntegerDigits + MaximumScale + 1)
        {
            return false;
        }

        if (!BigInteger.TryParse(digitBuilder.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var unscaled))
        {
            return false;
        }

        if (negative)
        {
            unscaled = BigInteger.Negate(unscaled);
        }

        var scaleLong = (long)fractionDigits - exponent;
        if (scaleLong < 0)
        {
            if (-scaleLong > MaximumIntegerDigits)
            {
                return false;
            }

            unscaled *= BigInteger.Pow(10, (int)-scaleLong);
            scaleLong = 0;
        }

        if (scaleLong > MaximumScale)
        {
            return false;
        }

        try
        {
            result = new ExactDecimal(unscaled, (int)scaleLong);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public int CompareTo(ExactDecimal other)
    {
        var scale = Math.Max(Scale, other.Scale);
        return (UnscaledValue * PowerOfTen(scale - Scale))
            .CompareTo(other.UnscaledValue * PowerOfTen(scale - other.Scale));
    }

    public bool Equals(ExactDecimal other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ExactDecimal other && Equals(other);

    public override int GetHashCode()
    {
        var value = UnscaledValue;
        var scale = Scale;
        while (scale > 0 && value % 10 == 0)
        {
            value /= 10;
            scale--;
        }

        return HashCode.Combine(value, scale);
    }

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (format is not null && !string.Equals(format, "G", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("ExactDecimal only supports general format.");
        }

        var negative = UnscaledValue.Sign < 0;
        var digits = BigInteger.Abs(UnscaledValue).ToString(CultureInfo.InvariantCulture);
        if (Scale == 0)
        {
            return negative ? "-" + digits : digits;
        }

        var padded = digits.Length <= Scale
            ? new string('0', Scale - digits.Length + 1) + digits
            : digits;
        var split = padded.Length - Scale;
        var formatted = padded.Insert(split, ".");
        return negative ? "-" + formatted : formatted;
    }

    public static bool operator ==(ExactDecimal left, ExactDecimal right) => left.Equals(right);
    public static bool operator !=(ExactDecimal left, ExactDecimal right) => !left.Equals(right);
    public static bool operator <(ExactDecimal left, ExactDecimal right) => left.CompareTo(right) < 0;
    public static bool operator >(ExactDecimal left, ExactDecimal right) => left.CompareTo(right) > 0;
    public static bool operator <=(ExactDecimal left, ExactDecimal right) => left.CompareTo(right) <= 0;
    public static bool operator >=(ExactDecimal left, ExactDecimal right) => left.CompareTo(right) >= 0;

    private static int DigitCount(BigInteger value) => value.IsZero ? 1 : BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture).Length;

    private static BigInteger PowerOfTen(int exponent) => exponent == 0 ? BigInteger.One : BigInteger.Pow(10, exponent);
}

public sealed class ExactDecimalJsonConverter : JsonConverter<ExactDecimal>
{
    public override ExactDecimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException("ExactDecimal JSON values must be numbers or null.");
        }

        var raw = reader.HasValueSequence
            ? System.Text.Encoding.UTF8.GetString(BuffersExtensions.ToArray(reader.ValueSequence))
            : System.Text.Encoding.UTF8.GetString(reader.ValueSpan);
        try
        {
            return ExactDecimal.Parse(raw);
        }
        catch (FormatException exception)
        {
            throw new JsonException("JSON number is outside the supported exact numeric range.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, ExactDecimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString(), skipInputValidation: false);
}
