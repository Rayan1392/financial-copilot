using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.Application.AI.Orchestration;

public static class FinancialStatementAnalysisIntentRules
{
    private static readonly string[] GeneralPhrases =
    [
        "صورت مالی",
        "صورتهای مالی",
        "صورت های مالی",
        "گزارش مالی",
        "گزارش 3 ماهه",
        "گزارش سه ماهه",
        "گزارش 6 ماهه",
        "گزارش شش ماهه",
        "گزارش 9 ماهه",
        "گزارش نه ماهه",
        "گزارش 12 ماهه",
        "گزارش دوازده ماهه",
        "گزارش سالانه",
        "آخرین گزارش",
        "آخرین صورت مالی",
        "سود و زیان",
        "ترازنامه"
    ];

    private static readonly string[] MetricPhrases =
    [
        "درآمد عملیاتی",
        "فروش خالص",
        "درآمد",
        "سود ناخالص",
        "زیان ناخالص",
        "سود عملیاتی",
        "زیان عملیاتی",
        "سود خالص",
        "زیان خالص",
        "eps",
        "سود هر سهم",
        "زیان هر سهم",
        "حاشیه سود",
        "حاشیه عملیاتی",
        "حاشیه سود عملیاتی",
        "حاشیه سود خالص",
        "دارایی",
        "بدهی",
        "حقوق صاحبان سهام",
        "حقوق مالکانه",
        "نسبت بدهی",
        "نسبت جاری",
        "roa",
        "roe",
        "بازده دارایی",
        "بازده دارایی ها",
        "بازده دارایی‌ها",
        "بازده حقوق صاحبان سهام",
        "بازده حقوق مالکانه"
    ];

    private static readonly string[] ExclusionPhrases =
    [
        "p/e",
        "pe ",
        " p e",
        "p/s",
        "ps ",
        "فروش ماهانه",
        "تولید ماهانه",
        "روند فروش",
        "نمودار فروش",
        "ترکیب درآمد محصولات",
        "مهم‌ترین محصول",
        "مهم ترین محصول",
        "پرفروش‌ترین محصول",
        "پرفروش ترین محصول",
        "تحلیل تکنیکال",
        "قیمت تعادلی",
        "رصد معاملات عمده"
    ];

    private static readonly string[] NormalizedGeneralPhrases = GeneralPhrases.Select(NormalizeText).ToArray();
    private static readonly string[] NormalizedMetricPhrases = MetricPhrases.Select(NormalizeText).ToArray();
    private static readonly string[] NormalizedExclusionPhrases = ExclusionPhrases.Select(NormalizeText).ToArray();

