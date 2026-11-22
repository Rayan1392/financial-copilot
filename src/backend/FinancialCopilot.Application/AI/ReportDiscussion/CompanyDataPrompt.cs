using System.Globalization;
using System.Text;
using FinancialCopilot.Application.AI.GroundedAnalysis;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

/// <summary>What the budget kept: the sent sections, the sections not sent with their reason, and the step reached (FR-12).</summary>
public sealed record CompanyDataBudgetFacts(
    IReadOnlyList<string> SectionsSent,
    IReadOnlyList<CompanyDataGap> NotAvailable,
    int DegradationStep,
    bool ShortHistory,
    IReadOnlyList<CompanyDataSectionRead> Reads);

/// <summary>
/// Change «local-company-data-context» (FR-5, FR-8 … FR-10): renders the company data pack as one
/// <c>&lt;company_data&gt;</c> block and fits it into the profile's budget by a degradation order instead of a refusal
/// (BUG-042). Values, units and labels are copied unchanged; only the period labels of a fact line become short keys.
/// </summary>
public static class CompanyDataPrompt
{
    /// <summary>Periods kept per supporting statement at step 2.</summary>
    public const int SupportingStatementPeriods = 2;
    /// <summary>Months kept in the supporting monthly section at step 2.</summary>
    public const int SupportingMonthlyPeriods = 12;

    /// <summary>The order supporting sections are left out at step 4 (the first is dropped first).</summary>
    private static readonly CompanyDataSectionKind[] DropOrder =
    [
        CompanyDataSectionKind.CashFlow,
        CompanyDataSectionKind.BalanceSheet,
        CompanyDataSectionKind.MonthlyActivity,
        CompanyDataSectionKind.IncomeStatement
    ];

    public sealed record Fitted(string DataBlock, ReportDataSet Grounding, CompanyDataBudgetFacts Facts, string UserContent, int EstimatedTokens);

    /// <summary>
    /// The first state of the degradation order whose estimate fits <paramref name="target"/>; when none does, the
    /// smallest state if it fits <paramref name="ceiling"/>; otherwise null (FR-8, FR-9).
    /// </summary>
    /// <param name="userContent">Builds the whole user content from the data block and the short-history switch.</param>
    public static Fitted? Fit(
        CompanyDataPack pack,
        Func<string, bool, string> userContent,
        Func<string, int> estimate,
        int systemTokens,
        int target,
        int ceiling)
    {
        Fitted? last = null;
        foreach (var (sections, gaps, step, shortHistory) in States(pack))
        {
            var displayed = CompanyDataPackEvidence.Renumber(sections
                .Select(section => section with { Data = ReportDisplayUnits.Apply(section.Data) }).ToArray());
            var block = Render(displayed, gaps);
            var content = userContent(block, shortHistory);
            var tokens = systemTokens + estimate(content);
            last = new Fitted(block, CompanyDataPackEvidence.Union(displayed),
                new CompanyDataBudgetFacts(displayed.Select(section => section.Kind.ToString()).ToArray(), gaps, step, shortHistory, pack.Reads),
                content, tokens);
            if (tokens <= target) return last;
        }

        return last is not null && last.EstimatedTokens <= ceiling ? last : null;
    }

    private static IEnumerable<(IReadOnlyList<CompanyDataSection> Sections, IReadOnlyList<CompanyDataGap> Gaps, int Step, bool ShortHistory)> States(
        CompanyDataPack pack)
    {
        var primary = pack.Primary;
        var supporting = pack.Sections.Skip(1).ToList();
        var gaps = pack.NotAvailable.ToList();

        // 1. Everything.
        yield return ([primary, .. supporting], gaps, 1, false);

        // 2. Supporting sections cut to their newest periods.
        supporting = supporting.Select(Trim).ToList();
        yield return ([primary, .. supporting], gaps, 2, false);

        // 3. Only the last two messages of the history.
        yield return ([primary, .. supporting], gaps, 3, true);

        // 4. Supporting sections left out, one at a time.
        foreach (var kind in DropOrder)
        {
            if (supporting.RemoveAll(section => section.Kind == kind) == 0) continue;
            gaps = [.. gaps, new CompanyDataGap(kind, CompanyDataGapReasons.Trimmed)];
            yield return ([primary, .. supporting], gaps, 4, true);
        }

        // 5. The primary section, oldest periods first.
        for (var periods = primary.Data.PeriodsNewestFirst.Count - 1; periods >= 1; periods--)
        {
            var kept = primary with { Data = primary.Data.KeepNewestPeriods(periods) };
            if (kept.Data.Facts.Count == 0) yield break;
            yield return ([kept, .. supporting], gaps, 5, true);
        }
    }

