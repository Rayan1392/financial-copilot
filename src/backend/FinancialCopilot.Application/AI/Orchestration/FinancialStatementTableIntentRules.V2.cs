using System.Globalization;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Feature 154 V2-only statement-table recognition. The methods in the other part of this class are shared with
/// the frozen V1 path and keep exactly their pre-Feature 154 behavior (POLICY-V1-FREEZE). V2 entry points first
/// canonicalize the binding PD-02 aliases to the alias whose route they must follow («وضعیت مالی» → «ترازنامه»,
/// «عملکرد مالی» → «صورت سود و زیان», PD-21), then reuse the shared rules on the canonical text.
/// </summary>
public static partial class FinancialStatementTableIntentRules
{
    private const string BalanceCanonical = "ترازنامه";
    private const string IncomeCanonical = "صورت سود و زیان";

    // Longest first so «صورت وضعیت مالی» is rewritten before «وضعیت مالی».
    private static readonly (string Alias, string Canonical)[] V2AliasCanonicalForms =
    [
        ("صورت دارایی‌ها و بدهی‌های", BalanceCanonical),
        ("صورت دارایی‌ها و بدهی‌ها", BalanceCanonical),
        ("دارایی‌ها و بدهی‌های", BalanceCanonical),
        ("دارایی‌ها و بدهی‌ها", BalanceCanonical),
        ("دارایی و بدهی", BalanceCanonical),
        ("statement of financial position", BalanceCanonical),
        ("profit and loss statement", IncomeCanonical),
        ("statement of profit or loss", IncomeCanonical),
        ("statement of operations", IncomeCanonical),
        ("صورت وضعیت مالی", BalanceCanonical),
        ("صورت عملکرد مالی", IncomeCanonical),
        // PD-27: «صورت وضعیت» is a balance-sheet alias in V2; bare «وضعیت» keeps its existing route.
        (BareStatementOfPositionAlias, BalanceCanonical),
        ("balance sheet", BalanceCanonical),
        ("وضعیت مالی", BalanceCanonical),
        ("عملکرد مالی", IncomeCanonical),
        ("بیلان", BalanceCanonical),
        ("p&l", IncomeCanonical)
    ];

    private const string BareStatementOfPositionAlias = "صورت وضعیت";

    private const string OtherStatementHeadGuard = @"(?!\s*(?:جریان|سود|درآمد|عملکرد|تغییرات))";

    /// <summary>Possessive forms and repeat words that are never company hints (R-26).</summary>
    private static readonly string[] V2NonCompanyTokens = ["اش", "شان", "ش", "ی", "همین", "همینو", "همینم", "هم"];

