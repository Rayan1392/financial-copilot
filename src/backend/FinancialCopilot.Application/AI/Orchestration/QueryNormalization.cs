using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData;

namespace FinancialCopilot.Application.AI.Orchestration;

public static class QueryNormalization
{
    private static readonly Regex NumericToken = new(@"(?<![\p{L}\p{N}])[0-9۰-۹٠-٩]+(?:[,.٬٫][0-9۰-۹٠-٩]+)*(?![\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParseFinancialStatementClues(string? value, out IReadOnlyList<FinancialStatementValueClue> clues, out string? error)
    {
        var result = new List<FinancialStatementValueClue>();
        error = null;
        foreach (Match match in NumericToken.Matches(value ?? string.Empty))
        {
            var token = new string(match.Value.Select(c => c switch
            {
                >= '۰' and <= '۹' => (char)('0' + c - '۰'),
                >= '٠' and <= '٩' => (char)('0' + c - '٠'),
                '٬' => ',', '٫' => '.', _ => c
            }).ToArray());
            var parts = token.Split(',', '.');
            if (parts.Length > 1 && parts.Skip(1).All(part => part.Length == 3)) token = string.Concat(parts);
            else if (parts.Length == 2) token = string.Join('.', parts);
            else if (parts.Length > 2) { error = "malformed_numeric_clue"; clues = []; return false; }
            if (!decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            { error = "malformed_numeric_clue"; clues = []; return false; }
            result.Add(new FinancialStatementValueClue(number));
        }
        if (result.Count == 0) { error = "numeric_clue_required"; clues = []; return false; }
        if (result.Count > 20) { error = "too_many_numeric_clues"; clues = []; return false; }
        clues = result; return true;
    }
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var original in value.Normalize(NormalizationForm.FormKC))
        {
            var character = original switch
            {
                '\u064a' or '\u0649' => '\u06cc',
                '\u0643' => '\u06a9',
                '\u0629' => '\u0647',
                '\u200c' or '\u200d' or '\u200e' or '\u200f' => ' ',
                '\u0660' => '0',
                '\u0661' => '1',
                '\u0662' => '2',
                '\u0663' => '3',
                '\u0664' => '4',
                '\u0665' => '5',
                '\u0666' => '6',
                '\u0667' => '7',
                '\u0668' => '8',
                '\u0669' => '9',
                '\u06f0' => '0',
                '\u06f1' => '1',
                '\u06f2' => '2',
                '\u06f3' => '3',
                '\u06f4' => '4',
                '\u06f5' => '5',
                '\u06f6' => '6',
                '\u06f7' => '7',
                '\u06f8' => '8',
                '\u06f9' => '9',
                _ => original
            };

            if (char.IsWhiteSpace(character) || char.IsPunctuation(character) || char.IsSymbol(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
                builder.Append(' ');
            pendingSpace = false;
            builder.Append(char.IsLetter(character) && character <= 0x7f
                ? char.ToLowerInvariant(character)
                : character);
        }

        return builder.ToString().Trim();
    }

    public static bool IsPresentationWord(string value) =>
        Normalize(value) switch
        {
            "chart" or "trend" or "graph" or "table" or "gauge" or "list" or "summary" => true,
            "چارت" or "نمودار" or "روند" or "جدول" or "گیج" or "فهرست" or "خلاصه" => true,
            _ => false
        };

    public static bool IsEntityDistractor(string value) =>
        Normalize(value) switch
        {
            "chart" or "trend" or "graph" or "table" or "gauge" or "list" or "summary" or
            "monthly" or "sales" or "sale" or "product" or "products" or "mix" or "revenue" or "fundamental" or "technical" or "show" or "please" or "p" or "e" or "s" or "eps" or "roe" or "roa" or
            "month" or "quarter" or "year" or "week" or "previous" or "prior" or "last" or "latest" or "same" or
            "before" or "current" or "and" or "or" or "for" or "of" or "this" => true,
            "چارت" or "نمودار" or "روند" or "جدول" or "گیج" or "فهرست" or "خلاصه" or
            "ماهانه" or "فروش" or "درآمد" or "سود" or "قیمت" or "محصول" or "محصولات" or "ترکیب" or "رکیب" or "بنیادی" or "تکنیکال" or "نشان" or "بده" or "لطفا" or "لطفاً" or
            "را" or "کن" or "کنید" or "است" or "چقدر" or "سال" or "ماه" or "فصل" or "هفته" or
            "خود" or "خودش" or "همان" or "مقایسه" or "بررسی" or "تحلیل" or
            "قبل" or "قبلی" or "گذشته" or "آخرین" or "اخیر" or "مشابه" or "جاری" or "امسال" or "پارسال" or
            "و" or "یا" or "برای" or "از" or "به" or "این" or "آن" or "نماد" or "شرکت" or "سهم" => true,
            _ => false
        };
}
