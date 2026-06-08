using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class NadpcoApiCompanyNormalizerTests
{
    private const string ProviderName = ProviderSources.NoavaranCurrentApiName;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-03T10:00:00Z");

    private const string CompaniesJson = """
        [
          {
            "coID": 13226,
            "coCode": "0113601",
            "coTitle": "کشت و صنعت آبشیرین",
            "coTitleEnglish": "Ab Shirin Agroindustrial Co.",
            "coSymbol": "آبین",
            "coSymbolEnglish": "ABYP",
            "floorID": 10186,
            "floorTitle": "تولید میوه جات",
            "industryID": 1,
            "industryTitle": "کشاورزی",
            "tseCode": "9987529074833218",
            "tseCIsinCode": "IRO7ABYP0004",
            "tseSIsinCode": "IRO7ABYP0001",
            "marketID": 16,
            "marketTitle": "پایه",
            "precedencyRight": 0,
            "acceptionDate": "1395/04/19",
            "acceptionDateGre": "2016-07-09T00:00:00",
            "enlistedDate": "1395/04/19",
            "enlistedDateGre": "2016-07-09T00:00:00",
            "ipoDate": "1395/11/26",
            "ipoDateGre": "2017-02-14T00:00:00",
            "fundTypeID": null,
            "fundTypeTitle": null,
            "coSymbolPinglish": "ABIN",
            "nationalID": "10260200698",
            "inExchange": 1,
            "establishmentDate": "1373/09/20",
            "establishmentDateGre": "1994-12-11T00:00:00",
            "businessStartDate": null,
            "businessStartDateGre": null,
            "registrationDate": "1373/09/20",
            "registrationDateGre": "1994-12-11T00:00:00",
            "registrationNumber": "1831",
            "registrationProvince": "تهران",
            "registrationCity": "تهران",
            "marketBoard": "بازار پایه زرد"
          }
        ]
        """;

    [Fact]
    public async Task Normalize_CreatesCompanySymbolAndDimensions()
    {
        await using var db = CreateIngestionDbContext();
        var count = await CreateNormalizer(db).NormalizeAsync(MakePayload(CompaniesJson), CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal(1, await db.Companies.CountAsync(c => c.ProviderName == ProviderName));
        Assert.Equal(1, await db.Symbols.CountAsync(s => s.ProviderName == ProviderName));
        Assert.Equal(1, await db.Industries.CountAsync(i => i.ProviderName == ProviderName));
        Assert.Equal(1, await db.IndustryGroups.CountAsync(g => g.ProviderName == ProviderName));
        Assert.Equal(1, await db.Markets.CountAsync(m => m.ProviderName == ProviderName));
    }

    [Fact]
    public async Task Normalize_PopulatesSupportedCompanyMetadata()
    {
        await using var db = CreateIngestionDbContext();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(CompaniesJson), CancellationToken.None);

        var company = await db.Companies.SingleAsync(c => c.ExternalCompanyId == "13226");
        Assert.Equal("کشت و صنعت آبشیرین", company.Name);
        Assert.Equal("Ab Shirin Agroindustrial Co.", company.NameEnglish);
        Assert.Equal("0113601", company.CompanyCode);
        Assert.Equal("ABYP", company.CompanySymbolEnglish);
        Assert.Equal("ABIN", company.CompanySymbolPinglish);
        Assert.Equal("آبین", company.CompanySymbol);
        Assert.Equal("ABYP", company.TseSymbol);
        Assert.Equal("9987529074833218", company.InstrumentCode);
        Assert.Equal("IRO7ABYP0004", company.CompanyIsin);
        Assert.Equal("IRO7ABYP0001", company.SymbolIsin);
        Assert.Equal(0, company.PrecedencyRight);
        Assert.Equal("1395/04/19", company.AcceptionDateJalali);
        Assert.Equal("2016-07-09T00:00:00", company.AcceptionDateGregorian);
        Assert.Equal("1395/04/19", company.EnlistedDateJalali);
        Assert.Equal("2016-07-09T00:00:00", company.EnlistedDateGregorian);
        Assert.Equal("1395/11/26", company.IpoDateJalali);
        Assert.Equal("2017-02-14T00:00:00", company.IpoDateGregorian);
        Assert.Null(company.FundTypeId);
        Assert.Null(company.FundTypeTitle);
        Assert.Equal("10260200698", company.NationalId);
        Assert.Equal(1, company.InExchange);
        Assert.Equal("1373/09/20", company.EstablishmentDateJalali);
        Assert.Equal("1994-12-11T00:00:00", company.EstablishmentDateGregorian);
        Assert.Null(company.BusinessStartDateJalali);
        Assert.Null(company.BusinessStartDateGregorian);
        Assert.Equal("1373/09/20", company.RegistrationDateJalali);
        Assert.Equal("1994-12-11T00:00:00", company.RegistrationDateGregorian);
        Assert.Equal("1831", company.RegistrationNumber);
        Assert.Equal("تهران", company.RegistrationProvince);
        Assert.Equal("تهران", company.RegistrationCity);
        Assert.Equal("بازار پایه زرد", company.MarketBoard);
        Assert.NotNull(company.IndustryId);
        Assert.NotNull(company.GroupId);
        Assert.NotNull(company.MarketId);
        Assert.Null(company.SourceModifiedAt);
    }

    [Fact]
    public async Task Normalize_UsesInstrumentCodeBeforeIsinForCanonicalSymbol()
    {
        await using var db = CreateIngestionDbContext();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(CompaniesJson), CancellationToken.None);

        var symbol = await db.Symbols.SingleAsync(s => s.ExternalSymbolId == "13226");
        Assert.Equal("9987529074833218", symbol.SymbolCode);
        Assert.Equal("InstrumentCode", symbol.LinkageBasis);
    }

    [Fact]
    public async Task Normalize_WhenTseCodeMissing_FallsBackToSymbolIsinAndLogsWarning()
    {
        const string json = """
            [
              {
                "coID": 2,
                "coTitle": "Fallback",
                "coSymbol": "فال",
                "tseCode": null,
                "tseSIsinCode": "IRO1FALL0001"
              }
            ]
            """;
        await using var db = CreateIngestionDbContext();
        var logger = new CapturingLogger<NadpcoApiCompanyNormalizer>();

        await CreateNormalizer(db, logger).NormalizeAsync(MakePayload(json), CancellationToken.None);

        var symbol = await db.Symbols.SingleAsync();
        Assert.Equal("IRO1FALL0001", symbol.SymbolCode);
        Assert.Equal("SymbolIsin", symbol.LinkageBasis);
        Assert.Contains(logger.Messages, message => message.Contains("TseCode was missing"));
    }

    [Fact]
    public async Task Normalize_IsIdempotent()
    {
        await using var db = CreateIngestionDbContext();
        var normalizer = CreateNormalizer(db);
        var payload = MakePayload(CompaniesJson);

        await normalizer.NormalizeAsync(payload, CancellationToken.None);
        await normalizer.NormalizeAsync(payload, CancellationToken.None);

        Assert.Equal(1, await db.Companies.CountAsync(c => c.ProviderName == ProviderName));
        Assert.Equal(1, await db.Symbols.CountAsync(s => s.ProviderName == ProviderName));
        Assert.Equal(1, await db.Industries.CountAsync(i => i.ProviderName == ProviderName));
    }

    [Fact]
    public async Task Normalize_LaterCatalogRefreshWithNewCompany_InsertsNewCompanyWithoutDeletingExisting()
    {
        const string secondCompanyJson = """
            [
              {
                "coID": 13226,
                "coTitle": "کشت و صنعت آبشیرین",
                "coSymbol": "آبین",
                "coSymbolEnglish": "ABYP",
                "tseCode": "9987529074833218"
              },
              {
                "coID": 99999,
                "coTitle": "New Listed Company",
                "coSymbol": "نیو",
                "coSymbolEnglish": "NEWC",
                "tseCode": "123456789"
              }
            ]
            """;
        await using var db = CreateIngestionDbContext();
        var normalizer = CreateNormalizer(db);

        await normalizer.NormalizeAsync(MakePayload(CompaniesJson), CancellationToken.None);
        await normalizer.NormalizeAsync(MakePayload(secondCompanyJson), CancellationToken.None);

        Assert.Equal(2, await db.Companies.CountAsync(c => c.ProviderName == ProviderName));
        Assert.Equal(2, await db.Symbols.CountAsync(s => s.ProviderName == ProviderName));
        Assert.NotNull(await db.Companies.SingleOrDefaultAsync(c => c.ExternalCompanyId == "13226"));
        Assert.NotNull(await db.Companies.SingleOrDefaultAsync(c => c.ExternalCompanyId == "99999"));
    }

    [Fact]
    public async Task Normalize_DuplicateCompanyWithConflictingIdentifiers_LastRowWinsAndLogsWarning()
    {
        const string json = """
            [
              { "coID": 10, "coTitle": "First", "coSymbol": "الف", "tseCode": "111" },
              { "coID": 10, "coTitle": "Second", "coSymbol": "ب", "tseCode": "222" }
            ]
            """;
        await using var db = CreateIngestionDbContext();
        var logger = new CapturingLogger<NadpcoApiCompanyNormalizer>();

        await CreateNormalizer(db, logger).NormalizeAsync(MakePayload(json), CancellationToken.None);

        var company = await db.Companies.SingleAsync();
        var symbol = await db.Symbols.SingleAsync();
        Assert.Equal("Second", company.Name);
        Assert.Equal("222", symbol.SymbolCode);
        Assert.Contains(logger.Messages, message => message.Contains("conflicting identifiers"));
    }

    [Fact]
    public async Task Normalize_CompanyWithoutIdentifiers_CreatesNoSymbolRow()
    {
        const string json = """[{ "coID": 11, "coTitle": "No identifiers" }]""";
        await using var db = CreateIngestionDbContext();

        var count = await CreateNormalizer(db).NormalizeAsync(MakePayload(json), CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal(1, await db.Companies.CountAsync());
        Assert.Empty(await db.Symbols.ToListAsync());
    }

    [Fact]
    public async Task Processor_RoutesNadpcoApiSymbolsAndStoresRawPayloadBeforeNormalization()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateIngestionDbContext();
        var payload = MakePayload(CompaniesJson);
        var provider = new StubSymbolProvider(payload);
        var router = new FinancialDataProviderRouter(
            new Dictionary<string, ISymbolDataProvider> { [ProviderName] = provider },
            new Dictionary<string, IFinancialStatementProvider>(),
            new Dictionary<string, IMonthlyProductionSalesProvider>());
        var processor = new FinancialDataSyncProcessor(
            ingestionDb,
            new ProviderRawPayloadStore(providerDb),
            new ThrowingSymbolProvider(),
            new ThrowingSymbolProvider(),
            new ThrowingSymbolProvider(),
            [CreateNormalizer(ingestionDb)],
            new StoredDerivedMetricRecalculationPublisher(ingestionDb),
            new FixedTimeProvider(Now),
            NullLogger<FinancialDataSyncProcessor>.Instance,
            providerRouter: router);

        var result = await processor.ProcessAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.Symbols,
                ExternalReference: null,
                Now,
                "nadpco-symbols-v1",
                ProviderName: ProviderName),
            CancellationToken.None);

        Assert.Equal(DataSyncRunStatus.Completed, result.Run.Status);
        Assert.Equal(ProviderName, result.Run.ProviderName);
        Assert.Single(await providerDb.ProviderRawPayloads.ToListAsync());
        Assert.Equal(1, await ingestionDb.Companies.CountAsync(c => c.ProviderName == ProviderName));
        Assert.Equal(1, await ingestionDb.Symbols.CountAsync(s => s.ProviderName == ProviderName));
    }

    [Fact]
    public async Task CleanSlate_RemovesCompaniesAndDependentStaleRows()
    {
        await using var db = CreateIngestionDbContext();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(CompaniesJson), CancellationToken.None);
        var company = await db.Companies.SingleAsync();
        var symbol = await db.Symbols.SingleAsync();
        var periodStart = new DateOnly(2026, 1, 1);
        var periodEnd = new DateOnly(2026, 3, 31);

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            SymbolId = symbol.Id,
            MetricCode = "PE_TTM",
            MetricVersion = "v1",
            CalculationPolicyVersion = "v1",
            PeriodType = "TTM",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Unit = "ratio",
            ObservedAt = Now,
            LastSynchronizedAt = Now
        });
        db.FeatureSnapshots.Add(new FeatureSnapshotRow
        {
            Id = Guid.NewGuid(),
            SymbolId = symbol.Id,
            FeatureCode = "value",
            FeatureVersion = "v1",
            PolicyVersion = "v1",
            PeriodType = "TTM",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Unit = "ratio",
            ObservedAt = Now,
            LastSynchronizedAt = Now,
            InputFingerprint = "fingerprint"
        });
        db.FeatureComputationJobs.Add(new FeatureComputationJobRow
        {
            Id = Guid.NewGuid(),
            SymbolId = symbol.Id,
            FeatureCode = "value",
            FeatureVersion = "v1",
            PeriodType = "TTM",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            IdempotencyKey = "feature-job",
            Status = "Pending",
            RequestedAt = Now
        });
        db.MetricRecalculationRequests.Add(new MetricRecalculationRequestRow
        {
            Id = Guid.NewGuid(),
            SourceDataset = "Symbols",
            ExternalReference = "13226",
            SourcePayloadChecksum = "checksum",
            RequestedAt = Now
        });
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "CyclicalWaves",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 9987529074833218,
            InstrumentIsin = "IRO7ABYP0001",
            Symbol = "ABYP",
            Name = "Ab Shirin",
            MarketCode = "TSE",
            InstrumentKind = "Equity",
            NormalizedCompanyId = company.Id,
            IsActive = true,
            SourceChangedAt = Now,
            LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();

        var result = await new NadpcoCompanyCatalogCleanSlateService(db).ClearAsync(CancellationToken.None);

        Assert.Equal(1, result.MetricRecalculationRequestsDeleted);
        Assert.Equal(1, result.FeatureComputationJobsDeleted);
        Assert.Equal(1, result.FeatureSnapshotsDeleted);
        Assert.Equal(1, result.DerivedMetricsDeleted);
        Assert.Equal(1, result.SymbolsDeleted);
        Assert.Equal(1, result.TradingInstrumentLinksCleared);
        Assert.Equal(1, result.CompaniesDeleted);
        Assert.Empty(await db.Companies.ToListAsync());
        Assert.Empty(await db.Symbols.ToListAsync());
        Assert.Empty(await db.DerivedMetrics.ToListAsync());
        Assert.Empty(await db.FeatureSnapshots.ToListAsync());
        Assert.Empty(await db.FeatureComputationJobs.ToListAsync());
        Assert.Empty(await db.MetricRecalculationRequests.ToListAsync());
        Assert.Null((await db.TradingInstruments.SingleAsync()).NormalizedCompanyId);
    }

    private static FinancialIngestionDbContext CreateIngestionDbContext() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FinancialProviderDbContext CreateProviderDbContext() =>
        new(new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static NadpcoApiCompanyNormalizer CreateNormalizer(
        FinancialIngestionDbContext db,
        ILogger<NadpcoApiCompanyNormalizer>? logger = null) =>
        new(db, new CanonicalSymbolLinkageResolver(), logger ?? NullLogger<NadpcoApiCompanyNormalizer>.Instance);

    private static ProviderRawPayload MakePayload(string json) =>
        new(
            Guid.NewGuid(),
            ProviderName,
            ProviderDataset.Symbols,
            "api/v3/BaseInfo/Companies",
            "all",
            json,
            "checksum-" + Guid.NewGuid(),
            Now);

    private sealed class StubSymbolProvider(ProviderRawPayload payload) :
        ISymbolDataProvider,
        IFinancialStatementProvider,
        IMonthlyProductionSalesProvider
    {
        public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(payload);

        public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingSymbolProvider :
        ISymbolDataProvider,
        IFinancialStatementProvider,
        IMonthlyProductionSalesProvider
    {
        public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Default provider should not be used.");

        public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
