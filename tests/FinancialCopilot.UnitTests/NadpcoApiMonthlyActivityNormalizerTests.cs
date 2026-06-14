using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class NadpcoApiMonthlyActivityNormalizerTests
{
    private const string ProviderName = ProviderSources.NoavaranCurrentApiName;
    private const string CompanyId = "3";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-03T10:00:00Z");

    private const string ProductSalesJson = """
        [
          {
            "activityID": 1001,
            "com_ID": 3,
            "bourseSymbol": "TEST",
            "comTitle": "Test Manufacturing",
            "industryID": 10,
            "industryTitle": "Metals",
            "tseCode": "123456",
            "year": 1402,
            "month": 1,
            "fiscalYearEnd": "1402/12/29",
            "jalaliFiscalYearEnd": "1402/12/29",
            "publishDate": "2023-04-25T00:00:00",
            "jalaliPublishDate": "1402/02/05",
            "outputType": 2,
            "outputTypeTitle": "Domestic",
            "categoryID": 7,
            "categoryTitle": "Finished goods",
            "productID": 77,
            "productTitle": "Steel Billet",
            "productUnit": "ton",
            "productionQuantity": 5000,
            "salesQuantity": 4200,
            "salesRate": 25000.5,
            "salesValue": 105000000
          },
          {
            "activityID": 1001,
            "com_ID": 3,
            "year": 1402,
            "month": 1,
            "productID": 78,
            "productTitle": "Steel Slab",
            "productUnit": "ton",
            "productionQuantity": 1000,
            "salesQuantity": 900,
            "salesRate": 28000,
            "salesValue": 25200000
          }
        ]
        """;

    private const string ServiceSalesJson = """
        [
          {
            "activityID": 2001,
            "comId": 3,
            "bourseSymbol": "SERV",
            "comTitle": "Test Services",
            "industryID": 20,
            "industryTitle": "Services",
            "tseCode": "654321",
            "year": 1402,
            "month": 1,
            "fiscalYearEnd": "1402/12/29",
            "jalaliFiscalYearEnd": "1402/12/29",
            "publishDate": "2023-04-26T00:00:00",
            "jalaliPublishDate": "1402/02/06",
            "categoryID": 4,
            "categoryTitle": "Consulting",
            "serviceID": 501,
            "serviceTitle": "Implementation service",
            "serviceUnit": "contract",
            "salesQuantity": 3,
            "salesRate": 1000000,
            "salesValue": 3000000
          }
        ]
        """;

    private const string ZeroServiceSalesJson = """
        [
          {
            "activityID": 2002,
            "comId": 4,
            "year": 1401,
            "month": 12,
            "serviceID": 502,
            "serviceTitle": "Maintenance",
            "serviceUnit": "contract",
            "salesQuantity": 0,
            "salesRate": 0,
            "salesValue": 0
          }
        ]
        """;

    private const string MissingProductIdJson = """
        [
          {
            "activityID": 3001,
            "com_ID": 5,
            "year": 1402,
            "month": 2,
            "productTitle": "Uncoded Product",
            "productUnit": "kg",
            "salesQuantity": 10,
            "salesValue": 1000
          }
        ]
        """;

    [Fact]
    public async Task Normalize_ProductRows_CreatesOneReportWithLineItems()
    {
        await using var db = CreateDb();

        var count = await CreateNormalizer(db).NormalizeAsync(
            MakePayload(ProductSalesJson, "[]"),
            CancellationToken.None);

        Assert.Equal(1, count);
        var report = await db.MonthlyReports.SingleAsync();
        Assert.Equal(ProviderName, report.ProviderName);
        Assert.Equal("ProductSales:1001:output-2", report.ExternalReportId);
        Assert.Equal(CompanyId, report.ExternalCompanyId);
        var lineItems = await db.MonthlyReportLineItems.ToListAsync();
        Assert.Equal(2, lineItems.Count);
        Assert.Contains(lineItems, item => item.ProductCode == "PRODUCT:77" && item.SalesAmount == 105000000m);
        Assert.Contains(lineItems, item => item.ProductCode == "PRODUCT:78" && item.ProductionQuantity == 1000m);
    }

    [Fact]
    public async Task Normalize_ServiceRows_CreatesSalesOnlyLineItem()
    {
        await using var db = CreateDb();

        var count = await CreateNormalizer(db).NormalizeAsync(
            MakePayload("[]", ServiceSalesJson),
            CancellationToken.None);

        Assert.Equal(1, count);
        var report = await db.MonthlyReports.SingleAsync();
        Assert.Equal("ServiceSales:2001:output-none", report.ExternalReportId);
        var item = await db.MonthlyReportLineItems.SingleAsync();
        Assert.Equal("SERVICE:501", item.ProductCode);
        Assert.Null(item.ProductionQuantity);
        Assert.Equal(3m, item.SalesQuantity);
        Assert.Equal(3000000m, item.SalesAmount);
    }

    [Fact]
    public async Task Normalize_ZeroActivityPeriod_IsRetained()
    {
        await using var db = CreateDb();

        await CreateNormalizer(db).NormalizeAsync(MakePayload("[]", ZeroServiceSalesJson), CancellationToken.None);

        Assert.Equal(1, await db.MonthlyReports.CountAsync());
        var item = await db.MonthlyReportLineItems.SingleAsync();
        Assert.Equal(0m, item.SalesQuantity);
        Assert.Equal(0m, item.SalesAmount);
    }

    [Fact]
    public async Task Normalize_JalaliMonth_UsesSharedGregorianWindow()
    {
        await using var db = CreateDb();

        await CreateNormalizer(db).NormalizeAsync(MakePayload(ProductSalesJson, "[]"), CancellationToken.None);

        var report = await db.MonthlyReports.SingleAsync();
        var cal = new PersianCalendar();
        Assert.Equal(DateOnly.FromDateTime(cal.ToDateTime(1402, 1, 1, 0, 0, 0, 0)), report.PeriodStart);
        Assert.Equal(DateOnly.FromDateTime(cal.ToDateTime(1402, 1, 31, 0, 0, 0, 0)), report.PeriodEnd);
    }

    [Fact]
    public async Task Normalize_MissingProductId_UsesDeterministicNaturalKeyAndEvidence()
    {
        await using var db = CreateDb();

        await CreateNormalizer(db).NormalizeAsync(MakePayload(MissingProductIdJson, "[]"), CancellationToken.None);

        var item = await db.MonthlyReportLineItems.SingleAsync();
        var report = await db.MonthlyReports.SingleAsync();
        Assert.StartsWith("PRODUCT:NATURAL:", item.ProductCode);
        Assert.Contains("not a fabricated vendor product/service id", report.WarningsJson);
        Assert.Contains("Uncoded Product", report.WarningsJson);
    }

    [Fact]
    public async Task Normalize_IdempotentRerun_DoesNotDuplicateRows()
    {
        await using var db = CreateDb();
        var normalizer = CreateNormalizer(db);
        var payload = MakePayload(ProductSalesJson, ServiceSalesJson);

        await normalizer.NormalizeAsync(payload, CancellationToken.None);
        await normalizer.NormalizeAsync(payload, CancellationToken.None);

        Assert.Equal(2, await db.MonthlyReports.CountAsync());
        Assert.Equal(3, await db.MonthlyReportLineItems.CountAsync());
    }

    [Fact]
    public async Task Normalize_CoexistsWithCodalDbSameExternalReportId()
    {
        await using var db = CreateDb();
        db.MonthlyReports.Add(new NormalizedMonthlyReportRow
        {
            Id = Guid.NewGuid(),
            ProviderName = ProviderSources.NoavaranArchiveSqlName,
            ExternalCompanyId = CompanyId,
            ExternalReportId = "ProductSales:1001:output-2",
            PeriodStart = new DateOnly(2023, 3, 21),
            PeriodEnd = new DateOnly(2023, 4, 20),
            SourcePayloadChecksum = "codal",
            LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();

        await CreateNormalizer(db).NormalizeAsync(MakePayload(ProductSalesJson, "[]"), CancellationToken.None);

        Assert.Equal(2, await db.MonthlyReports.CountAsync(row => row.ExternalReportId == "ProductSales:1001:output-2"));
    }

    // Single-month (outputType=0) fixture used by the metric-source filter tests.
    // Uses a different activityId (9001) and externalReportId suffix ":output-0" so it
    // does not collide with ProductSalesJson (which uses activityId 1001, outputType 2).
    private const string SingleMonthProductSalesJson = """
        [
          {
            "activityID": 9001,
            "com_ID": 3,
            "year": 1402,
            "month": 1,
            "outputType": 0,
            "productID": 77,
            "productTitle": "Steel Billet",
            "productUnit": "ton",
            "salesValue": 105000000
          },
          {
            "activityID": 9001,
            "com_ID": 3,
            "year": 1402,
            "month": 1,
            "outputType": 0,
            "productID": 78,
            "productTitle": "Steel Slab",
            "productUnit": "ton",
            "salesValue": 25200000
          }
        ]
        """;

    [Fact]
    public async Task Normalize_MonthlySalesMetricSource_ProductSalesYieldsOneObservation()
    {
        await using var db = CreateDb();

        // Use SingleMonthProductSalesJson (outputType=0) so the source filter includes the row.
        await CreateNormalizer(db).NormalizeAsync(MakePayload(SingleMonthProductSalesJson, "[]"), CancellationToken.None);

        var source = new MonthlySalesMetricInputSource(db);
        var observations = await source.LoadAsync(CompanyId, CancellationToken.None);

        // ProductSales (outputType=0) normalizes to one MonthlyReport → one observation.
        var observation = Assert.Single(observations);
        Assert.Equal(105000000m + 25200000m, observation.Value);
    }

    [Fact]
    public async Task Normalize_MonthlySalesMetricSource_ServiceSalesYieldsOneObservation()
    {
        await using var db = CreateDb();

        await CreateNormalizer(db).NormalizeAsync(MakePayload("[]", ServiceSalesJson), CancellationToken.None);

        var source = new MonthlySalesMetricInputSource(db);
        var observations = await source.LoadAsync(CompanyId, CancellationToken.None);

        // ServiceSales has OutputType=null → included by the null-pass-through rule.
        var observation = Assert.Single(observations);
        Assert.Equal(3000000m, observation.Value);
    }

    [Fact]
    public async Task Processor_RoutesNadpcoMonthlyActivityAndPublishesRecalculationRequest()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateDb();
        var payload = MakePayload(ProductSalesJson, ServiceSalesJson);
        var provider = new StubMonthlyProvider(payload);
        var router = new FinancialDataProviderRouter(
            new Dictionary<string, ISymbolDataProvider>(),
            new Dictionary<string, IFinancialStatementProvider>(),
            new Dictionary<string, IMonthlyProductionSalesProvider> { [ProviderName] = provider });
        var processor = new FinancialDataSyncProcessor(
            ingestionDb,
            new ProviderRawPayloadStore(providerDb),
            new ThrowingProvider(),
            new ThrowingProvider(),
            new ThrowingProvider(),
            [CreateNormalizer(ingestionDb)],
            new StoredDerivedMetricRecalculationPublisher(ingestionDb),
            new FixedTimeProvider(Now),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FinancialDataSyncProcessor>.Instance,
            providerRouter: router);

        var result = await processor.ProcessAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.MonthlyProductionSales,
                CompanyId,
                Now,
                "nadpco-monthly-v1",
                ProviderName),
            CancellationToken.None);

        Assert.Equal(DataSyncRunStatus.Completed, result.Run.Status);
        Assert.Single(await providerDb.ProviderRawPayloads.ToListAsync());
        var request = await ingestionDb.MetricRecalculationRequests.SingleAsync();
        Assert.Equal(ProviderDataset.MonthlyProductionSales.ToString(), request.SourceDataset);
        Assert.Equal(CompanyId, request.ExternalReference);
    }

    // Exact live v2 ProductSales shape captured 2026-06-10: company parent + nested productSales
    // items carrying month/year and per-product facts; productId 0 is a vendor placeholder.
    private const string LiveNestedProductSalesJson = """
        [
          {
            "companyTSESymbol": "کچاد",
            "categoryId": 3,
            "categoryTitle": "استخراج سنگ معدن های فلزی آهنی",
            "companyId": 3,
            "companyTitle": "معدنی و صنعتی چادرملو",
            "industryId": 3,
            "industryTitle": "استخراج کانه های فلزی",
            "instCode": 18027801615184692,
            "outputTypeId": 0,
            "productSales": [
              {
                "month": 2,
                "year": 1405,
                "fiscalEndDate": "1405/09/30",
                "productId": 0,
                "productTitle": "آپاتیت",
                "productProduceAmount": 120,
                "productUnit": "تن",
                "productSaleAmount": 100,
                "productSaleRate": 25000,
                "productSaleValue": 2500000,
                "outputTypeTitle": "دوره یک ماهه"
              },
              {
                "month": 2,
                "year": 1405,
                "fiscalEndDate": "1405/09/30",
                "productId": 0,
                "productTitle": "کنسانتره",
                "productProduceAmount": 900,
                "productUnit": "تن",
                "productSaleAmount": 800,
                "productSaleRate": 30000,
                "productSaleValue": 24000000,
                "outputTypeTitle": "دوره یک ماهه"
              }
            ]
          }
        ]
        """;

    // Exact live v3 ServiceSales shape captured 2026-06-10: flat records; the month's revenue is
    // "revenueDuringThePeriod" and there is no quantity/unit/code.
    private const string LiveServiceSalesJson = """
        [
          {
            "companyTSESymbol": "کیسون",
            "publishDateTime": "2026-05-25T17:40:44",
            "categoryId": 10173,
            "categoryTitle": "پیمانکاری املاک و مستغلات",
            "companyId": 13201,
            "companyTitle": "کیسون",
            "industryId": 28,
            "industryTitle": "انبوه سازی املاک و مستغلات",
            "instCode": "38628771709301941",
            "month": 2,
            "year": 1405,
            "fiscalEndDate": "1405/12/29",
            "serviceTitle": "پروژه‌های مسکن و ساختمان",
            "serviceContractDate": null,
            "serviceContractTerm": 0,
            "revenueDuringThePeriod": 227511.00,
            "revenueFromTheBeginning": 279363.00,
            "revenueEndOfLastPeriod": 7866204.00
          }
        ]
        """;

    [Fact]
    public async Task Normalize_LiveNestedProductSalesShape_FlattensCompanyMonthLineItems()
    {
        await using var db = CreateDb();

        var count = await CreateNormalizer(db).NormalizeAsync(
            MakePayload(LiveNestedProductSalesJson, "[]"),
            CancellationToken.None);

        Assert.Equal(1, count);
        var report = await db.MonthlyReports.SingleAsync();
        Assert.Equal("3", report.ExternalCompanyId);
        var cal = new PersianCalendar();
        Assert.Equal(DateOnly.FromDateTime(cal.ToDateTime(1405, 2, 1, 0, 0, 0, 0)), report.PeriodStart);

        var lineItems = await db.MonthlyReportLineItems.ToListAsync();
        Assert.Equal(2, lineItems.Count);
        // productId 0 is a placeholder → deterministic natural keys, not "PRODUCT:0" collisions.
        Assert.Equal(2, lineItems.Select(item => item.ProductCode).Distinct().Count());
        Assert.All(lineItems, item => Assert.StartsWith("PRODUCT:NATURAL:", item.ProductCode));
        var apatite = Assert.Single(lineItems, item => item.Title == "آپاتیت");
        Assert.Equal(120m, apatite.ProductionQuantity);
        Assert.Equal(100m, apatite.SalesQuantity);
        Assert.Equal(2500000m, apatite.SalesAmount);
        Assert.Equal(25000m, apatite.SalesRate);
        Assert.Equal("تن", apatite.Unit);
    }

    [Fact]
    public async Task Normalize_LiveServiceSalesShape_MapsPeriodRevenueAsSalesAmount()
    {
        await using var db = CreateDb();

        var count = await CreateNormalizer(db).NormalizeAsync(
            MakePayload("[]", LiveServiceSalesJson),
            CancellationToken.None);

        Assert.Equal(1, count);
        var report = await db.MonthlyReports.SingleAsync();
        Assert.Equal("13201", report.ExternalCompanyId);
        var item = await db.MonthlyReportLineItems.SingleAsync();
        Assert.Equal(227511.00m, item.SalesAmount);
        Assert.Null(item.ProductionQuantity);
        Assert.Equal("پروژه‌های مسکن و ساختمان", item.Title);
    }

    [Fact]
    public async Task Normalize_NewEnvelope_StoresAllFiveOutputTypes()
    {
        await using var db = CreateDb();

        // Single product JSON with activityID=9001 in each slot; outputType in the record itself
        // overrides the slot hint, so we use records without an embedded outputType to test the hint.
        var productJson = """
            [{"activityID": 9001, "com_ID": 3, "year": 1404, "month": 3,
              "productTitle": "P", "productUnit": "ton", "salesQuantity": 1, "salesValue": 100}]
            """;
        var envelope = new NadpcoMonthlyActivityEnvelope(
            ProductSalesType0: productJson,
            ProductSalesType1: productJson,
            ProductSalesType2: productJson,
            ProductSalesType3: productJson,
            ProductSalesType4: productJson,
            ServiceSales: "[]");
        var payload = new ProviderRawPayload(
            Guid.NewGuid(), ProviderName, ProviderDataset.MonthlyProductionSales,
            "api/v*/MonthlyActivity/*Sales", CompanyId,
            JsonSerializer.Serialize(envelope, JsonOptions),
            "checksum-multi", Now);

        var count = await CreateNormalizer(db).NormalizeAsync(payload, CancellationToken.None);

        Assert.Equal(5, count);
        Assert.Equal(5, await db.MonthlyReports.CountAsync());
        // Each report should have its slot output-type hint set (0–4).
        var outputTypes = await db.MonthlyReports.Select(r => r.OutputType).OrderBy(t => t).ToListAsync();
        Assert.Equal([0, 1, 2, 3, 4], outputTypes);
        // ExternalReportIds must be distinct (include output type suffix).
        var ids = await db.MonthlyReports.Select(r => r.ExternalReportId).ToListAsync();
        Assert.Equal(5, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Contains(":output-", id));
    }

    [Fact]
    public async Task Normalize_LegacyEnvelope_BackwardCompat_OutputTypeNull()
    {
        await using var db = CreateDb();

        // MakeLegacyPayload uses the old 2-field envelope; outputType should be null (no slot hint,
        // no outputType field in the record).
        var legacyJson = """
            [{"activityID": 8001, "com_ID": 3, "year": 1402, "month": 1,
              "productTitle": "OldProduct", "productUnit": "kg",
              "salesQuantity": 50, "salesValue": 5000}]
            """;

        var count = await CreateNormalizer(db).NormalizeAsync(
            MakeLegacyPayload(legacyJson, "[]"), CancellationToken.None);

        Assert.Equal(1, count);
        var report = await db.MonthlyReports.SingleAsync();
        Assert.Null(report.OutputType);
        Assert.Equal("ProductSales:8001:output-none", report.ExternalReportId);
    }

    [Fact]
    public async Task Normalize_NullEnvelopeSlots_NoRowCreated()
    {
        await using var db = CreateDb();

        // All product-sales slots null, empty service sales → no reports.
        var envelope = new NadpcoMonthlyActivityEnvelope(null, null, null, null, null, "[]");
        var payload = new ProviderRawPayload(
            Guid.NewGuid(), ProviderName, ProviderDataset.MonthlyProductionSales,
            "api/v*/MonthlyActivity/*Sales", CompanyId,
            JsonSerializer.Serialize(envelope, JsonOptions),
            "checksum-empty", Now);

        var count = await CreateNormalizer(db).NormalizeAsync(payload, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(0, await db.MonthlyReports.CountAsync());
    }

    [Fact]
    public async Task BuildExternalReportId_IncludesOutputType_WhenActivityIdPresent()
    {
        await using var db = CreateDb();

        // Record has activityID=5555 and outputType=1 — ExternalReportId must include both.
        var json = """
            [{"activityID": 5555, "com_ID": 3, "year": 1404, "month": 5,
              "outputType": 1,
              "productTitle": "CheckProduct", "salesQuantity": 1, "salesValue": 10}]
            """;
        var envelope = new NadpcoMonthlyActivityEnvelope(null, json, null, null, null, "[]");
        var payload = new ProviderRawPayload(
            Guid.NewGuid(), ProviderName, ProviderDataset.MonthlyProductionSales,
            "api/v*/MonthlyActivity/*Sales", CompanyId,
            JsonSerializer.Serialize(envelope, JsonOptions),
            "cs", Now);

        await CreateNormalizer(db).NormalizeAsync(payload, CancellationToken.None);

        var report = await db.MonthlyReports.SingleAsync();
        Assert.Equal("ProductSales:5555:output-1", report.ExternalReportId);
        Assert.Equal(1, report.OutputType);
    }

    // Wraps product sales JSON in the new 6-field envelope; uses slot 2 to match the outputType
    // embedded in the existing test JSON constants (outputType: 2). Pass null to leave a slot empty.
    private static ProviderRawPayload MakePayload(string productSalesJson, string serviceSalesJson)
    {
        var envelope = new NadpcoMonthlyActivityEnvelope(
            ProductSalesType0: null,
            ProductSalesType1: null,
            ProductSalesType2: productSalesJson == "[]" ? null : productSalesJson,
            ProductSalesType3: null,
            ProductSalesType4: null,
            ServiceSales: serviceSalesJson);
        return new ProviderRawPayload(
            Guid.NewGuid(),
            ProviderName,
            ProviderDataset.MonthlyProductionSales,
            "api/v*/MonthlyActivity/*Sales",
            CompanyId,
            JsonSerializer.Serialize(envelope, JsonOptions),
            "checksum-" + Guid.NewGuid(),
            Now);
    }

    // Wraps product sales JSON in the legacy 2-field envelope for backward-compatibility tests.
    private static ProviderRawPayload MakeLegacyPayload(string productSalesJson, string serviceSalesJson)
    {
        var envelope = new NadpcoMonthlyActivityLegacyEnvelope(productSalesJson, serviceSalesJson);
        return new ProviderRawPayload(
            Guid.NewGuid(),
            ProviderName,
            ProviderDataset.MonthlyProductionSales,
            "api/v*/MonthlyActivity/*Sales",
            CompanyId,
            JsonSerializer.Serialize(envelope, JsonOptions),
            "checksum-" + Guid.NewGuid(),
            Now);
    }

    private static NadpcoApiMonthlyActivityNormalizer CreateNormalizer(FinancialIngestionDbContext db) =>
        new(db);

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FinancialProviderDbContext CreateProviderDbContext() =>
        new(new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class StubMonthlyProvider(ProviderRawPayload payload) :
        ISymbolDataProvider,
        IFinancialStatementProvider,
        IMonthlyProductionSalesProvider
    {
        public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            Task.FromResult(payload);
    }

    private sealed class ThrowingProvider :
        ISymbolDataProvider,
        IFinancialStatementProvider,
        IMonthlyProductionSalesProvider
    {
        public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Default provider should not be used.");

        public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Default provider should not be used.");

        public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Default provider should not be used.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
