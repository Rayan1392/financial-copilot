using System.Globalization;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class CodalDbMonthlyReportNormalizerTests
{
    private const string ProviderName = "CodalDb";
    private const string ExternalCompanyId = "2001";

    // Single activity row: Jalali 1402/01, one product.
    private const string SingleActivityJson = """
        [
          {
            "id": 101,
            "companyId": 2001,
            "month": 1,
            "year": 1402,
            "fiscalYearEnd": "1402/12/29",
            "modifiedDateTime": null,
            "products": [
              {
                "productId": 10,
                "productTitle": "Steel Billet",
                "productProduceAmount": 5000,
                "productSaleAmount": 4200,
                "productSaleRate": 25000.50,
                "productSaleValue": 105000000,
                "productUnit": "ton"
              }
            ]
          }
        ]
        """;

    // Two-product row for the same month — used to test multi-product line items and sum.
    private const string MultiProductJson = """
        [
          {
            "id": 202,
            "companyId": 2002,
            "month": 5,
            "year": 1402,
            "fiscalYearEnd": "1402/12/29",
            "modifiedDateTime": null,
            "products": [
              {
                "productId": 20,
                "productTitle": "Hot Rolled Coil",
                "productProduceAmount": 8000,
                "productSaleAmount": 7500,
                "productSaleRate": 30000.00,
                "productSaleValue": 225000000,
                "productUnit": "ton"
              },
              {
                "productId": 21,
                "productTitle": "Cold Rolled Coil",
                "productProduceAmount": 3000,
                "productSaleAmount": 2800,
                "productSaleRate": 35000.00,
                "productSaleValue": 98000000,
                "productUnit": "ton"
              }
            ]
          }
        ]
        """;

    // Zero-amount row — valid data; must not be filtered.
    private const string ZeroAmountJson = """
        [
          {
            "id": 303,
            "companyId": 2003,
            "month": 12,
            "year": 1401,
            "fiscalYearEnd": "1401/12/29",
            "modifiedDateTime": null,
            "products": [
              {
                "productId": 30,
                "productTitle": "Zinc Sheet",
                "productProduceAmount": 0,
                "productSaleAmount": 0,
                "productSaleRate": 0.0,
                "productSaleValue": 0,
                "productUnit": null
              }
            ]
          }
        ]
        """;

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CodalDbMonthlyReportNormalizer CreateNormalizer(FinancialIngestionDbContext db) =>
        new(db);

    private static ProviderRawPayload MakePayload(string json, string companyId = ExternalCompanyId) =>
        new(Guid.NewGuid(), ProviderName, ProviderDataset.MonthlyProductionSales,
            $"codaldb://monthly-activity/{companyId}", companyId,
            json, "checksum-" + Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Normalize_SingleActivityRow_CreatesReportAndLineItem()
    {
        await using var db = CreateDb();
        var count = await CreateNormalizer(db).NormalizeAsync(MakePayload(SingleActivityJson), default);

        Assert.Equal(1, count);
        var report = await db.MonthlyReports.SingleAsync();
        Assert.Equal(ProviderName, report.ProviderName);
        Assert.Equal("101", report.ExternalReportId);
        Assert.Equal("2001", report.ExternalCompanyId);

        var lineItem = await db.MonthlyReportLineItems.SingleAsync();
        Assert.Equal(report.Id, lineItem.MonthlyReportId);
        Assert.Equal("10", lineItem.ProductCode);
        Assert.Equal(5000m, lineItem.ProductionQuantity);
        Assert.Equal(4200m, lineItem.SalesQuantity);
        Assert.Equal(105000000m, lineItem.SalesAmount);
    }

    [Fact]
    public async Task Normalize_JalaliMonth1402_01_MapsToCorrectGregorianWindow()
    {
        // Iranian Nowruz 1402 falls on March 21 2023 (Gregorian).
        // Farvardin (month 1) has 31 days → PeriodEnd = April 20, 2023.
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(SingleActivityJson), default);

        var report = await db.MonthlyReports.SingleAsync();

        // Cross-check with PersianCalendar to keep the test authoritative without hardcoding
        // calendar arithmetic manually.
        var cal = new PersianCalendar();
        var expectedStart = DateOnly.FromDateTime(cal.ToDateTime(1402, 1, 1, 0, 0, 0, 0));
        var daysInMonth = cal.GetDaysInMonth(1402, 1);
        var expectedEnd = DateOnly.FromDateTime(cal.ToDateTime(1402, 1, daysInMonth, 0, 0, 0, 0));

        Assert.Equal(expectedStart, report.PeriodStart); // 2023-03-21
        Assert.Equal(expectedEnd, report.PeriodEnd);     // 2023-04-20
    }

    [Fact]
    public async Task Normalize_MultiProductMonth_CreatesOneLineItemPerProduct()
    {
        await using var db = CreateDb();
        var count = await CreateNormalizer(db).NormalizeAsync(MakePayload(MultiProductJson, "2002"), default);

        Assert.Equal(1, count);
        Assert.Equal(1, await db.MonthlyReports.CountAsync());
        var lineItems = await db.MonthlyReportLineItems.ToListAsync();
        Assert.Equal(2, lineItems.Count);
        Assert.Contains(lineItems, li => li.ProductCode == "20" && li.SalesAmount == 225000000m);
        Assert.Contains(lineItems, li => li.ProductCode == "21" && li.SalesAmount == 98000000m);
    }

    [Fact]
    public async Task Normalize_MultiProductMonth_MonthlySalesMetricSourceSumsAcrossProducts()
    {
        // Verifies that MonthlySalesMetricInputSource is provider-agnostic: it sums all
        // line-item SalesAmount values for the report, so CodalDB multi-product months
        // produce the correct MONTHLY_SALES without any engine changes.
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(MultiProductJson, "2002"), default);

        var source = new MonthlySalesMetricInputSource(db);
        var observations = await source.LoadAsync("2002", default);

        Assert.Single(observations);
        var obs = observations.Single();
        Assert.Equal(225000000m + 98000000m, obs.Value); // 323000000
    }

    [Fact]
    public async Task Normalize_ZeroAmountMonth_IsRetained()
    {
        await using var db = CreateDb();
        var count = await CreateNormalizer(db).NormalizeAsync(MakePayload(ZeroAmountJson, "2003"), default);

        Assert.Equal(1, count);
        Assert.Equal(1, await db.MonthlyReports.CountAsync());
        var lineItem = await db.MonthlyReportLineItems.SingleAsync();
        Assert.Equal(0m, lineItem.SalesAmount);
        Assert.Equal(0m, lineItem.ProductionQuantity);
    }

    [Fact]
    public async Task Normalize_IdempotentRerun_NoDuplicateRowsCreated()
    {
        await using var db = CreateDb();
        var normalizer = CreateNormalizer(db);
        var payload = MakePayload(SingleActivityJson);

        await normalizer.NormalizeAsync(payload, default);
        await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(1, await db.MonthlyReports.CountAsync());
        Assert.Equal(1, await db.MonthlyReportLineItems.CountAsync());
    }

    [Fact]
    public async Task Normalize_EvidenceJson_ContainsJalaliDatesAndDeferredFieldNote()
    {
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(SingleActivityJson), default);

        var report = await db.MonthlyReports.SingleAsync();
        Assert.Contains("CodalMonthlyActivityPeriod", report.WarningsJson);
        Assert.Contains("1402", report.WarningsJson);                 // jalaliYear
        Assert.Contains("deferredLineItemFields", report.WarningsJson); // camelCase from Web defaults
        Assert.Contains("ProductTitle", report.WarningsJson);           // deferred field noted
    }
}
