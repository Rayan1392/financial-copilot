using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class EfCoreCompanyMonthlyActivityTrendSnapshotRepository(
    FinancialIngestionDbContext dbContext)
    : ICompanyMonthlyActivityTrendSnapshotRepository
{
    public async Task UpsertAsync(
        CompanyMonthlyActivityTrendSnapshotUpsertRow row,
        CancellationToken ct = default)
    {
        // Atomic delete-then-insert: one row per company/month.
        var existing = await dbContext.CompanyMonthlyActivityTrendSnapshots
            .Where(r => r.ExternalCompanyId == row.ExternalCompanyId
                     && r.ReportYear == row.ReportYear
                     && r.ReportMonth == row.ReportMonth)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
            dbContext.CompanyMonthlyActivityTrendSnapshots.Remove(existing);

        dbContext.CompanyMonthlyActivityTrendSnapshots.Add(MapToRow(row));
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetCompanyTrendAsync(
        string externalCompanyId,
        int fromYear,
        int fromMonth,
        int toYear,
        int toMonth,
        CancellationToken ct = default)
    {
        var rows = await dbContext.CompanyMonthlyActivityTrendSnapshots
            .Where(r => r.ExternalCompanyId == externalCompanyId
                     && (r.ReportYear > fromYear || (r.ReportYear == fromYear && r.ReportMonth >= fromMonth))
                     && (r.ReportYear < toYear || (r.ReportYear == toYear && r.ReportMonth <= toMonth)))
            .OrderBy(r => r.ReportYear)
            .ThenBy(r => r.ReportMonth)
            .ToListAsync(ct);

        return rows.Select(MapToSnapshot).ToList();
    }

    public async Task<CompanyMonthlyActivityTrendSnapshot?> GetLatestAsync(
        string externalCompanyId,
        CancellationToken ct = default)
    {
        var row = await dbContext.CompanyMonthlyActivityTrendSnapshots
            .Where(r => r.ExternalCompanyId == externalCompanyId)
            .OrderByDescending(r => r.ReportYear)
            .ThenByDescending(r => r.ReportMonth)
            .FirstOrDefaultAsync(ct);

        return row is null ? null : MapToSnapshot(row);
    }

    public async Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetLatestAvailablePeriodsAsync(
        string externalCompanyId,
        int count,
        CancellationToken ct = default)
    {
        var rows = await dbContext.CompanyMonthlyActivityTrendSnapshots
            .Where(r => r.ExternalCompanyId == externalCompanyId)
            .OrderByDescending(r => r.ReportYear)
            .ThenByDescending(r => r.ReportMonth)
            .Take(count)
            .ToListAsync(ct);

        return rows.Select(MapToSnapshot).ToList();
    }

    public async Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetAnnualComparisonBaseAsync(
        string externalCompanyId,
        int latestReportYear,
        int latestReportMonth,
        CancellationToken ct = default)
    {
        // Return the current fiscal year (12 months ending at latestReportYear/Month)
        // plus the same 12-month window one year prior for side-by-side comparison.
        var prevYear = latestReportYear - 1;

        var rows = await dbContext.CompanyMonthlyActivityTrendSnapshots
            .Where(r => r.ExternalCompanyId == externalCompanyId
                     && ((r.ReportYear == latestReportYear && r.ReportMonth <= latestReportMonth)
                      || (r.ReportYear == prevYear)))
            .OrderBy(r => r.ReportYear)
            .ThenBy(r => r.ReportMonth)
            .ToListAsync(ct);

        return rows.Select(MapToSnapshot).ToList();
    }

    private static CompanyMonthlyActivityTrendSnapshotRow MapToRow(
        CompanyMonthlyActivityTrendSnapshotUpsertRow row) =>
        new()
        {
            Id = row.Id,
            ExternalCompanyId = row.ExternalCompanyId,
            CompanySymbol = row.CompanySymbol,
            CompanyName = row.CompanyName,
            IndustryId = row.IndustryId,
            IndustryTitle = row.IndustryTitle,
            CategoryId = row.CategoryId,
            CategoryTitle = row.CategoryTitle,
            ReportYear = row.ReportYear,
            ReportMonth = row.ReportMonth,
            FiscalEndDate = row.FiscalEndDate,
            FiscalYear = row.FiscalYear,
            FiscalMonthIndex = row.FiscalMonthIndex,
            FiscalMonthNameFa = row.FiscalMonthNameFa,
            CalendarYear = row.CalendarYear,
            CalendarMonth = row.CalendarMonth,
            MonthlySalesAmount = row.MonthlySalesAmount,
            MonthlyProductionQuantity = row.MonthlyProductionQuantity,
            MonthlySalesQuantity = row.MonthlySalesQuantity,
            MonthlyAverageSalesRate = row.MonthlyAverageSalesRate,
            HasMixedProductUnits = row.HasMixedProductUnits,
            ProductUnitSummary = row.ProductUnitSummary,
            SameMonthPreviousYearSalesAmount = row.SameMonthPreviousYearSalesAmount,
            SameMonthPreviousYearProductionQuantity = row.SameMonthPreviousYearProductionQuantity,
            SameMonthPreviousYearSalesQuantity = row.SameMonthPreviousYearSalesQuantity,
            Average12MonthSalesAmount = row.Average12MonthSalesAmount,
            Average12MonthPeriodCount = row.Average12MonthPeriodCount,
            YtdSalesAmount = row.YtdSalesAmount,
            YtdProductionQuantity = row.YtdProductionQuantity,
            YtdSalesQuantity = row.YtdSalesQuantity,
            YtdPreviousMonthSalesAmount = row.YtdPreviousMonthSalesAmount,
            SalesAmountMomGrowthPercent = row.SalesAmountMomGrowthPercent,
            SalesAmountYoYGrowthPercent = row.SalesAmountYoYGrowthPercent,
            ProductionQuantityYoYGrowthPercent = row.ProductionQuantityYoYGrowthPercent,
            SalesQuantityYoYGrowthPercent = row.SalesQuantityYoYGrowthPercent,
            CurrentMonthOutputType = row.CurrentMonthOutputType,
            YtdOutputType = row.YtdOutputType,
            YtdPreviousMonthOutputType = row.YtdPreviousMonthOutputType,
            SourceProviderName = row.SourceProviderName,
            SourceReportId = row.SourceReportId,
            SourceRawPayloadId = row.SourceRawPayloadId,
            IsComparablePreviousYearAvailable = row.IsComparablePreviousYearAvailable,
            IsAverage12MonthComplete = row.IsAverage12MonthComplete,
            DataCompletenessScore = row.DataCompletenessScore,
            CalculatedAtUtc = row.CalculatedAtUtc
        };

    private static CompanyMonthlyActivityTrendSnapshot MapToSnapshot(
        CompanyMonthlyActivityTrendSnapshotRow r) =>
        new(
            r.ExternalCompanyId,
            r.CompanySymbol,
            r.CompanyName,
            r.ReportYear,
            r.ReportMonth,
            r.FiscalEndDate,
            r.FiscalYear,
            r.FiscalMonthIndex,
            r.FiscalMonthNameFa,
            r.MonthlySalesAmount,
            r.MonthlyProductionQuantity,
            r.MonthlySalesQuantity,
            r.MonthlyAverageSalesRate,
            r.HasMixedProductUnits,
            r.ProductUnitSummary,
            r.SameMonthPreviousYearSalesAmount,
            r.SameMonthPreviousYearProductionQuantity,
            r.SameMonthPreviousYearSalesQuantity,
            r.Average12MonthSalesAmount,
            r.Average12MonthPeriodCount,
            r.YtdSalesAmount,
            r.YtdPreviousMonthSalesAmount,
            r.SalesAmountMomGrowthPercent,
            r.SalesAmountYoYGrowthPercent,
            r.ProductionQuantityYoYGrowthPercent,
            r.SalesQuantityYoYGrowthPercent,
            r.SourceProviderName,
            r.IsComparablePreviousYearAvailable,
            r.IsAverage12MonthComplete,
            r.DataCompletenessScore,
            r.CalculatedAtUtc);
}
