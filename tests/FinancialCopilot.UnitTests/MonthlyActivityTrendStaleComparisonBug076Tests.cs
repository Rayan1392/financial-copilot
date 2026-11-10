using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Regression for specs/076-nadpco-monthly-activity-trend-snapshot/bug-001: the Mehr 1405 snapshot of کگهر kept a
/// month-over-month change (-52.1%) and 12-month average computed from a Shahrivar snapshot that was later rebuilt.
/// Amounts are the production values in million rial. All tests use in-memory EF Core, no network.
/// </summary>
public sealed class MonthlyActivityTrendStaleComparisonBug076Tests
{
    private const string CompanyId = "12795";
    private const string Symbol = "کگهر";
    private static readonly string Provider = ProviderSources.NoavaranCurrentApiName;

    // Accepted ProductSales OutputType=0 totals per month, million rial (production, 2026-11-06).
    private static readonly (int Year, byte Month, decimal Sales)[] KgoharMonths =
    [
        (1404, 1, 50_965_863m), (1404, 2, 70_194_754m), (1404, 3, 41_825_000m), (1404, 4, 34_880_438m),
        (1404, 5, 89_800_821m), (1404, 6, 93_220_068m), (1404, 7, 74_179_191m), (1404, 8, 73_783_333m),
        (1404, 9, 65_043_697m), (1404, 10, 75_900_232m), (1404, 11, 79_057_902m), (1404, 12, 77_564_054m),
        (1405, 1, 71_330_690m), (1405, 2, 69_097_786m), (1405, 3, 93_685_735m), (1405, 4, 155_570_231m),
        (1405, 5, 92_156_748m), (1405, 6, 131_473_818m), (1405, 7, 129_113_133m)
    ];

    private const decimal ShahrivarPendingRevisionSales = 138_103_563m;
    private const decimal StaleMehrMomPercent = -52.1054m;
    private const decimal StaleMehrAverage12 = 104_323_410.16666666666666666667m;

    [Fact]
    public async Task UseCase_KgoharMehr1405_MonthOverMonthUsesCurrentShahrivarNotStaleStoredValue()
    {
        await using var db = CreateDb();
        SeedSnapshots(db, KgoharMonths);
        await db.SaveChangesAsync();

        var result = await CreateUseCase(db).ExecuteAsync(new MonthlyActivityTrendQuery("روند فروش ماهانه کگهر را نشان بده", Symbol));

        Assert.NotNull(result);
        Assert.Equal(-1.8m, Math.Round(result.SalesAmountMomGrowthPercent!.Value, 1));
        Assert.Equal(13_147.3818m, result.PreviousMonthSalesAmount);
        Assert.Equal(9_281.478m, Math.Round(result.Average12MonthSalesAmount!.Value, 3));
        Assert.Equal(39.1m, Math.Round(result.SalesVsAverage12MonthPercent!.Value, 1));
        Assert.Equal(74.1m, Math.Round(result.SalesAmountYoYGrowthPercent!.Value, 1));

        // Summary and chart must share one basis.
        var shahrivar = result.ChartPoints.Single(point => point.FiscalMonthIndex == 6);
        var mehr = result.ChartPoints.Single(point => point.FiscalMonthIndex == 7);
        Assert.Equal(13_147.3818m, shahrivar.CurrentFiscalYearSalesAmount);
        Assert.Equal(12_911.3133m, mehr.CurrentFiscalYearSalesAmount);
        Assert.All(result.ChartPoints, point => Assert.Equal(result.Average12MonthSalesAmount, point.Average12MonthSalesAmount));

        var summary = FinancialCopilotWorkflowDefinition.BuildMonthlyActivityTrendContent(result);
        Assert.Contains("**تغییر نسبت به ماه قبل:** -1.8٪", summary);
        Assert.DoesNotContain("-52.1", summary);
        Assert.Contains("9,281.478", summary);
    }

    [Fact]
    public async Task UseCase_PreviousCalendarMonthMissing_DoesNotInventMonthOverMonth()
    {
        await using var db = CreateDb();
        SeedSnapshots(db, KgoharMonths.Where(month => !(month.Year == 1405 && month.Month == 6)).ToArray());
        await db.SaveChangesAsync();

        var result = await CreateUseCase(db).ExecuteAsync(new MonthlyActivityTrendQuery("روند فروش ماهانه کگهر", Symbol));

        Assert.NotNull(result);
        Assert.Null(result.PreviousMonthSalesAmount);
        Assert.Null(result.SalesAmountMomGrowthPercent);

        var summary = FinancialCopilotWorkflowDefinition.BuildMonthlyActivityTrendContent(result);
        Assert.Contains("**تغییر نسبت به ماه قبل:** دادهٔ ماه قبل موجود نیست.", summary);
        Assert.DoesNotContain("-52.1", summary);
    }

    [Fact]
    public async Task Calculator_RecalculatingShahrivar_RefreshesMehrStoredComparisons()
    {
        await using var db = CreateDb();
        // Shahrivar snapshot as written before 13430cfb: accepted + pending revisions summed.
        SeedSnapshots(db, KgoharMonths
            .Select(month => month is (1405, 6, _) ? month with { Sales = 131_473_818m + ShahrivarPendingRevisionSales } : month)
            .ToArray());

        var (start, end) = JalaliDateResolver.ResolveMonth(1405, 6);
        AddMonthlyReport(db, start, end, 131_473_818m, isAccepted: true, revisionStatus: "Accepted");
        AddMonthlyReport(db, start, end, ShahrivarPendingRevisionSales, isAccepted: false, revisionStatus: "Pending");
        await db.SaveChangesAsync();

        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, new EfCoreCompanyMonthlyActivityTrendSnapshotRepository(db));
        await calculator.RecalculateAsync(CompanyId, 1405, 6, Symbol, null, null);

