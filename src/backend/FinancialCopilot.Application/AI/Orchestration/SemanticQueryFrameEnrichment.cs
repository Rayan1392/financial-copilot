using System.Globalization;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Materializes route-specific optional parameters into governed frame slots before execution.
/// Legacy intent rules may still recognize parameters during migration, but executors consume only
/// the validated frame and never perform route-local raw-text extraction.
/// </summary>
public interface ISemanticQueryFrameEnricher
{
    IReadOnlyCollection<ResolvedQuerySlot> Enrich(
        string capabilityCode,
        QueryInterpretation interpretation,
        IReadOnlyCollection<ResolvedQuerySlot> slots,
        DateTimeOffset now);
}

public sealed class SemanticQueryFrameEnricher : ISemanticQueryFrameEnricher
{
    public IReadOnlyCollection<ResolvedQuerySlot> Enrich(
        string capabilityCode,
        QueryInterpretation interpretation,
        IReadOnlyCollection<ResolvedQuerySlot> slots,
        DateTimeOffset now)
    {
        var enriched = slots.ToDictionary(slot => slot.Type);
        switch (capabilityCode)
        {
            case "financial_statement_value_search":
                AddValueSearch(enriched, capabilityCode, interpretation.OriginalText);
                break;
            case "comprehensive_analysis":
                AddComprehensiveAnalysis(enriched, capabilityCode, interpretation.OriginalText, now);
                break;
            case "financial_statement_table":
                AddStatementTable(enriched, capabilityCode,
                    FinancialStatementTableIntentRules.BuildQuery(interpretation.OriginalText));
                break;
            case "financial_statement_period_analysis":
                AddStatementAnalysis(enriched, capabilityCode,
                    FinancialStatementAnalysisIntentRules.BuildQuery(interpretation.OriginalText));
                break;
            case "disclosure_listing":
                AddDisclosure(enriched, capabilityCode,
                    DisclosureListingIntentRules.BuildQuery(interpretation.OriginalText, now));
                break;
            case "monthly_sales_quality_ranking":
                AddMonthlyQuality(enriched, capabilityCode,
                    MonthlySalesQualityRankingIntentRules.BuildQuery(interpretation.OriginalText));
                break;
        }

        return enriched.Values.OrderBy(slot => slot.Type).ToArray();
    }

    private static void AddValueSearch(IDictionary<QuerySlotType, ResolvedQuerySlot> slots, string capabilityCode, string message)
    {
        if (!QueryNormalization.TryParseFinancialStatementClues(message, out var clues, out var error))
        {
            slots[QuerySlotType.NumericClues] = new(QuerySlotType.NumericClues, null, QueryValueProvenance.UserExplicit, 0m,
                error == "numeric_clue_required" ? QuerySlotValidationState.Missing : QuerySlotValidationState.Invalid, capabilityCode, error);
            return;
        }
        Add(slots, capabilityCode, QuerySlotType.NumericClues, string.Join(',', clues.Select(clue => clue.Value.ToString(CultureInfo.InvariantCulture))));
        Add(slots, capabilityCode, QuerySlotType.StatementType, FinancialStatementType.IncomeStatement.ToString(), QueryValueProvenance.PolicyDefaulted);
    }

