using System.Globalization;
using System.Text;
using FinancialCopilot.Application.FinancialData.FundPortfolio;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundPortfolioValueNormalizer : IFundPortfolioValueNormalizer
{
    private static readonly HashSet<string> ExcelErrors = new(StringComparer.OrdinalIgnoreCase)
    {
        "#NAME?", "#REF!", "#N/A", "#VALUE!", "#DIV/0!", "#NUM!", "#NULL!", "#SPILL!", "#CALC!", "#FIELD!"
    };

    public string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.GetUnicodeCategory(character) is UnicodeCategory.Format) continue;
            builder.Append(character switch
            {
                '۰' or '٠' => '0', '۱' or '١' => '1', '۲' or '٢' => '2', '۳' or '٣' => '3',
                '۴' or '٤' => '4', '۵' or '٥' => '5', '۶' or '٦' => '6', '۷' or '٧' => '7',
                '۸' or '٨' => '8', '۹' or '٩' => '9', 'ي' => 'ی', 'ى' => 'ی', 'ك' => 'ک',
                '٫' => '.', '٬' => ',', '٪' => '%', '\u200c' => ' ', '\u200f' => ' ', '\u202a' => ' ', '\u202b' => ' ', '\u202c' => ' ',
                '\u202d' => ' ', '\u202e' => ' ', _ => character
            });
        }
        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public bool IsExcelError(string? value) => !string.IsNullOrWhiteSpace(value) && ExcelErrors.Contains(value.Trim());

    public bool TryParseDecimal(string? value, out decimal result)
    {
        result = default;
        var normalized = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(normalized) || IsExcelError(normalized)) return false;
        var percent = normalized.EndsWith('%');
        normalized = normalized.TrimEnd('%').Replace("ریال", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("﷼", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty).Trim();
        if (normalized.StartsWith('(') && normalized.EndsWith(')')) normalized = $"-{normalized[1..^1]}";
        if (!decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out result)) return false;
        if (percent) result /= 100m;
        return true;
    }
}