        var shahrivar = await db.CompanyMonthlyActivityTrendSnapshots.AsNoTracking()
            .SingleAsync(row => row.ExternalCompanyId == CompanyId && row.ReportYear == 1405 && row.ReportMonth == 6);
        var mehr = await db.CompanyMonthlyActivityTrendSnapshots.AsNoTracking()
            .SingleAsync(row => row.ExternalCompanyId == CompanyId && row.ReportYear == 1405 && row.ReportMonth == 7);

        Assert.Equal(131_473_818m, shahrivar.MonthlySalesAmount);
        Assert.Equal(-1.7956m, mehr.SalesAmountMomGrowthPercent);
        Assert.Equal(92_814_779.917m, Math.Round(mehr.Average12MonthSalesAmount!.Value, 3));
        Assert.Equal(12, mehr.Average12MonthPeriodCount);
        Assert.Equal(74_179_191m, mehr.SameMonthPreviousYearSalesAmount);
        Assert.Equal(74.0557m, mehr.SalesAmountYoYGrowthPercent);
        Assert.Equal(129_113_133m, mehr.MonthlySalesAmount);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static MonthlyActivityTrendQueryUseCase CreateUseCase(FinancialIngestionDbContext db) =>
        new(new KgoharResolver(),
            new ResolverBackedCanonicalCompanies(),
            new EfCoreCompanyMonthlyActivityTrendSnapshotRepository(db),
            NullLogger<MonthlyActivityTrendQueryUseCase>.Instance);

    /// <summary>
    /// Seeds snapshots whose Mehr 1405 row carries the stale comparison fields observed in production.
    /// Other rows carry no comparison fields, so any correct value must be derived from the series.
    /// </summary>
    private static void SeedSnapshots(FinancialIngestionDbContext db, IReadOnlyList<(int Year, byte Month, decimal Sales)> months)
    {
        foreach (var (year, month, sales) in months)
        {
            var isStaleMehr = year == 1405 && month == 7;
            db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = CompanyId,
                CompanySymbol = Symbol,
                CompanyName = "معدنی و صنعتی گهر زمین",
                ReportYear = year,
                ReportMonth = month,
                MonthlySalesAmount = sales,
                SalesAmountMomGrowthPercent = isStaleMehr ? StaleMehrMomPercent : null,
                Average12MonthSalesAmount = isStaleMehr ? StaleMehrAverage12 : null,
                Average12MonthPeriodCount = isStaleMehr ? 12 : 0,
                IsAverage12MonthComplete = isStaleMehr,
                SameMonthPreviousYearSalesAmount = isStaleMehr ? 74_179_191m : null,
                SalesAmountYoYGrowthPercent = isStaleMehr ? 74.0557m : null,
                IsComparablePreviousYearAvailable = isStaleMehr,
                CurrentMonthOutputType = 0,
                SourceProviderName = Provider,
                CalculatedAtUtc = isStaleMehr
                    ? new DateTimeOffset(2026, 10, 28, 14, 12, 10, TimeSpan.Zero)
                    : new DateTimeOffset(2026, 11, 1, 12, 45, 27, TimeSpan.Zero)
            });
        }
    }

    private static void AddMonthlyReport(
        FinancialIngestionDbContext db,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal salesAmount,
        bool isAccepted,
        string revisionStatus)
    {
        var reportId = Guid.NewGuid();
        db.MonthlyReports.Add(new NormalizedMonthlyReportRow
        {
            Id = reportId,
            ProviderName = Provider,
            ExternalCompanyId = CompanyId,
            ExternalReportId = "ProductSales:12795:1405-06:output-0",
            ReportType = "ProductSales",
            OutputType = 0,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            SourcePayloadChecksum = revisionStatus,
            RevisionFingerprint = revisionStatus,
            LastSynchronizedAt = DateTimeOffset.UtcNow,
            IsAccepted = isAccepted,
            RevisionStatus = revisionStatus
        });
        db.MonthlyReportLineItems.Add(new NormalizedMonthlyReportLineItemRow
        {
            Id = Guid.NewGuid(),
            MonthlyReportId = reportId,
            ProductCode = "PRODUCT:CONCENTRATE",
            Title = "کنسانتره سنگ آهن",
            Unit = "تن",
            SalesAmount = salesAmount
        });
    }

    private sealed class ResolverBackedCanonicalCompanies : ICanonicalMonthlyCompanyResolver
    {
        public async Task<CompanyResolutionOutcome> ResolveAsync(string symbol, CancellationToken cancellationToken) =>
            new(CompanyResolutionStatus.Resolved, await new KgoharResolver().ResolveBySymbolAsync(symbol, cancellationToken));
    }

    private sealed class KgoharResolver : ICompanyResolverService
    {
        public Task<ResolvedCompany?> ResolveBySymbolAsync(string symbol, CancellationToken ct = default) =>
            Task.FromResult<ResolvedCompany?>(new ResolvedCompany(
                Guid.NewGuid(), CompanyId, Symbol, null, null, null, null, CompanySymbol: Symbol));
    }
}
