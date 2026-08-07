using System.Text;

namespace FinancialCopilot.Application.AI.Orchestration;

public static class QueryNormalization
{
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
            "monthly" or "sales" or "sale" or "show" or "please" or "p" or "e" or "s" or "eps" or "roe" or "roa" => true,
            "چارت" or "نمودار" or "روند" or "جدول" or "گیج" or "فهرست" or "خلاصه" or
            "ماهانه" or "فروش" or "درآمد" or "سود" or "قیمت" or "نشان" or "بده" or "لطفا" or "لطفاً" or
            "را" or "کن" or "کنید" or "است" or "چقدر" or "سال" or "ماه" or "هفته" => true,
            _ => false
        };
}
