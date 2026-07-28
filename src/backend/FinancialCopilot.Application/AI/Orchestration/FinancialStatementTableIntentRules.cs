using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.Application.AI.Orchestration;

public static class FinancialStatementTableIntentRules
{
    private static readonly string[] IncomeAliases =
    [
        "صورت سود و زیان",
        "سود و زیان",
        "صورت درآمد",
        "صورت عملکرد",
        "درآمدها و هزینه ها",
        "درآمدها و هزینه‌ها"
    ];

    private static readonly string[] BalanceAliases =
    [
        "ترازنامه",
        "صورت وضعیت مالی",
        "دارایی و بدهی",
        "دارایی‌ها و بدهی‌ها",
        "دارایی ها و بدهی ها"
    ];

    private static readonly string[] CashFlowAliases =
    [
        "جریان وجه نقد",
        "جریان وجوه نقد",
        "صورت جریان وجوه نقد",
        "صورت جریان نقد",
        "جریان نقدی"
    ];

    private static readonly string[] TableActionAliases =
    [
        "آخرین",
        "نشان بده",
        "نمایش بده",
        "بده",
        "چیست",
        "جدول",
        "گزارش"
    ];

    private static readonly string[] AnalysisAliases =
    [
        "تحلیل",
        "بررسی",
        "ارزیابی",
        "نظر"
    ];

    private static readonly string[] StopWords =
    [
        "و",
        "یا",
        "در",
        "به",
        "از",
        "برای",
        "نماد",
        "شرکت",
        "سهم",
        "را",
        "کن",
        "بده",
        "نشان",
        "نمایش",
        "گزارش",
        "آخرین",
        "جدیدترین",
        "جدول",
        "چیست",
        "چیه",
        "لطفا",
        "لطفاً"
    ];

    public static bool LooksLikeFinancialStatementTableQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var normalized = NormalizeText(query);
        if (ContainsAny(normalized, AnalysisAliases) && !ContainsAny(normalized, "نشان بده", "نمایش بده", "جدول"))
            return false;

        return ResolveStatementType(normalized) is not null &&
               (ContainsAny(normalized, TableActionAliases) ||
                ContainsAny(normalized, IncomeAliases) ||
                ContainsAny(normalized, BalanceAliases) ||
                ContainsAny(normalized, CashFlowAliases));
    }

    public static FinancialStatementTableQuery BuildQuery(string userMessage)
    {
        var normalized = NormalizeText(userMessage);
        return new FinancialStatementTableQuery(
            userMessage,
            ExtractCompanyHint(userMessage),
            ResolveStatementType(normalized),
            ResolvePeriodMonths(normalized),
            ResolveAuditedPreference(normalized),
            ResolveRepresentedPreference(normalized),
            ResolveComposingPreference(normalized));
    }

    public static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Trim()
            .Replace('ك', 'ک')
            .Replace('ي', 'ی')
            .Replace('ى', 'ی')
            .Replace("\u200c", " ")
            .ToLowerInvariant();

        for (var i = 0; i <= 9; i++)
        {
            normalized = normalized.Replace((char)(0x06F0 + i), (char)('0' + i));
            normalized = normalized.Replace((char)(0x0660 + i), (char)('0' + i));
        }

        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        return normalized;
    }

    private static FinancialStatementType? ResolveStatementType(string normalized)
    {
        if (ContainsAny(normalized, BalanceAliases))
            return FinancialStatementType.BalanceSheet;

        if (ContainsAny(normalized, CashFlowAliases))
            return FinancialStatementType.CashFlow;

        if (ContainsAny(normalized, IncomeAliases))
            return FinancialStatementType.IncomeStatement;

        return null;
    }

    private static int? ResolvePeriodMonths(string normalized) => normalized switch
    {
        var s when ContainsAny(s, "12 ماهه", "12ماهه", "دوازده ماهه", "سالانه") => 12,
        var s when ContainsAny(s, "9 ماهه", "9ماهه", "نه ماهه") => 9,
        var s when ContainsAny(s, "6 ماهه", "6ماهه", "شش ماهه") => 6,
        var s when ContainsAny(s, "3 ماهه", "3ماهه", "سه ماهه", "فصلی") => 3,
        _ => null
    };

    private static bool? ResolveAuditedPreference(string normalized)
    {
        if (ContainsAny(normalized, "حسابرسی نشده", "حسابرسی‌نشده"))
            return false;

        if (ContainsAny(normalized, "حسابرسی شده", "حسابرسی‌شده"))
            return true;

        return null;
    }

    private static bool? ResolveRepresentedPreference(string normalized)
    {
        if (ContainsAny(normalized, "تجدید ارائه نشده", "تجدیدارائه نشده", "اصلی"))
            return false;

        if (ContainsAny(normalized, "تجدید ارائه شده", "تجدیدارائه شده"))
            return true;

        return null;
    }

    private static bool? ResolveComposingPreference(string normalized)
    {
        if (ContainsAny(normalized, "غیرتلفیقی", "غیر تلفیقی", "شرکت اصلی", "standalone", "parent"))
            return false;

        if (ContainsAny(normalized, "تلفیقی", "consolidated", "گروه"))
            return true;

        return null;
    }

    private static string? ExtractCompanyHint(string userMessage)
    {
        var normalized = NormalizeText(userMessage);
        foreach (var phrase in IncomeAliases
                     .Concat(BalanceAliases)
                     .Concat(CashFlowAliases)
                     .Concat(TableActionAliases)
                     .Concat(AnalysisAliases)
                     .OrderByDescending(phrase => phrase.Length))
        {
            normalized = normalized.Replace(NormalizeText(phrase), " ", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var phrase in new[]
                 {
                     "3 ماهه", "سه ماهه", "6 ماهه", "شش ماهه", "9 ماهه", "نه ماهه",
                     "12 ماهه", "دوازده ماهه", "سالانه", "حسابرسی شده", "حسابرسی نشده",
                     "تجدید ارائه شده", "تجدید ارائه نشده", "تلفیقی", "غیرتلفیقی",
                     "غیر تلفیقی", "شرکت اصلی", "اصلی"
                 })
        {
            normalized = normalized.Replace(NormalizeText(phrase), " ", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var ch in new[] { "؟", "?", "!", "،", ",", ":", ";", "(", ")", "«", "»" })
            normalized = normalized.Replace(ch, " ", StringComparison.OrdinalIgnoreCase);

        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !StopWords.Contains(token, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return tokens.Count == 0 ? null : string.Join(' ', tokens);
    }

    private static bool ContainsAny(string value, IEnumerable<string> candidates) =>
        candidates.Any(candidate => value.Contains(NormalizeText(candidate), StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(NormalizeText(candidate), StringComparison.OrdinalIgnoreCase));
}
