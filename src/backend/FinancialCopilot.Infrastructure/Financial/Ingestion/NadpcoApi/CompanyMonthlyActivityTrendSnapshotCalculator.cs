using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class CompanyMonthlyActivityTrendSnapshotCalculator(
    FinancialIngestionDbContext dbContext,
    ICompanyMonthlyActivityTrendSnapshotRepository repository)
    : ICompanyMonthlyActivityTrendSnapshotCalculator
{
    private const string ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName;

    public async Task RecalculateAsync(
        string externalCompanyId,
        int jalaliYear,
        byte jalaliMonth,
        string? bourseSymbol,
        string? companyName,
        string? fiscalEndDate,
        CancellationToken ct = default)
    {
        var (periodStart, _) = JalaliDateResolver.ResolveMonth(jalaliYear, jalaliMonth);

        // Resolve industry and category from the authoritative Companies → Industries / IndustryGroups join.
        // This is always preferred over caller-supplied values (which may be missing or stale).
        var (industryId, industryTitle, categoryId, categoryTitle) =
            await ResolveCompanyTaxonomyAsync(externalCompanyId, ct);

        // Load OutputType=0 (single-month) ProductSales reports for this company/month.
        var reports = await dbContext.MonthlyReports
            .Where(r => r.ProviderName == ProviderName
                     && r.ExternalCompanyId == externalCompanyId
                     && r.ReportType == "ProductSales"
                     && r.OutputType == 0
                     && r.PeriodStart == periodStart)
            .ToListAsync(ct);

        if (reports.Count == 0) return;

        var reportIds = reports.Select(r => r.Id).ToHashSet();
        var sourceReportId = reports[0].ExternalReportId;

        // Load line items for current month (all output types).
        var currentLineItems = await dbContext.MonthlyReportLineItems
            .Where(li => reportIds.Contains(li.MonthlyReportId))
            .ToListAsync(ct);

        if (currentLineItems.Count == 0) return;

        // Aggregate company-level totals from outputType=0 rows (all line items, including negatives).
        var salesItems = currentLineItems.Where(li => li.SalesAmount.HasValue).ToList();

        var monthlySalesAmount = salesItems.Sum(li => li.SalesAmount ?? 0m);

        var units = salesItems
            .Where(li => li.SalesQuantity.HasValue && li.SalesQuantity != 0 && !string.IsNullOrWhiteSpace(li.Unit))
            .Select(li => li.Unit!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hasMixedUnits = units.Count > 1;
        var productUnitSummary = units.Count > 0 ? string.Join(", ", units) : null;

        decimal? monthlyProductionQuantity = null;
        decimal? monthlySalesQuantity = null;
        decimal? monthlyAverageSalesRate = null;

        if (!hasMixedUnits)
        {
            var prodItems = currentLineItems.Where(li => li.ProductionQuantity.HasValue).ToList();
            if (prodItems.Count > 0)
                monthlyProductionQuantity = prodItems.Sum(li => li.ProductionQuantity ?? 0m);

            var qtyItems = currentLineItems.Where(li => li.SalesQuantity.HasValue).ToList();
            if (qtyItems.Count > 0)
            {
                monthlySalesQuantity = qtyItems.Sum(li => li.SalesQuantity ?? 0m);
                if (monthlySalesQuantity != 0)
                    monthlyAverageSalesRate = monthlySalesAmount / monthlySalesQuantity;
            }
        }

        // Load YTD values from outputType=1.
        var ytdReports = await dbContext.MonthlyReports
            .Where(r => r.ProviderName == ProviderName
                     && r.ExternalCompanyId == externalCompanyId
                     && r.ReportType == "ProductSales"
                     && r.OutputType == 1
                     && r.PeriodStart == periodStart)
            .Select(r => r.Id)
            .ToListAsync(ct);

        decimal? ytdSalesAmount = null;
        decimal? ytdProductionQuantity = null;
        decimal? ytdSalesQuantity = null;
        int? ytdOutputType = null;

        if (ytdReports.Count > 0)
        {
            var ytdItems = await dbContext.MonthlyReportLineItems
                .Where(li => ytdReports.Contains(li.MonthlyReportId))
                .ToListAsync(ct);

            ytdSalesAmount = ytdItems.Where(li => li.SalesAmount.HasValue).Sum(li => li.SalesAmount ?? 0m);
            var ytdProdItems = ytdItems.Where(li => li.ProductionQuantity.HasValue).ToList();
            if (ytdProdItems.Count > 0)
                ytdProductionQuantity = ytdProdItems.Sum(li => li.ProductionQuantity ?? 0m);
            var ytdQtyItems = ytdItems.Where(li => li.SalesQuantity.HasValue).ToList();
            if (ytdQtyItems.Count > 0)
                ytdSalesQuantity = ytdQtyItems.Sum(li => li.SalesQuantity ?? 0m);
            ytdOutputType = 1;
        }

        // Load YTD-to-previous-month values from outputType=4.
        var ytdPrevMonthReports = await dbContext.MonthlyReports
            .Where(r => r.ProviderName == ProviderName
                     && r.ExternalCompanyId == externalCompanyId
                     && r.ReportType == "ProductSales"
                     && r.OutputType == 4
                     && r.PeriodStart == periodStart)
            .Select(r => r.Id)
            .ToListAsync(ct);

        decimal? ytdPreviousMonthSalesAmount = null;
        int? ytdPreviousMonthOutputType = null;

        if (ytdPrevMonthReports.Count > 0)
        {
            var ytdPrevItems = await dbContext.MonthlyReportLineItems
                .Where(li => ytdPrevMonthReports.Contains(li.MonthlyReportId) && li.SalesAmount.HasValue)
                .ToListAsync(ct);

            ytdPreviousMonthSalesAmount = ytdPrevItems.Sum(li => li.SalesAmount ?? 0m);
            ytdPreviousMonthOutputType = 4;
        }

        // Load previous-year same-month snapshot (already persisted) for YoY comparison.
        var prevYearSnapshots = await GetSnapshotByPeriodAsync(externalCompanyId, jalaliYear - 1, jalaliMonth, ct);

        decimal? sameMonthPrevYearSales = prevYearSnapshots?.MonthlySalesAmount;
        decimal? sameMonthPrevYearProd = prevYearSnapshots?.MonthlyProductionQuantity;
        decimal? sameMonthPrevYearQty = prevYearSnapshots?.MonthlySalesQuantity;
        var isComparablePrevYearAvailable = sameMonthPrevYearSales.HasValue;

        // Load previous month snapshot for MoM.
        var (prevMonthYear, prevMonthMonth) = DecrementMonth(jalaliYear, jalaliMonth);
        var prevMonthSnapshot = await GetSnapshotByPeriodAsync(externalCompanyId, prevMonthYear, prevMonthMonth, ct);

        // Trailing 12-month average: current month plus up to 11 persisted prior snapshots.
        // The current month's snapshot has not been persisted yet, so we read 11 prior months
        // and prepend the current month's value.
        var priorPeriods = await GetTrailingSnapshotsAsync(externalCompanyId, jalaliYear, jalaliMonth, 11, ct);
        var allPeriodAmounts = new List<decimal>(priorPeriods.Count + 1) { monthlySalesAmount };
        allPeriodAmounts.AddRange(priorPeriods.Select(s => s.MonthlySalesAmount));
        decimal? average12MonthSales = null;
        var periodCount = allPeriodAmounts.Count;
        var isAverage12MonthComplete = periodCount >= 12;

        if (periodCount > 0)
            average12MonthSales = allPeriodAmounts.Average();

        // Growth percentages.
        decimal? momGrowth = null;
        if (prevMonthSnapshot is not null && prevMonthSnapshot.MonthlySalesAmount != 0)
            momGrowth = Math.Round((monthlySalesAmount - prevMonthSnapshot.MonthlySalesAmount) / prevMonthSnapshot.MonthlySalesAmount * 100m, 4);

        decimal? yoyGrowth = null;
        if (sameMonthPrevYearSales.HasValue && sameMonthPrevYearSales.Value != 0)
            yoyGrowth = Math.Round((monthlySalesAmount - sameMonthPrevYearSales.Value) / sameMonthPrevYearSales.Value * 100m, 4);

        decimal? prodYoYGrowth = null;
        if (sameMonthPrevYearProd.HasValue && sameMonthPrevYearProd.Value != 0 && monthlyProductionQuantity.HasValue)
            prodYoYGrowth = Math.Round((monthlyProductionQuantity.Value - sameMonthPrevYearProd.Value) / sameMonthPrevYearProd.Value * 100m, 4);

        decimal? qtyYoYGrowth = null;
        if (sameMonthPrevYearQty.HasValue && sameMonthPrevYearQty.Value != 0 && monthlySalesQuantity.HasValue)
            qtyYoYGrowth = Math.Round((monthlySalesQuantity.Value - sameMonthPrevYearQty.Value) / sameMonthPrevYearQty.Value * 100m, 4);

        // Completeness score: simple heuristic (0–1).
        var score = ComputeCompletenessScore(
            isComparablePrevYearAvailable,
            isAverage12MonthComplete,
            periodCount,
            ytdSalesAmount.HasValue,
            ytdPreviousMonthSalesAmount.HasValue);

        var row = new CompanyMonthlyActivityTrendSnapshotUpsertRow(
            Id: Guid.NewGuid(),
            ExternalCompanyId: externalCompanyId,
            CompanySymbol: bourseSymbol,
            CompanyName: companyName,
            IndustryId: industryId,
            IndustryTitle: industryTitle,
            CategoryId: categoryId,
            CategoryTitle: categoryTitle,
            ReportYear: jalaliYear,
            ReportMonth: jalaliMonth,
            FiscalEndDate: fiscalEndDate,
            FiscalYear: null,
            FiscalMonthIndex: null,
            FiscalMonthNameFa: null,
            CalendarYear: periodStart.Year,
            CalendarMonth: periodStart.Month,
            MonthlySalesAmount: monthlySalesAmount,
            MonthlyProductionQuantity: monthlyProductionQuantity,
            MonthlySalesQuantity: monthlySalesQuantity,
            MonthlyAverageSalesRate: monthlyAverageSalesRate,
            HasMixedProductUnits: hasMixedUnits,
            ProductUnitSummary: productUnitSummary,
            SameMonthPreviousYearSalesAmount: sameMonthPrevYearSales,
            SameMonthPreviousYearProductionQuantity: sameMonthPrevYearProd,
            SameMonthPreviousYearSalesQuantity: sameMonthPrevYearQty,
            Average12MonthSalesAmount: average12MonthSales,
            Average12MonthPeriodCount: periodCount,
            YtdSalesAmount: ytdSalesAmount,
            YtdProductionQuantity: ytdProductionQuantity,
            YtdSalesQuantity: ytdSalesQuantity,
            YtdPreviousMonthSalesAmount: ytdPreviousMonthSalesAmount,
            SalesAmountMomGrowthPercent: momGrowth,
            SalesAmountYoYGrowthPercent: yoyGrowth,
            ProductionQuantityYoYGrowthPercent: prodYoYGrowth,
            SalesQuantityYoYGrowthPercent: qtyYoYGrowth,
            CurrentMonthOutputType: 0,
            YtdOutputType: ytdOutputType,
            YtdPreviousMonthOutputType: ytdPreviousMonthOutputType,
            SourceProviderName: ProviderName,
            SourceReportId: sourceReportId,
            SourceRawPayloadId: null,
            IsComparablePreviousYearAvailable: isComparablePrevYearAvailable,
            IsAverage12MonthComplete: isAverage12MonthComplete,
            DataCompletenessScore: score,
            CalculatedAtUtc: DateTimeOffset.UtcNow);

        await repository.UpsertAsync(row, ct);
    }

    public async Task RecalculateRangeAsync(
        string externalCompanyId,
        int fromYear,
        int fromMonth,
        int toYear,
        int toMonth,
        CancellationToken ct = default)
    {
        var cursor = new MonthCursor(fromYear, (byte)fromMonth);
        var end = new MonthCursor(toYear, (byte)toMonth);

        while (!cursor.IsAfter(end))
        {
            await RecalculateAsync(
                externalCompanyId,
                cursor.Year,
                cursor.Month,
                bourseSymbol: null,
                companyName: null,
                fiscalEndDate: null,
                ct);

            cursor = cursor.Next();
        }
    }

    private async Task<CompanyMonthlyActivityTrendSnapshot?> GetSnapshotByPeriodAsync(
        string externalCompanyId,
        int year,
        int month,
        CancellationToken ct)
    {
        var row = await dbContext.CompanyMonthlyActivityTrendSnapshots
            .Where(r => r.ExternalCompanyId == externalCompanyId
                     && r.ReportYear == year
                     && r.ReportMonth == month)
            .FirstOrDefaultAsync(ct);

        return row is null ? null : MapToSnapshot(row);
    }

    private async Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetTrailingSnapshotsAsync(
        string externalCompanyId,
        int toYear,
        int toMonth,
        int count,
        CancellationToken ct)
    {
        // Exclude the current month itself — it is provided directly by the caller.
        var rows = await dbContext.CompanyMonthlyActivityTrendSnapshots
            .Where(r => r.ExternalCompanyId == externalCompanyId
                     && (r.ReportYear < toYear || (r.ReportYear == toYear && r.ReportMonth < toMonth)))
            .OrderByDescending(r => r.ReportYear)
            .ThenByDescending(r => r.ReportMonth)
            .Take(count)
            .ToListAsync(ct);

        return rows.Select(MapToSnapshot).ToList();
    }

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

    private static decimal ComputeCompletenessScore(
        bool hasPrevYear,
        bool hasFullAverage,
        int periodCount,
        bool hasYtd,
        bool hasYtdPrevMonth)
    {
        var score = 0.4m; // base: current month exists
        if (hasPrevYear) score += 0.25m;
        if (hasFullAverage) score += 0.2m;
        else if (periodCount >= 6) score += 0.1m;
        if (hasYtd) score += 0.1m;
        if (hasYtdPrevMonth) score += 0.05m;
        return Math.Min(score, 1m);
    }

    /// <summary>
    /// Looks up IndustryId/Title and CategoryId/Title for a company from the
    /// Companies → Industries / IndustryGroups join. These are the authoritative
    /// values stored in the NoavaranEligibleCompanies view.
    /// Returns (null, null, null, null) when the company or its dimension rows are absent.
    /// </summary>
    private async Task<(int? IndustryId, string? IndustryTitle, int? CategoryId, string? CategoryTitle)>
        ResolveCompanyTaxonomyAsync(string externalCompanyId, CancellationToken ct)
    {
        var result = await dbContext.Companies
            .Where(c => c.ProviderName == ProviderName && c.ExternalCompanyId == externalCompanyId)
            .Select(c => new
            {
                IndustryExternalId = c.IndustryId != null
                    ? dbContext.Industries
                        .Where(i => i.Id == c.IndustryId)
                        .Select(i => new { i.ExternalId, i.Name })
                        .FirstOrDefault()
                    : null,
                GroupExternalId = c.GroupId != null
                    ? dbContext.IndustryGroups
                        .Where(g => g.Id == c.GroupId)
                        .Select(g => new { g.ExternalId, g.Name })
                        .FirstOrDefault()
                    : null
            })
            .FirstOrDefaultAsync(ct);

        if (result is null)
            return (null, null, null, null);

        int? industryId = null;
        string? industryTitle = null;
        if (result.IndustryExternalId is not null &&
            int.TryParse(result.IndustryExternalId.ExternalId, out var parsedIndustryId))
        {
            industryId = parsedIndustryId;
            industryTitle = result.IndustryExternalId.Name;
        }

        int? categoryId = null;
        string? categoryTitle = null;
        if (result.GroupExternalId is not null &&
            int.TryParse(result.GroupExternalId.ExternalId, out var parsedCategoryId))
        {
            categoryId = parsedCategoryId;
            categoryTitle = result.GroupExternalId.Name;
        }

        return (industryId, industryTitle, categoryId, categoryTitle);
    }

    private static (int Year, byte Month) DecrementMonth(int year, byte month)
    {
        if (month == 1) return (year - 1, 12);
        return (year, (byte)(month - 1));
    }

    private readonly record struct MonthCursor(int Year, byte Month)
    {
        public bool IsAfter(MonthCursor other) =>
            Year > other.Year || (Year == other.Year && Month > other.Month);

        public MonthCursor Next() =>
            Month == 12 ? new MonthCursor(Year + 1, 1) : new MonthCursor(Year, (byte)(Month + 1));
    }
}
