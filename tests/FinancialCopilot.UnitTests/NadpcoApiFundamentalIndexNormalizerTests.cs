using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class NadpcoApiFundamentalIndexNormalizerTests
{
    private const string ProviderName = ProviderSources.NoavaranCurrentApiName;
    private const string ExternalCompanyId = "4";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-03T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Normalize_AllowlistedIndexes_CreateSourceMarkedDerivedMetrics()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db);

        var outcome = await CreateNormalizer(db).NormalizeAsync(MakePayload(SampleJson), CancellationToken.None);

        Assert.Equal(2, outcome.ProcessedRecords);
        var rows = await db.DerivedMetrics.OrderBy(row => row.MetricCode).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("nadpco-api-fundamental-index-source-v1", row.CalculationPolicyVersion));
        Assert.Contains(rows, row => row.MetricCode == "CURRENT_RATIO" && row.Value == 1.03m && row.Unit == "Ratio");
        Assert.Contains(rows, row => row.MetricCode == "NET_WORKING_CAPITAL" && row.Value == 9831438m && row.Unit == "Amount");
    }

    [Fact]
    public async Task Normalize_IgnoresUnmappedIndexes()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db);

        var outcome = await CreateNormalizer(db).NormalizeAsync(MakePayload(UnmappedJson), CancellationToken.None);

        Assert.Equal(0, outcome.ProcessedRecords);
        Assert.Empty(await db.DerivedMetrics.ToListAsync());
    }

    [Fact]
    public async Task Normalize_ConvertsJalaliPeriodAndRetainsVendorEvidence()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db);

        await CreateNormalizer(db).NormalizeAsync(MakePayload(SampleJson), CancellationToken.None);

        var currentRatio = await db.DerivedMetrics.SingleAsync(row => row.MetricCode == "CURRENT_RATIO");
        Assert.Equal("SixMonths", currentRatio.PeriodType);
        Assert.Equal(ConvertJalali(1401, 12, 29).AddYears(-1).AddDays(1), currentRatio.PeriodStart);
        Assert.Equal(ConvertJalali(1401, 6, 31), currentRatio.PeriodEnd);
        Assert.Contains(ProviderSources.NoavaranCurrentApiName, currentRatio.SourceEvidenceJson);
        Assert.Contains("companyIndexGroupTitle", currentRatio.SourceEvidenceJson);
        Assert.Contains("companyIndexTitle", currentRatio.SourceEvidenceJson);
        Assert.Contains("vendorPrecomputed", currentRatio.SourceEvidenceJson);
    }

    [Fact]
    public async Task Normalize_CanonicalVariantSelection_PrefersAuditedVariant()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db);

        await CreateNormalizer(db).NormalizeAsync(MakePayload(VariantJson), CancellationToken.None);

        var currentRatio = await db.DerivedMetrics.SingleAsync(row => row.MetricCode == "CURRENT_RATIO");
        Assert.Equal(2.25m, currentRatio.Value);
        Assert.Contains("\"comBSID\":323929", currentRatio.SourceEvidenceJson);
    }

    [Fact]
    public async Task Normalize_IdempotentRerun_UpdatesWithoutDuplicates()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db);
        var normalizer = CreateNormalizer(db);
        var payload = MakePayload(SampleJson);

        await normalizer.NormalizeAsync(payload, CancellationToken.None);
        await normalizer.NormalizeAsync(payload, CancellationToken.None);

        Assert.Equal(2, await db.DerivedMetrics.CountAsync());
    }

    [Fact]
    public async Task Normalize_DistinctPolicyVersion_DoesNotOverwriteEngineRows()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db);
        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = ExternalCompanyId,
            MetricCode = "CURRENT_RATIO",
            MetricVersion = "v1",
            CalculationPolicyVersion = "engine-current-ratio-v1",
            PeriodType = "SixMonths",
            PeriodStart = ConvertJalali(1401, 12, 29).AddYears(-1).AddDays(1),
            PeriodEnd = ConvertJalali(1401, 6, 31),
            Value = 9m,
            Unit = "Ratio",
            ObservedAt = Now,
            LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();

        await CreateNormalizer(db).NormalizeAsync(MakePayload(SampleJson), CancellationToken.None);

        var currentRatioRows = await db.DerivedMetrics
            .Where(row => row.MetricCode == "CURRENT_RATIO")
            .ToListAsync();
        Assert.Equal(2, currentRatioRows.Count);
        Assert.Contains(currentRatioRows, row => row.CalculationPolicyVersion == "engine-current-ratio-v1");
        Assert.Contains(currentRatioRows, row => row.CalculationPolicyVersion == "nadpco-api-fundamental-index-source-v1");
    }

    [Fact]
    public async Task Normalize_InvalidJalaliDate_ThrowsProviderException()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db);

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(
            () => CreateNormalizer(db).NormalizeAsync(MakePayload(InvalidDateJson), CancellationToken.None));

        Assert.Equal(FinancialProviderErrorCode.InvalidResponse, exception.Code);
    }

    [Fact]
    public async Task Processor_RoutesNadpcoFundamentalIndexesThroughDedicatedDataset()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateDb();
        await SeedSymbolAsync(ingestionDb);
        var payload = MakePayload(SampleJson);
        var provider = new StubRatioProvider(payload);
        var router = new FinancialDataProviderRouter(
            new Dictionary<string, ISymbolDataProvider>(),
            new Dictionary<string, IFinancialStatementProvider>(),
            new Dictionary<string, IMonthlyProductionSalesProvider>(),
            new Dictionary<string, IFinancialRatioProvider> { [ProviderName] = provider });
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
            ratioProvider: new ThrowingProvider(),
            providerRouter: router);

        var result = await processor.ProcessAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.FundamentalIndexes,
                ExternalCompanyId,
                Now,
                "nadpco-fundamental-indexes",
                ProviderName),
            CancellationToken.None);

        Assert.Equal(2, result.Run.ProcessedRecords);
        Assert.Equal(2, await ingestionDb.DerivedMetrics.CountAsync());
        var recalculation = await ingestionDb.MetricRecalculationRequests.SingleAsync();
        Assert.Equal(nameof(ProviderDataset.FundamentalIndexes), recalculation.SourceDataset);
    }

    private const string SampleJson = """
        [
          {
            "comBS_ID": 323928,
            "comId": 4,
            "comTitle": "معدنی و صنعتی گل گهر",
            "periodType": 6,
            "jalaliFiscalYearEnd": "1401/12/29",
            "jalaliPeriodEnd": "1401/06/31",
            "jalaliAnouncementDate": "2023-01-07T11:01:32",
            "isAudited": true,
            "isRepresented": false,
            "isComposing": false,
            "indexes": [
              {
                "companyIndexId": 65,
                "companyIndexTitle": "نسبت جاری",
                "companyIndexGroupId": 1156,
                "companyIndexGroupTitle": "سنجش نقدینگی",
                "companyIndexValue": 1.03,
                "companyIndexUnit": ""
              },
              {
                "companyIndexId": 4069,
                "companyIndexTitle": "خالص سرمایه در گردش",
                "companyIndexGroupId": 1156,
                "companyIndexGroupTitle": "سنجش نقدینگی",
                "companyIndexValue": 9831438,
                "companyIndexUnit": ""
              },
              {
                "companyIndexId": 4138,
                "companyIndexTitle": "بازده حقوق صاحبان سهام",
                "companyIndexGroupId": 1160,
                "companyIndexGroupTitle": "سنجش سودآوری",
                "companyIndexValue": 18.5,
                "companyIndexUnit": ""
              }
            ]
          }
        ]
        """;

    private const string VariantJson = """
        [
          {
            "comBS_ID": 323928,
            "comId": 4,
            "comTitle": "معدنی و صنعتی گل گهر",
            "periodType": 6,
            "jalaliFiscalYearEnd": "1401/12/29",
            "jalaliPeriodEnd": "1401/06/31",
            "jalaliAnouncementDate": "2023-01-01T00:00:00",
            "isAudited": false,
            "isRepresented": false,
            "isComposing": false,
            "indexes": [
              { "companyIndexId": 65, "companyIndexTitle": "نسبت جاری", "companyIndexGroupId": 1156, "companyIndexGroupTitle": "سنجش نقدینگی", "companyIndexValue": 1.03, "companyIndexUnit": "" }
            ]
          },
          {
            "comBS_ID": 323929,
            "comId": 4,
            "comTitle": "معدنی و صنعتی گل گهر",
            "periodType": 6,
            "jalaliFiscalYearEnd": "1401/12/29",
            "jalaliPeriodEnd": "1401/06/31",
            "jalaliAnouncementDate": "2023-01-02T00:00:00",
            "isAudited": true,
            "isRepresented": false,
            "isComposing": false,
            "indexes": [
              { "companyIndexId": 65, "companyIndexTitle": "نسبت جاری", "companyIndexGroupId": 1156, "companyIndexGroupTitle": "سنجش نقدینگی", "companyIndexValue": 2.25, "companyIndexUnit": "" }
            ]
          }
        ]
        """;

    private const string UnmappedJson = """
        [
          {
            "comBS_ID": 323928,
            "comId": 4,
            "comTitle": "معدنی و صنعتی گل گهر",
            "periodType": 6,
            "jalaliFiscalYearEnd": "1401/12/29",
            "jalaliPeriodEnd": "1401/06/31",
            "jalaliAnouncementDate": "2023-01-07T11:01:32",
            "isAudited": true,
            "isRepresented": false,
            "isComposing": false,
            "indexes": [
              { "companyIndexId": 99999, "companyIndexTitle": "unmapped", "companyIndexGroupId": 0, "companyIndexGroupTitle": "ignored", "companyIndexValue": 1, "companyIndexUnit": "" }
            ]
          }
        ]
        """;

    private const string InvalidDateJson = """
        [
          {
            "comBS_ID": 323928,
            "comId": 4,
            "comTitle": "معدنی و صنعتی گل گهر",
            "periodType": 6,
            "jalaliFiscalYearEnd": "not-a-date",
            "jalaliPeriodEnd": "1401/06/31",
            "jalaliAnouncementDate": "2023-01-07T11:01:32",
            "isAudited": true,
            "isRepresented": false,
            "isComposing": false,
            "indexes": [
              { "companyIndexId": 65, "companyIndexTitle": "نسبت جاری", "companyIndexGroupId": 1156, "companyIndexGroupTitle": "سنجش نقدینگی", "companyIndexValue": 1.03, "companyIndexUnit": "" }
            ]
          }
        ]
        """;

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FinancialProviderDbContext CreateProviderDbContext() =>
        new(new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static NadpcoApiFundamentalIndexNormalizer CreateNormalizer(FinancialIngestionDbContext db) =>
        new(db);

    private static ProviderRawPayload MakePayload(string json) =>
        new(Guid.NewGuid(), ProviderName, ProviderDataset.FundamentalIndexes,
            "api/v2/CompanyFundamentalIndex/Values", ExternalCompanyId,
            json, "checksum-" + Guid.NewGuid(), Now);

    private static async Task SeedSymbolAsync(FinancialIngestionDbContext db)
    {
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = ProviderName,
            ExternalCompanyId = ExternalCompanyId,
            Name = "Gol Gohar",
            LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();
    }

    private static DateOnly ConvertJalali(int year, int month, int day)
    {
        var calendar = new PersianCalendar();
        return DateOnly.FromDateTime(calendar.ToDateTime(year, month, day, 0, 0, 0, 0));
    }

    private sealed class StubRatioProvider(ProviderRawPayload payload) : IFinancialRatioProvider
    {
        public Task<ProviderRawPayload> FetchFinancialRatiosAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            Task.FromResult(payload);
    }

    private sealed class ThrowingProvider :
        ISymbolDataProvider,
        IFinancialStatementProvider,
        IMonthlyProductionSalesProvider,
        IFinancialRatioProvider
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
            throw new NotSupportedException();

        public Task<ProviderRawPayload> FetchFinancialRatiosAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