    private static void AddComprehensiveAnalysis(
        IDictionary<QuerySlotType, ResolvedQuerySlot> slots,
        string capabilityCode,
        string message,
        DateTimeOffset now)
    {
        var normalized = QueryNormalization.Normalize(message);
        var topics = new List<string>();
        AddTopic(topics, normalized, "تحلیل_تکنیکال", "تحلیل تکنیکال", "technical analysis");
        AddTopic(topics, normalized, "قیمت_تعادلی", "قیمت تعادلی", "equilibrium price");
        AddTopic(topics, normalized, "رصد_معاملات_عمده", "رصد معاملات عمده", "معاملات عمده", "suspicious volume", "block trades");
        AddTopic(topics, normalized, "گزارش_فصلی", "گزارش فصلی", "quarterly report");
        AddTopic(topics, normalized, "گزارش_ماهانه", "گزارش ماهانه", "monthly report");
        AddTopic(topics, normalized, "نمودار_P_S", "نمودار p s", "p s chart");
        AddTopic(topics, normalized, "نمودار_P_E", "نمودار p e", "p e chart");
        Add(slots, capabilityCode, QuerySlotType.AnalysisTopic,
            topics.Count == 0 ? null : string.Join(',', topics));

        var fromDate = ResolveAnalysisFromDate(normalized, now);
        Add(slots, capabilityCode, QuerySlotType.Period,
            fromDate?.ToString("O", CultureInfo.InvariantCulture));

        var limitMatch = Regex.Match(normalized,
            @"(?:(?:آخرین|last|limit)\s+([1-5])|([1-5])\s+(?:تحلیل|گزارش|نتیجه|analysis|reports?|results?))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var limitText = limitMatch.Success
            ? limitMatch.Groups.Cast<Group>().Skip(1).FirstOrDefault(group => group.Success)?.Value
            : null;
        Add(slots, capabilityCode, QuerySlotType.ResultLimit, limitText ?? "3",
            limitText is null ? QueryValueProvenance.PolicyDefaulted : QueryValueProvenance.UserExplicit);
    }

    private static void AddTopic(ICollection<string> topics, string normalized, string slug, params string[] phrases)
    {
        if (phrases.Any(phrase => normalized.Contains(QueryNormalization.Normalize(phrase), StringComparison.OrdinalIgnoreCase)))
            topics.Add(slug);
    }

    private static DateTimeOffset? ResolveAnalysisFromDate(string normalized, DateTimeOffset now)
    {
        var iso = Regex.Match(normalized, @"(?<!\d)(\d{4})\s+(\d{1,2})\s+(\d{1,2})(?!\d)", RegexOptions.CultureInvariant);
        if (iso.Success &&
            int.TryParse(iso.Groups[1].Value, out var year) &&
            int.TryParse(iso.Groups[2].Value, out var month) &&
            int.TryParse(iso.Groups[3].Value, out var day))
        {
            try { return new DateTimeOffset(year, month, day, 0, 0, 0, now.Offset); }
            catch (ArgumentOutOfRangeException) { }
        }

        if (ContainsAny(normalized, "دیروز", "yesterday")) return now.AddDays(-1);
        if (ContainsAny(normalized, "هفته قبل", "هفته گذشته", "last week"))
            return now.AddDays(-(int)now.DayOfWeek - 7);
        if (ContainsAny(normalized, "این هفته", "هفته جاری", "this week"))
            return now.AddDays(-(int)now.DayOfWeek);
        if (ContainsAny(normalized, "ماه قبل", "ماه گذشته", "last month"))
            return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddMonths(-1);
        if (ContainsAny(normalized, "این ماه", "ماه جاری", "this month"))
            return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        return null;
    }

    private static bool ContainsAny(string normalized, params string[] phrases) =>
        phrases.Any(phrase => normalized.Contains(QueryNormalization.Normalize(phrase), StringComparison.OrdinalIgnoreCase));

    private static void AddStatementTable(
        IDictionary<QuerySlotType, ResolvedQuerySlot> slots,
        string capabilityCode,
        FinancialStatementTableQuery query)
    {
        Add(slots, capabilityCode, QuerySlotType.StatementType, query.StatementType?.ToString());
        Add(slots, capabilityCode, QuerySlotType.Period,
            query.PeriodMonths?.ToString(CultureInfo.InvariantCulture));
        Add(slots, capabilityCode, QuerySlotType.AuditStatus, Boolean(query.IsAudited));
        Add(slots, capabilityCode, QuerySlotType.RestatementStatus, Boolean(query.IsRepresented));
        Add(slots, capabilityCode, QuerySlotType.ConsolidationScope, Boolean(query.IsComposing));
    }

    private static void AddStatementAnalysis(
        IDictionary<QuerySlotType, ResolvedQuerySlot> slots,
        string capabilityCode,
        FinancialStatementAnalysisQuery query)
    {
        Add(slots, capabilityCode, QuerySlotType.StatementType, query.StatementTypeFocus?.ToString());
        Add(slots, capabilityCode, QuerySlotType.Period,
            query.PeriodMonths?.ToString(CultureInfo.InvariantCulture));
        Add(slots, capabilityCode, QuerySlotType.AuditStatus, Boolean(query.IsAuditedPreference));
        Add(slots, capabilityCode, QuerySlotType.ConsolidationScope,
            query.VariantPreference.ToString(),
            query.VariantPreference == FinancialStatementVariantPreference.DefaultNonConsolidated
                ? QueryValueProvenance.PolicyDefaulted
                : QueryValueProvenance.UserExplicit);
        Add(slots, capabilityCode, QuerySlotType.MetricSet,
            query.MetricFocusCodes is { Count: > 0 } ? string.Join(',', query.MetricFocusCodes) : null);
    }

    private static void AddDisclosure(
        IDictionary<QuerySlotType, ResolvedQuerySlot> slots,
        string capabilityCode,
        DisclosureListingQuery query)
    {
        Add(slots, capabilityCode, QuerySlotType.DisclosureTypes,
            query.Types is { Count: > 0 } ? string.Join(',', query.Types) : null);
        Add(slots, capabilityCode, QuerySlotType.PublishedFrom,
            query.PublishedFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(slots, capabilityCode, QuerySlotType.PublishedTo,
            query.PublishedTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(slots, capabilityCode, QuerySlotType.ConsolidationScope,
            query.ConsolidationScope.ToString(),
            query.ConsolidationScope == DisclosureConsolidationScope.NonConsolidated
                ? QueryValueProvenance.PolicyDefaulted
                : QueryValueProvenance.UserExplicit);
    }

    private static void AddMonthlyQuality(
        IDictionary<QuerySlotType, ResolvedQuerySlot> slots,
        string capabilityCode,
        MonthlySalesQualityRankingQuery query)
    {
        Add(slots, capabilityCode, QuerySlotType.Industry, query.IndustryTitle);
        Add(slots, capabilityCode, QuerySlotType.Sort, query.Direction.ToString(),
            query.Direction == MonthlySalesQualityDirection.Top
                ? QueryValueProvenance.PolicyDefaulted
                : QueryValueProvenance.UserExplicit);
        Add(slots, capabilityCode, QuerySlotType.ResultLimit,
            query.Limit.ToString(CultureInfo.InvariantCulture),
            query.Limit == 10 ? QueryValueProvenance.PolicyDefaulted : QueryValueProvenance.UserExplicit);
    }

    private static void Add(
        IDictionary<QuerySlotType, ResolvedQuerySlot> slots,
        string capabilityCode,
        QuerySlotType type,
        string? value,
        QueryValueProvenance provenance = QueryValueProvenance.UserExplicit)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        slots[type] = new ResolvedQuerySlot(
            type,
            value,
            provenance,
            1m,
            QuerySlotValidationState.Valid,
            capabilityCode);
    }

    private static string? Boolean(bool? value) => value?.ToString(CultureInfo.InvariantCulture);
}
