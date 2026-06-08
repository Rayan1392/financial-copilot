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
        Assert.Equal("ProductSales:1001", report.ExternalReportId);
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
        Assert.Equal("ServiceSales:2001", report.ExternalReportId);
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
            ExternalReportId = "ProductSales:1001",
            PeriodStart = new DateOnly(2023, 3, 21),
            PeriodEnd = new DateOnly(2023, 4, 20),
            SourcePayloadChecksum = "codal",
            LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();

        await CreateNormalizer(db).NormalizeAsync(MakePayload(ProductSalesJson, "[]"), CancellationToken.None);

        Assert.Equal(2, await db.MonthlyReports.CountAsync(row => row.ExternalReportId == "ProductSales:1001"));
    }

    [Fact]
    public async Task Normalize_MonthlySalesMetricSourceAggregatesProductAndServiceRows()
    {
        await using var db = CreateDb();

        await CreateNormalizer(db).NormalizeAsync(MakePayload(ProductSalesJson, ServiceSalesJson), CancellationToken.None);

        var source = new MonthlySalesMetricInputSource(db);
        var observations = await source.LoadAsync(CompanyId, CancellationToken.None);

        Assert.Equal(2, observations.Count);
        Assert.Contains(observations, observation => observation.Value == 105000000m + 25200000m);
        Assert.Contains(observations, observation => observation.Value == 3000000m);
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

    private static ProviderRawPayload MakePayload(string productSalesJson, string serviceSalesJson)
    {
        var envelope = new NadpcoMonthlyActivityEnvelope(productSalesJson, serviceSalesJson);
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
