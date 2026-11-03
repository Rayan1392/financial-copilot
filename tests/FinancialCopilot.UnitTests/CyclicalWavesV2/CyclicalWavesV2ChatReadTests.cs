using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests.CyclicalWavesV2;

public sealed class CyclicalWavesV2ChatReadTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FinancialIngestionDbContext _db;

    public CyclicalWavesV2ChatReadTests()
    {
        _connection.Open();
        _db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        // P/E: الف 4 (match), ب 0 (not computable), ج null (missing), د 8 (no match).
        Seed("الف", ("pe", 4m), ("ps", 0.8m), ("lastQuarterNetProfit", 10m), ("lastMonthSale", 1_500_000_000_000m),
            ("penultimateMonthSale", 1_000_000_000_000m), ("lastQuarterSale", 130m), ("penultimateQuarterSale", 100m),
            ("lastQuarterGrossProfitMargin", 35m), ("lastYearSameMonthSale", 500_000_000_000m));
        Seed("ب", ("pe", 0m), ("ps", 1.5m), ("lastQuarterNetProfit", -5m), ("lastMonthSale", 900_000_000_000m),
            ("penultimateMonthSale", 1_000_000_000_000m), ("lastQuarterSale", 100m), ("penultimateQuarterSale", 100m),
            ("lastQuarterGrossProfitMargin", 25m), ("lastYearSameMonthSale", 1_000_000_000_000m));
        Seed("ج", ("pe", null), ("ps", 0.5m), ("lastQuarterNetProfit", null), ("lastMonthSale", null),
            ("lastQuarterSale", 50m), ("penultimateQuarterSale", 0m), ("lastQuarterGrossProfitMargin", 31m));
        Seed("د", ("pe", 8m), ("ps", 2m), ("lastQuarterNetProfit", 3m), ("lastMonthSale", 1_100_000_000_000m),
            ("penultimateMonthSale", 1_000_000_000_000m), ("lastQuarterSale", 90m), ("penultimateQuarterSale", 100m),
            ("lastQuarterGrossProfitMargin", 40m), ("lastYearSameMonthSale", 1_000_000_000_000m));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Scanner_PeBelowFiveExcludesNotComputableAndMissing()
    {
        var table = await Scan("P/E کمتر از ۵", Condition("PE_TTM", ConditionOperator.LessThan, 5m));

        Assert.Equal(["الف"], Symbols(table));
        Assert.Null(table.Issue);
        Assert.Equal(1, table.ExecutionFacts.ExcludedByReason![CyclicalWavesV2ScannerExecutionService.PeNotComputableReason]);
        Assert.Equal(1, table.ExecutionFacts.ExcludedByReason[CyclicalWavesV2ScannerExecutionService.MissingValueReason]);
    }

    [Fact]
    public async Task Scanner_PsBelowOne() =>
        Assert.Equal(["الف", "ج"], Symbols(await Scan("P/S کمتر از ۱", Condition("PS_TTM", ConditionOperator.LessThan, 1m))).Order(StringComparer.Ordinal));

    [Fact]
    public async Task Scanner_NetProfitAboveZeroNeedsNoUnit() =>
        Assert.Equal(["الف", "د"], Symbols(await Scan("سود خالص فصل اخیر بیشتر از صفر", Condition("NET_PROFIT", ConditionOperator.GreaterThan, 0m))).Order(StringComparer.Ordinal));

    [Fact]
    public async Task Scanner_MonthlySalesThresholdIsConvertedFromTomanToIrr()
    {
        // 100 billion toman = 1e12 IRR: الف (1.5e12) and د (1.1e12) pass, ب (9e11) does not.
        var table = await Scan("فروش ماه اخیر بیشتر از ۱۰۰ میلیارد تومان", Condition("MONTHLY_SALES", ConditionOperator.GreaterThan, 100m));

        Assert.Equal(["الف", "د"], Symbols(table).Order(StringComparer.Ordinal));
        Assert.Equal("1,500,000", table.Rows.Single(row => row.SymbolCode == "الف").Cells["MONTHLY_SALES"].FormattedValue);
    }

    [Fact]
    public async Task Scanner_QuarterlySalesGrowthAboveTwentyPercent()
    {
        var table = await Scan("رشد فروش فصل اخیر نسبت به فصل قبل بیشتر از ۲۰ درصد",
            Condition("REVENUE", ConditionOperator.GreaterThan, 20m, GrowthComparison.QuarterOverQuarter));

        // الف grows 30%; ج has a zero base, so its growth is unavailable and it is counted missing.
        Assert.Equal(["الف"], Symbols(table));
        Assert.Equal(1, table.ExecutionFacts.ExcludedByReason![CyclicalWavesV2ScannerExecutionService.MissingValueReason]);
    }

    [Fact]
    public async Task Scanner_GrossMarginAboveThirtyPercent() =>
        Assert.Equal(["الف", "ج", "د"], Symbols(await Scan("حاشیه سود ناخالص بیش از ۳۰ درصد",
            Condition("GROSS_PROFIT_MARGIN", ConditionOperator.GreaterThan, 30m))).Order(StringComparer.Ordinal));

    [Fact]
    public async Task Scanner_BetweenIsTwoBoundConditions() =>
        Assert.Equal(["الف", "ب"], Symbols(await Scan("P/S بین ۰٫۶ و ۱٫۵",
            Condition("PS_TTM", ConditionOperator.GreaterThanOrEqual, 0.6m),
            Condition("PS_TTM", ConditionOperator.LessThanOrEqual, 1.5m))).Order(StringComparer.Ordinal));

    [Fact]
    public async Task Scanner_MonthOverMonthAndSameMonthGrowth()
    {
        Assert.Equal(["الف", "د"], Symbols(await Scan("رشد ماهانه فروش بیشتر از ۵ درصد",
            Condition("MONTHLY_SALES_GROWTH_MOM", ConditionOperator.GreaterThan, 5m))).Order(StringComparer.Ordinal));
        Assert.Equal(["الف", "د"], Symbols(await Scan("رشد فروش نسبت به ماه مشابه سال قبل مثبت",
            Condition("MONTHLY_SALES_GROWTH_YOY", ConditionOperator.GreaterThan, 0m))).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Scanner_MissingUnitAsksForClarificationBeforeReadingData()
    {
        var table = await Scan("فروش ماه اخیر بیشتر از ۱۰۰ میلیارد", Condition("MONTHLY_SALES", ConditionOperator.GreaterThan, 100m));

        Assert.Equal(ScannerExecutionIssueKind.ClarificationRequired, table.Issue!.Kind);
        Assert.Equal(CyclicalWavesV2ScannerExecutionService.UnitClarificationMessage, table.Issue.Message);
        Assert.Empty(table.Rows);
    }

    [Theory]
    [InlineData("EPS_GROWTH_QOQ", "رشد EPS")]
    [InlineData("EBIT", "EBIT")]
    [InlineData("MONTHLY_SALES_QUANTITY", "مقدار فروش")]
    public async Task Scanner_UnsupportedCriteriaAreUnavailableAndNamed(string code, string term)
    {
        var table = await Scan($"{term} بیشتر از ۱۰", Condition(code, ConditionOperator.GreaterThan, 10m, term: term));

        Assert.Equal(ScannerExecutionIssueKind.Unavailable, table.Issue!.Kind);
        Assert.Contains(term, table.Issue.Message);
    }

    [Fact]
    public async Task Scanner_IndustryUniverseIsUnavailable()
    {
        var service = Service();
        var table = await service.ExecuteAsync(new ScannerExecutionRequest(
            Plan("P/E کمتر از ۵", Condition("PE_TTM", ConditionOperator.LessThan, 5m)), DateOnly.FromDateTime(Now.UtcDateTime),
            Universe: new ScannerUniverseScope(IndustryCode: "27")), CancellationToken.None);

        Assert.Equal(ScannerExecutionIssueKind.Unavailable, table.Issue!.Kind);
    }

    [Theory]
    [InlineData(SalesGrowthComparisonBaseline.PreviousMonth, SalesGrowthThresholdKind.Percent, 20.0, new[] { "الف" })]
    [InlineData(SalesGrowthComparisonBaseline.SameMonthPreviousYear, SalesGrowthThresholdKind.Positive, null, new[] { "الف", "د" })]
    [InlineData(SalesGrowthComparisonBaseline.SameMonthPreviousYear, SalesGrowthThresholdKind.Multiple, 2.0, new[] { "الف" })]
    public async Task Scanner_Feature116SalesGrowthPlans(SalesGrowthComparisonBaseline baseline, SalesGrowthThresholdKind kind, double? threshold, string[] expected)
    {
        var plan = Plan("رشد فروش") with
        {
            SalesGrowth = new SalesGrowthScannerPlan(new SalesGrowthScannerSemantics(
                baseline, kind, ConditionOperator.GreaterThan, (decimal?)threshold, FilterOrigin.Explicit, SalesGrowthPolicyVersions.V1))
        };

        var table = await Service().ExecuteAsync(new ScannerExecutionRequest(plan, DateOnly.FromDateTime(Now.UtcDateTime)), CancellationToken.None);

        Assert.Equal(expected, Symbols(table).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Scanner_AveragePrevious12MonthsBaselineIsUnavailable()
    {
        var plan = Plan("رشد فروش") with
        {
            SalesGrowth = new SalesGrowthScannerPlan(new SalesGrowthScannerSemantics(
                SalesGrowthComparisonBaseline.AveragePrevious12Months, SalesGrowthThresholdKind.Positive, ConditionOperator.GreaterThan,
                null, FilterOrigin.Explicit, SalesGrowthPolicyVersions.V1))
        };

        var table = await Service().ExecuteAsync(new ScannerExecutionRequest(plan, DateOnly.FromDateTime(Now.UtcDateTime)), CancellationToken.None);

        Assert.Equal(ScannerExecutionIssueKind.Unavailable, table.Issue!.Kind);
    }

    [Fact]
    public async Task Lookup_ShowsMillionRialsPercentAndNotComputablePe()
    {
        var service = new CyclicalWavesV2SymbolMetricLookupService(new CyclicalWavesV2ReadModel(_db), TimeProvider.System);
        var result = await service.LookupAsync(new SymbolLookupRequest(
            [
                new SymbolLookupRequestPair("الف", new MetricCode("MONTHLY_SALES")),
                new SymbolLookupRequestPair("الف", new MetricCode("GROSS_PROFIT_MARGIN")),
                new SymbolLookupRequestPair("ب", new MetricCode("PE_TTM")),
                new SymbolLookupRequestPair("ب", new MetricCode("ROE")),
                new SymbolLookupRequestPair("ناشناس", new MetricCode("PE_TTM"))
            ],
            DateOnly.FromDateTime(Now.UtcDateTime)), CancellationToken.None);

        var alef = result.Rows.Single(row => row.SymbolCode == "الف");
        var be = result.Rows.Single(row => row.SymbolCode == "ب");
        Assert.Equal("1,500,000", alef.Cells["MONTHLY_SALES"].FormattedValue);
        Assert.Equal("35%", alef.Cells["GROSS_PROFIT_MARGIN"].FormattedValue);
        Assert.Equal(CyclicalWavesV2Presentation.NotComputable, be.Cells["PE_TTM"].FormattedValue);
        Assert.Equal(CellFreshnessStatus.Missing, be.Cells["ROE"].FreshnessStatus);
        Assert.Equal(["ناشناس"], result.UnresolvedSymbols);
        Assert.Contains(result.MissingDataWarnings, warning => warning.Contains("ROE"));
        Assert.Contains(result.Columns, column => column.DisplayName == "فروش آخرین ماه (میلیون ریال)");
    }

    [Fact]
    public async Task QuarterlyTable_UsesProviderLinesMillionRialsAndIrrEps()
    {
        Seed("فولاد", ("pe", 6m));
        using var fixture = CyclicalWavesV2Fixtures.Parse("statements-folad.json");
        var statements = FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2.CyclicalWavesV2PayloadParser
            .ParseIncomeStatements(fixture.RootElement);
        var store = new EfCoreCyclicalWavesV2Store(_db, Microsoft.Extensions.Logging.Abstractions.NullLogger<EfCoreCyclicalWavesV2Store>.Instance);
        await store.ApplyIncomeStatementsAsync(statements, Now, CancellationToken.None);
        var useCase = new CyclicalWavesV2QuarterlyIncomeStatementUseCase(_db, new CyclicalWavesV2ReadModel(_db), TimeProvider.System);

        var table = await useCase.ExecuteAsync(
            new FinancialCopilot.Application.FinancialData.Ingestion.QuarterlyFinancialReportTableQuery(string.Empty, "فولاد", 4),
            CancellationToken.None);

        Assert.Equal(FinancialCopilot.Application.FinancialData.Ingestion.QuarterlyFinancialReportOutcome.SuccessfulTable, table.Outcome);
        Assert.Equal(4, table.PeriodsNewestFirst.Count);
        var latest = table.PeriodsNewestFirst[0];
        Assert.Equal((1405, 2), (latest.FiscalYear, latest.Quarter));
        Assert.Equal("فروش", table.Rows[0].LabelFa);
        Assert.Equal(1_675_255_385m, table.Rows.Single(row => row.SourceItemId == 44).Cells[latest.Label].Value);
        Assert.Equal(-1_158_078_707m, table.Rows.Single(row => row.SourceItemId == 48).Cells[latest.Label].Value);
        var eps = table.Rows.Single(row => row.SourceItemId == 85);
        Assert.Equal("ریال", eps.Unit);
        Assert.Equal(243.55m, eps.Cells[latest.Label].Value);
        Assert.Equal(16, table.Rows.Count);
        var salesGrowth = table.Comparisons.Single(item => item.RowId == "cw2_44");
        Assert.Equal(FinancialCopilot.Application.FinancialData.Ingestion.QuarterlyComparisonState.Available, salesGrowth.State);
        Assert.Equal(CyclicalWavesV2CanonicalEntityResolver.ExternalIdPrefix, table.ExternalCompanyId[..4]);
    }

    [Fact]
    public async Task QuarterlyTable_UnknownTickerIsNotFoundWithoutAnyOtherSource()
    {
        var useCase = new CyclicalWavesV2QuarterlyIncomeStatementUseCase(_db, new CyclicalWavesV2ReadModel(_db), TimeProvider.System);

        var table = await useCase.ExecuteAsync(
            new FinancialCopilot.Application.FinancialData.Ingestion.QuarterlyFinancialReportTableQuery(string.Empty, "ناشناس"),
            CancellationToken.None);

        Assert.Equal(FinancialCopilot.Application.FinancialData.Ingestion.QuarterlyFinancialReportOutcome.CompanyNotFound, table.Outcome);
        Assert.Empty(table.Rows);
    }

    [Theory]
    [InlineData("[{\"sourceProvider\":\"NoavaranCurrentApi\"}]", "[]", DerivedMetricProvenance.NotLegacy)]
    [InlineData("[{\"sourceProvider\":\"CyclicalWaves\"}]", "[]", DerivedMetricProvenance.ExclusivelyLegacy)]
    [InlineData("[]", "[{\"SourceProvider\":\"CyclicalWaves\"}]", DerivedMetricProvenance.ExclusivelyLegacy)]
    [InlineData("[{\"sourceProvider\":\"CyclicalWaves\"},{\"sourceProvider\":\"NoavaranCurrentApi\"}]", "[]", DerivedMetricProvenance.MixedWithLegacy)]
    [InlineData("[]", "[]", DerivedMetricProvenance.NotLegacy)]
    [InlineData("[{\"source\":\"NoavaranCurrentApi\",\"fundamentalIndexId\":4101}]", "[]", DerivedMetricProvenance.NotLegacy)]
    [InlineData("not json", null, DerivedMetricProvenance.Undetermined)]
    [InlineData("{\"sourceProvider\":\"NoavaranCurrentApi\"}", "[]", DerivedMetricProvenance.Undetermined)]
    public void C1Filter_ClassifiesByStructuredProviderOnly(string source, string? dependency, DerivedMetricProvenance expected) =>
        Assert.Equal(expected, LegacyCyclicalWavesDerivedMetricFilter.Classify(source, dependency));

    [Theory]
    [InlineData("cw-avg-12m-monthly-sales-passthrough-v1", "[]", DerivedMetricProvenance.LegacyCalculationPolicy)]
    [InlineData("vendor-pe-ratio-passthrough-v1", "[]", DerivedMetricProvenance.LegacyCalculationPolicy)]
    [InlineData("nadpco-api-fundamental-index-source-v1", "[]", DerivedMetricProvenance.NotLegacy)]
    [InlineData("codal-ratio-source-v1", "[]", DerivedMetricProvenance.NotLegacy)]
    // A recorded provider wins over the policy: the policy set classifies only rows that record no provider.
    [InlineData("cw-avg-12m-monthly-sales-passthrough-v1", "[{\"sourceProvider\":\"CyclicalWaves\"}]", DerivedMetricProvenance.ExclusivelyLegacy)]
    [InlineData("cw-net-profit-passthrough-v1", "[{\"sourceProvider\":\"NoavaranCurrentApi\"}]", DerivedMetricProvenance.NotLegacy)]
    public void C1Filter_ClassifiesUnattributedRowsByTheirRegisteredLegacyOnlyPolicy(
        string policy, string source, DerivedMetricProvenance expected) =>
        Assert.Equal(expected, LegacyCyclicalWavesDerivedMetricFilter.Classify(source, "[]", policy));

    [Fact]
    public void C1Filter_LegacyOnlyPoliciesAreRegisteredCyclicalWavesPassthroughPolicies()
    {
        var registered = FinancialCopilot.Domain.Financial.Metrics.PhaseOneFinancialSemanticCatalog.Policies
            .Select(policy => policy.Version.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(LegacyCyclicalWavesDerivedMetricFilter.LegacyOnlyCalculationPolicies,
            policy => Assert.Contains(policy, registered));
    }

    [Fact]
    public void C1Filter_IgnoresTheMarkerInPolicyVersionAndValues()
    {
        var row = Derived("[{\"sourceProvider\":\"NoavaranCurrentApi\"}]");
        row.CalculationPolicyVersion = "CyclicalWaves-looking-policy";

        Assert.False(LegacyCyclicalWavesDerivedMetricFilter.IsExcluded(row));
    }

    [Fact]
    public void C1Filter_RemovesLegacyAndMixedRowsBeforeLatestSelectionWithoutTouchingThem()
    {
        var older = Derived("[{\"sourceProvider\":\"NoavaranCurrentApi\"}]", new DateOnly(2026, 3, 20));
        var legacy = Derived("[{\"sourceProvider\":\"CyclicalWaves\"}]", new DateOnly(2026, 6, 21));
        var mixed = Derived("[{\"sourceProvider\":\"CyclicalWaves\"},{\"sourceProvider\":\"NoavaranCurrentApi\"}]", new DateOnly(2026, 6, 21));
        var unattributedLegacy = Derived("[]", new DateOnly(2026, 6, 21));
        unattributedLegacy.CalculationPolicyVersion = "cw-avg-12m-monthly-sales-passthrough-v1";
        var input = new[] { older, legacy, mixed, unattributedLegacy };
        var before = input.Select(row => (row.Id, row.SourceEvidenceJson, row.Value)).ToArray();

        var kept = LegacyCyclicalWavesDerivedMetricFilter.Apply(input);

        Assert.Equal([older], kept);
        Assert.Equal(new DateOnly(2026, 3, 20), kept.MaxBy(row => row.PeriodEnd)!.PeriodEnd);
        Assert.Equal(before, input.Select(row => (row.Id, row.SourceEvidenceJson, row.Value)).ToArray());
    }

    [Fact]
    public void C1Filter_RemovedSettingsCannotReactivateLegacyOrMixedRows()
    {
        // The pre-C1-M switches are gone from FinancialDataOptions; binding them is a no-op.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FinancialData:ExcludeLegacyCyclicalWavesDerivedMetrics"] = "false",
                ["FinancialData:LegacyCyclicalWavesMixedProvenance"] = "Include"
            })
            .Build();
        var options = new FinancialDataOptions();
        configuration.GetSection(FinancialDataOptions.SectionName).Bind(options);

        Assert.Equal(["ChatProvider"], typeof(FinancialDataOptions).GetProperties().Select(property => property.Name));
        Assert.True(LegacyCyclicalWavesDerivedMetricFilter.IsExcluded(Derived("[{\"sourceProvider\":\"CyclicalWaves\"}]")));
        Assert.True(LegacyCyclicalWavesDerivedMetricFilter.IsExcluded(
            Derived("[{\"sourceProvider\":\"CyclicalWaves\"},{\"sourceProvider\":\"NoavaranCurrentApi\"}]")));
    }

    [Theory]
    [InlineData("شتوکا- گزارش عملکرد مهر 1405", "شتوکا", true)]
    [InlineData("وبملت", "وبملت", true)]
    [InlineData("اخابر؛", "اخابر", true)]
    [InlineData("خودرو- لیدر فشل بازار", "خودرو", true)]
    [InlineData("رصد معاملات عمده‌ها - سه شنبه 5 آبان 1405", "فولاد", false)]
    [InlineData("فولادی - گزارش", "فولاد", false)]
    public void Analysis_TitleLeadingSegmentMatchesTicker(string title, string ticker, bool matches) =>
        Assert.Equal(matches, CyclicalWavesV2ComprehensiveAnalysisQuery.MatchesTicker(
            title, [], FinancialCopilot.Domain.Financial.Services.PersianSymbolNormalizer.Normalize(ticker)));

    [Fact]
    public void Analysis_TagMatchesTicker() =>
        Assert.True(CyclicalWavesV2ComprehensiveAnalysisQuery.MatchesTicker(
            "گزارش بازار", [new CyclicalWavesV2AnalysisTagRow { Name = "فولاد", NormalizedName = "فولاد" }], "فولاد"));

    [Theory]
    [InlineData("نمودار_P_S", "PS")]
    [InlineData("P-E", "PE")]
    [InlineData("تحلیل تکنیکال", "تحلیلتکنیکال")]
    [InlineData("گزارش_فصلی", "گزارشفصلی")]
    public void Analysis_TopicSlugsNormalizeToStoredTagNames(string topic, string normalized) =>
        Assert.Equal(normalized, CyclicalWavesV2ComprehensiveAnalysisQuery.NormalizeTopic(topic));

    private void Seed(string ticker, params (string Field, decimal? Value)[] values)
    {
        var id = Guid.NewGuid();
        _db.CyclicalWavesV2Tickers.Add(new CyclicalWavesV2TickerRow
        {
            Id = id, Ticker = ticker, NormalizedTicker = FinancialCopilot.Domain.Financial.Services.PersianSymbolNormalizer.Normalize(ticker),
            IsActive = true, CreatedAt = Now, UpdatedAt = Now, LastSeenInCatalogAt = Now
        });
        _db.CyclicalWavesV2ScreenerMetadata.Add(new CyclicalWavesV2ScreenerMetadataRow
        {
            TickerId = id, LastMonthSaleDate = new DateOnly(2026, 9, 22), LastQuarterDate = new DateOnly(2026, 9, 22),
            LastChangedAt = Now, LastSyncedAt = Now
        });
        foreach (var (field, value) in values)
        {
            _db.CyclicalWavesV2FinancialValues.Add(new CyclicalWavesV2FinancialValueRow
            {
                TickerId = id, MetricDefinitionId = ScreenerMetricCatalog.Get(field).Id, NumericValue = value, UpdatedAt = Now
            });
        }

        _db.SaveChanges();
    }

    private CyclicalWavesV2ScannerExecutionService Service() =>
        new(new CyclicalWavesV2ReadModel(_db), new ScannerResultColumnPolicy(), new ScannerResultRanker(), TimeProvider.System);

    private Task<ScannerTableResult> Scan(string query, params ScannerCondition[] conditions) =>
        Service().ExecuteAsync(new ScannerExecutionRequest(Plan(query, conditions), DateOnly.FromDateTime(Now.UtcDateTime)), CancellationToken.None);

    private static ScannerQueryPlan Plan(string query, params ScannerCondition[] conditions) =>
        new(Guid.NewGuid(), query, "fa", conditions, [], false, null, [], [], Now, "test-v1");

    private static ScannerCondition Condition(string code, ConditionOperator op, decimal threshold, GrowthComparison? comparison = null, string? term = null) =>
        new(new ScannerMetricReference(term ?? code, new MetricCode(code), new MetricVersion("v1"), new CalculationPolicyVersion("test-v1"),
            FiscalPeriodType.LatestQuarter, comparison), op, threshold, FilterOrigin.Explicit);

    private static string[] Symbols(ScannerTableResult table) => table.Rows.Select(row => row.SymbolCode).ToArray();

    private static DerivedMetricRow Derived(string sourceEvidence, DateOnly? periodEnd = null) => new()
    {
        Id = Guid.NewGuid(),
        ExternalCompanyId = "100",
        MetricCode = "PE_TTM",
        PeriodEnd = periodEnd ?? new DateOnly(2026, 6, 21),
        SourceEvidenceJson = sourceEvidence,
        DependencyEvidenceJson = "[]"
    };
}
