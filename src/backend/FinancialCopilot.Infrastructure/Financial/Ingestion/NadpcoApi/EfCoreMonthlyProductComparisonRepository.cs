using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class EfCoreMonthlyProductComparisonRepository(FinancialIngestionDbContext db) : IMonthlyProductComparisonReadRepository, IMonthlyProductCatalogReadRepository
{
    private static readonly PersianCalendar Calendar = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<ProductSalesObservation>> productCatalogCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<ProductSalesObservation>> catalogCache = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<ProductSalesObservation>> GetProductCatalogAsync(
        string externalCompanyId,
        CancellationToken ct = default)
    {
        if (productCatalogCache.TryGetValue(externalCompanyId, out var cached))
            return cached;

        var rows = await (from report in db.MonthlyReports.AsNoTracking()
                          join item in db.MonthlyReportLineItems.AsNoTracking() on report.Id equals item.MonthlyReportId
                          where report.ExternalCompanyId == externalCompanyId
                                && report.ReportType == "ProductSales" && report.OutputType == 0 && report.IsAccepted
                          select new
                          {
                              report.ProviderName,
                              report.ExternalCompanyId,
                              item.ProductCode,
                              item.Title,
                              item.Unit,
                              item.ProviderProductCode,
                              item.ProviderProductId
                          }).Distinct().ToListAsync(ct);

        var observations = rows.Select(row => new ProductSalesObservation(
            Guid.Empty,
            Guid.Empty,
            row.ExternalCompanyId,
            new JalaliPeriod(1, 1),
            row.ProviderName,
            string.Empty,
            DateOnly.MinValue,
            DateOnly.MinValue,
            row.ProductCode,
            row.Title,
            row.Unit,
            null,
            null,
            null,
            null,
            0,
            row.ProviderProductCode,
            row.ProviderProductId,
            null)).Select(row => row with
            {
                ProductKey = MonthlyProductTrendCalculator.ProductKey(externalCompanyId, row)
            }).ToArray();

        productCatalogCache.TryAdd(externalCompanyId, observations);
        return observations;
    }

    public async Task<IReadOnlyList<ProductSalesObservation>> GetAllProductSalesAsync(
        string externalCompanyId,
        CancellationToken ct = default)
    {
        if (catalogCache.TryGetValue(externalCompanyId, out var cached))
            return cached;

        var rows = await (from report in db.MonthlyReports.AsNoTracking()
                          join item in db.MonthlyReportLineItems.AsNoTracking() on report.Id equals item.MonthlyReportId
                          where report.ExternalCompanyId == externalCompanyId
                                && report.ReportType == "ProductSales" && report.OutputType == 0 && report.IsAccepted
                          select new { report, item }).ToListAsync(ct);

        var observations = rows.Select(entry => new ProductSalesObservation(
            entry.item.Id,
            entry.report.Id,
            entry.report.ExternalCompanyId,
            ToPeriod(entry.report.PeriodStart),
            entry.report.ProviderName,
            entry.report.ExternalReportId,
            entry.report.PeriodStart,
            entry.report.PeriodEnd,
            entry.item.ProductCode,
            entry.item.Title,
            entry.item.Unit,
            entry.item.ProductionQuantity,
            entry.item.SalesQuantity,
            entry.item.SalesRate,
            entry.item.SalesAmount,
            0,
            entry.item.ProviderProductCode,
            entry.item.ProviderProductId,
            null)).ToArray();

        catalogCache.TryAdd(externalCompanyId, observations);
        return observations;
    }

    public async Task<IReadOnlyList<JalaliPeriod>> GetAvailablePeriodsAsync(string externalCompanyId, CancellationToken ct = default)
    {
        var dates = await db.MonthlyReports.AsNoTracking()
            .Where(r => r.ExternalCompanyId == externalCompanyId && r.ReportType == "ProductSales" && r.OutputType == 0 && r.IsAccepted)
            .Select(r => r.PeriodStart).Distinct().ToListAsync(ct);
        return dates.Select(ToPeriod).Distinct().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToArray();
    }

    public async Task<MonthlyProductComparisonPeriod?> GetPeriodAsync(string externalCompanyId, JalaliPeriod period, CancellationToken ct = default)
    {
        var (start, end) = Resolve(period);
        var rows = await (from report in db.MonthlyReports.AsNoTracking()
                          join item in db.MonthlyReportLineItems.AsNoTracking() on report.Id equals item.MonthlyReportId
                          where report.ExternalCompanyId == externalCompanyId
                                && report.ReportType == "ProductSales" && report.OutputType == 0 && report.IsAccepted
                                && report.PeriodStart == start && report.PeriodEnd == end
                          select new ProductSalesObservation(
                              item.Id, report.Id, report.ExternalCompanyId, period,
                              report.ProviderName, report.ExternalReportId, report.PeriodStart, report.PeriodEnd,
                              item.ProductCode, item.Title, item.Unit, item.ProductionQuantity,
                              item.SalesQuantity, item.SalesRate, item.SalesAmount, 0,
                              item.ProviderProductCode, item.ProviderProductId, null)).ToListAsync(ct);
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
