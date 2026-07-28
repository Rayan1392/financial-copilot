using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Deterministic routing for explicit requests to list published disclosures.</summary>
public static class DisclosureListingIntentRules
{
    private static readonly string[] ListingCues = ["فهرست", "لیست", "اطلاعیه های منتشر شده", "اطلاعیه‌های منتشر شده", "گزارش های منتشر شده", "گزارش‌های منتشر شده", "list disclosures", "list reports", "published disclosures", "published reports"];
    private static readonly string[] MonthlyAliases = ["تولید و فروش", "گزارش ماهانه", "فروش ماهانه", "monthly production", "monthly sales"];
    private static readonly string[] IncomeAliases = ["صورت سود و زیان", "سود و زیان", "income statement"];
    private static readonly string[] BalanceAliases = ["ترازنامه", "صورت وضعیت مالی", "balance sheet"];
    private static readonly string[] CashFlowAliases = ["جریان وجه نقد", "جریان وجوه نقد", "cash flow"];
    private static readonly string[] FinancialStatementsAliases = ["صورت های مالی", "صورت‌های مالی", "financial statements"];

    public static bool LooksLikeDisclosureListingQuery(string? query) =>
        !string.IsNullOrWhiteSpace(query) && ContainsAny(Normalize(query), ListingCues) && ResolveTypes(Normalize(query)).Count > 0;

    public static DisclosureListingQuery BuildQuery(string userMessage, DateTimeOffset now, int page = 1, int pageSize = 20)
    {
        var normalized = Normalize(userMessage);
        var today = DateOnly.FromDateTime(now.LocalDateTime.Date);
        DateOnly? publishedFrom = ContainsAny(normalized, "امروز", "today") ? today : ContainsAny(normalized, "این هفته", "this week") ? today.AddDays(-6) : null;
        var scope = ContainsAny(normalized, "غیر تلفیقی", "غیرتلفیقی", "standalone", "parent") ? DisclosureConsolidationScope.NonConsolidated : ContainsAny(normalized, "تلفیقی", "consolidated", "گروه") ? DisclosureConsolidationScope.Consolidated : DisclosureConsolidationScope.NonConsolidated;
        return new DisclosureListingQuery(Types: ResolveTypes(normalized), SymbolOrCompany: ExtractCompanyHint(normalized), PublishedFrom: publishedFrom, ConsolidationScope: scope, Page: Math.Max(1, page), PageSize: Math.Clamp(pageSize, 1, 100));
    }

    private static IReadOnlyCollection<CompanyDisclosureType> ResolveTypes(string text)
    {
        var types = new List<CompanyDisclosureType>();
        if (ContainsAny(text, MonthlyAliases)) types.Add(CompanyDisclosureType.MonthlyProductionSales);
        if (ContainsAny(text, FinancialStatementsAliases)) types.AddRange([CompanyDisclosureType.IncomeStatement, CompanyDisclosureType.BalanceSheet, CompanyDisclosureType.CashFlowStatement]);
        else { if (ContainsAny(text, IncomeAliases)) types.Add(CompanyDisclosureType.IncomeStatement); if (ContainsAny(text, BalanceAliases)) types.Add(CompanyDisclosureType.BalanceSheet); if (ContainsAny(text, CashFlowAliases)) types.Add(CompanyDisclosureType.CashFlowStatement); }
        return types.Distinct().ToArray();
    }

    private static string? ExtractCompanyHint(string text)
    {
        foreach (var phrase in ListingCues.Concat(MonthlyAliases).Concat(IncomeAliases).Concat(BalanceAliases).Concat(CashFlowAliases).Concat(FinancialStatementsAliases).OrderByDescending(value => value.Length)) text = text.Replace(Normalize(phrase), " ", StringComparison.OrdinalIgnoreCase);
        foreach (var phrase in new[] { "آخرین", "جدیدترین", "منتشر شده", "منتشرشده", "را", "بده", "نمایش بده", "برای", "شرکت", "نماد", "تلفیقی", "غیر تلفیقی", "غیرتلفیقی", "امروز", "این هفته" }) text = text.Replace(phrase, " ", StringComparison.OrdinalIgnoreCase);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length is >= 2 and <= 80).ToArray();
        return words.Length == 0 ? null : string.Join(' ', words);
    }

    private static bool ContainsAny(string value, params string[] candidates) => candidates.Any(candidate => value.Contains(Normalize(candidate), StringComparison.OrdinalIgnoreCase));
    private static bool ContainsAny(string value, IEnumerable<string> candidates) => candidates.Any(candidate => value.Contains(Normalize(candidate), StringComparison.OrdinalIgnoreCase));
    private static string Normalize(string text) => text.Trim().Replace('ك', 'ک').Replace('ي', 'ی').Replace('\u200c', ' ').ToLowerInvariant();
}
