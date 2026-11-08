using System.Globalization;
using System.Numerics;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

/// <summary>
/// Maps an exact decimal to bounded PostgreSQL numeric coefficient chunks and a scale. PostgreSQL
/// permits at most 131072 integer digits in a numeric value, while the coefficient of a valid
/// value can be as large as 147455 digits (integer digits plus fractional scale). Splitting the
/// coefficient keeps each stored numeric within PostgreSQL's limit without losing precision.
/// </summary>
public static class ExactDecimalValueConverter
{
    public const int ChunkDigits = 60_000;

    public readonly record struct Parts(short Sign, BigInteger High, BigInteger Middle, BigInteger Low, short Scale);

    public static Parts Split(ExactDecimal value)
    {
        var digits = BigInteger.Abs(value.UnscaledValue).ToString(CultureInfo.InvariantCulture);
        var lowStart = Math.Max(0, digits.Length - ChunkDigits);
        var low = ParseChunk(digits[lowStart..]);
        var middleStart = Math.Max(0, lowStart - ChunkDigits);
        var middle = lowStart == 0 ? BigInteger.Zero : ParseChunk(digits[middleStart..lowStart]);
        var high = middleStart == 0 ? BigInteger.Zero : ParseChunk(digits[..middleStart]);
        return new Parts((short)value.UnscaledValue.Sign, high, middle, low, (short)value.Scale);
    }

    public static ExactDecimal? Restore(
        BigInteger? high,
        BigInteger? middle,
        BigInteger? low,
        short? sign,
        short? scale)
    {
        if (high is null && middle is null && low is null && sign is null && scale is null)
            return null;
        if (high is null || middle is null || low is null || sign is null || scale is null)
            throw new InvalidDataException("Exact decimal storage parts must be all null or all present.");
        if (sign is < -1 or > 1 || high < 0 || middle < 0 || low < 0 ||
            high >= BigInteger.Pow(10, ChunkDigits) || middle >= BigInteger.Pow(10, ChunkDigits) || low >= BigInteger.Pow(10, ChunkDigits))
            throw new InvalidDataException("Exact decimal storage parts are outside the supported range.");

        var digits = string.Concat(
            high.Value.IsZero ? string.Empty : high.Value.ToString(CultureInfo.InvariantCulture),
            middle.Value.ToString($"D{ChunkDigits}", CultureInfo.InvariantCulture),
            low.Value.ToString($"D{ChunkDigits}", CultureInfo.InvariantCulture));
        if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var coefficient))
            throw new InvalidDataException("Exact decimal coefficient could not be restored.");
        if (sign == -1) coefficient = BigInteger.Negate(coefficient);
        if (sign == 0 && !coefficient.IsZero || sign != 0 && coefficient.IsZero)
            throw new InvalidDataException("Exact decimal sign does not match its coefficient.");
        return new ExactDecimal(coefficient, scale.Value);
    }

    private static BigInteger ParseChunk(string digits) =>
        BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
}