    public static bool LooksLikeFinancialStatementAnalysisQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var normalized = NormalizeText(query);
        if (NormalizedExclusionPhrases.Any(p => normalized.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (ContainsThresholdLanguage(normalized))
            return false;

        if (LooksLikeDirectPeriodMetricLookup(normalized))
            return false;

        return NormalizedGeneralPhrases.Any(p => normalized.Contains(p, StringComparison.OrdinalIgnoreCase)) ||
               NormalizedMetricPhrases.Any(p => normalized.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    public static FinancialStatementAnalysisQuery BuildQuery(string userMessage)
    {
        var normalized = NormalizeText(userMessage);
        var periodMonths = ResolvePeriodMonths(normalized);
        var variant = ResolveVariantPreference(normalized);
        var audited = ResolveAuditedPreference(normalized);
        var focus = ResolveStatementTypeFocus(normalized);
        var metricFocusCodes = ResolveMetricFocusCodes(normalized);

        var includeBalanceSheetSummary = metricFocusCodes.Any(code =>
            code is "TOTAL_ASSETS" or "TOTAL_LIABILITIES" or "TOTAL_EQUITY" or "CURRENT_RATIO" or "DEBT_RATIO");
        var includeReturnMetrics = metricFocusCodes.Any(code => code is "ROA" or "ROE");

        return new FinancialStatementAnalysisQuery(
            userMessage,
            ExtractCompanyHint(userMessage),
            periodMonths,
            focus,
            variant,
            audited,
            metricFocusCodes,
            IncludeBalanceSheetSummary: includeBalanceSheetSummary || focus == FinancialStatementType.BalanceSheet,
            IncludeReturnMetrics: includeReturnMetrics,
            IncludeSourceDetails: true);
    }

    public static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Trim()
            .Replace('ك', 'ک')
            .Replace('ي', 'ی')
            .Replace('ى', 'ی')
            .Replace("‌", " ")
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

    private static int? ResolvePeriodMonths(string normalized) => normalized switch
    {
        var s when ContainsAny(s, "12 ماهه", "12ماهه", "دوازده ماهه", "سالانه") => 12,
        var s when ContainsAny(s, "9 ماهه", "9ماهه", "نه ماهه") => 9,
        var s when ContainsAny(s, "6 ماهه", "6ماهه", "شش ماهه") => 6,
        var s when ContainsAny(s, "3 ماهه", "3ماهه", "سه ماهه", "فصلی") => 3,
        _ => null
    };

    private static FinancialStatementVariantPreference ResolveVariantPreference(string normalized)
    {
        if (ContainsAny(normalized, "تلفیقی", "consolidated", "گروه"))
            return FinancialStatementVariantPreference.ConsolidatedOnly;

        if (ContainsAny(normalized, "غیرتلفیقی", "شرکت اصلی", "standalone", "parent", "اصلی"))
            return FinancialStatementVariantPreference.NonConsolidatedOnly;

        return FinancialStatementVariantPreference.DefaultNonConsolidated;
    }

    private static bool? ResolveAuditedPreference(string normalized)
    {
        if (ContainsAny(normalized, "حسابرسی نشده", "حسابرسی‌نشده"))
            return false;

        if (ContainsAny(normalized, "حسابرسی شده", "حسابرسی‌شده"))
            return true;

        return null;
    }

    private static FinancialStatementType? ResolveStatementTypeFocus(string normalized)
    {
        if (ContainsAny(normalized, "ترازنامه", "دارایی", "بدهی", "حقوق صاحبان سهام", "حقوق مالکانه", "نسبت جاری"))
            return FinancialStatementType.BalanceSheet;

        if (ContainsAny(normalized, "جریان نقد", "cash flow"))
            return FinancialStatementType.CashFlow;

        if (ContainsAny(normalized, "سود و زیان", "درآمد عملیاتی", "سود خالص", "eps", "حاشیه سود"))
            return FinancialStatementType.IncomeStatement;

        return null;
    }

    private static IReadOnlyList<string> ResolveMetricFocusCodes(string normalized)
    {
        var codes = new List<string>();

        AddIfContains(codes, normalized, "REVENUE", "درآمد عملیاتی", "فروش خالص", "درآمد");
        AddIfContains(codes, normalized, "GROSS_PROFIT", "سود ناخالص", "زیان ناخالص");
        AddIfContains(codes, normalized, "OPERATING_PROFIT", "سود عملیاتی", "زیان عملیاتی");
        AddIfContains(codes, normalized, "NET_PROFIT", "سود خالص", "زیان خالص");
        AddIfContains(codes, normalized, "EPS", "eps", "سود هر سهم", "زیان هر سهم");
        AddIfContains(codes, normalized, "GROSS_PROFIT_MARGIN", "حاشیه سود ناخالص");
        AddIfContains(codes, normalized, "OPERATING_PROFIT_MARGIN", "حاشیه سود عملیاتی", "حاشیه عملیاتی");
        AddIfContains(codes, normalized, "NET_PROFIT_MARGIN", "حاشیه سود خالص", "حاشیه سود");
        AddIfContains(codes, normalized, "TOTAL_ASSETS", "دارایی");
        AddIfContains(codes, normalized, "TOTAL_LIABILITIES", "بدهی");
        AddIfContains(codes, normalized, "TOTAL_EQUITY", "حقوق صاحبان سهام", "حقوق مالکانه");
        AddIfContains(codes, normalized, "CURRENT_RATIO", "نسبت جاری");
        AddIfContains(codes, normalized, "DEBT_RATIO", "نسبت بدهی");
        AddIfContains(codes, normalized, "ROA", "roa", "بازده دارایی", "بازده دارایی ها", "بازده دارایی‌ها");
        AddIfContains(codes, normalized, "ROE", "roe", "بازده حقوق صاحبان سهام", "بازده حقوق مالکانه");

        return codes;
    }

    private static string? ExtractCompanyHint(string userMessage)
    {
        var normalized = NormalizeText(userMessage);
        foreach (var phrase in NormalizedGeneralPhrases.Concat(NormalizedMetricPhrases).OrderByDescending(p => p.Length))
            normalized = normalized.Replace(phrase, " ", StringComparison.OrdinalIgnoreCase);

        normalized = normalized
            .Replace("را", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("کن", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("بگو", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("چطور", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("چگونه", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("چقدر", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("است", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("شده", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("آخرین", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("اخیر", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("گزارش", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("مالی", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("تحلیل", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("خلاصه", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("تلفیقی", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("غیرتلفیقی", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("شرکت اصلی", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("اصلی", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("consolidated", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("standalone", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("parent", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("حسابرسی شده", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("حسابرسی‌شده", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("حسابرسی نشده", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("حسابرسی‌نشده", " ", StringComparison.OrdinalIgnoreCase);

        foreach (var ch in new[] { "؟", "?", "!", "،", ",", ":", ";", "(", ")", "«", "»" })
            normalized = normalized.Replace(ch, " ", StringComparison.OrdinalIgnoreCase);

        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !StopWords.Contains(token))
            .ToList();

        if (tokens.Count == 0)
            return null;

        return string.Join(' ', tokens);
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsThresholdLanguage(string normalized) =>
        normalized.Contains('<') ||
        normalized.Contains('>') ||
        ContainsAny(
            normalized,
            " above ",
            " below ",
            " greater than ",
            " less than ",
            " greater ",
            " less ",
            " over ",
            " under ",
            " بالای ",
            " بیشتر از ",
            " کمتر از ",
            " زیر ");

    private static bool LooksLikeDirectPeriodMetricLookup(string normalized)
    {
        var hasPeriodSelector = ContainsAny(
            normalized,
            "فصل قبل",
            "فصل گذشته",
            "آخرین فصل",
            "فصل اخیر",
            "فصل مشابه سال قبل",
            "فصل سال قبل",
            "ماه قبل",
            "ماه گذشته",
            "آخرین ماه",
            "ماه اخیر",
            "ماه مشابه سال قبل",
            "متوسط فروش 12 ماهه",
            "متوسط فروش ۱۲ ماهه",
            "میانگین فروش 12 ماهه",
            "میانگین فروش ۱۲ ماهه");

        if (!hasPeriodSelector)
            return false;

        return ContainsAny(
            normalized,
            "حاشیه سود خالص",
            "حاشیه سود ناخالص",
            "حاشیه سود عملیاتی",
            "فروش ماه",
            "فروش ماهانه",
            "متوسط فروش",
            "میانگین فروش",
            "قیمت به سود",
            "قیمت به فروش",
            "pe",
            "ps");
    }

    private static void AddIfContains(List<string> codes, string normalized, string code, params string[] phrases)
    {
        if (phrases.Any(phrase => normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase)) &&
            !codes.Contains(code, StringComparer.OrdinalIgnoreCase))
        {
            codes.Add(code);
        }
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "و",
        "یا",
        "در",
        "به",
        "از",
        "برای",
        "نماد",
        "شرکت",
        "سهم",
        "وضعیت",
        "عملکرد",
        "منتشر",
        "شده",
        "این",
        "آن",
        "هم",
        "را",
        "کن"
    };
}
