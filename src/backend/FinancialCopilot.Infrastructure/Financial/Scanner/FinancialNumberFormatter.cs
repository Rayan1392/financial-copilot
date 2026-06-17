using System.Globalization;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

internal static class FinancialNumberFormatter
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Whole(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", Culture);

    public static string Ratio(decimal value) =>
        TrimFraction(value.ToString("N2", Culture));

    public static string SignedPercent(decimal value) =>
        value switch
        {
            > 0 => $"+{Ratio(value)}%",
            < 0 => $"{Ratio(value)}%",
            _ => "0%"
        };

    public static string Metric(string metricCode, decimal value) =>
        IsRatioLike(metricCode)
            ? Ratio(value)
            : Whole(value);

    public static string LargeNumber(decimal value) => Whole(value);

    private static string TrimFraction(string formatted)
    {
        if (!formatted.Contains('.', StringComparison.Ordinal))
            return formatted;

        return formatted.TrimEnd('0').TrimEnd('.');
    }

    private static bool IsRatioLike(string metricCode)
    {
        var normalized = metricCode.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        return normalized.Contains("PE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("PS", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("PB", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("RATIO", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("MARGIN", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("GROWTH", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("PCT", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("PERCENT", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ROE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ROA", StringComparison.OrdinalIgnoreCase);
    }
}
