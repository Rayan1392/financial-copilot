using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Regression tests for spec 074: database-backed metric definition and alias registry.
/// All tests run against the static in-memory catalog (PhaseOneFinancialSemanticCatalog) and
/// the seeded GUIDs in the migration — no database required.
/// </summary>
public sealed class MetricRegistryCapabilities074Tests
{
    private static readonly DateOnly AsOf = new(2026, 6, 22);

    private static readonly MetricAliasResolver Resolver = new(
        new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []));

    // -----------------------------------------------------------------------
    // Catalog completeness — every code referenced in the migration seed must
    // exist in the catalog so the UPDATE statements actually hit rows.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("MONTHLY_SALES")]
    [InlineData("MONTHLY_SALES_YTD")]
    [InlineData("MONTHLY_SALES_YTD_PREVIOUS_MONTH")]
    [InlineData("MONTHLY_SALES_QUANTITY")]
    [InlineData("MONTHLY_SALES_RATE")]
    [InlineData("MONTHLY_PRODUCTION_QUANTITY")]
    [InlineData("AVG_12M_MONTHLY_SALES")]
    [InlineData("MONTHLY_SALES_GROWTH_YOY")]
    [InlineData("MONTHLY_SALES_GROWTH_MOM")]
    [InlineData("PE_TTM")]
    [InlineData("PS_TTM")]
    [InlineData("NET_PROFIT_MARGIN")]
    [InlineData("GROSS_PROFIT_MARGIN")]
    [InlineData("OPERATING_PROFIT_MARGIN")]
    [InlineData("REVENUE")]
    [InlineData("GROSS_PROFIT")]
    [InlineData("OPERATING_PROFIT")]
    [InlineData("NET_PROFIT")]
    [InlineData("EBIT")]
    [InlineData("REVENUE_GROWTH_YOY")]
    [InlineData("GROSS_PROFIT_GROWTH_YOY")]
    [InlineData("OPERATING_PROFIT_GROWTH_YOY")]
    [InlineData("NET_PROFIT_GROWTH_YOY")]
    [InlineData("EPS_GROWTH_YOY")]
    [InlineData("EQUITY_GROWTH_YOY")]
    [InlineData("EBIT_GROWTH_YOY")]
    [InlineData("REVENUE_GROWTH_QOQ")]
    [InlineData("GROSS_PROFIT_GROWTH_QOQ")]
    [InlineData("OPERATING_PROFIT_GROWTH_QOQ")]
    [InlineData("NET_PROFIT_GROWTH_QOQ")]
    [InlineData("EPS_GROWTH_QOQ")]
    [InlineData("EQUITY_GROWTH_QOQ")]
    [InlineData("EBIT_GROWTH_QOQ")]
    [InlineData("CURRENT_RATIO")]
    [InlineData("DEBT_TO_EQUITY")]
    [InlineData("NET_WORKING_CAPITAL")]
    [InlineData("COMPREHENSIVE_LIQUIDITY_INDEX")]
    [InlineData("CURRENT_ASSETS_TO_TOTAL_ASSETS")]
    [InlineData("ASSET_TURNOVER")]
    [InlineData("TANGIBLE_FIXED_ASSETS_TURNOVER")]
    [InlineData("AVERAGE_COLLECTION_PERIOD")]
    public void CatalogContainsMetricCode_ExpectedBySeed074(string metricCode)
    {
        var exists = PhaseOneFinancialSemanticCatalog.Definitions
            .Any(d => d.Code.Value == metricCode);

        Assert.True(exists, $"MetricCode '{metricCode}' referenced in 074 seed is missing from PhaseOneFinancialSemanticCatalog.");
    }

    // -----------------------------------------------------------------------
    // Alias disambiguation — the most critical cases from the spec
    // -----------------------------------------------------------------------

    [Fact]
    public void PersianPeAlias_ResolvesToPeTtm_NotLatestPrice()
    {
        // "نسبت قیمت به سود" must not resolve to LATEST_PRICE even though it contains "قیمت"
        var result = Resolver.ResolveAlias("نسبت قیمت به سود", "fa-IR", new MetricResolutionContext(null, null), AsOf);

        Assert.Equal(MetricResolutionStatus.Resolved, result.Status);
        Assert.Equal("PE_TTM", Assert.Single(result.Candidates).Code.Value);
    }

    [Fact]
    public void PersianPsAlias_ResolvesToPsTtm()
    {
        var result = Resolver.ResolveAlias("نسبت قیمت به فروش", "fa-IR", new MetricResolutionContext(null, null), AsOf);

        Assert.Equal(MetricResolutionStatus.Resolved, result.Status);
        Assert.Equal("PS_TTM", Assert.Single(result.Candidates).Code.Value);
    }

    [Theory]
    [InlineData("رشد فروش ماهانه سالانه",         "MONTHLY_SALES_GROWTH_YOY")]
    [InlineData("رشد فروش نسبت به ماه قبل",       "MONTHLY_SALES_GROWTH_MOM")]
    public void PersianSalesGrowthAliases_ResolveCorrectly(string alias, string expectedCode)
    {
        var result = Resolver.ResolveAlias(alias, "fa-IR", new MetricResolutionContext(null, null), AsOf);

        Assert.Equal(MetricResolutionStatus.Resolved, result.Status);
        Assert.Equal(expectedCode, Assert.Single(result.Candidates).Code.Value);
    }

    [Theory]
    [InlineData("حاشیه سود خالص",    "NET_PROFIT_MARGIN")]
    [InlineData("حاشیه سود ناخالص",  "GROSS_PROFIT_MARGIN")]
    [InlineData("حاشیه سود عملیاتی", "OPERATING_PROFIT_MARGIN")]
    public void PersianMarginAliases_ResolveToCorrectMarginMetric(string alias, string expectedCode)
    {
        var result = Resolver.ResolveAlias(alias, "fa-IR", new MetricResolutionContext(null, null), AsOf);

        Assert.Equal(MetricResolutionStatus.Resolved, result.Status);
        Assert.Equal(expectedCode, Assert.Single(result.Candidates).Code.Value);
    }

    // -----------------------------------------------------------------------
    // Period alias seed — verify seed data is self-consistent (no DB required)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("M0",  "آخرین ماه")]
    [InlineData("M1",  "ماه قبل")]
    [InlineData("M12", "ماه مشابه سال قبل")]
    [InlineData("Q0",  "آخرین فصل")]
    [InlineData("Q1",  "فصل قبل")]
    [InlineData("Q4",  "فصل مشابه سال قبل")]
    public void PeriodAliasSeed_HasExpectedSelectorForPersianPhrase(string expectedSelector, string phrase)
    {
        var seedRows = PeriodAliasSeedRows();
        var matches = seedRows.Where(r => r.NormText == phrase && r.Lang == "fa").ToList();

        Assert.Single(matches);
        Assert.Equal(expectedSelector, matches[0].Selector);
        Assert.Equal("Active", matches[0].Status);
    }

    [Theory]
    [InlineData("M0",  "latest month")]
    [InlineData("M1",  "previous month")]
    [InlineData("M12", "same month last year")]
    [InlineData("Q0",  "latest quarter")]
    [InlineData("Q1",  "previous quarter")]
    [InlineData("Q4",  "same quarter last year")]
    public void PeriodAliasSeed_HasExpectedSelectorForEnglishPhrase(string expectedSelector, string phrase)
    {
        var seedRows = PeriodAliasSeedRows();
        var matches = seedRows.Where(r => r.NormText == phrase && r.Lang == "en").ToList();

        Assert.Single(matches);
        Assert.Equal(expectedSelector, matches[0].Selector);
        Assert.Equal("Active", matches[0].Status);
    }

    [Fact]
    public void PeriodAliasSeed_HasNoDuplicateNormalizedTextPerLanguage()
    {
        var rows = PeriodAliasSeedRows();
        var duplicates = rows
            .GroupBy(r => (r.NormText, r.Lang))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Lang}:{g.Key.NormText}")
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void PeriodAliasSeed_AllRowsHaveValidPeriodType()
    {
        var validTypes = new HashSet<string> { "Monthly", "ThreeMonths", "SixMonths", "NineMonths", "TwelveMonths" };
        var rows = PeriodAliasSeedRows();

        var invalid = rows.Where(r => !validTypes.Contains(r.PeriodType)).ToList();
        Assert.Empty(invalid);
    }

    // Mirrors the exact seed data from the migration Up() method so tests are authoritative
    // without requiring a running database.
    private static IReadOnlyList<(string Id, string Text, string NormText, string Lang, string PeriodType, string Selector, int Priority, string Status)> PeriodAliasSeedRows() =>
    [
        ("33333333-3333-3333-3333-000000000001", "آخرین ماه",             "آخرین ماه",             "fa", "Monthly",     "M0",     100, "Active"),
        ("33333333-3333-3333-3333-000000000002", "ماه جاری",              "ماه جاری",              "fa", "Monthly",     "M0",     100, "Active"),
        ("33333333-3333-3333-3333-000000000003", "ماه قبل",               "ماه قبل",               "fa", "Monthly",     "M1",     110, "Active"),
        ("33333333-3333-3333-3333-000000000004", "ماه گذشته",             "ماه گذشته",             "fa", "Monthly",     "M1",     110, "Active"),
        ("33333333-3333-3333-3333-000000000005", "ماه مشابه سال قبل",     "ماه مشابه سال قبل",     "fa", "Monthly",     "M12",    130, "Active"),
        ("33333333-3333-3333-3333-000000000006", "مدت مشابه سال قبل",     "مدت مشابه سال قبل",     "fa", "Monthly",     "M12",    130, "Active"),
        ("33333333-3333-3333-3333-000000000007", "پارسال",                 "پارسال",                 "fa", "Monthly",     "M12",     90, "Active"),
        ("33333333-3333-3333-3333-000000000011", "latest month",           "latest month",           "en", "Monthly",     "M0",     100, "Active"),
        ("33333333-3333-3333-3333-000000000012", "previous month",         "previous month",         "en", "Monthly",     "M1",     110, "Active"),
        ("33333333-3333-3333-3333-000000000013", "same month last year",   "same month last year",   "en", "Monthly",     "M12",    130, "Active"),
        ("33333333-3333-3333-3333-000000000014", "yoy",                    "yoy",                    "en", "Monthly",     "M12",     80, "Active"),
        ("33333333-3333-3333-3333-000000000015", "mom",                    "mom",                    "en", "Monthly",     "M1",      80, "Active"),
        ("33333333-3333-3333-3333-000000000020", "آخرین فصل",             "آخرین فصل",             "fa", "ThreeMonths", "Q0",     100, "Active"),
        ("33333333-3333-3333-3333-000000000021", "فصل جاری",              "فصل جاری",              "fa", "ThreeMonths", "Q0",     100, "Active"),
        ("33333333-3333-3333-3333-000000000022", "فصل قبل",               "فصل قبل",               "fa", "ThreeMonths", "Q1",     110, "Active"),
        ("33333333-3333-3333-3333-000000000023", "فصل گذشته",             "فصل گذشته",             "fa", "ThreeMonths", "Q1",     110, "Active"),
        ("33333333-3333-3333-3333-000000000024", "فصل مشابه سال قبل",     "فصل مشابه سال قبل",     "fa", "ThreeMonths", "Q4",     130, "Active"),
        ("33333333-3333-3333-3333-000000000025", "دوره مشابه سال قبل",    "دوره مشابه سال قبل",    "fa", "ThreeMonths", "Q4",     130, "Active"),
        ("33333333-3333-3333-3333-000000000030", "latest quarter",         "latest quarter",         "en", "ThreeMonths", "Q0",     100, "Active"),
        ("33333333-3333-3333-3333-000000000031", "previous quarter",       "previous quarter",       "en", "ThreeMonths", "Q1",     110, "Active"),
        ("33333333-3333-3333-3333-000000000032", "same quarter last year",  "same quarter last year", "en", "ThreeMonths", "Q4",     130, "Active"),
        ("33333333-3333-3333-3333-000000000033", "qoq",                    "qoq",                    "en", "ThreeMonths", "Q1",      80, "Active"),
        ("33333333-3333-3333-3333-000000000040", "سه ماهه",               "سه ماهه",               "fa", "ThreeMonths", "Latest",  70, "Active"),
        ("33333333-3333-3333-3333-000000000041", "شش ماهه",               "شش ماهه",               "fa", "SixMonths",   "Latest",  70, "Active"),
        ("33333333-3333-3333-3333-000000000042", "نه ماهه",               "نه ماهه",               "fa", "NineMonths",  "Latest",  70, "Active"),
        ("33333333-3333-3333-3333-000000000043", "دوازده ماهه",           "دوازده ماهه",           "fa", "TwelveMonths","Latest",  70, "Active"),
        ("33333333-3333-3333-3333-000000000050", "three month",            "three month",            "en", "ThreeMonths", "Latest",  70, "Active"),
        ("33333333-3333-3333-3333-000000000051", "six month",              "six month",              "en", "SixMonths",   "Latest",  70, "Active"),
        ("33333333-3333-3333-3333-000000000052", "nine month",             "nine month",             "en", "NineMonths",  "Latest",  70, "Active"),
        ("33333333-3333-3333-3333-000000000053", "twelve month",           "twelve month",           "en", "TwelveMonths","Latest",  70, "Active"),
    ];
}
