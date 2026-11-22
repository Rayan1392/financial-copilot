using System.Diagnostics;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.GroundedAnalysis;

// Change «local-company-data-context» (162 and 163, Gate A and B approved by the owner on 2026-11-22): every grounded
// analysis of one company carries Sapio's own copy of its three financial statements and its monthly activity totals,
// exactly as the chat answers «صورت سود و زیان»، «ترازنامه»، «جریان وجوه نقد» and «روند تولید و فروش» return them, so the
// model does not search the web for them (FR-1 … FR-4).

/// <summary>The kinds of section a data pack can hold, in the order they are read and sent.</summary>
public enum CompanyDataSectionKind
{
    IncomeStatement,
    BalanceSheet,
    CashFlow,
    MonthlyActivity
}

/// <summary>Why a section is not in the pack (FR-2). Sent to the model and logged; never a value.</summary>
public static class CompanyDataGapReasons
{
    public const string Disabled = "disabled";
    public const string NoData = "no_data";
    public const string Timeout = "timeout";
    public const string Error = "error";
    public const string OtherCompany = "other_company";
    /// <summary>The overall read budget ran out before the section was read.</summary>
    public const string TimeBudget = "time_budget";
    /// <summary>Left out by the prompt budget (FR-8 step 4); set by the budget, not the loader.</summary>
    public const string Trimmed = "trimmed";
}

public sealed record CompanyDataSection(CompanyDataSectionKind Kind, ReportDataSet Data, bool Primary);

public sealed record CompanyDataGap(CompanyDataSectionKind Kind, string Reason);

/// <summary>Safe facts of one section read: kind, outcome and latency (FR-12).</summary>
public sealed record CompanyDataSectionRead(CompanyDataSectionKind Kind, string Outcome, long ElapsedMilliseconds);

/// <summary>
/// The sections of one company, the primary one (the report the question is about) first, and the sections that are
/// not available with their reason. Evidence ids are unique across sections only after
/// <see cref="CompanyDataPackEvidence.Renumber"/>.
/// </summary>
public sealed record CompanyDataPack(
    IReadOnlyList<CompanyDataSection> Sections,
    IReadOnlyList<CompanyDataGap> NotAvailable,
    IReadOnlyList<CompanyDataSectionRead> Reads)
{
    public CompanyDataSection Primary => Sections[0];

    /// <summary>A pack with only the primary report: the rollback shape (FR-13) and the shape before this change.</summary>
    public static CompanyDataPack PrimaryOnly(ReportDataSet primary) => new([new(KindOf(primary.Family), primary, true)], [], []);

    public IReadOnlyList<string> SectionNames => Sections.Select(section => section.Kind.ToString()).ToArray();

    public static CompanyDataSectionKind KindOf(ReportFamily family) => family switch
    {
        ReportFamily.BalanceSheet => CompanyDataSectionKind.BalanceSheet,
        ReportFamily.MonthlySales => CompanyDataSectionKind.MonthlyActivity,
        ReportFamily.CashFlowStatement => CompanyDataSectionKind.CashFlow,
        _ => CompanyDataSectionKind.IncomeStatement
    };
}

