using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;

namespace FinancialCopilot.Application.Scanner.CyclicalWavesV2;

public enum MonetaryThresholdStatus
{
    Resolved,
    ZeroNoUnitNeeded,
    MissingUnit,
    Ambiguous
}

public sealed record MonetaryThresholdResolution(MonetaryThresholdStatus Status, decimal? IrrValue, string? Expression);

public sealed record MonetaryExpression(decimal Number, MonetaryScale Scale, MonetaryCurrency? Currency, string Text)
{
    public bool HasUnit => Currency is not null || Scale == MonetaryScale.Hemat;

    public decimal? IrrValue => HasUnit ? MonetaryAmountUnits.ToIrr(Number, Scale, Currency ?? MonetaryCurrency.Toman) : null;
}

/// <summary>
/// Converts the amount a user typed («۱۰۰ میلیارد تومان», «۲ همت») into IRR for CyclicalWaves-mode screening.
/// The scanner parser's threshold carries no unit, so the resolver re-reads the user's question and pairs each
/// monetary condition with the expression whose number equals the threshold. No unit is ever assumed:
/// a missing or ambiguous unit asks the user, except for a threshold of zero.
/// </summary>
public static class MonetaryThresholdResolver
{
    private static readonly Regex AmountPattern = new(
        @"(?<number>\d+(?:\.\d+)?)\s*(?<scale>هزار\s*میلیارد|همت|میلیارد|میلیون|هزار)?\s*(?<currency>ریال|تومان|تومن)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static MonetaryThresholdResolution Resolve(string? userQuery, decimal threshold) =>
        Resolve(userQuery, [threshold])[0];

    public static IReadOnlyList<MonetaryThresholdResolution> Resolve(string? userQuery, IReadOnlyList<decimal> thresholds)
    {
        var expressions = Extract(userQuery);
        return thresholds.Select(threshold => ResolveOne(expressions, threshold)).ToArray();
    }

    public static IReadOnlyList<MonetaryExpression> Extract(string? userQuery)
    {
        var text = NormalizeDigits(userQuery ?? string.Empty);
        return AmountPattern.Matches(text)
            .Select(match => new MonetaryExpression(
                decimal.Parse(match.Groups["number"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
                ParseScale(match.Groups["scale"].Value),
                ParseCurrency(match.Groups["currency"].Value),
                match.Value.Trim()))
            .ToArray();
    }

    private static MonetaryThresholdResolution ResolveOne(IReadOnlyList<MonetaryExpression> expressions, decimal threshold)
    {
        var matching = expressions.Where(expression => Matches(expression, threshold)).ToArray();
        var withUnit = matching.Where(expression => expression.HasUnit).ToArray();
        var distinctIrr = withUnit.Select(expression => expression.IrrValue!.Value).Distinct().ToArray();

        if (distinctIrr.Length == 1 && withUnit.Length == matching.Length)
        {
            return new MonetaryThresholdResolution(MonetaryThresholdStatus.Resolved, distinctIrr[0], withUnit[0].Text);
        }

        if (distinctIrr.Length > 1)
        {
            return new MonetaryThresholdResolution(MonetaryThresholdStatus.Ambiguous, null, null);
        }

        return threshold == 0m
            ? new MonetaryThresholdResolution(MonetaryThresholdStatus.ZeroNoUnitNeeded, 0m, null)
            : new MonetaryThresholdResolution(MonetaryThresholdStatus.MissingUnit, null, matching.FirstOrDefault()?.Text);
    }

    /// <summary>The parser may keep the typed number, scale it, or convert it; any of these identifies the expression.</summary>
    private static bool Matches(MonetaryExpression expression, decimal threshold)
    {
        if (expression.Number == threshold || expression.Number * MonetaryAmountUnits.ScaleFactor(expression.Scale) == threshold)
        {
            return true;
        }

        return expression.IrrValue == threshold;
    }

    private static MonetaryScale ParseScale(string value) => Regex.Replace(value, @"\s+", " ") switch
    {
        "" => MonetaryScale.One,
        "هزار" => MonetaryScale.Thousand,
        "میلیون" => MonetaryScale.Million,
        "میلیارد" => MonetaryScale.Billion,
        "همت" => MonetaryScale.Hemat,
        _ => MonetaryScale.ThousandBillion
    };

    private static MonetaryCurrency? ParseCurrency(string value) => value switch
    {
        "ریال" => MonetaryCurrency.Rial,
        "تومان" or "تومن" => MonetaryCurrency.Toman,
        _ => null
    };

    /// <summary>
    /// Persian and Arabic digits become ASCII. ٬ or , followed by exactly three digits is a thousands separator;
    /// otherwise ٬ , ٫ and / between digits are the decimal point («۱٬۵» is one and a half). Zero-width joiners
    /// and the Arabic Ye/Kaf variants are normalized so unit words match.
    /// </summary>
    internal static string NormalizeDigits(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                >= '۰' and <= '۹' => (char)('0' + (character - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (character - '٠')),
                'ي' => 'ی',
                'ك' => 'ک',
                '‌' or '‍' or '‏' => ' ',
                _ => character
            });
        }

        var text = builder.ToString();
        text = Regex.Replace(text, @"(?<=\d)[٬,](?=\d{3}(?!\d))", string.Empty);
        return Regex.Replace(text, @"(?<=\d)[٬,٫/](?=\d)", ".");
    }
}
