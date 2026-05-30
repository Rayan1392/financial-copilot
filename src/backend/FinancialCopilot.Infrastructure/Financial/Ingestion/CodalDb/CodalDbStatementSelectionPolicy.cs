using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Selects one canonical statement variant per <c>(PeriodEnd.Date, PeriodType)</c> group from the
/// list of CodalDB statement rows for a single company. Priority order:
/// <list type="number">
///   <item>IsAudited = true over unaudited/null.</item>
///   <item>IsRepresented = true (latest revision) over earlier revisions.</item>
///   <item>Consolidated (IsComposing = true) or parent by configuration; default: consolidated.</item>
///   <item>Lowest StmtId as a deterministic tie-break.</item>
/// </list>
/// Soft-deleted rows (<c>isDeleted = 1</c>) are already excluded by the SQL query executor.
/// </summary>
public static class CodalDbStatementSelectionPolicy
{
    /// <summary>
    /// Groups <paramref name="rows"/> by <c>(PeriodEnd.Date, PeriodType)</c> and returns the canonical
    /// variant per group. Order within the returned list is stable (ordered by PeriodEnd, then PeriodType).
    /// </summary>
    public static IReadOnlyList<CodalStatementRow> SelectAll(
        IReadOnlyList<CodalStatementRow> rows,
        bool preferConsolidated = true) =>
        rows
            .GroupBy(row => (row.PeriodEnd.Date, row.PeriodType))
            .OrderBy(group => group.Key.Date)
            .ThenBy(group => group.Key.PeriodType)
            .Select(group => SelectFromGroup(group.ToList(), preferConsolidated))
            .ToList();

    private static CodalStatementRow SelectFromGroup(
        List<CodalStatementRow> variants,
        bool preferConsolidated) =>
        variants
            .OrderByDescending(row => row.IsAudited == true ? 1 : 0)
            .ThenByDescending(row => row.IsRepresented == true ? 1 : 0)
            .ThenByDescending(row =>
                preferConsolidated
                    ? (row.IsComposing == true ? 1 : 0)
                    : (row.IsComposing == true ? 0 : 1))
            .ThenBy(row => row.StmtId)
            .First();
}
