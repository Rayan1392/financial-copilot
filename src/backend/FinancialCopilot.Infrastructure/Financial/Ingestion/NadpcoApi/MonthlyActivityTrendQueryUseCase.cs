using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class MonthlyActivityTrendQueryUseCase(
    ICompanyResolverService companyResolver,
    ICompanyMonthlyActivityTrendSnapshotRepository repository,
    ILogger<MonthlyActivityTrendQueryUseCase> logger)
    : IMonthlyActivityTrendQueryUseCase
{
    private const decimal MillionRialToBillionTooman = 0.0001m;

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
        {
            logger.LogInformation(
                "Monthly activity trend skipped. Route={Route} CompanyResolution={CompanyResolution} Reason={ReasonCode}",
                "monthly_activity_trend", "not_attempted", "company_reference_missing");
            return null;
        }

        var company = await companyResolver.ResolveBySymbolAsync(symbol, ct);
        if (company is null)
        {
            logger.LogInformation(
                "Monthly activity trend company resolution completed. Route={Route} ExtractedCompanySymbol={CompanySymbol} CompanyResolution={CompanyResolution} DataRetrieval={DataRetrieval}",
                "monthly_activity_trend", symbol, "not_found", "not_attempted");
            return null;
        }

        var requestedPeriods = query.LastAvailableMonths is > 0
            ? await repository.GetLatestAvailablePeriodsAsync(company.ExternalCompanyId, Math.Clamp(query.LastAvailableMonths.Value, 1, 120), ct)
            : null;
        var latest = requestedPeriods is { Count: > 0 }
            ? requestedPeriods[0]
            : query.LatestReportYear.HasValue && query.LatestReportMonth.HasValue
            ? await repository.GetCompanyTrendAsync(
                company.ExternalCompanyId,
                query.LatestReportYear.Value,
                query.LatestReportMonth.Value,
                query.LatestReportYear.Value,
                query.LatestReportMonth.Value,
                ct).ContinueWith(t => t.Result.Count > 0 ? t.Result[0] : null, ct)
            : await repository.GetLatestAsync(company.ExternalCompanyId, ct);

        if (latest is null)
        {
            logger.LogInformation(
                "Monthly activity trend retrieval completed. Route={Route} ExtractedCompanySymbol={CompanySymbol} CompanyResolution={CompanyResolution} RequestedMetric={RequestedMetric} RequestedPeriod={RequestedPeriod} ChartRequested={ChartRequested} GrowthCalculation={GrowthCalculation} DataRetrieval={DataRetrieval} ObservationCount={ObservationCount}",
                "monthly_activity_trend", symbol, "resolved", "monthly_sales_amount",
                query.LatestReportYear.HasValue && query.LatestReportMonth.HasValue
                    ? $"{query.LatestReportYear.Value}/{query.LatestReportMonth.Value:D2}"
                    : "latest_available",
                true, "not_calculated_no_snapshot", "no_latest_snapshot", 0);
            return null;
        }

        var annualBase = await repository.GetAnnualComparisonBaseAsync(
            company.ExternalCompanyId,
            latest.ReportYear,
            latest.ReportMonth,
            ct);

        logger.LogInformation(
            "Monthly activity trend retrieval completed. Route={Route} ExtractedCompanySymbol={CompanySymbol} CompanyResolution={CompanyResolution} RequestedMetric={RequestedMetric} RequestedPeriod={RequestedPeriod} ChartRequested={ChartRequested} GrowthCalculation={GrowthCalculation} DataRetrieval={DataRetrieval} ObservationCount={ObservationCount} LatestReportPeriod={LatestReportPeriod}",
            "monthly_activity_trend", latest.CompanySymbol ?? symbol, "resolved", "monthly_sales_amount",
            query.LatestReportYear.HasValue && query.LatestReportMonth.HasValue
                ? $"{query.LatestReportYear.Value}/{query.LatestReportMonth.Value:D2}"
                : "latest_available",
            true, latest.SalesAmountMomGrowthPercent.HasValue ? "month_over_month_growth_available" : "month_over_month_growth_unavailable",
            "observations_found", annualBase.Count,
            $"{latest.ReportYear}/{latest.ReportMonth:D2}");

        var chartPoints = !query.IncludeChartPayload
            ? []
            : requestedPeriods is not null
                ? BuildLatestAvailableChartPoints(requestedPeriods)
                : BuildChartPoints(latest.ReportYear, latest.ReportMonth, annualBase);
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
            UnitLabelFa: "میلیارد تومان",
            LatestMonthlySalesAmount: ConvertMillionRialToBillionTooman(latest.MonthlySalesAmount),
            SameMonthPreviousYearSalesAmount: ConvertMillionRialToBillionTooman(latest.SameMonthPreviousYearSalesAmount),
            Average12MonthSalesAmount: ConvertMillionRialToBillionTooman(latest.Average12MonthSalesAmount),
            SalesAmountYoYGrowthPercent: latest.SalesAmountYoYGrowthPercent,
            SalesVsAverage12MonthPercent: salesVsAvg,
            YtdSalesAmount: ConvertMillionRialToBillionTooman(latest.YtdSalesAmount),
            YtdPreviousMonthSalesAmount: ConvertMillionRialToBillionTooman(latest.YtdPreviousMonthSalesAmount),
            ChartPoints: chartPoints,
            Insights: insights,
            MissingDataPoints: missingPoints,
            SourceProviderName: latest.SourceProviderName,
            CalculatedAtUtc: latest.CalculatedAtUtc)
        {
            SalesAmountMomGrowthPercent = latest.SalesAmountMomGrowthPercent,
            RequestedPeriod = query.LastAvailableMonths is > 0
                ? $"last_available_months:{Math.Clamp(query.LastAvailableMonths.Value, 1, 120)}"
                : null,
            RequestedOperations = query.RequestedOperations?.ToArray() ?? [],
            UnsupportedParts = query.UnsupportedParts?.ToArray() ?? []
        };
    }

    private static IReadOnlyList<MonthlyActivityTrendChartPoint> BuildLatestAvailableChartPoints(
        IReadOnlyList<CompanyMonthlyActivityTrendSnapshot> snapshots)
    {
        var ordered = snapshots.OrderBy(item => item.ReportYear).ThenBy(item => item.ReportMonth).ToArray();
        var average = ordered.LastOrDefault()?.Average12MonthSalesAmount;
        return ordered.Select((snapshot, index) => new MonthlyActivityTrendChartPoint(
            FiscalMonthIndex: index + 1,
            FiscalMonthNameFa: $"{snapshot.ReportYear}/{snapshot.ReportMonth:D2}",
            PreviousFiscalYear: null,
            PreviousFiscalYearSalesAmount: null,
            CurrentFiscalYear: snapshot.ReportYear,
            CurrentFiscalYearSalesAmount: ConvertMillionRialToBillionTooman(snapshot.MonthlySalesAmount),
            Average12MonthSalesAmount: ConvertMillionRialToBillionTooman(average),
            IsCurrentYearReported: true,
            IsPreviousYearReported: false)).ToArray();
    }

    private static IReadOnlyList<MonthlyActivityTrendChartPoint> BuildChartPoints(
        int latestYear,
        int latestMonth,
        IReadOnlyList<CompanyMonthlyActivityTrendSnapshot> snapshots)
    {
        var latestSnapshot = snapshots.FirstOrDefault(s => s.ReportYear == latestYear && s.ReportMonth == latestMonth);
        var currentFiscalYear = latestYear;
        var previousFiscalYear = currentFiscalYear - 1;

        var points = new List<MonthlyActivityTrendChartPoint>(12);

        for (var fiscalMonthIdx = 1; fiscalMonthIdx <= 12; fiscalMonthIdx++)
        {
            var fiscalMonthNameFa = PersianFiscalMonthNames[fiscalMonthIdx - 1];

            var currentSnap = snapshots.FirstOrDefault(
                s => s.ReportYear == currentFiscalYear && s.ReportMonth == fiscalMonthIdx);
            var previousSnap = snapshots.FirstOrDefault(
                s => s.ReportYear == previousFiscalYear && s.ReportMonth == fiscalMonthIdx);

            decimal? currentYearSales = null;
            var isCurrentYearReported = false;
            if (currentSnap is not null)
            {
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
            var avg12 = latestSnapshot?.Average12MonthSalesAmount;

            points.Add(new MonthlyActivityTrendChartPoint(
                FiscalMonthIndex: fiscalMonthIdx,
                FiscalMonthNameFa: fiscalMonthNameFa,
                PreviousFiscalYear: previousFiscalYear,
                PreviousFiscalYearSalesAmount: ConvertMillionRialToBillionTooman(previousYearSales),
                CurrentFiscalYear: currentFiscalYear,
                CurrentFiscalYearSalesAmount: ConvertMillionRialToBillionTooman(currentYearSales),
                Average12MonthSalesAmount: ConvertMillionRialToBillionTooman(avg12),
                IsCurrentYearReported: isCurrentYearReported,
                IsPreviousYearReported: isPreviousYearReported));
        }

        return points;
    }

    private static IReadOnlyList<MonthlyActivityTrendInsight> BuildInsights(
        CompanyMonthlyActivityTrendSnapshot latest)
    {
        var insights = new List<MonthlyActivityTrendInsight>();

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

    private static decimal ConvertMillionRialToBillionTooman(decimal value) =>
        value * MillionRialToBillionTooman;

    private static decimal? ConvertMillionRialToBillionTooman(decimal? value) =>
        value.HasValue ? ConvertMillionRialToBillionTooman(value.Value) : null;
}