    /// <summary>V2 alias canonicalization over the shared normalization, plus «ۀ» → «ه».</summary>
    public static string CanonicalizeV2(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;
        var normalized = NormalizeText(message.Replace('ۀ', 'ه'));
        foreach (var (alias, canonical) in V2AliasCanonicalForms)
        {
            var words = Regex.Escape(NormalizeText(alias)).Replace("\\ ", "\\s+");
            // PD-27: «صورت وضعیت» is the balance sheet only when it does not head another statement name
            // («صورت وضعیت جریان وجوه نقد …» keeps its own statement type).
            var guard = alias == BareStatementOfPositionAlias ? OtherStatementHeadGuard : string.Empty;
            normalized = Regex.Replace(normalized, $"(?<![\\p{{L}}\\p{{N}}&]){words}(?![\\p{{L}}\\p{{N}}&]){guard}", canonical,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return normalized;
    }

    /// <summary>The statement type an explicit alias in this message fixes, or null (PD-02, design §4.6).</summary>
    public static FinancialStatementType? ResolveV2StatementType(string? message) =>
        string.IsNullOrWhiteSpace(message) ? null : ResolveStatementType(CanonicalizeV2(message));

    /// <summary>
    /// Quarterly-report reading of a V2 message: <see cref="CanonicalizeV2"/> plus the PD-27 shorthand «عملکرد فصلی»
    /// → «صورت سود و زیان فصلی» (owner instruction 2026-11-05: «عملکرد فصلی فملی را بررسی کن» is the same request as
    /// «عملکرد مالی فصلی فملی را بررسی کن»). Applied only when the message names no statement of its own, and only on
    /// the quarterly path, so statement-type resolution and analysis routes keep their behavior.
    /// </summary>
    private static string CanonicalizeV2Quarterly(string message)
    {
        var canonical = CanonicalizeV2(message);
        return ResolveStatementType(canonical) is null
            ? Regex.Replace(canonical, @"(?<![\p{L}\p{N}])عملکرد\s+فصلی(?![\p{L}\p{N}])", $"{IncomeCanonical} فصلی",
                RegexOptions.CultureInvariant)
            : canonical;
    }

    public static bool LooksLikeV2QuarterlyReportTableQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var canonical = CanonicalizeV2Quarterly(query);
        // PD-05: an explicit balance-sheet alias is never the quarterly income-statement table.
        if (ContainsAny(canonical, BalanceAliases)) return false;
        if (LooksLikeQuarterlyReportTableQuery(canonical)) return true;
        // PD-27: an income-statement alias with the quarter cue «فصلی» and a plain review cue («بررسی کن») opens the
        // Feature 144 quarterly report, exactly like «گزارش سود و زیان فصلی X را بررسی کن». «تحلیل» keeps analysis.
        return IsPlainReviewRequest(canonical) &&
               ResolveStatementType(canonical) == FinancialStatementType.IncomeStatement &&
               ContainsAny(canonical, "فصلی") &&
               LooksLikeQuarterlyReportTableQuery(WithoutReviewCue(canonical));
    }

    public static bool LooksLikeV2FinancialStatementTableQuery(string? query) =>
        !string.IsNullOrWhiteSpace(query) &&
        (LooksLikeFinancialStatementTableQuery(CanonicalizeV2(query)) || LooksLikeBalanceSheetReportQuery(query));

    /// <summary>PD-27 review cue: «بررسی» opens the report; every other analysis cue keeps the analysis route.</summary>
    private static readonly string[] PlainReviewCues = ["بررسی کنید", "بررسی کن", "بررسی"];

    private static readonly string[] AnalysisCuesOtherThanReview = ["تحلیل", "ارزیابی", "analyze", "analyse", "analysis", "review"];

    /// <summary>
    /// True when the request asks to «بررسی» exactly one statement and carries no other analysis cue (PD-27). A
    /// review of several statements («بررسی ترازنامه و سود و زیان X») keeps the analysis route.
    /// </summary>
    private static bool IsPlainReviewRequest(string normalized) =>
        ContainsAny(normalized, "بررسی") && !ContainsAny(normalized, AnalysisCuesOtherThanReview) &&
        new[] { ContainsAny(normalized, BalanceAliases), ContainsAny(normalized, CashFlowAliases), ContainsAny(normalized, IncomeAliases) }
            .Count(mentioned => mentioned) == 1;

    private static string WithoutReviewCue(string normalized)
    {
        foreach (var cue in PlainReviewCues)
            normalized = RemoveWholePhrase(normalized, NormalizeText(cue));
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    public static FinancialStatementTableQuery BuildV2QuarterlyQuery(string userMessage)
    {
        var query = BuildQuarterlyQuery(CanonicalizeV2Quarterly(userMessage));
        return query with { UserMessage = userMessage, CompanyQuery = CleanV2CompanyHint(query.CompanyQuery) };
    }

    /// <summary>
    /// V2 statement-table request for the deterministic branch and the interpreter: the shared request over the
    /// canonical text, plus the Feature 154 balance-sheet form when Feature 154 serves the request.
    /// </summary>
    public static FinancialStatementTableQuery BuildV2Query(string userMessage)
    {
        var canonical = CanonicalizeV2(userMessage);
        var query = BuildQuery(canonical) with { UserMessage = userMessage };
        query = query with { CompanyQuery = CleanV2CompanyHint(query.CompanyQuery) };
        var balanceSheet = ResolveBalanceSheetRequest(userMessage);
        if (!balanceSheet.Use || query.StatementType != FinancialStatementType.BalanceSheet)
            return query;
        return query with
        {
            CompanyQuery = ExtractBalanceSheetCompanyHint(canonical),
            UseBalanceSheetReport = true,
            BalanceSheetView = balanceSheet.View,
            BaselineKind = balanceSheet.BaselineKind,
            ComparisonDates = balanceSheet.Dates,
            UnsupportedVariantFilter = balanceSheet.UnsupportedVariantFilter
        };
    }

    private static string? CleanV2CompanyHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return null;
        var tokens = hint.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !V2NonCompanyTokens.Contains(token, StringComparer.Ordinal))
            .ToArray();
        return tokens.Length == 0 ? null : string.Join(' ', tokens);
    }

