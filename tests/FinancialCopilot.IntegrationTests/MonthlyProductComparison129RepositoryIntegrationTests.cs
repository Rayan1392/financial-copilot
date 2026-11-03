using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class MonthlyProductComparison129RepositoryIntegrationTests(PostgreSqlIntegrationFixture fixture)
{
    [SkippableFact]
    public async Task ReadQuery_UsesRelationalProductSalesOutputZeroAndProjectsEvidence()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var companyA = "feature129-company-a";
        var companyB = "feature129-company-b";
        var current = Period(1403, 2);
        var previous = Period(1403, 1);

        await using (var db = database.CreateContext())
        {
            await AddReport(db, companyA, current, "ProductSales", 0, [
                ("A", "Alpha", 100m), ("ZERO", "Zero", 0m), ("NEG", "Negative", -20m), ("NULL", "Null", null)]);
            await AddReport(db, companyA, previous, "ProductSales", 0, [("A", "Alpha", 80m)]);
            await AddReport(db, companyA, new(1403, 3), "ProductSales", 1, [("WRONG", "Wrong output", 999m)]);
            await AddReport(db, companyA, current, "ServiceSales", 0, [("SERVICE", "Service", 999m)]);
            await AddReport(db, companyB, current, "ProductSales", 0, [("A", "Other company", 777m)]);
            await db.SaveChangesAsync();
        }

        await using var readDb = database.CreateContext();
        var repository = new EfCoreMonthlyProductComparisonRepository(readDb);
        var periods = await repository.GetAvailablePeriodsAsync(companyA);
        Assert.Equal([current, previous], periods.Take(2).ToArray());

        var result = await repository.GetPeriodAsync(companyA, current);
        Assert.NotNull(result);
        Assert.Equal(4, result.Observations.Count);
        Assert.Contains(result.Observations, x => x.ProductCode == "A" && x.SalesAmount == 100m);
        Assert.Contains(result.Observations, x => x.ProductCode == "ZERO" && x.SalesAmount == 0m);
        Assert.Contains(result.Observations, x => x.ProductCode == "NEG" && x.SalesAmount == -20m);
        Assert.Contains(result.Observations, x => x.ProductCode == "NULL" && x.SalesAmount is null);
        Assert.All(result.Observations, x => Assert.Equal(companyA, x.ExternalCompanyId));
        Assert.All(result.Evidence, x => Assert.Contains(result.Observations, row => row.RowId == x.RowId && row.ReportId == x.ReportId));
        Assert.DoesNotContain(result.Observations, x => x.ProductCode == "WRONG" || x.ProductCode == "SERVICE" || x.SalesAmount == 777m);
    }

    private static async Task AddReport(
        FinancialIngestionDbContext db,
        string company,
        JalaliPeriod period,
        string reportType,
        int outputType,
        IReadOnlyList<(string Code, string Title, decimal? Amount)> rows)
    {
        var reportId = Guid.NewGuid();
        var (start, end) = Dates(period);
        db.MonthlyReports.Add(new NormalizedMonthlyReportRow
        {
            Id = reportId, ProviderName = "Feature129Test", ExternalCompanyId = company,
            ExternalReportId = $"feature129:{company}:{period}:{reportType}:{outputType}:{reportId:N}", ReportType = reportType,
            OutputType = outputType, PeriodStart = start, PeriodEnd = end,
            SourcePayloadChecksum = Guid.NewGuid().ToString("N"), LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        foreach (var row in rows)
            db.MonthlyReportLineItems.Add(new NormalizedMonthlyReportLineItemRow
            {
                Id = Guid.NewGuid(), MonthlyReportId = reportId, ProductCode = row.Code,
                Title = row.Title, Unit = "ton", SalesAmount = row.Amount
            });
        await Task.CompletedTask;
    }

    private static (DateOnly Start, DateOnly End) Dates(JalaliPeriod period)
    {
        var calendar = new PersianCalendar();
        var start = DateOnly.FromDateTime(calendar.ToDateTime(period.Year, period.Month, 1, 0, 0, 0, 0));
        var end = DateOnly.FromDateTime(calendar.ToDateTime(period.Year, period.Month, calendar.GetDaysInMonth(period.Year, period.Month), 0, 0, 0, 0));
        return (start, end);
    }

    private static JalaliPeriod Period(int year, int month) => new(year, month);
}
