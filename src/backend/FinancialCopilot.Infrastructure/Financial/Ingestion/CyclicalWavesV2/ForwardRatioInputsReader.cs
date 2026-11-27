using FinancialCopilot.Application.FinancialData.ValuationGauges;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// Read-only estimator inputs for Sapio's forward P/E and P/S (change «valuation-gauge-forward-ratio»). It translates
/// rows to the plain input records and decides nothing:
/// <list type="bullet">
/// <item>the vendor ticker is matched exactly as the gauge snapshot reader matches it;</item>
/// <item>the last 8 stored income statements give each quarter's discrete revenue and net profit (Feature 148 keys);</item>
/// <item>the ticker's ISIN maps to exactly one company by <c>Companies.SymbolIsin</c>, whose industry external id and
/// last 16 months of accepted monthly activity reports (the month itself: output type 0 or none, summed over line items,
/// converted from million rials to rials) are read.</item>
/// </list>
/// Zero or several matches return null. Every query is bounded.
/// </summary>
public sealed class ForwardRatioInputsReader(FinancialIngestionDbContext db) : IForwardRatioInputsReader
{
    private const int MonthCount = 16;
    private const int QuarterCount = 8;
    private const int MonthItselfOutputType = 0;

    private static readonly int[] LineKeys = [.. CyclicalWavesV2IncomeStatementLines.RevenueInPriority, CyclicalWavesV2IncomeStatementLines.NetIncome];

    public async Task<ForwardRatioInputs?> ReadAsync(ValuationGaugeSecurity security, string vendorTicker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(security);
        var (match, ticker) = await ValuationGaugeTickerMatcher.MatchAsync(db, security, cancellationToken);
        if (match != ValuationGaugeMatchOutcome.Matched || ticker is null || !string.Equals(ticker.Ticker, vendorTicker, StringComparison.Ordinal))
            return null;

        var isin = ValuationGaugeSnapshotReader.NormalizeIsin(ticker.Enticker);
        if (isin.Length == 0)
            return null;
        var companies = await db.Companies.AsNoTracking()
            .Where(row => row.SymbolIsin != null && row.SymbolIsin.Trim().ToUpper() == isin)
            .Select(row => new { row.ProviderName, row.ExternalCompanyId, row.IndustryId })
            .Take(2)
            .ToListAsync(cancellationToken);
        if (companies.Count != 1)
            return null;
        var company = companies[0];

        var industry = company.IndustryId is { } industryId
            ? await db.Industries.AsNoTracking()
                .Where(row => row.Id == industryId)
                .Select(row => row.ExternalId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new ForwardRatioInputs(
            await ReadMonthlySalesAsync(company.ProviderName, company.ExternalCompanyId, cancellationToken),
            await ReadQuartersAsync(ticker.Id, cancellationToken),
            industry);
    }

    private async Task<IReadOnlyList<ForwardMonthlySales>> ReadMonthlySalesAsync(
        string providerName,
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var months = await (
                from report in db.MonthlyReports.AsNoTracking()
                where report.ProviderName == providerName &&
                      report.ExternalCompanyId == externalCompanyId &&
                      report.IsAccepted &&
                      (report.OutputType == MonthItselfOutputType || report.OutputType == null)
                join line in db.MonthlyReportLineItems.AsNoTracking() on report.Id equals line.MonthlyReportId
                group line.SalesAmount by report.PeriodEnd
                into month
                orderby month.Key descending
                select new { MonthEnd = month.Key, Sales = month.Sum() })
            .Take(MonthCount)
            .ToListAsync(cancellationToken);
        var millionRials = MonetaryAmountUnits.ScaleFactor(MonetaryScale.Million);
        return months
            .OrderBy(month => month.MonthEnd)
            .Select(month => new ForwardMonthlySales(month.MonthEnd, (month.Sales ?? 0m) * millionRials))
            .ToArray();
    }

    private async Task<IReadOnlyList<ForwardQuarter>> ReadQuartersAsync(Guid tickerId, CancellationToken cancellationToken)
    {
        var statements = await db.CyclicalWavesV2IncomeStatements.AsNoTracking()
            .Where(row => row.TickerId == tickerId)
            .OrderByDescending(row => row.PeriodEndingDate)
            .ThenByDescending(row => row.FiscalMonth)
            .Select(row => new { row.Id, row.PeriodEndingDate })
            .Take(QuarterCount)
            .ToListAsync(cancellationToken);
        var ids = statements.Select(row => row.Id).ToArray();
        var values = await db.CyclicalWavesV2IncomeStatementValues.AsNoTracking()
            .Where(row => ids.Contains(row.StatementId) && LineKeys.Contains(row.LineDefinitionId))
            .Select(row => new { row.StatementId, row.LineDefinitionId, row.Quarter })
            .ToListAsync(cancellationToken);
        var byStatement = values.ToLookup(row => row.StatementId);

        return statements
            .OrderBy(row => row.PeriodEndingDate)
            .Select(statement =>
            {
                var lines = byStatement[statement.Id].ToDictionary(row => row.LineDefinitionId, row => row.Quarter);
                var revenue = CyclicalWavesV2IncomeStatementLines.RevenueInPriority
                    .Select(key => lines.GetValueOrDefault(key))
                    .FirstOrDefault(value => value is not null);
                return new ForwardQuarter(statement.PeriodEndingDate, revenue, lines.GetValueOrDefault(CyclicalWavesV2IncomeStatementLines.NetIncome));
            })
            .ToArray();
    }
}
