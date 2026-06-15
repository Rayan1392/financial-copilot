using System.Net;
using System.Text;
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

public sealed class NadpcoApiFinancialStatementNormalizerTests
{
    private const string ProviderName = ProviderSources.NoavaranCurrentApiName;
    private const string CompanyId = "3";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-03T10:00:00Z");

    private const string IncomeJson = """
        [
          {
            "statementID": 474147,
            "com_ID": 3,
            "bourseSymbol": "کچاد",
            "fullTitle": "معدنی و صنعتی چادرملو",
            "periodType": 6,
            "fiscalYearEnd": "2023-03-20T00:00:00",
            "jalaliFiscalYearEnd": "1401/12/29",
            "periodEnd": "2022-09-22T00:00:00",
            "jalaliPeriodEnd": "1401/06/31",
            "anouncementDate": "2022-10-01T00:00:00",
            "jalaliAnouncementDate": "1401/07/09",
            "isAudited": false,
            "isRepresented": false,
            "isComposing": false,
            "items": [
              { "itemID": 143, "itemTitle": "سود خالص", "amount": 100, "amountUnit": "N/A" },
              { "itemID": 15, "itemTitle": "فروش خالص", "amount": 500, "amountUnit": "N/A" }
            ]
          },
          {
            "statementID": 474148,
            "com_ID": 3,
            "bourseSymbol": "کچاد",
            "fullTitle": "معدنی و صنعتی چادرملو",
            "periodType": 6,
            "fiscalYearEnd": "2023-03-20T00:00:00",
            "jalaliFiscalYearEnd": "1401/12/29",
            "periodEnd": "2022-09-22T00:00:00",
            "jalaliPeriodEnd": "1401/06/31",
            "anouncementDate": "2022-10-22T21:13:01",
            "jalaliAnouncementDate": "1401/07/30",
            "isAudited": true,
            "isRepresented": false,
            "isComposing": false,
            "items": [
              { "itemID": 143, "itemTitle": "سود خالص", "amount": 200, "amountUnit": "N/A" },
              { "itemID": 15, "itemTitle": "فروش خالص", "amount": 600, "amountUnit": "N/A" },
              { "itemID": 999, "itemTitle": "unmapped", "amount": 1, "amountUnit": "N/A" }
            ]
          }
        ]
        """;

    private const string BalanceJson = """
        [
          {
            "statementID": 333653,
            "com_ID": 3,
            "bourseSymbol": "کچاد",
            "fullTitle": "معدنی و صنعتی چادرملو",
            "periodType": 3,
            "fiscalYearEnd": "2024-03-19T00:00:00",
            "jalaliFiscalYearEnd": "1402/12/29",
            "periodEnd": "2023-06-21T00:00:00",
            "jalaliPeriodEnd": "1402/03/31",
            "anouncementDate": "2023-07-20T19:28:39",
            "jalaliAnouncementDate": "1402/04/29",
            "isAudited": false,
            "isRepresented": false,
            "isComposing": false,
            "items": [
              { "itemID": 147, "itemTitle": "حقوق صاحبان سهام", "amount": 8000, "amountUnit": "N/A" }
            ]
          }
        ]
        """;

    private const string CashFlowJson = """
        [
          {
            "statementID": 231242,
            "com_ID": 3,
            "bourseSymbol": "کچاد",
            "fullTitle": "معدنی و صنعتی چادرملو",
            "periodType": 6,
            "fiscalYearEnd": "2023-03-20T00:00:00",
            "jalaliFiscalYearEnd": "1401/12/29",
            "periodEnd": "2022-09-22T00:00:00",
            "jalaliPeriodEnd": "1401/06/31",
            "anouncementDate": "2022-10-22T21:13:01",
            "jalaliAnouncementDate": "1401/07/30",
            "isAudited": false,
            "isRepresented": false,
            "isComposing": false,
            "items": [
              { "itemID": 1, "itemTitle": "جریان وجه نقد عملیاتی", "amount": 62864564, "amountUnit": "N/A" }
            ]
          }
        ]
        """;

    [Fact]
    public async Task Normalize_WritesDistinctIncomeBalanceAndCashFlowRows()
    {
        await using var db = CreateDb();
        var outcome = await CreateNormalizer(db).NormalizeAsync(MakePayload(), CancellationToken.None);

        Assert.Equal(3, outcome.ProcessedRecords);
        Assert.Contains(await db.FinancialStatements.ToListAsync(), s => s.StatementType == "IncomeStatement");
        Assert.Contains(await db.FinancialStatements.ToListAsync(), s => s.StatementType == "BalanceSheet");
        Assert.Contains(await db.FinancialStatements.ToListAsync(), s => s.StatementType == "CashFlow");
    }

    [Fact]
    public async Task Normalize_MapsCuratedItemsAndIgnoresUnmappedItems()
    {
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(), CancellationToken.None);

        var income = await db.FinancialStatements.SingleAsync(s => s.StatementType == "IncomeStatement");
        var incomeItems = await db.FinancialStatementLineItems
            .Where(i => i.FinancialStatementId == income.Id)
            .ToListAsync();
        var balance = await db.FinancialStatements.SingleAsync(s => s.StatementType == "BalanceSheet");
        var cashFlow = await db.FinancialStatements.SingleAsync(s => s.StatementType == "CashFlow");