    /// <summary>Feature 154 form of a balance-sheet request (spec §4.2). <see cref="Use"/> false keeps the existing route.</summary>
    public sealed record BalanceSheetRequestCue(
        bool Use,
        string View,
        string? BaselineKind,
        IReadOnlyList<DateOnly> Dates,
        string? UnsupportedVariantFilter);

    private static readonly BalanceSheetRequestCue NoBalanceSheetReport = new(false, BalanceSheetViews.Default, null, [], null);

    private static readonly string[] BalanceSheetComparisonCues =
    [
        "مقایسه", "تغییرات", "نسبت به", "compare", "comparison", "versus", " vs "
    ];

    private static readonly (string Kind, string[] Phrases)[] BalanceSheetBaselineCues =
    [
        (BalanceSheetBaselineKinds.SameStagePreviousYear, ["مدت مشابه سال مالی قبل", "مدت مشابه سال قبل", "دوره مشابه سال مالی قبل", "دوره مشابه سال قبل", "مدت مشابه", "دوره مشابه"]),
        (BalanceSheetBaselineKinds.FiscalYearEnd, ["پایان سال مالی قبل", "پایان سال قبل", "سال مالی قبل", "fiscal year end"]),
        (BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd, ["پایان فصل قبل", "فصل قبل", "previous quarter"]),
        (BalanceSheetBaselineKinds.PreviousReport, ["گزارش قبلی", "گزارش قبل", "دوره قبل", "previous report"])
    ];

    private static readonly string[] BalanceSheetFullTableCues = ["همه اقلام", "تمام اقلام", "جدول کامل", "کامل", "all items", "full"];
    private static readonly string[] BalanceSheetSingleValueCues = ["چقدر", "چند است", "چنده", "how much"];
    private static readonly string[] BalanceSheetAnalysisCues = ["تحلیل", "بررسی", "ارزیابی", "analyze", "analyse", "analysis", "review"];

    public static bool LooksLikeBalanceSheetReportQuery(string? query) =>
        !string.IsNullOrWhiteSpace(query) && ResolveBalanceSheetRequest(query).Use;

    /// <summary>
    /// Decides whether a balance-sheet request is served by Feature 154 and in which form. The alias fixes the
    /// statement type; the whole request decides the form (PD-03). Analysis, single-value, full-table and
    /// variant requests keep their existing routes; a date combined with a Feature 083-only filter is typed
    /// <c>unsupported_date_with_variant</c> so the date is never dropped (PD-20).
    /// </summary>
    public static BalanceSheetRequestCue ResolveBalanceSheetRequest(string? userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return NoBalanceSheetReport;
        var normalized = CanonicalizeV2(userMessage);
        if (!ContainsAny(normalized, BalanceAliases)) return NoBalanceSheetReport;
        // PD-27 (owner instruction 2026-11-05): «بررسی / بررسی کن» with a balance-sheet alias opens the report; any
        // other analysis cue («تحلیل ترازنامه X») keeps the Feature 081 analysis route.
        if (IsPlainReviewRequest(normalized)) normalized = WithoutReviewCue(normalized);
        if (ContainsAny(normalized, BalanceSheetAnalysisCues)) return NoBalanceSheetReport;
        if (ContainsAny(normalized, BalanceSheetSingleValueCues)) return NoBalanceSheetReport;

        var dates = ResolveDates(normalized);
        var baselineKind = BalanceSheetBaselineCues
            .FirstOrDefault(cue => ContainsAny(normalized, cue.Phrases)).Kind;
        var isComparison = baselineKind is not null || ContainsAny($" {normalized} ", BalanceSheetComparisonCues) || dates.Count >= 2;
        var variantFilter = ResolveBalanceSheetVariantFilter(normalized);
        if (variantFilter is not null)
        {
            // Feature 083 has no date filter and Feature 154 is standalone and curated: name both, never drop the date.
            return dates.Count > 0
                ? new BalanceSheetRequestCue(true, isComparison ? BalanceSheetViews.Comparison : BalanceSheetViews.SingleDate,
                    baselineKind, dates, variantFilter)
                : NoBalanceSheetReport;
        }

        if (isComparison)
            return new BalanceSheetRequestCue(true, BalanceSheetViews.Comparison,
                dates.Count > 0 && baselineKind is null ? BalanceSheetBaselineKinds.Explicit : baselineKind ?? BalanceSheetBaselineKinds.FiscalYearEnd,
                dates, null);
        return dates.Count == 1
            ? new BalanceSheetRequestCue(true, BalanceSheetViews.SingleDate, null, dates, null)
            : new BalanceSheetRequestCue(true, BalanceSheetViews.Default, null, [], null);
    }

