using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

public sealed record NumericGroundingResult(bool Passed, int NumbersChecked, int UngroundedCount);

/// <summary>
/// FR-7: every number the model writes must come from the supplied facts or calculations (display rounding allowed),
/// or from the period labels, company identity or the user's question. Persian, Arabic and Latin digits, thousands
/// separators and decimal marks are normalized. Signs are compared by magnitude because the answer may express a
/// negative value in words (کاهش، زیان، منفی).
/// </summary>
public static partial class NumericGroundingValidator
{
    [GeneratedRegex(@"\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    /// <summary>Evidence ids (F12, C3, F۴۵) cited inline are references, not numbers (BUG-031).</summary>
    [GeneratedRegex(@"\b[FC][0-9۰-۹٠-٩]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex EvidenceIdPattern();

    /// <param name="serverNumbers">Numbers the server itself wrote into the text (the report score and 100).</param>
    public static NumericGroundingResult Validate(
        string answer,
        ReportDataSet data,
        string question,
        IReadOnlyCollection<decimal>? serverNumbers = null)
    {
        var allowedExact = new HashSet<decimal>(serverNumbers ?? []);
        foreach (var text in data.PeriodsNewestFirst
                     .Concat(data.Facts.Select(fact => fact.Period + " " + fact.Label))
                     .Concat(data.Calculations.Select(calculation => calculation.Period + " " + calculation.Label))
                     .Append(data.CompanySymbol)
                     .Append(data.CompanyName ?? string.Empty)
                     .Append(question))
        {
            foreach (var number in Extract(text)) allowedExact.Add(Math.Abs(number.Value));
        }

        var allowedValues = data.Facts.Select(fact => Math.Abs(fact.Value))
            .Concat(data.Calculations.Select(calculation => Math.Abs(calculation.Value)))
            .ToArray();

        var checkedCount = 0;
        var ungrounded = 0;
        foreach (var number in Extract(EvidenceIdPattern().Replace(answer, " ")))
        {
            checkedCount++;
            var magnitude = Math.Abs(number.Value);
            if (allowedExact.Contains(magnitude)) continue;
            // Small counts and ordinals ("سه فصل", "۲ مورد") are allowed unless written as a percentage.
            if (!number.IsPercent && number.Decimals == 0 && magnitude is >= 1m and <= 12m) continue;
            var tolerance = 0.5m * (decimal)Math.Pow(10, -number.Decimals);
            if (allowedValues.Any(value => Math.Abs(value - magnitude) <= tolerance)) continue;
            ungrounded++;
        }

        return new NumericGroundingResult(ungrounded == 0, checkedCount, ungrounded);
    }

    internal readonly record struct ExtractedNumber(decimal Value, int Decimals, bool IsPercent);

    internal static IEnumerable<ExtractedNumber> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var normalized = Normalize(text);
        foreach (Match match in NumberPattern().Matches(normalized))
        {
            var raw = match.Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) continue;
            var dot = raw.IndexOf('.', StringComparison.Ordinal);
            var decimals = dot < 0 ? 0 : raw.Length - dot - 1;
            var tail = normalized.AsSpan(match.Index + match.Length).TrimStart().ToString();
            var isPercent = tail.StartsWith('%') || tail.StartsWith("درصد", StringComparison.Ordinal);
            yield return new ExtractedNumber(value, decimals, isPercent);
        }
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(character switch
            {
                >= '۰' and <= '۹' => (char)('0' + (character - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (character - '٠')),
                '٬' => ',',
                '٫' => '.',
                '٪' => '%',
                _ => character
            });
        }
        return builder.ToString();
    }
}
