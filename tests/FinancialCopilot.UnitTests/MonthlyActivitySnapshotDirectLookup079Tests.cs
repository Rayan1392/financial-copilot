using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class MonthlyActivitySnapshotDirectLookup079Tests
{
    private static readonly DateOnly AsOf = new(2026, 6, 25);
    private static readonly Guid TenantId = Guid.Parse("91b7838d-f7f6-4b68-967d-b7c274aa0d27");

    [Fact]
    public async Task SnapshotLookup_MonthlySalesRequest_ReturnsSnapshotBackedCompanionColumns()
    {
        var service = BuildSnapshotService([
            MakeSnapshot(reportYear: 1405, reportMonth: 2, monthlySalesAmount: 1124787m, ytdSalesAmount: 4231708m, ytdPreviousMonthSalesAmount: 2503882m, sameMonthPreviousYearSalesAmount: 992763m),
            MakeSnapshot(reportYear: 1405, reportMonth: 1, monthlySalesAmount: 1370915m)
        ]);

        var result = await service.LookupAsync(
            new SymbolLookupRequest(
                [new SymbolLookupRequestPair("خفنر", new MetricCode("MONTHLY_SALES"))],
                AsOf,
                QueryText: "آخرین فروش ماهانه خفنر؟"),
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("1,124,787", row.Cells["MONTHLY_SALES"].FormattedValue);
        Assert.Equal("992,763", row.Cells["MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH"].FormattedValue);
        Assert.Equal("4,231,708", row.Cells["MONTHLY_SALES_YTD"].FormattedValue);
        Assert.Equal("2,503,882", row.Cells["MONTHLY_SALES_YTD_PREVIOUS_MONTH"].FormattedValue);
    }

    [Fact]
    public async Task SnapshotLookup_ProductionGrowthAndSalesToProductionRatio_UsesLatestSnapshot()
    {
        var service = BuildSnapshotService([
            MakeSnapshot(
                reportYear: 1405,
                reportMonth: 2,
                monthlySalesAmount: 1124787m,
                monthlyProductionQuantity: 5000m,
                monthlySalesQuantity: 3750m,
                productionQuantityYoYGrowthPercent: 18.5m)
        ]);

        var result = await service.LookupAsync(
            new SymbolLookupRequest(
                [
                    new SymbolLookupRequestPair("خفنر", new MetricCode("MONTHLY_PRODUCTION_GROWTH_YOY")),
                    new SymbolLookupRequestPair("خفنر", new MetricCode("MONTHLY_SALES_TO_PRODUCTION_RATIO"))
                ],
                AsOf,
                QueryText: "میزان رشد تولید و نسبت فروش به تولید خفنر"),
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("18.5", row.Cells["MONTHLY_PRODUCTION_GROWTH_YOY"].FormattedValue);
        Assert.Equal("0.75", row.Cells["MONTHLY_SALES_TO_PRODUCTION_RATIO"].FormattedValue);
    }

    [Fact]
    public async Task Switcher_TrendSnapshotMode_UsesSnapshotService_ForSupportedMonthlyMetrics()
    {
        var legacy = new RecordingLookupService();
        var snapshot = new RecordingLookupService();
        var sut = new SwitchableMonthlyActivitySymbolMetricLookupService(
            legacy,
            snapshot,
            Options.Create(new MonthlyActivityLookupOptions
            {
                DirectLookupSourceMode = MonthlyActivityDirectLookupSourceMode.TrendSnapshot
            }));

        await sut.LookupAsync(
            new SymbolLookupRequest(
                [new SymbolLookupRequestPair("خفنر", new MetricCode("MONTHLY_SALES"))],
                AsOf,
                QueryText: "آخرین فروش ماهانه خفنر"),
            CancellationToken.None);

        Assert.Equal(0, legacy.Calls);
        Assert.Equal(1, snapshot.Calls);
    }

    [Fact]
    public async Task Switcher_DerivedMetricsMode_PreservesLegacyPath()
    {
        var legacy = new RecordingLookupService();
        var snapshot = new RecordingLookupService();
        var sut = new SwitchableMonthlyActivitySymbolMetricLookupService(
            legacy,
            snapshot,
            Options.Create(new MonthlyActivityLookupOptions
            {
                DirectLookupSourceMode = MonthlyActivityDirectLookupSourceMode.DerivedMetrics
            }));

        await sut.LookupAsync(
            new SymbolLookupRequest(
                [new SymbolLookupRequestPair("خفنر", new MetricCode("MONTHLY_SALES"))],
                AsOf,
                QueryText: "آخرین فروش ماهانه خفنر"),
            CancellationToken.None);

        Assert.Equal(1, legacy.Calls);
        Assert.Equal(0, snapshot.Calls);
    }

    [Theory]
    [InlineData("میزان رشد تولید خفنر؟", "MONTHLY_PRODUCTION_GROWTH_YOY")]
    [InlineData("نسبت فروش به تولید خفنر؟", "MONTHLY_SALES_TO_PRODUCTION_RATIO")]
    [InlineData("فروش ماه مشابه دوره قبل خفنر؟", "MONTHLY_SALES")]
    public async Task Parser_DirectMonthlySnapshotQuestions_ResolveToExpectedMetric(
        string userMessage,
        string expectedMetricCode)
    {
        var resolver = new MetricAliasResolver(new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []));
        var parser = new LlmSymbolLookupParser(
            new StubAiModelExecutionService(BuildClarificationJson()),
            resolver,
            new DirectMetricRoutingRegistry(
                resolver,
                new FinancialCopilot.Infrastructure.Financial.Semantics.DefaultMetricAliasExpressionNormalizer()));

        var result = await parser.ParseAsync(
            new SymbolLookupParseRequest(userMessage, "fa", "corr-079", TenantId, AsOf),
            CancellationToken.None);

        var pair = Assert.Single(result.Pairs);
        Assert.Equal(LookupParseStatus.Parsed, result.Status);
        Assert.Equal(expectedMetricCode, pair.ResolvedMetricCode?.Value);
    }

    private static SnapshotMonthlyActivitySymbolMetricLookupService BuildSnapshotService(
        IReadOnlyList<CompanyMonthlyActivityTrendSnapshot> snapshots) =>
        new(
            new StubTrendRepository(snapshots),
            new StubCompanyResolver(),
            new DirectMetricRoutingRegistry(
                new MetricAliasResolver(new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, [])),
                new FinancialCopilot.Infrastructure.Financial.Semantics.DefaultMetricAliasExpressionNormalizer()),
            TimeProvider.System);

    private static CompanyMonthlyActivityTrendSnapshot MakeSnapshot(
        int reportYear,
        byte reportMonth,
        decimal monthlySalesAmount,
        decimal? monthlyProductionQuantity = null,
        decimal? monthlySalesQuantity = null,
        decimal? sameMonthPreviousYearSalesAmount = null,
        decimal? ytdSalesAmount = null,
        decimal? ytdPreviousMonthSalesAmount = null,
        decimal? productionQuantityYoYGrowthPercent = null) =>
        new(
            ExternalCompanyId: "368",
            CompanySymbol: "خفنر",
            CompanyName: "فنرسازی خاور",
            ReportYear: reportYear,
            ReportMonth: reportMonth,
            FiscalEndDate: "12/29",
            FiscalYear: 1405,
            FiscalMonthIndex: reportMonth,
            FiscalMonthNameFa: "اردیبهشت",
            MonthlySalesAmount: monthlySalesAmount,
            MonthlyProductionQuantity: monthlyProductionQuantity,
            MonthlySalesQuantity: monthlySalesQuantity,
            MonthlyAverageSalesRate: null,
            HasMixedProductUnits: false,
            ProductUnitSummary: null,
            SameMonthPreviousYearSalesAmount: sameMonthPreviousYearSalesAmount,
            SameMonthPreviousYearProductionQuantity: null,
            SameMonthPreviousYearSalesQuantity: null,
            Average12MonthSalesAmount: 57549287m,
            Average12MonthPeriodCount: 12,
            YtdSalesAmount: ytdSalesAmount,
            YtdPreviousMonthSalesAmount: ytdPreviousMonthSalesAmount,
            SalesAmountMomGrowthPercent: 4.2m,
            SalesAmountYoYGrowthPercent: 13.3m,
            ProductionQuantityYoYGrowthPercent: productionQuantityYoYGrowthPercent,
            SalesQuantityYoYGrowthPercent: 11.4m,
            SourceProviderName: "NoavaranCurrentApi",
            IsComparablePreviousYearAvailable: true,
            IsAverage12MonthComplete: true,
            DataCompletenessScore: 1m,
            CalculatedAtUtc: new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero));

    private static string BuildClarificationJson() =>
        """
        {
          "detectedLanguage": "fa",
          "pairs": [],
          "clarificationRequired": true,
          "clarificationMessage": "No pairs"
        }
        """;

    private sealed class StubTrendRepository(IReadOnlyList<CompanyMonthlyActivityTrendSnapshot> snapshots)
        : ICompanyMonthlyActivityTrendSnapshotRepository
    {
        public Task UpsertAsync(CompanyMonthlyActivityTrendSnapshotUpsertRow row, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetCompanyTrendAsync(string externalCompanyId, int fromYear, int fromMonth, int toYear, int toMonth, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>>(snapshots);

        public Task<CompanyMonthlyActivityTrendSnapshot?> GetLatestAsync(string externalCompanyId, CancellationToken ct = default) =>
            Task.FromResult<CompanyMonthlyActivityTrendSnapshot?>(snapshots.FirstOrDefault());

        public Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetLatestAvailablePeriodsAsync(string externalCompanyId, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>>(snapshots.Take(count).ToList());

        public Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetAnnualComparisonBaseAsync(string externalCompanyId, int latestReportYear, int latestReportMonth, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>>(snapshots);
    }

    private sealed class StubCompanyResolver : ICompanyResolverService
    {
        public Task<ResolvedCompany?> ResolveBySymbolAsync(string symbol, CancellationToken ct = default) =>
            Task.FromResult<ResolvedCompany?>(new ResolvedCompany(
                Guid.Parse("b8784cdb-464b-448e-b98d-a4f0a56c3406"),
                "368",
                "خفنر",
                null,
                null,
                null,
                null,
                "خفنر",
                "خفنر"));
    }

    private sealed class RecordingLookupService
        : ILegacySymbolMetricLookupService, ISnapshotMonthlyActivitySymbolMetricLookupService
    {
        public int Calls { get; private set; }

        public Task<SymbolLookupTableResult> LookupAsync(SymbolLookupRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new SymbolLookupTableResult(
                Guid.NewGuid(),
                [],
                [],
                new ScannerExecutionFacts(DateTimeOffset.UtcNow, TimeSpan.Zero, 0, 0, false, 1, 1, 1),
                [],
                [],
                request.Pairs.Select(p => p.MetricCode.Value).ToList()));
        }
    }

    private sealed class StubAiModelExecutionService(string json)
        : FinancialCopilot.Application.AI.ModelProviders.IAiModelExecutionService
    {
        public Task<FinancialCopilot.Application.AI.ModelProviders.AiModelResult> ExecuteAsync(
            FinancialCopilot.Application.AI.ModelProviders.AiModelSelectionRequest selection,
            FinancialCopilot.Application.AI.ModelProviders.AiModelRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FinancialCopilot.Application.AI.ModelProviders.AiModelResult(
                Text: null,
                StructuredJson: json,
                ToolCalls: [],
                Usage: new FinancialCopilot.Application.AI.ModelProviders.AiExecutionUsageFacts(
                    request.CorrelationId,
                    "Stub",
                    "Stub",
                    FinancialCopilot.Application.AI.ModelProviders.AiExecutionStatus.Completed,
                    TimeSpan.Zero,
                    0)));

        public IAsyncEnumerable<FinancialCopilot.Application.AI.ModelProviders.AiStreamingChunk> StreamAsync(
            FinancialCopilot.Application.AI.ModelProviders.AiModelSelectionRequest selection,
            FinancialCopilot.Application.AI.ModelProviders.AiModelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