    /// <summary>
    /// Inside an active Feature 154 task, a message without its own statement alias continues that task: a
    /// baseline or comparison cue, a date, or a «همین … برای X» repeat (spec §4.4). Analysis cues never continue it.
    /// </summary>
    public static BalanceSheetRequestCue ResolveBalanceSheetFollowUp(string? userMessage) =>
        string.IsNullOrWhiteSpace(userMessage) || ResolveV2StatementType(userMessage) is not null
            ? NoBalanceSheetReport
            : ResolveBalanceSheetRequest($"{BalanceCanonical} {userMessage}");

    /// <summary>Glue words a bare follow-up may contain besides its cue and dates.</summary>
    private static readonly string[] BareFollowUpGlueWords =
    [
        "با", "را", "رو", "کن", "بکن", "کنید", "بکنید", "مقایسه", "تغییرات", "نسبت", "به", "در", "و", "هم", "لطفا", "لطفاً",
        "بده", "بدید", "نشان", "نشون", "نمایش", "compare", "with", "to", "the", "vs", "versus", "comparison", "please"
    ];

    /// <summary>
    /// True only for a bare balance-sheet follow-up: a baseline/comparison cue or a date, no statement alias, no
    /// analysis cue, and nothing else with its own subject («با گزارش قبلی مقایسه کن», «با ۱۴۰۴/۱۲/۲۹ مقایسه کن»).
    /// A message that names a metric, product, period or company («فروش ماهانه وبملت را با سال قبل مقایسه کن»,
    /// «قیمت وبملت نسبت به هفته قبل») keeps its own routing (F154-R-14).
    /// </summary>
    public static bool LooksLikeBalanceSheetFollowUpCue(string? userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || ResolveV2StatementType(userMessage) is not null) return false;
        var normalized = CanonicalizeV2(userMessage);
        if (ContainsAny(normalized, BalanceSheetAnalysisCues)) return false;
        var hasCue = BalanceSheetBaselineCues.Any(cue => ContainsAny(normalized, cue.Phrases)) ||
                     ContainsAny($" {normalized} ", BalanceSheetComparisonCues) ||
                     ResolveDates(normalized).Count > 0;
        if (!hasCue) return false;
        var remainder = Regex.Replace(normalized, @"(?<!\d)(13\d{2}|14\d{2})[/-]\d{1,2}[/-]\d{1,2}(?!\d)|20\d{2}-\d{2}-\d{2}", " ");
        foreach (var phrase in BalanceSheetBaselineCues.SelectMany(cue => cue.Phrases)
                     .Concat(BalanceSheetComparisonCues.Select(cue => cue.Trim()))
                     .Select(NormalizeText).OrderByDescending(phrase => phrase.Length))
            remainder = RemoveWholePhrase(remainder, phrase);
        foreach (var ch in new[] { "؟", "?", "!", "،", ",", ":", ";", "(", ")", "«", "»", "." })
            remainder = remainder.Replace(ch, " ", StringComparison.Ordinal);
        return remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(token => BareFollowUpGlueWords.Contains(token, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>«همین را برای شگل»: repeat the active report for another company (Feature 120 carry-over).</summary>
    public static bool LooksLikeRepeatForCompany(string? userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return false;
        var tokens = CanonicalizeV2(userMessage).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token => token is "همین" or "همینو" or "همینم") && tokens.Contains("برای");
    }

    private static string? ResolveBalanceSheetVariantFilter(string normalized)
    {
        var fullTable = BalanceSheetFullTableCues.FirstOrDefault(cue => ContainsAny(normalized, cue));
        if (fullTable is not null) return fullTable == "کامل" ? "جدول کامل" : fullTable;
        if (ResolveComposingPreference(normalized) == true) return "تلفیقی";
        if (ResolveAuditedPreference(normalized) is { } audited) return audited ? "حسابرسی‌شده" : "حسابرسی‌نشده";
        if (ContainsAny(normalized, "تجدید ارائه شده", "تجدیدارائه شده", "تجدید ارائه نشده", "تجدیدارائه نشده"))
            return "تجدید ارائه‌شده";
        var months = Regex.Match(normalized, @"(?<!\d)(3|6|9|12)\s*ماهه");
        if (months.Success) return $"{months.Groups[1].Value} ماهه";
        foreach (var (words, value) in new[] { ("سه ماهه", "3 ماهه"), ("شش ماهه", "6 ماهه"), ("نه ماهه", "9 ماهه"), ("دوازده ماهه", "12 ماهه"), ("سالانه", "12 ماهه") })
            if (ContainsAny(normalized, words)) return value;
        return null;
    }

    /// <summary>Up to two Jalali (13xx/14xx) or ISO dates, in the order written. Invalid dates are ignored.</summary>
    public static IReadOnlyList<DateOnly> ResolveDates(string normalized)
    {
        var dates = new List<(int Index, DateOnly Date)>();
        foreach (Match match in Regex.Matches(normalized, @"(?<!\d)(?<year>13\d{2}|14\d{2})[/-](?<month>\d{1,2})[/-](?<day>\d{1,2})(?!\d)"))
        {
            try
            {
                dates.Add((match.Index, DateOnly.FromDateTime(new PersianCalendar().ToDateTime(
                    int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture), 0, 0, 0, 0))));
            }
            catch (ArgumentOutOfRangeException) { }
        }

        foreach (Match match in Regex.Matches(normalized, @"(?<!\d)(?<date>20\d{2}-\d{2}-\d{2})(?!\d)"))
            if (DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                dates.Add((match.Index, parsed));
        return dates.OrderBy(item => item.Index).Select(item => item.Date).Distinct().Take(2).ToArray();
    }

    private static readonly string[] BalanceSheetHintStopWords =
    [
        "را", "رو", "با", "در", "به", "از", "و", "کن", "بکن", "کنید", "نشون", "نشان", "بده", "بدهید", "نمایش", "گزارش",
        "آخرین", "جدیدترین", "فصلی", "فصل", "تاریخ", "مقایسه", "تغییرات", "نسبت", "اش", "شان", "ی", "همین", "برای",
        "لطفا", "لطفاً", "مالی", "صورت", "سال", "قبل", "قبلی", "پایان", "مدت", "دوره", "مشابه", "منتهی", "the", "of", "for",
        "show", "me", "latest", "compare", "with", "and", "vs", "versus",
        // Variant-filter words of an unsupported_date_with_variant request.
        "تلفیقی", "غیرتلفیقی", "غیر", "جداگانه", "همه", "تمام", "اقلام", "کامل", "جدول", "حسابرسی", "شده", "نشده",
        "تجدید", "ارائه", "تجدیدارائه", "ماهه", "سالانه", "all", "items", "full"
    ];

    /// <summary>True when the message carries the Feature 154 «عملکرد مالی» income-statement alias.</summary>
    public static bool HasFinancialPerformanceAlias(string? query) =>
        !string.IsNullOrWhiteSpace(query) && NormalizeText(query).Contains("عملکرد مالی", StringComparison.Ordinal);

    private static string? ExtractBalanceSheetCompanyHint(string canonical)
    {
        var normalized = Regex.Replace(canonical, @"(?<!\d)(13\d{2}|14\d{2})[/-]\d{1,2}[/-]\d{1,2}(?!\d)|20\d{2}-\d{2}-\d{2}", " ");
        var phrases = BalanceAliases
            .Concat(BalanceSheetBaselineCues.SelectMany(cue => cue.Phrases))
            .Concat(BalanceSheetComparisonCues.Select(cue => cue.Trim()))
            .Concat(TableActionAliases)
            .Concat(["گزارشات", "گزارش ها", "گزارش های"])
            .Concat(PlainReviewCues)
            .Select(NormalizeText)
            .OrderByDescending(phrase => phrase.Length);
        foreach (var phrase in phrases)
            normalized = RemoveWholePhrase(normalized, phrase);
        foreach (var ch in new[] { "؟", "?", "!", "،", ",", ":", ";", "(", ")", "«", "»", "." })
            normalized = normalized.Replace(ch, " ", StringComparison.Ordinal);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !BalanceSheetHintStopWords.Contains(token, StringComparer.OrdinalIgnoreCase) &&
                            !StopWords.Contains(token, StringComparer.OrdinalIgnoreCase) && !token.All(char.IsDigit))
            .ToArray();
        return tokens.Length == 0 ? null : string.Join(' ', tokens);
    }
}