/// <summary>
/// Reads the supporting sections of one company through the existing chat query use cases (refinement R1), one after
/// another on the request's scope (BUG-035), each within its own time limit and all within an overall budget (FR-2). A
/// section that fails, times out, is disabled, is empty or belongs to another company is left out with its reason; the
/// request's own cancellation is never swallowed.
/// </summary>
public sealed class CompanyAnalysisDataPackLoader(
    IFinancialStatementTableQueryUseCase statementTables,
    IMonthlyActivityTrendQueryUseCase monthlyTrends,
    GroundedAnalysisOptions options,
    bool cashFlowEnabled)
{
    private static readonly CompanyDataSectionKind[] ReadOrder =
    [
        CompanyDataSectionKind.IncomeStatement,
        CompanyDataSectionKind.BalanceSheet,
        CompanyDataSectionKind.CashFlow,
        CompanyDataSectionKind.MonthlyActivity
    ];

    public bool Enabled => options.DataPack.Enabled;

    /// <param name="primary">The report the question is about: the one the user saw (162) or the statement 163 just read; it is not read again (FR-4).</param>
    public async Task<CompanyDataPack> LoadAsync(ReportDataSet primary, CancellationToken cancellationToken)
    {
        if (!Enabled) return CompanyDataPack.PrimaryOnly(primary);

        var primaryKind = CompanyDataPack.KindOf(primary.Family);
        var symbol = primary.CompanySymbol.Trim();
        var sections = new List<CompanyDataSection> { new(primaryKind, primary, true) };
        var gaps = new List<CompanyDataGap>();
        var reads = new List<CompanyDataSectionRead>();
        var overall = Stopwatch.StartNew();
        var total = TimeSpan.FromSeconds(options.DataPack.TotalTimeoutSeconds);
        var perSection = TimeSpan.FromSeconds(options.DataPack.SectionTimeoutSeconds);

        foreach (var kind in ReadOrder.Where(kind => kind != primaryKind))
        {
            if (kind == CompanyDataSectionKind.CashFlow && !cashFlowEnabled)
            {
                gaps.Add(new(kind, CompanyDataGapReasons.Disabled));
                continue;
            }

            var left = total - overall.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                gaps.Add(new(kind, CompanyDataGapReasons.TimeBudget));
                continue;
            }

            var started = overall.ElapsedMilliseconds;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(left < perSection ? left : perSection);
            string outcome;
            try
            {
                var data = await ReadAsync(kind, symbol, limit.Token);
                if (data is not { Facts.Count: > 0 })
                    outcome = CompanyDataGapReasons.NoData;
                else if (!SameCompany(data.CompanySymbol, symbol))
                    outcome = CompanyDataGapReasons.OtherCompany;
                else
                {
                    sections.Add(new(kind, data, false));
                    outcome = "sent";
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                outcome = CompanyDataGapReasons.Timeout;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A supporting section never fails the turn (FR-2); only the outcome code is kept, never the message.
                outcome = CompanyDataGapReasons.Error;
            }

            if (outcome != "sent") gaps.Add(new(kind, outcome));
            reads.Add(new(kind, outcome, overall.ElapsedMilliseconds - started));
        }

        return new CompanyDataPack(sections, gaps, reads);
    }

    private async Task<ReportDataSet?> ReadAsync(CompanyDataSectionKind kind, string symbol, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case CompanyDataSectionKind.IncomeStatement:
                return ReportDataSetBuilder.BuildQuarterly(await statementTables.ExecuteAsync(
                    FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(RelatedFollowUpTexts.QuarterlyReport(symbol)),
                    cancellationToken));
            case CompanyDataSectionKind.BalanceSheet:
                return ReportDataSetBuilder.BuildBalanceSheet((await statementTables.ExecuteAsync(
                    FinancialStatementTableIntentRules.BuildV2Query(RelatedFollowUpTexts.BalanceSheet(symbol)),
                    cancellationToken))?.BalanceSheetReport);
            case CompanyDataSectionKind.CashFlow:
                return ReportDataSetBuilder.BuildCashFlow((await statementTables.ExecuteAsync(
                    FinancialStatementTableIntentRules.BuildV2Query(RelatedFollowUpTexts.CashFlowStatement(symbol), cashFlowEnabled: true),
                    cancellationToken))?.CashFlowReport);
            case CompanyDataSectionKind.MonthlyActivity:
                return ReportDataSetBuilder.BuildMonthly(await monthlyTrends.ExecuteAsync(
                    new MonthlyActivityTrendQuery(RelatedFollowUpTexts.MonthlySalesTrend(symbol), symbol), cancellationToken));
            default:
                return null;
        }
    }

    private static bool SameCompany(string candidate, string symbol) =>
        RelatedFollowUpActionFactory.Canonical(candidate) == RelatedFollowUpActionFactory.Canonical(symbol);
}

/// <summary>
/// One F and one C counter across every section (refinement R2): ids stay in the existing <c>F12</c> / <c>C3</c> form,
/// so the evidence-id pattern and the JSON contracts do not change. Operand ids follow their facts.
/// </summary>
public static class CompanyDataPackEvidence
{
    public static IReadOnlyList<CompanyDataSection> Renumber(IReadOnlyList<CompanyDataSection> sections)
    {
        var facts = 0;
        var calculations = 0;
        var result = new List<CompanyDataSection>(sections.Count);
        foreach (var section in sections)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var newFacts = section.Data.Facts.Select(fact =>
            {
                var id = $"F{++facts}";
                map[fact.EvidenceId] = id;
                return fact with { EvidenceId = id };
            }).ToArray();
            var newCalculations = section.Data.Calculations.Select(calculation => calculation with
            {
                EvidenceId = $"C{++calculations}",
                OperandEvidenceIds = calculation.OperandEvidenceIds
                    .Select(operand => map.TryGetValue(operand, out var mapped) ? mapped : operand).ToArray()
            }).ToArray();
            result.Add(section with { Data = section.Data with { Facts = newFacts, Calculations = newCalculations } });
        }
        return result;
    }

    /// <summary>All sections as one data set: the numeric grounding check accepts a number from any section (FR-7).</summary>
    public static ReportDataSet Union(IReadOnlyList<CompanyDataSection> sections)
    {
        var primary = sections[0].Data;
        return primary with
        {
            PeriodsNewestFirst = sections.SelectMany(section => section.Data.PeriodsNewestFirst).Distinct(StringComparer.Ordinal).ToArray(),
            Facts = sections.SelectMany(section => section.Data.Facts).ToArray(),
            Calculations = sections.SelectMany(section => section.Data.Calculations).ToArray()
        };
    }
}
