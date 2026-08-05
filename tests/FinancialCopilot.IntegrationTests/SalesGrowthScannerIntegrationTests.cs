using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Provider-neutral integration coverage for Feature 116.  The fixture uses the
/// production EF execution service against a deterministic in-memory ingestion DB;
/// no market-data provider is reachable from these tests.
/// </summary>
public sealed class SalesGrowthScannerIntegrationTests : IClassFixture<SalesGrowthScannerApiFactory>
{
    private readonly SalesGrowthScannerApiFactory factory;

    public SalesGrowthScannerIntegrationTests(SalesGrowthScannerApiFactory factory)
    {
        this.factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task PersianPositiveGrowth_ReturnsDeterministicRowsAndValues()
    {
        var result = await ExecuteAsync(SalesGrowthComparisonBaseline.SameMonthPreviousYear, SalesGrowthThresholdKind.Positive, null);

        Assert.Equal(["AAA", "DDD", "BBB", "EEE"], result.Rows.Select(row => row.SymbolCode).ToArray());
        Assert.Equal(100m, result.Rows.Single(row => row.SymbolCode == "AAA").Cells["MONTHLY_SALES_GROWTH_PERCENT"].Value);
        Assert.Equal(50m, result.Rows.Single(row => row.SymbolCode == "BBB").Cells["MONTHLY_SALES_GROWTH_PERCENT"].Value);
    }

    [Fact]
    public async Task ExplicitThresholds_UseStrictAndInclusiveSemantics()
    {
        var yoy = await ExecuteAsync(SalesGrowthComparisonBaseline.SameMonthPreviousYear, SalesGrowthThresholdKind.Percent, 30m);
        Assert.Equal(["AAA", "DDD", "BBB", "EEE"], yoy.Rows.Select(row => row.SymbolCode).ToArray());

        var previousMonth = await ExecuteAsync(SalesGrowthComparisonBaseline.PreviousMonth, SalesGrowthThresholdKind.Percent, 20m);
        Assert.Equal(["AAA", "BBB", "EEE"], previousMonth.Rows.Select(row => row.SymbolCode).ToArray());

        var doubleGrowth = await ExecuteAsync(
            SalesGrowthComparisonBaseline.SameMonthPreviousYear,
            SalesGrowthThresholdKind.Multiple,
            2m,
            ConditionOperator.GreaterThanOrEqual);
        Assert.Equal(["AAA", "DDD"], doubleGrowth.Rows.Select(row => row.SymbolCode).ToArray());
        Assert.All(doubleGrowth.Rows, row => Assert.Equal(2m, row.Cells["MONTHLY_SALES_GROWTH_MULTIPLE"].Value));
    }

    [Fact]
    public async Task AverageTwelveMonths_RequiresTwelveEligibleObservations()
    {
        var result = await ExecuteAsync(
            SalesGrowthComparisonBaseline.AveragePrevious12Months,
            SalesGrowthThresholdKind.Multiple,
            1.5m,
            ConditionOperator.GreaterThanOrEqual);

        Assert.Equal(["DDD", "AAA", "BBB"], result.Rows.Select(row => row.SymbolCode).ToArray());
        Assert.DoesNotContain(result.Rows, row => row.SymbolCode == "EEE");
        Assert.Equal(1.5m, result.Rows.Single(row => row.SymbolCode == "BBB").Cells["MONTHLY_SALES_GROWTH_MULTIPLE"].Value);
    }

    [Fact]
    public async Task MissingPeriodsAndZeroBaseline_NeverProduceFabricatedRows()
    {
        var result = await ExecuteAsync(SalesGrowthComparisonBaseline.PreviousMonth, SalesGrowthThresholdKind.Positive, null);

        Assert.DoesNotContain(result.Rows, row => row.SymbolCode is "CCC" or "DDD");
        Assert.Contains("missing_or_unusable_data", result.ExecutionFacts.ExcludedByReason!.Keys);
        Assert.True(result.ExecutionFacts.ExcludedByReason["missing_or_unusable_data"] >= 2);
    }

    [Fact]
    public async Task NoMatchingSymbols_ReturnsEmptyTableWithEvidence()
    {
        var result = await ExecuteAsync(SalesGrowthComparisonBaseline.SameMonthPreviousYear, SalesGrowthThresholdKind.Percent, 200m);

        Assert.Empty(result.Rows);
        Assert.Equal(5, result.ExecutionFacts.TotalSymbolsEvaluated);
        Assert.Equal(5, result.ExecutionFacts.EvaluatedSymbolCount);
        Assert.NotNull(result.SalesGrowthMetadata);
    }

    [Fact]
    public async Task CommonPeriodCoverageBelowPolicy_ReturnsUnavailableRowsWithoutInventingData()
    {
        try
        {
            factory.MarkAverageCoverageBelowPolicy();
            var result = await ExecuteAsync(SalesGrowthComparisonBaseline.AveragePrevious12Months, SalesGrowthThresholdKind.Positive, null);

            Assert.Empty(result.Rows);
            Assert.Null(result.SalesGrowthMetadata);
            Assert.Contains("unavailable", result.MissingDataWarnings.Single(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            factory.RestoreAverageCoverage();
        }
    }

    [Fact]
    public async Task CompositionWithPeFilter_RemovesRowsFailingTheOtherCondition()
    {
        var salesPlan = new SalesGrowthScannerPlan(
            new SalesGrowthScannerSemantics(
                SalesGrowthComparisonBaseline.SameMonthPreviousYear,
                SalesGrowthThresholdKind.Percent,
                ConditionOperator.GreaterThan,
                30m,
                FilterOrigin.Explicit,
                SalesGrowthPolicyVersions.V1));
        var pe = new ScannerCondition(
            new ScannerMetricReference("P/E", new FinancialCopilot.Domain.Financial.Metrics.MetricCode("PE_TTM"),
                new FinancialCopilot.Domain.Financial.Metrics.MetricVersion("v1"),
                new FinancialCopilot.Domain.Financial.Metrics.CalculationPolicyVersion("PE_TTM_v1"),
                FinancialCopilot.Domain.Financial.Periods.FiscalPeriodType.TrailingTwelveMonths, null),
            ConditionOperator.LessThan, 4m, FilterOrigin.Explicit);
        var result = await ExecuteAsync(salesPlan, [pe]);

        Assert.Equal(["AAA"], result.Rows.Select(row => row.SymbolCode).ToArray());
        Assert.Equal(3m, result.Rows.Single().Cells["PE_TTM"].Value);
    }

    private Task<ScannerTableResult> ExecuteAsync(
        SalesGrowthComparisonBaseline baseline,
        SalesGrowthThresholdKind thresholdKind,
        decimal? threshold,
        ConditionOperator comparison = ConditionOperator.GreaterThan) =>
        ExecuteAsync(new SalesGrowthScannerPlan(new SalesGrowthScannerSemantics(
            baseline, thresholdKind, comparison, threshold, FilterOrigin.Explicit, SalesGrowthPolicyVersions.V1)));

    private Task<ScannerTableResult> ExecuteAsync(SalesGrowthScannerPlan salesPlan, IReadOnlyCollection<ScannerCondition>? conditions = null)
    {
        var plan = new ScannerQueryPlan(
            Guid.NewGuid(), "integration", "fa-IR", conditions ?? [], [], false, null, [], [],
            DateTimeOffset.UtcNow, "v1", salesPlan);
        using var scope = factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IScannerExecutionService>();
        return executor.ExecuteAsync(new ScannerExecutionRequest(plan, new DateOnly(2026, 8, 5), QueryText: "integration"), CancellationToken.None);
    }
}

public sealed class SalesGrowthScannerApiFactory : AiFacadeApiFactory
{
    private readonly string ingestionDatabaseName = $"sales-growth-integration-{Guid.NewGuid():N}";
    private bool seeded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => ReplaceIngestionDbContext(services, ingestionDatabaseName));
    }

    public void EnsureSeeded()
    {
        if (seeded) return;
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        var companies = new[] { ("aaa", "AAA"), ("bbb", "BBB"), ("ccc", "CCC"), ("ddd", "DDD"), ("eee", "EEE") };
        foreach (var (id, symbol) in companies)
        {
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(), ExternalCompanyId = id, CompanySymbol = symbol,
                Name = $"{symbol} Corp", ProviderName = "deterministic-test", LastSynchronizedAt = DateTimeOffset.UtcNow
            });
            SeedCompanySnapshots(db, id, symbol);
            db.DerivedMetrics.Add(new DerivedMetricRow
            {
                Id = Guid.NewGuid(), ExternalCompanyId = id, MetricCode = "PE_TTM", MetricVersion = "v1",
                CalculationPolicyVersion = "PE_TTM_v1", PeriodType = "TrailingTwelveMonths",
                PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2026, 6, 30), Value = id == "aaa" ? 3m : 75m,
                Unit = "Ratio", ObservedAt = DateTimeOffset.UtcNow, LastSynchronizedAt = DateTimeOffset.UtcNow,
                WarningsJson = "[]", SourceEvidenceJson = "[]", DependencyEvidenceJson = "[]"
            });
        }
        db.SaveChanges();
        seeded = true;
    }

    public void MarkAverageCoverageBelowPolicy()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        foreach (var snapshot in db.CompanyMonthlyActivityTrendSnapshots)
        {
            snapshot.IsAverage12MonthComplete = snapshot.ExternalCompanyId == "aaa";
        }
        db.SaveChanges();
    }

    private static void SeedCompanySnapshots(FinancialIngestionDbContext db, string id, string symbol)
    {
        var now = DateTimeOffset.UtcNow;
        var current = id switch { "aaa" => 200m, "bbb" => 150m, "ccc" => 120m, "ddd" => 200m, _ => 150m };
        var previous = id == "ddd" ? 0m : 100m;
        db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
        {
            Id = Guid.NewGuid(), ExternalCompanyId = id, CompanySymbol = symbol, CompanyName = $"{symbol} Corp",
            ReportYear = 2026, ReportMonth = 6, CalendarYear = 2026, CalendarMonth = 6, MonthlySalesAmount = current,
            SourceProviderName = "deterministic-test", SourceReportId = $"{id}-202606", CalculatedAtUtc = now,
            IsComparablePreviousYearAvailable = id != "ccc", IsAverage12MonthComplete = id is not "eee", DataCompletenessScore = 1m
        });
        if (id != "ccc")
        {
            db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
            {
                Id = Guid.NewGuid(), ExternalCompanyId = id, CompanySymbol = symbol, ReportYear = 2025, ReportMonth = 6,
                CalendarYear = 2025, CalendarMonth = 6, MonthlySalesAmount = 100m, SourceProviderName = "deterministic-test",
                SourceReportId = $"{id}-202506", CalculatedAtUtc = now, IsComparablePreviousYearAvailable = true,
                IsAverage12MonthComplete = true, DataCompletenessScore = 1m
            });
        }
        var averagePeriods = id == "ccc"
            ? []
            : Enumerable.Range(1, 11)
                .Select(index => new DateOnly(2025, 7, 1).AddMonths(index - 1))
                .Where(period => id != "eee" || period != new DateOnly(2026, 1, 1));
        foreach (var period in averagePeriods)
        {
            if (period is { Year: 2025, Month: 6 }) continue;
            db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
            {
                Id = Guid.NewGuid(), ExternalCompanyId = id, CompanySymbol = symbol, ReportYear = period.Year,
                ReportMonth = (byte)period.Month, CalendarYear = period.Year, CalendarMonth = period.Month,
                MonthlySalesAmount = period == new DateOnly(2026, 5, 1) ? previous : 100m,
                SourceProviderName = "deterministic-test", SourceReportId = $"{id}-{period:yyyyMM}",
                CalculatedAtUtc = now, IsComparablePreviousYearAvailable = true, IsAverage12MonthComplete = true,
                DataCompletenessScore = 1m
            });
        }
    }

    public void RestoreAverageCoverage()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        foreach (var snapshot in db.CompanyMonthlyActivityTrendSnapshots)
        {
            snapshot.IsAverage12MonthComplete = snapshot.ExternalCompanyId is not "eee";
        }
        db.SaveChanges();
    }
}
