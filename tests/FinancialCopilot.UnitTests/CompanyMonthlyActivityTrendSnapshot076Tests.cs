using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 076: NADPCO Monthly Activity Trend Snapshot.
/// Tests cover company-level aggregation, YoY/MoM growth, 12-month average,
/// mixed-unit detection, and YTD provenance. All tests use in-memory EF Core.
/// </summary>
public sealed class CompanyMonthlyActivityTrendSnapshot076Tests
{
    private const string ExternalId = "EXT-076";
    private const string Symbol = "کچاد";
    private static readonly string Provider = ProviderSources.NoavaranCurrentApiName;

    // -----------------------------------------------------------------------
    // Aggregation: outputType=0 sums all line items including negatives
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Calculator_AggregatesTotalSalesFromOutputType0LineItems()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();
        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);

        var (start, end) = JalaliDateResolver.ResolveMonth(1403, 1);
        var reportId = AddReport(db, ExternalId, start, end, outputType: 0);
        AddLineItem(db, reportId, "سنگ آهن", salesAmount: 600m, unit: "تن");
        AddLineItem(db, reportId, "کنسانتره", salesAmount: 400m, unit: "تن");
        await db.SaveChangesAsync();

        await calculator.RecalculateAsync(ExternalId, 1403, 1, Symbol, null, null);

        Assert.NotNull(repo.Upserted);
        Assert.Equal(1000m, repo.Upserted!.MonthlySalesAmount);
    }

    [Fact]
    public async Task Calculator_IncludesNegativeSalesInNetTotal()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();
        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);

        var (start, end) = JalaliDateResolver.ResolveMonth(1403, 2);
        var reportId = AddReport(db, ExternalId, start, end, outputType: 0);
        AddLineItem(db, reportId, "فروش اصلی", salesAmount: 1000m, unit: "تن");
        AddLineItem(db, reportId, "برگشت از فروش", salesAmount: -150m, unit: "تن");
        await db.SaveChangesAsync();

        await calculator.RecalculateAsync(ExternalId, 1403, 2, Symbol, null, null);

        Assert.NotNull(repo.Upserted);
        Assert.Equal(850m, repo.Upserted!.MonthlySalesAmount);
    }

    [Fact]
    public async Task Calculator_DoesNotUseOutputType1Or4ForMonthlyBar()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();
        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);

        var (start, end) = JalaliDateResolver.ResolveMonth(1403, 3);

        // OutputType=1 with large value — must NOT be used for monthly bar
        var ytdId = AddReport(db, ExternalId, start, end, outputType: 1);
        AddLineItem(db, ytdId, "YTD فروش", salesAmount: 99999m, unit: "تن");

        // OutputType=4 with large value — must NOT be used for monthly bar
        var ytdPrevId = AddReport(db, ExternalId, start, end, outputType: 4);
        AddLineItem(db, ytdPrevId, "YTD قبل", salesAmount: 88888m, unit: "تن");

        // OutputType=0: the only authoritative source
        var reportId = AddReport(db, ExternalId, start, end, outputType: 0);
        AddLineItem(db, reportId, "فروش ماهانه", salesAmount: 500m, unit: "تن");
        await db.SaveChangesAsync();

        await calculator.RecalculateAsync(ExternalId, 1403, 3, Symbol, null, null);

        Assert.NotNull(repo.Upserted);
        Assert.Equal(500m, repo.Upserted!.MonthlySalesAmount);
    }

    // -----------------------------------------------------------------------
    // Mixed units detection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Calculator_DetectsMixedUnitsAndSuppressesQuantityAggregation()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();
        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);

        var (start, end) = JalaliDateResolver.ResolveMonth(1403, 4);
        var reportId = AddReport(db, ExternalId, start, end, outputType: 0);
        AddLineItem(db, reportId, "محصول الف", salesAmount: 300m, unit: "تن", salesQty: 100m);
        AddLineItem(db, reportId, "محصول ب", salesAmount: 200m, unit: "مترمربع", salesQty: 50m);
        await db.SaveChangesAsync();

        await calculator.RecalculateAsync(ExternalId, 1403, 4, Symbol, null, null);

        Assert.NotNull(repo.Upserted);
        Assert.True(repo.Upserted!.HasMixedProductUnits);
        Assert.Null(repo.Upserted.MonthlySalesQuantity);
        Assert.Null(repo.Upserted.MonthlyProductionQuantity);
    }

    [Fact]
    public async Task Calculator_AggregatesQuantityWhenAllRowsShareSameUnit()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();
        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);

        var (start, end) = JalaliDateResolver.ResolveMonth(1403, 5);
        var reportId = AddReport(db, ExternalId, start, end, outputType: 0);
        AddLineItem(db, reportId, "محصول الف", salesAmount: 300m, unit: "تن", salesQty: 100m, prodQty: 110m);
        AddLineItem(db, reportId, "محصول ب", salesAmount: 200m, unit: "تن", salesQty: 60m, prodQty: 70m);
        await db.SaveChangesAsync();

        await calculator.RecalculateAsync(ExternalId, 1403, 5, Symbol, null, null);

        Assert.NotNull(repo.Upserted);
        Assert.False(repo.Upserted!.HasMixedProductUnits);
        Assert.Equal(160m, repo.Upserted.MonthlySalesQuantity);
        Assert.Equal(180m, repo.Upserted.MonthlyProductionQuantity);
    }

    // -----------------------------------------------------------------------
    // YoY growth
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Calculator_ComputesYoYGrowthFromPreviousYearSnapshot()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();

        // Seed a prior-year snapshot directly into the db for the calculator to read.
        db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = ExternalId,
            ReportYear = 1402,
            ReportMonth = 6,
            MonthlySalesAmount = 500m,
            SourceProviderName = Provider,
            CalculatedAtUtc = DateTimeOffset.UtcNow
        });

        var (start, end) = JalaliDateResolver.ResolveMonth(1403, 6);
        var reportId = AddReport(db, ExternalId, start, end, outputType: 0);
        AddLineItem(db, reportId, "فروش", salesAmount: 600m, unit: "تن");
        await db.SaveChangesAsync();

        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);
        await calculator.RecalculateAsync(ExternalId, 1403, 6, Symbol, null, null);

        Assert.NotNull(repo.Upserted);
        // YoY = (600 - 500) / 500 * 100 = 20%
        Assert.Equal(20m, repo.Upserted!.SalesAmountYoYGrowthPercent);
        Assert.True(repo.Upserted.IsComparablePreviousYearAvailable);
    }

    [Fact]
    public async Task Calculator_SetsYoYNullWhenPreviousYearMissing()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();
        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);

        var (start, end) = JalaliDateResolver.ResolveMonth(1403, 7);
        var reportId = AddReport(db, ExternalId, start, end, outputType: 0);
        AddLineItem(db, reportId, "فروش", salesAmount: 400m, unit: "تن");
        await db.SaveChangesAsync();

        await calculator.RecalculateAsync(ExternalId, 1403, 7, Symbol, null, null);

        Assert.NotNull(repo.Upserted);
        Assert.Null(repo.Upserted!.SalesAmountYoYGrowthPercent);
        Assert.False(repo.Upserted.IsComparablePreviousYearAvailable);
    }

    // -----------------------------------------------------------------------
    // Trailing 12-month average
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Calculator_Computes12MonthAverageFromPersistedSnapshots()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();

        // Seed 11 prior snapshots (1402/1 through 1402/11) with 100m each
        for (byte m = 1; m <= 11; m++)
        {
            db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = ExternalId,
                ReportYear = 1402,
                ReportMonth = m,
                MonthlySalesAmount = 100m,
                SourceProviderName = Provider,
                CalculatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        // 12th is the current month being calculated (1402/12) with 200m
        var (start, end) = JalaliDateResolver.ResolveMonth(1402, 12);
        var reportId = AddReport(db, ExternalId, start, end, outputType: 0);
        AddLineItem(db, reportId, "فروش", salesAmount: 200m, unit: "تن");
        await db.SaveChangesAsync();

        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);
        await calculator.RecalculateAsync(ExternalId, 1402, 12, Symbol, null, null);

        Assert.NotNull(repo.Upserted);
        // Average of 11×100 + 200 = 1300 / 12 ≈ 108.33
        Assert.Equal(12, repo.Upserted!.Average12MonthPeriodCount);
        Assert.True(repo.Upserted.IsAverage12MonthComplete);
        Assert.NotNull(repo.Upserted.Average12MonthSalesAmount);
        Assert.Equal(Math.Round((11m * 100m + 200m) / 12m, 10), Math.Round(repo.Upserted.Average12MonthSalesAmount!.Value, 10));
    }

    [Fact]
    public async Task Calculator_FlagsIncomplete12MonthAverageWhenFewerPeriods()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();

        // Only 3 prior snapshots
        for (byte m = 1; m <= 3; m++)
        {
            db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = ExternalId,
                ReportYear = 1402,
                ReportMonth = m,
                MonthlySalesAmount = 100m,
                SourceProviderName = Provider,
                CalculatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        var (start, end) = JalaliDateResolver.ResolveMonth(1402, 4);
        var reportId = AddReport(db, ExternalId, start, end, outputType: 0);
        AddLineItem(db, reportId, "فروش", salesAmount: 100m, unit: "تن");
        await db.SaveChangesAsync();

        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);
        await calculator.RecalculateAsync(ExternalId, 1402, 4, Symbol, null, null);

        Assert.NotNull(repo.Upserted);
        Assert.Equal(4, repo.Upserted!.Average12MonthPeriodCount);
        Assert.False(repo.Upserted.IsAverage12MonthComplete);
    }

    // -----------------------------------------------------------------------
    // YTD provenance: outputType 1 and 4 stored as context, not monthly bar
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Calculator_StoresYtdFromOutputType1AsContext()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();
        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);

        var (start, end) = JalaliDateResolver.ResolveMonth(1403, 8);
        var reportId = AddReport(db, ExternalId, start, end, outputType: 0);
        AddLineItem(db, reportId, "فروش ماه", salesAmount: 300m, unit: "تن");

        var ytdId = AddReport(db, ExternalId, start, end, outputType: 1);
        AddLineItem(db, ytdId, "YTD", salesAmount: 1800m, unit: "تن");
        await db.SaveChangesAsync();

        await calculator.RecalculateAsync(ExternalId, 1403, 8, Symbol, null, null);

        Assert.NotNull(repo.Upserted);
        // Monthly bar must remain 300 — not YTD
        Assert.Equal(300m, repo.Upserted!.MonthlySalesAmount);
        // YTD must be stored separately
        Assert.Equal(1800m, repo.Upserted.YtdSalesAmount);
        Assert.Equal(1, repo.Upserted.YtdOutputType);
    }

    [Fact]
    public async Task Calculator_StoresYtdPreviousMonthFromOutputType4()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();
        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);

        var (start, end) = JalaliDateResolver.ResolveMonth(1403, 9);
        var reportId = AddReport(db, ExternalId, start, end, outputType: 0);
        AddLineItem(db, reportId, "فروش ماه", salesAmount: 250m, unit: "تن");

        var ytdPrevId = AddReport(db, ExternalId, start, end, outputType: 4);
        AddLineItem(db, ytdPrevId, "YTD قبل", salesAmount: 1200m, unit: "تن");
        await db.SaveChangesAsync();

        await calculator.RecalculateAsync(ExternalId, 1403, 9, Symbol, null, null);

        Assert.NotNull(repo.Upserted);
        Assert.Equal(250m, repo.Upserted!.MonthlySalesAmount);
        Assert.Equal(1200m, repo.Upserted.YtdPreviousMonthSalesAmount);
        Assert.Equal(4, repo.Upserted.YtdPreviousMonthOutputType);
    }

    // -----------------------------------------------------------------------
    // Early return: no outputType=0 report → nothing persisted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Calculator_DoesNothingWhenNoOutputType0ReportExists()
    {
        var db = CreateDb();
        var repo = new InMemoryTrendRepository();
        var calculator = new CompanyMonthlyActivityTrendSnapshotCalculator(db, repo);

        var (start, end) = JalaliDateResolver.ResolveMonth(1403, 10);
        var ytdId = AddReport(db, ExternalId, start, end, outputType: 1);
        AddLineItem(db, ytdId, "YTD only", salesAmount: 999m, unit: "تن");
        await db.SaveChangesAsync();

        await calculator.RecalculateAsync(ExternalId, 1403, 10, Symbol, null, null);

        Assert.Null(repo.Upserted);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Guid AddReport(
        FinancialIngestionDbContext db,
        string externalCompanyId,
        DateOnly periodStart,
        DateOnly periodEnd,
        int outputType)
    {
        var id = Guid.NewGuid();
        db.MonthlyReports.Add(new NormalizedMonthlyReportRow
        {
            Id = id,
            ProviderName = Provider,
            ExternalCompanyId = externalCompanyId,
            ExternalReportId = Guid.NewGuid().ToString(),
            ReportType = "ProductSales",
            OutputType = outputType,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            SourcePayloadChecksum = "chk",
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static void AddLineItem(
        FinancialIngestionDbContext db,
        Guid reportId,
        string title,
        decimal salesAmount,
        string? unit = null,
        decimal? salesQty = null,
        decimal? prodQty = null)
    {
        db.MonthlyReportLineItems.Add(new NormalizedMonthlyReportLineItemRow
        {
            Id = Guid.NewGuid(),
            MonthlyReportId = reportId,
            ProductCode = title,
            Title = title,
            SalesAmount = salesAmount,
            Unit = unit,
            SalesQuantity = salesQty,
            ProductionQuantity = prodQty
        });
    }

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

    private sealed class InMemoryTrendRepository : ICompanyMonthlyActivityTrendSnapshotRepository
    {
        public CompanyMonthlyActivityTrendSnapshotUpsertRow? Upserted { get; private set; }

        public Task UpsertAsync(CompanyMonthlyActivityTrendSnapshotUpsertRow row, CancellationToken ct = default)
        {
            Upserted = row;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetCompanyTrendAsync(
            string externalCompanyId, int fromYear, int fromMonth, int toYear, int toMonth, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>>([]);

        public Task<CompanyMonthlyActivityTrendSnapshot?> GetLatestAsync(string externalCompanyId, CancellationToken ct = default)
            => Task.FromResult<CompanyMonthlyActivityTrendSnapshot?>(null);

        public Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetLatestAvailablePeriodsAsync(
            string externalCompanyId, int count, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>>([]);

        public Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetAnnualComparisonBaseAsync(
            string externalCompanyId, int latestReportYear, int latestReportMonth, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>>([]);
    }
}
