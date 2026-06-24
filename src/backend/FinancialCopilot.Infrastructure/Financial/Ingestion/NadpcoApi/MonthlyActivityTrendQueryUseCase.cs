using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class MonthlyActivityTrendQueryUseCase(
    ICompanyResolverService companyResolver,
    ICompanyMonthlyActivityTrendSnapshotRepository repository)
    : IMonthlyActivityTrendQueryUseCase
{
    private static readonly string[] PersianFiscalMonthNames =
    [
        "فروردین", "اردیبهشت", "خرداد",
        "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر",
        "دی", "بهمن", "اسفند"
    ];

    public async Task<MonthlyActivityTrendResponse?> ExecuteAsync(
        MonthlyActivityTrendQuery query,
        CancellationToken ct = default)
    {
        var symbol = query.SymbolOrCompanyName;
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var company = await companyResolver.ResolveBySymbolAsync(symbol, ct);
        if (company is null)
            return null;

        // Load the latest snapshot to anchor the reporting period.
        var latest = query.LatestReportYear.HasValue && query.LatestReportMonth.HasValue
            ? await repository.GetCompanyTrendAsync(
                company.ExternalCompanyId,
                query.LatestReportYear.Value, query.LatestReportMonth.Value,
                query.LatestReportYear.Value, query.LatestReportMonth.Value, ct)
                    .ContinueWith(t => t.Result.Count > 0 ? t.Result[0] : null, ct)
            : await repository.GetLatestAsync(company.ExternalCompanyId, ct);

        if (latest is null)
            return null;

        // Load all snapshots needed to build the annual comparison chart.
        var annualBase = await repository.GetAnnualComparisonBaseAsync(
            company.ExternalCompanyId,
            latest.ReportYear,
            latest.ReportMonth,
            ct);

        var chartPoints = BuildChartPoints(latest.ReportYear, latest.ReportMonth, annualBase);
        var insights = BuildInsights(latest);
        var missingPoints = BuildMissingDataPoints(chartPoints);

        decimal? salesVsAvg = null;
        if (latest.Average12MonthSalesAmount.HasValue && latest.Average12MonthSalesAmount.Value != 0)
        {
            salesVsAvg = (latest.MonthlySalesAmount - latest.Average12MonthSalesAmount.Value)
                / latest.Average12MonthSalesAmount.Value * 100m;
        }

        return new MonthlyActivityTrendResponse(
            CompanySymbol: latest.CompanySymbol ?? symbol,
            CompanyName: latest.CompanyName,
            LatestReportYear: latest.ReportYear,
            LatestReportMonth: latest.ReportMonth,
            UnitLabelFa: "میلیون ریال",
            LatestMonthlySalesAmount: latest.MonthlySalesAmount,
            SameMonthPreviousYearSalesAmount: latest.SameMonthPreviousYearSalesAmount,
            Average12MonthSalesAmount: latest.Average12MonthSalesAmount,
            SalesAmountYoYGrowthPercent: latest.SalesAmountYoYGrowthPercent,
            SalesVsAverage12MonthPercent: salesVsAvg,
            YtdSalesAmount: latest.YtdSalesAmount,
            YtdPreviousMonthSalesAmount: latest.YtdPreviousMonthSalesAmount,
            ChartPoints: chartPoints,
            Insights: insights,
            MissingDataPoints: missingPoints,
            SourceProviderName: latest.SourceProviderName,
            CalculatedAtUtc: latest.CalculatedAtUtc);
    }

    private static IReadOnlyList<MonthlyActivityTrendChartPoint> BuildChartPoints(
        int latestYear,
        int latestMonth,
        IReadOnlyList<CompanyMonthlyActivityTrendSnapshot> snapshots)
    {
        // Group by (year, month) for fast lookup.
        var byPeriod = snapshots.ToDictionary(s => (s.ReportYear, (int)s.ReportMonth));

        // The current fiscal year is determined from the latest snapshot's FiscalYear if available,
        // otherwise we treat the report year as the fiscal year.
        var latestSnapshot = snapshots.FirstOrDefault(s => s.ReportYear == latestYear && s.ReportMonth == latestMonth);
        var currentFiscalYear = latestSnapshot?.FiscalYear ?? latestYear;
        var previousFiscalYear = currentFiscalYear - 1;

        // We need to find which calendar months map to fiscal months 1–12 for the current year.
        // Determine the fiscal month offset: find the snapshot with FiscalMonthIndex == 1 and use its calendar month.
        int? fiscalStartCalendarMonth = null;
        foreach (var s in snapshots)
        {
            if (s.FiscalYear == currentFiscalYear && s.FiscalMonthIndex == 1)
            {
                fiscalStartCalendarMonth = s.ReportMonth;
                break;
            }
        }
        foreach (var s in snapshots)
        {
            if (s.FiscalYear == previousFiscalYear && s.FiscalMonthIndex == 1 && fiscalStartCalendarMonth is null)
            {
                fiscalStartCalendarMonth = s.ReportMonth;
                break;
            }
        }

        var points = new List<MonthlyActivityTrendChartPoint>(12);

        // Build 12 fiscal month slots.
        for (var fiscalMonthIdx = 1; fiscalMonthIdx <= 12; fiscalMonthIdx++)
        {
            var fiscalMonthNameFa = PersianFiscalMonthNames[fiscalMonthIdx - 1];

            // Find current-year and previous-year snapshots for this fiscal month.
            var currentSnap = snapshots.FirstOrDefault(
                s => s.FiscalYear == currentFiscalYear && s.FiscalMonthIndex == fiscalMonthIdx);
            var previousSnap = snapshots.FirstOrDefault(
                s => s.FiscalYear == previousFiscalYear && s.FiscalMonthIndex == fiscalMonthIdx);

            // Current-year months beyond the latest reported month must be null.
            decimal? currentYearSales = null;
            var isCurrentYearReported = false;
            if (currentSnap is not null)
            {
                // Reported if it is at or before the latest report period.
                var isAtOrBefore = currentSnap.ReportYear < latestYear
                    || (currentSnap.ReportYear == latestYear && currentSnap.ReportMonth <= latestMonth);
                if (isAtOrBefore)
                {
                    currentYearSales = currentSnap.MonthlySalesAmount;
                    isCurrentYearReported = true;
                }
            }

            var previousYearSales = previousSnap?.SameMonthPreviousYearSalesAmount
                ?? previousSnap?.MonthlySalesAmount;
            var isPreviousYearReported = previousSnap is not null;

            // Use the latest snapshot's persisted average as a constant horizontal reference line.
            var avg12 = latestSnapshot?.Average12MonthSalesAmount;

            points.Add(new MonthlyActivityTrendChartPoint(
                FiscalMonthIndex: fiscalMonthIdx,
                FiscalMonthNameFa: fiscalMonthNameFa,
                PreviousFiscalYear: previousFiscalYear,
                PreviousFiscalYearSalesAmount: previousYearSales,
                CurrentFiscalYear: currentFiscalYear,
                CurrentFiscalYearSalesAmount: currentYearSales,
                Average12MonthSalesAmount: avg12,
                IsCurrentYearReported: isCurrentYearReported,
                IsPreviousYearReported: isPreviousYearReported));
        }

        return points;
    }

    private static IReadOnlyList<MonthlyActivityTrendInsight> BuildInsights(
        CompanyMonthlyActivityTrendSnapshot latest)
    {
        var insights = new List<MonthlyActivityTrendInsight>();

        // YoY growth insight.
        if (latest.SalesAmountYoYGrowthPercent.HasValue)
        {
            var pct = latest.SalesAmountYoYGrowthPercent.Value;
            var sign = pct >= 0 ? "+" : "";
            var direction = pct >= 0 ? "رشد" : "افت";
            insights.Add(new MonthlyActivityTrendInsight(
                MonthlyActivityTrendInsightKind.YoYGrowth,
                $"فروش ماهانه نسبت به ماه مشابه سال قبل {sign}{pct:F1}٪ {direction} داشته است."));
        }
        else if (!latest.IsComparablePreviousYearAvailable)
        {
            insights.Add(new MonthlyActivityTrendInsight(
                MonthlyActivityTrendInsightKind.MissingData,
                "داده سال قبل برای مقایسه یکسان‌ماهه موجود نیست."));
        }

        // 12-month average insight.
        if (latest.Average12MonthSalesAmount.HasValue && latest.Average12MonthSalesAmount.Value != 0)
        {
            var vsAvg = (latest.MonthlySalesAmount - latest.Average12MonthSalesAmount.Value)
                / latest.Average12MonthSalesAmount.Value * 100m;
            var sign = vsAvg >= 0 ? "+" : "";
            var direction = vsAvg >= 0 ? "بالاتر" : "پایین‌تر";
            var completenessNote = latest.IsAverage12MonthComplete
                ? ""
                : $" (میانگین با {latest.Average12MonthPeriodCount} دوره موجود محاسبه شده است)";
            insights.Add(new MonthlyActivityTrendInsight(
                MonthlyActivityTrendInsightKind.VsAverage12Month,
                $"فروش این ماه نسبت به میانگین ۱۲ ماهه {sign}{vsAvg:F1}٪ {direction} است{completenessNote}."));
        }
        else if (!latest.IsAverage12MonthComplete)
        {
            insights.Add(new MonthlyActivityTrendInsight(
                MonthlyActivityTrendInsightKind.DataQuality,
                $"میانگین ۱۲ ماهه با {latest.Average12MonthPeriodCount} دوره موجود محاسبه شده است."));
        }

        return insights;
    }

    private static IReadOnlyList<MonthlyActivityTrendMissingDataPoint> BuildMissingDataPoints(
        IReadOnlyList<MonthlyActivityTrendChartPoint> chartPoints)
    {
        var missing = new List<MonthlyActivityTrendMissingDataPoint>();
        foreach (var pt in chartPoints)
        {
            if (!pt.IsPreviousYearReported && pt.PreviousFiscalYear.HasValue)
            {
                missing.Add(new MonthlyActivityTrendMissingDataPoint(
                    pt.PreviousFiscalYear.Value,
                    pt.FiscalMonthIndex,
                    $"داده ماه {pt.FiscalMonthNameFa} سال {pt.PreviousFiscalYear} موجود نیست."));
            }
        }
        return missing;
    }
}