        Assert.Contains(incomeItems, i => i.MetricCode == "NET_PROFIT" && i.Value == 200m);
        Assert.Contains(incomeItems, i => i.MetricCode == "REVENUE" && i.Value == 600m);
        Assert.DoesNotContain(incomeItems, i => i.MetricCode == "UNMAPPED");
        Assert.Contains(await db.FinancialStatementLineItems.ToListAsync(),
            i => i.FinancialStatementId == balance.Id && i.MetricCode == "TOTAL_EQUITY" && i.Value == 8000m);
        Assert.Contains(await db.FinancialStatementLineItems.ToListAsync(),
            i => i.FinancialStatementId == cashFlow.Id && i.MetricCode == "OPERATING_CASH_FLOW" && i.Value == 62864564m);
    }

    [Fact]
    public async Task Normalize_PeriodMapping_UsesFiscalYearStartAndGregorianPeriodEnd()
    {
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(), CancellationToken.None);

        var income = await db.FinancialStatements.SingleAsync(s => s.StatementType == "IncomeStatement");
        Assert.Equal("SixMonths", income.PeriodType);
        Assert.Equal(new DateOnly(2022, 3, 21), income.PeriodStart);
        Assert.Equal(new DateOnly(2022, 9, 22), income.PeriodEnd);
    }

    [Fact]
    public async Task Normalize_SelectionPolicy_PrefersAuditedVariant()
    {
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(), CancellationToken.None);

        var income = await db.FinancialStatements.SingleAsync(s => s.StatementType == "IncomeStatement");
        Assert.Equal("474148", income.ExternalStatementId);
        var netProfit = await db.FinancialStatementLineItems.SingleAsync(
            i => i.FinancialStatementId == income.Id && i.MetricCode == "NET_PROFIT");
        Assert.Equal(200m, netProfit.Value);
    }

    [Fact]
    public async Task Normalize_RecordsJalaliDatesAndVariantFlagsInWarningsJson()
    {
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(), CancellationToken.None);

        var income = await db.FinancialStatements.SingleAsync(s => s.StatementType == "IncomeStatement");
        Assert.Contains("NadpcoApiStatementSelection", income.WarningsJson);
        Assert.Contains("1401/06/31", income.WarningsJson);
        Assert.Contains("MillionRials", income.WarningsJson);
        Assert.Contains("isAudited", income.WarningsJson);
    }

    [Fact]
    public async Task Normalize_IsIdempotent()
    {
        await using var db = CreateDb();
        var normalizer = CreateNormalizer(db);
        var payload = MakePayload();

        await normalizer.NormalizeAsync(payload, CancellationToken.None);
        await normalizer.NormalizeAsync(payload, CancellationToken.None);

        Assert.Equal(3, await db.FinancialStatements.CountAsync());
        Assert.Equal(4, await db.FinancialStatementLineItems.CountAsync());
    }

    [Fact]
    public async Task Normalize_CoexistsWithCodalDbSameExternalStatementId()
    {
        await using var db = CreateDb();
        db.FinancialStatements.Add(new NormalizedFinancialStatementRow
        {
            Id = Guid.NewGuid(),
            ProviderName = ProviderSources.NoavaranArchiveSqlName,
            ExternalCompanyId = CompanyId,
            ExternalStatementId = "474148",
            StatementType = "IncomeStatement",
            PeriodType = "SixMonths",
            PeriodStart = new DateOnly(2022, 3, 21),
            PeriodEnd = new DateOnly(2022, 9, 22),
            SourcePayloadChecksum = "codal",
            LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();

        await CreateNormalizer(db).NormalizeAsync(MakePayload(), CancellationToken.None);

        Assert.Equal(2, await db.FinancialStatements.CountAsync(s =>
            s.ExternalStatementId == "474148" && s.StatementType == "IncomeStatement"));
    }

    [Fact]
    public async Task Normalize_MalformedChildPayload_ThrowsProviderException()
    {
        await using var db = CreateDb();
        var payload = MakePayload(incomeJson: "{not-json}");

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(
            () => CreateNormalizer(db).NormalizeAsync(payload, CancellationToken.None));

        Assert.Equal(FinancialProviderErrorCode.InvalidResponse, exception.Code);
    }

    [Fact]
    public async Task Processor_RoutesNadpcoStatementsAndPublishesRecalculationRequest()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateDb();
        var payload = MakePayload();
        var provider = new StubStatementProvider(payload);
        var router = new FinancialDataProviderRouter(
            new Dictionary<string, ISymbolDataProvider>(),
            new Dictionary<string, IFinancialStatementProvider> { [ProviderName] = provider },
            new Dictionary<string, IMonthlyProductionSalesProvider>());
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
                ProviderDataset.FinancialStatements,
                CompanyId,
                Now,
                "nadpco-statements-v1",
                ProviderName),
            CancellationToken.None);

        Assert.Equal(DataSyncRunStatus.Completed, result.Run.Status);
        Assert.Single(await providerDb.ProviderRawPayloads.ToListAsync());
        Assert.Equal(1, await ingestionDb.MetricRecalculationRequests.CountAsync());
    }

    private static ProviderRawPayload MakePayload(
        string incomeJson = IncomeJson,
        string balanceJson = BalanceJson,
        string cashFlowJson = CashFlowJson)
    {
        var envelope = new NadpcoFinancialStatementEnvelope(balanceJson, incomeJson, cashFlowJson);
        return new ProviderRawPayload(
            Guid.NewGuid(),
            ProviderName,
            ProviderDataset.FinancialStatements,
            "api/v2/FS/*/Values",
            CompanyId,
            JsonSerializer.Serialize(envelope, JsonOptions),
            "checksum-" + Guid.NewGuid(),
            Now);
    }

    private static NadpcoApiFinancialStatementNormalizer CreateNormalizer(FinancialIngestionDbContext db) =>
        new(db);

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FinancialProviderDbContext CreateProviderDbContext() =>
        new(new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class StubStatementProvider(ProviderRawPayload payload) :
        ISymbolDataProvider,
        IFinancialStatementProvider,
        IMonthlyProductionSalesProvider
    {
        public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            Task.FromResult(payload);

        public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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
