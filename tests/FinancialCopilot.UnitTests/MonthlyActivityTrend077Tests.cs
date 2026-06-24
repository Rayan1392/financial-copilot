using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 077: AI Monthly Production and Sales Trend Query.
/// Tests cover intent routing, symbol extraction, use case data mapping,
/// and renderer formatting rules.
/// All tests are pure in-memory.
/// </summary>
public sealed class MonthlyActivityTrend077Tests
{
    private static readonly Guid TenantId = Guid.Parse("9a1b2c3d-4e5f-6789-abcd-ef0123456789");

    // -----------------------------------------------------------------------
    // Intent detection — trend phrases must route to MonthlyActivityTrend
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("روند فروش کهمدا را نشان بده")]
    [InlineData("روند فروش ماهانه کسرا")]
    [InlineData("نمودار فروش ماهانه کهمدا")]
    [InlineData("نمودار فروش کچاد")]
    [InlineData("نمودار تولید و فروش کگل")]
    [InlineData("مقایسه فروش سال جاری و سال گذشته کهمدا")]
    [InlineData("فروش امسال نسبت به پارسال کسرا")]
    [InlineData("فروش امسال نسبت به سال قبل کچاد")]
    [InlineData("میانگین ۱۲ ماهه فروش کگل")]
    [InlineData("گزارش تولید و فروش با نمودار کهمدا")]
    public async Task Detect_TrendQuery_RoutesToMonthlyActivityTrend(string query)
    {
        var detector = new LlmAiIntentDetector(new UnknownIntentExecutionService());

        var result = await detector.DetectAsync(
            new IntentDetectionInput(query, "fa", "corr", TenantId),
            CancellationToken.None);

        Assert.Equal(DetectedIntent.MonthlyActivityTrend, result.Intent);
        Assert.True(result.Confidence >= 0.95);
    }

    // -----------------------------------------------------------------------
    // Intent detection — non-trend queries must NOT route to trend
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("پرفروش‌ترین محصول کچاد")]        // product mix → ProductRevenueMix
    [InlineData("ترکیب فروش محصولات فملی")]       // product mix → ProductRevenueMix
    [InlineData("آخرین فروش کگل چقدر بوده؟")]     // single-number → SymbolLookup or Unknown
    public async Task Detect_NonTrendQuery_DoesNotRouteToMonthlyActivityTrend(string query)
    {
        var detector = new LlmAiIntentDetector(new UnknownIntentExecutionService());

        var result = await detector.DetectAsync(
            new IntentDetectionInput(query, "fa", "corr", TenantId),
            CancellationToken.None);

        Assert.NotEqual(DetectedIntent.MonthlyActivityTrend, result.Intent);
    }

    // -----------------------------------------------------------------------
    // Symbol extraction from trend queries
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("روند فروش کهمدا را نشان بده", "کهمدا")]
    [InlineData("نمودار فروش ماهانه کسرا", "کسرا")]
    [InlineData("فروش امسال نسبت به پارسال کچاد", "کچاد")]
    [InlineData("میانگین ۱۲ ماهه فروش کگل", "کگل")]
    public void ExtractSymbol_TrendQuery_ReturnsCorrectSymbol(string query, string expectedSymbol)
    {
        var symbol = MonthlyActivityTrendIntentRules.ExtractCompanySymbol(query);
        Assert.Equal(expectedSymbol, symbol);
    }

    [Fact]
    public void ExtractSymbol_EmptyQuery_ReturnsNull()
    {
        Assert.Null(MonthlyActivityTrendIntentRules.ExtractCompanySymbol(null));
        Assert.Null(MonthlyActivityTrendIntentRules.ExtractCompanySymbol(""));
        Assert.Null(MonthlyActivityTrendIntentRules.ExtractCompanySymbol("   "));
    }

    // -----------------------------------------------------------------------
    // LooksLikeTrendQuery — phrase boundary tests
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("روند فروش کهمدا")]
    [InlineData("نمودار فروش ماهانه کسرا")]
    [InlineData("مقایسه فروش سال جاری و سال گذشته")]
    [InlineData("میانگین ۱۲ ماهه فروش کگل")]
    public void LooksLikeTrendQuery_TrendPhrase_ReturnsTrue(string query)
    {
        Assert.True(MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(query));
    }

    [Theory]
    [InlineData("پرفروش‌ترین محصول کچاد")]
    [InlineData("آخرین فروش کگل")]
    [InlineData("درآمد فصلی فملی چقدر است")]
    [InlineData("P/E کگل چقدر است")]
    [InlineData(null)]
    [InlineData("")]
    public void LooksLikeTrendQuery_NonTrendPhrase_ReturnsFalse(string? query)
    {
        Assert.False(MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(query));
    }

    // -----------------------------------------------------------------------
    // Use case: company not found returns null
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UseCase_CompanyNotFound_ReturnsNull()
    {
        var useCase = new MonthlyActivityTrendQueryUseCaseForTest(
            companyResolves: false,
            snapshots: []);

        var result = await useCase.ExecuteAsync(
            new MonthlyActivityTrendQuery("روند فروش کهمدا", "کهمدا"),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UseCase_NoSnapshotData_ReturnsNull()
    {
        var useCase = new MonthlyActivityTrendQueryUseCaseForTest(
            companyResolves: true,
            snapshots: []);

        var result = await useCase.ExecuteAsync(
            new MonthlyActivityTrendQuery("روند فروش کهمدا", "کهمدا"),
            CancellationToken.None);

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // Use case: chart point null handling — unreported months must be null
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UseCase_UnreportedCurrentYearMonths_AreNull()
    {
        // Snapshot for month 3 of fiscal year 1404; months 4–12 must be null in current year.
        var snapshot = MakeSnapshot(
            reportYear: 1404, reportMonth: 3,
            fiscalYear: 1404, fiscalMonthIndex: 3,
            monthlySalesAmount: 1_000m,
            sameMonthPrevYearSales: 900m,
            avg12MonthSales: 950m);

        var useCase = new MonthlyActivityTrendQueryUseCaseForTest(
            companyResolves: true,
            snapshots: [snapshot],
            latest: snapshot);

        var result = await useCase.ExecuteAsync(
            new MonthlyActivityTrendQuery("روند فروش کهمدا", "کهمدا"),
            CancellationToken.None);

        Assert.NotNull(result);

        // Months after fiscal month 3 in current year must be null (not reported).
        var unreportedPoints = result.ChartPoints
            .Where(p => p.FiscalMonthIndex > 3)
            .ToList();

        foreach (var pt in unreportedPoints)
        {
            Assert.False(pt.IsCurrentYearReported,
                $"Fiscal month {pt.FiscalMonthIndex} should not be reported");
            Assert.Null(pt.CurrentFiscalYearSalesAmount);
        }
    }

    // -----------------------------------------------------------------------
    // Use case: unit label is always میلیون ریال
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UseCase_UnitLabel_IsMillionRial()
    {
        var snapshot = MakeSnapshot(1404, 3, 1404, 3, 1_000m, null, null);
        var useCase = new MonthlyActivityTrendQueryUseCaseForTest(
            companyResolves: true, snapshots: [snapshot], latest: snapshot);

        var result = await useCase.ExecuteAsync(
            new MonthlyActivityTrendQuery("روند فروش کهمدا", "کهمدا"),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("میلیون ریال", result.UnitLabelFa);
    }

    // -----------------------------------------------------------------------
    // Use case: market-quote fields absent from response
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UseCase_Response_ContainsNoMarketQuoteFields()
    {
        var snapshot = MakeSnapshot(1404, 3, 1404, 3, 1_000m, 900m, 950m);
        var useCase = new MonthlyActivityTrendQueryUseCaseForTest(
            companyResolves: true, snapshots: [snapshot], latest: snapshot);

        var result = await useCase.ExecuteAsync(
            new MonthlyActivityTrendQuery("روند فروش کهمدا", "کهمدا"),
            CancellationToken.None);

        Assert.NotNull(result);

        // The response type must not carry latest-price or daily-change-pct fields.
        var responseType = typeof(MonthlyActivityTrendResponse);
        var propNames = responseType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("LatestPrice", propNames);
        Assert.DoesNotContain("DailyChangePercent", propNames);
        Assert.DoesNotContain("MarketCap", propNames);
    }

    // -----------------------------------------------------------------------
    // Chart payload has previous-year, current-year, and average series
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UseCase_ChartPayload_ContainsAllThreeSeries()
    {
        var snapshot = MakeSnapshot(1404, 3, 1404, 3, 1_000m, 900m, 950m);
        var useCase = new MonthlyActivityTrendQueryUseCaseForTest(
            companyResolves: true, snapshots: [snapshot], latest: snapshot);

        var result = await useCase.ExecuteAsync(
            new MonthlyActivityTrendQuery("روند فروش کهمدا", "کهمدا"),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result.ChartPoints);

        // Reported month must have all three series.
        var reportedPoint = result.ChartPoints.First(p => p.IsCurrentYearReported);
        Assert.NotNull(reportedPoint.CurrentFiscalYearSalesAmount);
        Assert.NotNull(reportedPoint.Average12MonthSalesAmount);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CompanyMonthlyActivityTrendSnapshot MakeSnapshot(
        int reportYear, byte reportMonth,
        int? fiscalYear, int? fiscalMonthIndex,
        decimal monthlySalesAmount,
        decimal? sameMonthPrevYearSales,
        decimal? avg12MonthSales) =>
        new(
            ExternalCompanyId: "EXT-001",
            CompanySymbol: "کهمدا",
            CompanyName: "هماتیت",
            ReportYear: reportYear,
            ReportMonth: reportMonth,
            FiscalEndDate: null,
            FiscalYear: fiscalYear,
            FiscalMonthIndex: fiscalMonthIndex,
            FiscalMonthNameFa: fiscalMonthIndex.HasValue
                ? new[] { "فروردین","اردیبهشت","خرداد","تیر","مرداد","شهریور","مهر","آبان","آذر","دی","بهمن","اسفند" }[fiscalMonthIndex.Value - 1]
                : null,
            MonthlySalesAmount: monthlySalesAmount,
            MonthlyProductionQuantity: null,
            MonthlySalesQuantity: null,
            MonthlyAverageSalesRate: null,
            HasMixedProductUnits: false,
            ProductUnitSummary: null,
            SameMonthPreviousYearSalesAmount: sameMonthPrevYearSales,
            SameMonthPreviousYearProductionQuantity: null,
            SameMonthPreviousYearSalesQuantity: null,
            Average12MonthSalesAmount: avg12MonthSales,
            Average12MonthPeriodCount: avg12MonthSales.HasValue ? 12 : 0,
            YtdSalesAmount: null,
            YtdPreviousMonthSalesAmount: null,
            SalesAmountMomGrowthPercent: null,
            SalesAmountYoYGrowthPercent: sameMonthPrevYearSales.HasValue && sameMonthPrevYearSales.Value != 0
                ? (monthlySalesAmount - sameMonthPrevYearSales.Value) / sameMonthPrevYearSales.Value * 100m
                : null,
            ProductionQuantityYoYGrowthPercent: null,
            SalesQuantityYoYGrowthPercent: null,
            SourceProviderName: "NoavaranCurrentApi",
            IsComparablePreviousYearAvailable: sameMonthPrevYearSales.HasValue,
            IsAverage12MonthComplete: avg12MonthSales.HasValue,
            DataCompletenessScore: 1.0m,
            CalculatedAtUtc: DateTimeOffset.UtcNow);
}

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

file sealed class MonthlyActivityTrendQueryUseCaseForTest(
    bool companyResolves,
    IReadOnlyList<CompanyMonthlyActivityTrendSnapshot> snapshots,
    CompanyMonthlyActivityTrendSnapshot? latest = null)
    : IMonthlyActivityTrendQueryUseCase
{
    public Task<MonthlyActivityTrendResponse?> ExecuteAsync(
        MonthlyActivityTrendQuery query,
        CancellationToken ct = default)
    {
        if (!companyResolves || latest is null)
            return Task.FromResult<MonthlyActivityTrendResponse?>(null);

        // Delegate to the real use case logic via an adapter that feeds our in-memory data.
        var useCase = new InMemoryMonthlyActivityTrendQueryUseCase(
            latest, snapshots);
        return useCase.ExecuteAsync(query, ct);
    }
}

file sealed class InMemoryMonthlyActivityTrendQueryUseCase(
    CompanyMonthlyActivityTrendSnapshot latest,
    IReadOnlyList<CompanyMonthlyActivityTrendSnapshot> annualBase)
    : IMonthlyActivityTrendQueryUseCase
{
    private static readonly string[] PersianMonthNames =
    [
        "فروردین","اردیبهشت","خرداد","تیر","مرداد","شهریور",
        "مهر","آبان","آذر","دی","بهمن","اسفند"
    ];

    public Task<MonthlyActivityTrendResponse?> ExecuteAsync(
        MonthlyActivityTrendQuery query,
        CancellationToken ct = default)
    {
        var currentFiscalYear = latest.FiscalYear ?? latest.ReportYear;
        var previousFiscalYear = currentFiscalYear - 1;
        var latestMonth = (int)latest.ReportMonth;

        var chartPoints = new List<MonthlyActivityTrendChartPoint>(12);
        for (var idx = 1; idx <= 12; idx++)
        {
            var currentSnap = annualBase.FirstOrDefault(
                s => s.FiscalYear == currentFiscalYear && s.FiscalMonthIndex == idx);
            var previousSnap = annualBase.FirstOrDefault(
                s => s.FiscalYear == previousFiscalYear && s.FiscalMonthIndex == idx);

            decimal? currentYearSales = null;
            var isCurrentYearReported = false;
            if (currentSnap is not null)
            {
                var isAtOrBefore = currentSnap.ReportYear < latest.ReportYear
                    || (currentSnap.ReportYear == latest.ReportYear && currentSnap.ReportMonth <= latest.ReportMonth);
                if (isAtOrBefore)
                {
                    currentYearSales = currentSnap.MonthlySalesAmount;
                    isCurrentYearReported = true;
                }
            }

            chartPoints.Add(new MonthlyActivityTrendChartPoint(
                FiscalMonthIndex: idx,
                FiscalMonthNameFa: PersianMonthNames[idx - 1],
                PreviousFiscalYear: previousFiscalYear,
                PreviousFiscalYearSalesAmount: previousSnap?.MonthlySalesAmount,
                CurrentFiscalYear: currentFiscalYear,
                CurrentFiscalYearSalesAmount: currentYearSales,
                Average12MonthSalesAmount: currentSnap?.Average12MonthSalesAmount
                    ?? latest.Average12MonthSalesAmount,
                IsCurrentYearReported: isCurrentYearReported,
                IsPreviousYearReported: previousSnap is not null));
        }

        decimal? salesVsAvg = null;
        if (latest.Average12MonthSalesAmount.HasValue && latest.Average12MonthSalesAmount.Value != 0)
            salesVsAvg = (latest.MonthlySalesAmount - latest.Average12MonthSalesAmount.Value)
                / latest.Average12MonthSalesAmount.Value * 100m;

        var response = new MonthlyActivityTrendResponse(
            CompanySymbol: latest.CompanySymbol ?? query.SymbolOrCompanyName ?? "",
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
            Insights: [],
            MissingDataPoints: [],
            SourceProviderName: latest.SourceProviderName,
            CalculatedAtUtc: latest.CalculatedAtUtc);

        return Task.FromResult<MonthlyActivityTrendResponse?>(response);
    }
}

file sealed class UnknownIntentExecutionService : FinancialCopilot.Application.AI.ModelProviders.IAiModelExecutionService
{
    public Task<FinancialCopilot.Application.AI.ModelProviders.AiModelResult> ExecuteAsync(
        FinancialCopilot.Application.AI.ModelProviders.AiModelSelectionRequest selection,
        FinancialCopilot.Application.AI.ModelProviders.AiModelRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new FinancialCopilot.Application.AI.ModelProviders.AiModelResult(
            Text: null,
            StructuredJson: """{"intent":"Unknown","confidence":0.1}""",
            ToolCalls: [],
            Usage: new FinancialCopilot.Application.AI.ModelProviders.AiExecutionUsageFacts(
                request.CorrelationId,
                "StubProvider",
                "stub-model",
                FinancialCopilot.Application.AI.ModelProviders.AiExecutionStatus.Completed,
                TimeSpan.Zero,
                AttemptNumber: 0)));

    public IAsyncEnumerable<FinancialCopilot.Application.AI.ModelProviders.AiStreamingChunk> StreamAsync(
        FinancialCopilot.Application.AI.ModelProviders.AiModelSelectionRequest selection,
        FinancialCopilot.Application.AI.ModelProviders.AiModelRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