    private static CompanyDataSection Trim(CompanyDataSection section)
    {
        var periods = section.Kind == CompanyDataSectionKind.MonthlyActivity ? SupportingMonthlyPeriods : SupportingStatementPeriods;
        return section.Data.PeriodsNewestFirst.Count > periods
            ? section with { Data = section.Data.KeepNewestPeriods(periods) }
            : section;
    }

    /// <summary>The delimited data block (FR-5, FR-10). Labels are untrusted source text and are cleaned.</summary>
    public static string Render(IReadOnlyList<CompanyDataSection> sections, IReadOnlyList<CompanyDataGap> notAvailable)
    {
        var builder = new StringBuilder("<company_data>\n");
        var first = sections[0].Data;
        builder.Append("company: ").Append(Clean(first.CompanySymbol));
        if (!string.IsNullOrWhiteSpace(first.CompanyName)) builder.Append(" — ").Append(Clean(first.CompanyName));
        builder.Append('\n');

        foreach (var section in sections)
        {
            var data = section.Data;
            var keys = data.PeriodsNewestFirst.Select((label, index) => (label, key: $"P{index}"))
                .ToDictionary(item => item.label, item => item.key, StringComparer.Ordinal);
            builder.Append("<section kind=\"").Append(section.Kind).Append("\" title=\"")
                .Append(ReportDataSetBuilder.FamilyLabel(data.Family)).Append('"');
            if (section.Primary) builder.Append(" primary=\"true\"");
            builder.Append(">\n");
            builder.Append("scope: ").Append(Clean(data.Scope)).Append('\n');
            builder.Append("periods (newest first): ")
                .Append(string.Join("; ", data.PeriodsNewestFirst.Select(label => $"{keys[label]} = {Clean(label)}"))).Append('\n');
            builder.Append("<facts>\nid | label | period | value | unit\n");
            foreach (var fact in data.Facts)
            {
                builder.Append(fact.EvidenceId).Append(" | ").Append(Clean(fact.Label)).Append(" | ")
                    .Append(Key(keys, fact.Period)).Append(" | ").Append(Format(fact.Value)).Append(" | ")
                    .Append(ReportDataSetBuilder.UnitLabel(fact.Unit)).Append('\n');
            }
            builder.Append("</facts>\n");
            if (data.Calculations.Count > 0)
            {
                builder.Append("<calculations>\nid | label | period | value | unit | formula | operands\n");
                foreach (var calculation in data.Calculations)
                {
                    builder.Append(calculation.EvidenceId).Append(" | ").Append(Clean(calculation.Label)).Append(" | ")
                        .Append(Key(keys, calculation.Period)).Append(" | ").Append(Format(calculation.Value)).Append(" | ")
                        .Append(ReportDataSetBuilder.UnitLabel(calculation.Unit)).Append(" | ")
                        .Append(Clean(WithKeys(calculation.Formula, keys))).Append(" | ")
                        .Append(string.Join(',', calculation.OperandEvidenceIds)).Append('\n');
                }
                builder.Append("</calculations>\n");
            }
            builder.Append("</section>\n");
        }

        if (notAvailable.Count > 0)
        {
            builder.Append("<not_available>\n");
            foreach (var gap in notAvailable)
                builder.Append(gap.Kind).Append(": ").Append(gap.Reason).Append('\n');
            builder.Append("</not_available>\n");
        }

        builder.Append("</company_data>\n");
        return builder.ToString();
    }

    private static string Key(IReadOnlyDictionary<string, string> keys, string period) =>
        keys.TryGetValue(period, out var key) ? key : Clean(period);

    /// <summary>Period labels inside a formula become their keys, the longest label first so no label is cut.</summary>
    private static string WithKeys(string formula, IReadOnlyDictionary<string, string> keys)
    {
        foreach (var (label, key) in keys.OrderByDescending(item => item.Key.Length))
            formula = formula.Replace(label, key, StringComparison.Ordinal);
        return formula;
    }

    private static string Clean(string text) =>
        text.ReplaceLineEndings(" ").Replace("|", "/", StringComparison.Ordinal)
            .Replace("<", "‹", StringComparison.Ordinal).Replace(">", "›", StringComparison.Ordinal)
            .Replace("\"", "'", StringComparison.Ordinal);

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
