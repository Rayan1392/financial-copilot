using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed record NadpcoApiTypedStatement(
    FinancialStatementType StatementType,
    NadpcoApiStatementRecord Record);

public static class NadpcoApiStatementSelectionPolicy
{
    public static IReadOnlyList<NadpcoApiTypedStatement> SelectAll(
        IEnumerable<NadpcoApiTypedStatement> statements,
        bool preferComposing = true)
    {
        ArgumentNullException.ThrowIfNull(statements);

        return statements
            .GroupBy(item => new
            {
                item.StatementType,
                item.Record.ComID,
                item.Record.PeriodType,
                PeriodEnd = item.Record.PeriodEnd.Date
            })
            .Select(group => group
                .OrderByDescending(item => item.Record.IsAudited)
                .ThenBy(item => item.Record.IsRepresented)
                .ThenByDescending(item => preferComposing ? item.Record.IsComposing : !item.Record.IsComposing)
                .ThenByDescending(item => item.Record.AnouncementDate)
                .ThenByDescending(item => item.Record.StatementID)
                .First())
            .OrderBy(item => item.StatementType)
            .ThenBy(item => item.Record.ComID)
            .ThenBy(item => item.Record.PeriodEnd)
            .ToArray();
    }
}
