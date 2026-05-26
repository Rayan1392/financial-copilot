using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Domain.Financial.ValueObjects;

namespace FinancialCopilot.UnitTests;

public sealed class FinancialDomainTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-05-25T09:00:00Z");
    private static readonly DateTimeOffset SyncedAt = DateTimeOffset.Parse("2026-05-26T09:00:00Z");

    [Fact]
    public void NormalizedFinancialEntities_PreserveCanonicalIdentityAndExternalReferences()
    {
        var industry = new Industry(Guid.NewGuid(), "Chemicals");
        var company = new Company(
            Guid.NewGuid(),
            "Example Petrochemical",
            industry.Id,
            [new ProviderExternalReference("DataProvider", "company-104")]);
        var symbol = new Symbol(
            Guid.NewGuid(),
            company.Id,
            new SymbolCode("  chem1 "),
            [new ProviderExternalReference("DataProvider", "symbol-72")]);

        Assert.Equal("CHEM1", symbol.Code.Value);
        Assert.Equal(industry.Id, company.IndustryId);
        Assert.Equal("company-104", company.ExternalReferences.Single().ExternalId);
        Assert.Equal("symbol-72", symbol.ExternalReferences.Single().ExternalId);
    }

    [Fact]
    public void ReportingAndMarketEntities_RepresentMissingAndStaleObservations()
    {
        var companyId = Guid.NewGuid();
        var symbolId = Guid.NewGuid();
        var quality = new FinancialObservationQuality(
            ObservedAt,
            SyncedAt,
            [
                new FinancialDataWarning(FinancialDataWarningCode.MissingData, "Sales amount is unavailable."),
                new FinancialDataWarning(FinancialDataWarningCode.StaleData, "Last quote is older than policy.")
            ]);
        var source = new FinancialSourceEvidence("DataProvider", ObservedAt, SyncedAt, "report-1");
        var quarter = FiscalPeriod.Closed(
            FiscalPeriodType.ThreeMonths,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31));
        var month = FiscalPeriod.Closed(
            FiscalPeriodType.Monthly,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 30));

        var statement = new FinancialStatement(
            Guid.NewGuid(),
            companyId,
            FinancialStatementType.IncomeStatement,
            quarter,
            source,
            [new FinancialStatementLineItem(new MetricCode("NET_PROFIT"), null, quality)]);
        var monthlyReport = new MonthlyReport(
            Guid.NewGuid(),
            companyId,
            month,
            source,
            [new MonthlyReportLineItem("PRODUCT_A", 10, 8, null, quality)]);
        var marketSnapshot = new MarketSnapshot(
            Guid.NewGuid(),
            symbolId,
            ObservedAt,
            latestPrice: null,
            priceChangePercentage: null,
            marketCapitalization: null,
            source,
            quality);

        Assert.Null(statement.LineItems.Single().Value);
        Assert.Null(monthlyReport.LineItems.Single().SalesAmount);
        Assert.Null(marketSnapshot.LatestPrice);
        Assert.True(quality.HasMissingData);
        Assert.True(quality.IsStale);
    }

    [Fact]
    public void DerivedMetric_RetainsSemanticAndCalculationPolicyVersions()
    {
        var metric = new DerivedMetric(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new MetricCode("net_profit_growth_yoy"),
            new MetricVersion("v1"),
            new CalculationPolicyVersion("yoy-quarterly-v1"),
            FiscalPeriod.Closed(
                FiscalPeriodType.ThreeMonths,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 3, 31)),
            value: null,
            MetricValueUnit.Percentage,
            new FinancialObservationQuality(
                ObservedAt,
                SyncedAt,
                [new FinancialDataWarning(FinancialDataWarningCode.MissingData, "Prior value is absent.")]),
            [new FinancialSourceEvidence("DataProvider", ObservedAt, SyncedAt)]);

        Assert.Equal("NET_PROFIT_GROWTH_YOY", metric.Code.Value);
        Assert.Equal("v1", metric.MetricVersion.Value);
        Assert.Equal("yoy-quarterly-v1", metric.CalculationPolicyVersion.Value);
        Assert.True(metric.Quality.HasMissingData);
    }

    [Fact]
    public void FiscalPeriods_RepresentSupportedClosedAndLatestSelectionTypes()
    {
        var closedTypes = new[]
        {
            FiscalPeriodType.Monthly,
            FiscalPeriodType.ThreeMonths,
            FiscalPeriodType.SixMonths,
            FiscalPeriodType.NineMonths,
            FiscalPeriodType.TwelveMonths,
            FiscalPeriodType.TrailingTwelveMonths
        };

        var periods = closedTypes
            .Select(type => FiscalPeriod.Closed(type, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)))
            .ToArray();

        Assert.Equal(closedTypes, periods.Select(period => period.Type));
        Assert.Equal(FiscalPeriodType.LatestQuarter, FiscalPeriod.LatestQuarter().Type);
        Assert.Equal(FiscalPeriodType.LatestMonth, FiscalPeriod.LatestMonth().Type);
    }

    [Fact]
    public void PeriodComparisonPolicy_ResolvesYearOverYearForQuarterlyStatement()
    {
        var current = FiscalPeriod.Closed(
            FiscalPeriodType.ThreeMonths,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31));

        var comparison = new PeriodComparisonPolicy().GetComparisonPeriod(
            current,
            GrowthComparison.YearOverYear);

        Assert.Equal(new DateOnly(2025, 1, 1), comparison.StartDate);
        Assert.Equal(new DateOnly(2025, 3, 31), comparison.EndDate);
    }

    [Fact]
    public void PeriodComparisonPolicy_ResolvesMonthOverMonthForMonthlyReport()
    {
        var current = FiscalPeriod.Closed(
            FiscalPeriodType.Monthly,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31));

        var comparison = new PeriodComparisonPolicy().GetComparisonPeriod(
            current,
            GrowthComparison.MonthOverMonth);

        Assert.Equal(new DateOnly(2026, 4, 1), comparison.StartDate);
        Assert.Equal(new DateOnly(2026, 4, 30), comparison.EndDate);
    }

    [Fact]
    public void PeriodComparisonPolicy_RejectsUnresolvedLatestSelectionAndQuarterlyMonthOverMonth()
    {
        var policy = new PeriodComparisonPolicy();
        var quarter = FiscalPeriod.Closed(
            FiscalPeriodType.ThreeMonths,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31));

        Assert.Throws<InvalidOperationException>(() =>
            policy.GetComparisonPeriod(FiscalPeriod.LatestMonth(), GrowthComparison.MonthOverMonth));
        Assert.Throws<InvalidOperationException>(() =>
            policy.GetComparisonPeriod(quarter, GrowthComparison.MonthOverMonth));
    }

    [Fact]
    public void MetricRegistry_RegistersAdditionalMetricWithoutProceduralDispatch()
    {
        var custom = new MetricIdentity(
            new MetricCode("FREE_CASH_FLOW_MARGIN"),
            new MetricVersion("v1"),
            MetricValueUnit.Percentage,
            [FiscalPeriodType.TrailingTwelveMonths]);
        var registry = new MetricIdentityRegistry([custom]);

        var resolved = registry.Resolve(new MetricCode("free_cash_flow_margin"));

        Assert.Same(custom, resolved);
        Assert.Single(registry.GetRegisteredMetrics());
    }

    [Fact]
    public void MetricRegistry_CanCarryPhaseOneScannerMetricsAsRegisteredData()
    {
        var codes = new[]
        {
            "NET_PROFIT_GROWTH_YOY",
            "MONTHLY_SALES_GROWTH_YOY",
            "MONTHLY_SALES_GROWTH_MOM",
            "TTM_SALES",
            "TTM_EPS",
            "PE_TTM",
            "PS_TTM"
        };
        var registry = new MetricIdentityRegistry(
            codes.Select(code => new MetricIdentity(
                new MetricCode(code),
                new MetricVersion("v1"),
                MetricValueUnit.Ratio,
                [FiscalPeriodType.TrailingTwelveMonths])));

        Assert.Equal(codes.Length, registry.GetRegisteredMetrics().Count);
        Assert.All(codes, code => Assert.Equal(code, registry.Resolve(new MetricCode(code)).Code.Value));
    }

    [Fact]
    public void CalculationPolicy_ProvidesComparisonAndDependencyInputsForDerivedMetricServices()
    {
        var metricCode = new MetricCode("MONTHLY_SALES_GROWTH_MOM");
        var policy = new MetricCalculationPolicy(
            metricCode,
            new CalculationPolicyVersion("mom-sales-v1"),
            MetricValueUnit.Percentage,
            GrowthComparison.MonthOverMonth,
            MissingDataPolicy.ReturnMissingValue,
            [new MetricDataRequirement(new MetricCode("MONTHLY_SALES"), FiscalPeriodType.Monthly, true)]);

        Assert.Equal(GrowthComparison.MonthOverMonth, policy.Comparison);
        Assert.Equal("mom-sales-v1", policy.Version.Value);
        Assert.Equal("MONTHLY_SALES", policy.Requirements.Single().MetricCode.Value);
    }

    [Fact]
    public void Percentage_ExpressesMetricRatiosAsDisplayedPercentageValues()
    {
        Assert.Equal(72.4m, Percentage.FromRatio(0.724m).Value);
    }
}
