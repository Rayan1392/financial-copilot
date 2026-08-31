using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class EfCoreMonthlyProductComparisonRepository(FinancialIngestionDbContext db) : IMonthlyProductComparisonReadRepository
{
    private static readonly PersianCalendar Calendar = new();

    public async Task<IReadOnlyList<JalaliPeriod>> GetAvailablePeriodsAsync(string externalCompanyId, CancellationToken ct = default)
    {
        var dates = await db.MonthlyReports.AsNoTracking()
            .Where(r => r.ExternalCompanyId == externalCompanyId && r.ReportType == "ProductSales" && r.OutputType == 0)
            .Select(r => r.PeriodStart).Distinct().ToListAsync(ct);
        return dates.Select(ToPeriod).Distinct().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToArray();
    }

    public async Task<MonthlyProductComparisonPeriod?> GetPeriodAsync(string externalCompanyId, JalaliPeriod period, CancellationToken ct = default)
    {
        var (start, end) = Resolve(period);
        var rows = await (from report in db.MonthlyReports.AsNoTracking()
                          join item in db.MonthlyReportLineItems.AsNoTracking() on report.Id equals item.MonthlyReportId
                          where report.ExternalCompanyId == externalCompanyId
                                && report.ReportType == "ProductSales" && report.OutputType == 0
                                && report.PeriodStart == start && report.PeriodEnd == end
                          select new ProductSalesObservation(
                              item.Id, report.Id, report.ExternalCompanyId, period,
                              report.ProviderName, report.ExternalReportId, report.PeriodStart, report.PeriodEnd,
                              item.ProductCode, item.Title, item.Unit, item.ProductionQuantity,
                              item.SalesQuantity, item.SalesRate, item.SalesAmount, 0)).ToListAsync(ct);
        if (rows.Count == 0) return null;
        return new MonthlyProductComparisonPeriod(period, rows, rows.Select(x => new MonthlyProductComparisonEvidence(x.ReportId, x.RowId, x.ProviderName, x.ExternalReportId, period)).ToArray());
    }

    private static JalaliPeriod ToPeriod(DateOnly value) => new(Calendar.GetYear(value.ToDateTime(TimeOnly.MinValue)), Calendar.GetMonth(value.ToDateTime(TimeOnly.MinValue)));
    private static (DateOnly Start, DateOnly End) Resolve(JalaliPeriod period)
    {
        var start = DateOnly.FromDateTime(Calendar.ToDateTime(period.Year, period.Month, 1, 0, 0, 0, 0));
        var end = DateOnly.FromDateTime(Calendar.ToDateTime(period.Year, period.Month, Calendar.GetDaysInMonth(period.Year, period.Month), 0, 0, 0, 0));
        return (start, end);
    }
}
