using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class CyclicalWavesNormalizerTests
{
    private const string ProviderName = "CyclicalWaves";
    private const string NadpcoProviderName = ProviderSources.NoavaranCurrentApiName;
    private const string NadpcoCompanyId = "13226";
    private const string MainTicker = "\u0634\u0644\u0631\u062f";
    private const string SecondTicker = "\u062a\u0627\u067e\u06cc\u06a9\u0648";
    private const string ThirdTicker = "\u0641\u0648\u0644\u0627\u062f";

    private static string TickerListJson =>
        $$"""["{{MainTicker}}","{{SecondTicker}}","{{ThirdTicker}}"]""";

    private static string TickerDetailJson =>
        $$"""
        {
          "success": true,
          "data": {
            "_id": "6a144b2e5fad5d3fae081f92",
            "ticker": "{{MainTicker}}",
            "enticker": "IRO7SHLP0001",
            "last_quarter_sale": 53244165000000,
            "penultimate_quarter_sale": 48760460000000,
            "last_year_same_quarter_sale": 22690236000000,
            "average_4_quarter_sale": 57549286500000,
            "last_quarter_net_profit": -222559000000,
            "penultimate_quarter_net_profit": 8401790000000,
            "last_year_same_quarter_net_profit": -3957691000000,
            "last_quarter_gross_profit": 23160189000000,
            "penultimate_quarter_gross_profit": 14785508000000,
            "last_year_same_quarter_gross_profit": 6713062000000,
            "last_quarter_operating_profit": 9632455000000,
            "penultimate_quarter_operating_profit": 10980303000000,
            "last_year_same_quarter_operating_profit": 904067000000,
            "last_quarter_net_profit_margin": -0.42,
            "penultimate_quarter_net_profit_margin": 17.23,
            "last_year_same_quarter_net_profit_margin": -17.44,
            "last_quarter_gross_profit_margin": 43.5,
            "penultimate_quarter_gross_profit_margin": 30.32,
            "last_year_same_quarter_gross_profit_margin": 29.59,
            "last_quarter_operating_profit_margin": 18.09,
            "penultimate_quarter_operating_profit_margin": 22.52,
            "last_year_same_quarter_operating_profit_margin": 3.98,
            "last_month_sale": 2297714000000,
            "penultimate_month_sale": 23119257000000,
            "last_year_same_month_sale": 1221867000000,
            "average_12_month_sale": 57549286500000,
            "pe": 20.66,
            "ps": 0.42
          }
        }
        """;

    private static string KchadTickerDetailJson =>
        """
        {
          "success": true,
          "data": {
            "_id": "cw-kchad-doc",
            "ticker": "\u06a9\u0686\u0627\u062f",
            "enticker": "IRO1CHML0001",
            "last_quarter_sale": 249211279000000,
            "penultimate_quarter_sale": 210000000000000,
            "last_year_same_quarter_sale": 190000000000000,
            "average_4_quarter_sale": 265915619500000,
            "last_quarter_net_profit": 75257854000000,
            "penultimate_quarter_net_profit": 61234567000000,
            "last_year_same_quarter_net_profit": 55234567000000,
            "last_quarter_gross_profit": 62289927000000,
            "penultimate_quarter_gross_profit": 51234567000000,
            "last_year_same_quarter_gross_profit": 41234567000000,
            "last_quarter_operating_profit": 54150691000000,
            "penultimate_quarter_operating_profit": 43234567000000,
            "last_year_same_quarter_operating_profit": 33234567000000,
            "last_quarter_net_profit_margin": 30.2,
            "penultimate_quarter_net_profit_margin": 29.15,
            "last_year_same_quarter_net_profit_margin": 28.11,
            "last_quarter_gross_profit_margin": 24.99,
            "penultimate_quarter_gross_profit_margin": 24.1,
            "last_year_same_quarter_gross_profit_margin": 23.2,
            "last_quarter_operating_profit_margin": 21.73,
            "penultimate_quarter_operating_profit_margin": 20.5,
            "last_year_same_quarter_operating_profit_margin": 19.4,
            "last_month_sale": 90879722000000,
            "penultimate_month_sale": 78000000000000,
            "last_year_same_month_sale": 69220219000000,
            "average_12_month_sale": 57549286500000,
            "last_year_average_12_month_sale": 50000000000000,
            "pe": 9.73,
            "ps": 2.14,
            "last_quarter_date": "2026-03-20",
            "last_month_sale_date": "2026-05-31"
          }
        }
        """;

    private static FinancialIngestionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FinancialIngestionDbContext(options);
    }

    private static FinancialProviderDbContext CreateProviderDbContext()
    {
        var options = new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FinancialProviderDbContext(options);
    }

    private static ProviderRawPayload MakePayload(ProviderDataset dataset, string json) =>
        new(
            Guid.NewGuid(),
            ProviderName,
            dataset,
            "test-endpoint",
            MainTicker,
            json,
            "checksum-" + Guid.NewGuid(),
            DateTimeOffset.UtcNow);

    private static void SeedKchadCompany(FinancialIngestionDbContext db)
    {
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = NadpcoProviderName,
            ExternalCompanyId = "3",
            CompanySymbol = "\u06a9\u0686\u0627\u062f",
            SymbolIsin = "IRO1CHML0001",
            Name = "KCHAD",
            LastSynchronizedAt = DateTimeOffset.Parse("2026-06-17T00:00:00Z")
        });
    }

    private static void AddPendingRequest(
        FinancialIngestionDbContext db,
        ProviderDataset dataset,
        string externalRef,
        string checksumSuffix)
    {
        db.MetricRecalculationRequests.Add(new MetricRecalculationRequestRow
        {
            Id = Guid.NewGuid(),
            SourceDataset = dataset.ToString(),
            ExternalReference = externalRef,
            SourcePayloadChecksum = $"cw-{dataset}-{externalRef}-{checksumSuffix}",
            RequestedAt = DateTimeOffset.Parse("2026-06-17T00:00:00Z")
        });
    }

    private static MetricRecalculationProcessor NewCyclicalWavesProcessor(FinancialIngestionDbContext db)
    {
        IFinancialMetricCalculator[] calculators =
        [
            new SourceLineItemPassthroughMetricCalculator(new MetricCode("REVENUE"), new MetricCode("REVENUE")),
            new SourceLineItemPassthroughMetricCalculator(new MetricCode("NET_PROFIT"), new MetricCode("NET_PROFIT")),
            new SourceLineItemPassthroughMetricCalculator(new MetricCode("GROSS_PROFIT"), new MetricCode("GROSS_PROFIT")),
            new SourceLineItemPassthroughMetricCalculator(new MetricCode("OPERATING_PROFIT"), new MetricCode("OPERATING_PROFIT")),
            new SourceLineItemPassthroughMetricCalculator(new MetricCode("AVG_4Q_REVENUE"), new MetricCode("AVG_4Q_REVENUE")),
            new SourceLineItemPassthroughMetricCalculator(new MetricCode("NET_PROFIT_MARGIN"), new MetricCode("NET_PROFIT_MARGIN")),
            new SourceLineItemPassthroughMetricCalculator(new MetricCode("GROSS_PROFIT_MARGIN"), new MetricCode("GROSS_PROFIT_MARGIN")),
            new SourceLineItemPassthroughMetricCalculator(new MetricCode("OPERATING_PROFIT_MARGIN"), new MetricCode("OPERATING_PROFIT_MARGIN")),
            new SourceLineItemPassthroughMetricCalculator(new MetricCode("PE_TTM"), new MetricCode("PE_RATIO")),
            new SourceLineItemPassthroughMetricCalculator(new MetricCode("PS_TTM"), new MetricCode("PS_RATIO")),
            new AdditiveCompositeMetricCalculator(new MetricCode("MONTHLY_SALES"), [new MetricCode("MONTHLY_SALES")]),
            new SourceLineItemPassthroughMetricCalculator(new MetricCode("AVG_12M_MONTHLY_SALES"), new MetricCode("AVG_12M_MONTHLY_SALES"))
        ];

        var registry = new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, calculators);
        var policyProvider = new MetricCalculationPolicyProvider(PhaseOneFinancialSemanticCatalog.Policies);
        INormalizedMetricInputSource[] sources =
        [
            new LineItemMetricInputSource(db, new MetricCode("REVENUE")),
            new LineItemMetricInputSource(db, new MetricCode("NET_PROFIT")),
            new LineItemMetricInputSource(db, new MetricCode("GROSS_PROFIT")),
            new LineItemMetricInputSource(db, new MetricCode("OPERATING_PROFIT")),
            new LineItemMetricInputSource(db, new MetricCode("AVG_4Q_REVENUE")),
            new LineItemMetricInputSource(db, new MetricCode("NET_PROFIT_MARGIN")),
            new LineItemMetricInputSource(db, new MetricCode("GROSS_PROFIT_MARGIN")),
            new LineItemMetricInputSource(db, new MetricCode("OPERATING_PROFIT_MARGIN")),
            new LineItemMetricInputSource(db, new MetricCode("PE_RATIO")),
            new LineItemMetricInputSource(db, new MetricCode("PS_RATIO")),
            new MonthlySalesMetricInputSource(db),
            new MonthlyAvgSaleMetricInputSource(db)
        ];
        var inputReader = new NormalizedMetricInputReader(sources);
        var resultStore = new PersistedDerivedMetricResultStore(db);
        var calcService = new DerivedMetricCalculationService(registry, policyProvider, resultStore);
        var recalcCommand = new DerivedMetricRecalculationCommand(calcService);
        return new MetricRecalculationProcessor(
            db,
            registry,
            policyProvider,
            inputReader,
            recalcCommand,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-06-17T00:00:00Z")),
            NullLogger<MetricRecalculationProcessor>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ThrowingFinancialProvider :
        ISymbolDataProvider,
        IFinancialStatementProvider,
        IMonthlyProductionSalesProvider
    {
        public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider fetch should not be used.");

        public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider fetch should not be used.");

        public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider fetch should not be used.");
    }

    private sealed class CountingStatementProvider(ProviderRawPayload payload) : IFinancialStatementProvider
    {
        public int FetchCount { get; private set; }

        public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken)
        {
            FetchCount++;
            return Task.FromResult(payload);
        }
    }

    private sealed class RecordingSyncProcessor : IFinancialDataSyncProcessor
    {
        public List<(DataSyncRequest Request, ProviderRawPayload Payload)> Processed { get; } = [];

        public Task<DataSyncProcessingResult> ProcessAsync(
            DataSyncRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Full sync should process a fetched shared payload.");

        public Task<DataSyncProcessingResult> ProcessPayloadAsync(
            DataSyncRequest request,
            ProviderRawPayload payload,
            CancellationToken cancellationToken)
        {
            Processed.Add((request, payload));
            return Task.FromResult(new DataSyncProcessingResult(
                new DataSyncRun(
                    request.RequestId,
                    request.IdempotencyKey,
                    request.Dataset,
                    request.ExternalReference,
                    DataSyncRunStatus.Completed,
                    request.RequestedAt,
                    request.RequestedAt,
                    request.RequestedAt,
                    ProcessedRecords: 1,
                    ErrorCount: 0,
                    ErrorMessage: null,
                    SourcePayloadChecksum: payload.Checksum,
                    request.ProviderName),
                AlreadyProcessed: false));
        }
    }

    private static NormalizedCompanyRow SeedNadpcoCompany(
        FinancialIngestionDbContext db,
        string externalCompanyId = NadpcoCompanyId,
        string ticker = MainTicker,
        string? symbolIsin = "IRO7SHLP0001")
    {
        var company = new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = NadpcoProviderName,
            ExternalCompanyId = externalCompanyId,
            Name = "NADPCO authoritative name",
            NameEnglish = "NADPCO English",
            CompanySymbol = ticker,
            CompanySymbolEnglish = "NADP",
            SymbolIsin = symbolIsin,
            MarketBoard = "NADPCO board",
            RegistrationNumber = "NADPCO registration",
            LastSynchronizedAt = DateTimeOffset.UtcNow
        };
        db.Companies.Add(company);
        db.SaveChanges();
        return company;
    }

    private static void SeedTickerListCompanies(FinancialIngestionDbContext db)
    {
        SeedNadpcoCompany(db, "13226", MainTicker, "IRO7SHLP0001");
        SeedNadpcoCompany(db, "13227", SecondTicker, "IRO7TAPC0001");
        SeedNadpcoCompany(db, "13228", ThirdTicker, "IRO7FOOL0001");
    }

    [Fact]
    public async Task FinancialStatementNormalizer_ProducesThreeStatementRows()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        var outcome = await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, outcome.ProcessedRecords);
        Assert.Equal(3, await db.FinancialStatements.CountAsync());
    }

    [Fact]
    public async Task FinancialStatementNormalizer_WritesIncomeStatementTypeAndThreeMonthsPeriodType()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);

        var rows = await db.FinancialStatements.ToListAsync();
        Assert.All(rows, row => Assert.Equal("IncomeStatement", row.StatementType));
        Assert.All(rows, row => Assert.Equal("ThreeMonths", row.PeriodType));
    }

    [Fact]
    public async Task FinancialStatementNormalizer_Q0RowHasPeAndPsLineItems()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);

        var q0 = await db.FinancialStatements.FirstAsync(s => s.ExternalStatementId.EndsWith(":Q0"));
        var items = await db.FinancialStatementLineItems
            .Where(i => i.FinancialStatementId == q0.Id)
            .ToListAsync();

        Assert.Contains(items, i => i.MetricCode == "PE_RATIO");
        Assert.Contains(items, i => i.MetricCode == "PS_RATIO");
    }

    [Fact]
    public async Task FinancialStatementNormalizer_CyclicalWavesPrecomputedValues_AreStoredAsIs()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);

        var q0 = await db.FinancialStatements.FirstAsync(s => s.ExternalStatementId.EndsWith(":Q0"));
        var items = await db.FinancialStatementLineItems
            .Where(i => i.FinancialStatementId == q0.Id)
            .ToDictionaryAsync(i => i.MetricCode);

        Assert.Equal(53244165000000m, items["REVENUE"].Value);
        Assert.Equal(57549286500000m, items["AVG_4Q_REVENUE"].Value);
        Assert.Equal(20.66m, items["PE_RATIO"].Value);
        Assert.Equal(0.42m, items["PS_RATIO"].Value);
    }

    [Fact]
    public async Task FinancialStatementNormalizer_Q1AndQ4RowsHaveNoPeOrPs()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);

        var nonQ0 = await db.FinancialStatements
            .Where(s => !s.ExternalStatementId.EndsWith(":Q0"))
            .Select(s => s.Id)
            .ToListAsync();

        foreach (var id in nonQ0)
        {
            var hasPe = await db.FinancialStatementLineItems.AnyAsync(
                i => i.FinancialStatementId == id && (i.MetricCode == "PE_RATIO" || i.MetricCode == "PS_RATIO"));
            Assert.False(hasPe);
        }
    }

    [Fact]
    public async Task FinancialStatementNormalizer_AllSevenLineItemsPerQuarterRow()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);

        var q1 = await db.FinancialStatements.FirstAsync(s => s.ExternalStatementId.EndsWith(":Q1"));
        var items = await db.FinancialStatementLineItems
            .Where(i => i.FinancialStatementId == q1.Id)
            .Select(i => i.MetricCode)
            .ToListAsync();

        foreach (var code in new[]
        {
            "REVENUE",
            "NET_PROFIT",
            "GROSS_PROFIT",
            "OPERATING_PROFIT",
            "NET_PROFIT_MARGIN",
            "GROSS_PROFIT_MARGIN",
            "OPERATING_PROFIT_MARGIN"
        })
        {
            Assert.Contains(code, items);
        }
    }

    [Fact]
    public async Task FinancialStatementNormalizer_IsIdempotent_NoDuplicatesOnSecondCall()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);
        await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, await db.FinancialStatements.CountAsync());
    }

    [Fact]
    public async Task FinancialStatementNormalizer_WhenNadpcoLinkExists_UsesNadpcoCompanyAndDoesNotOverwriteMetadata()
    {
        await using var db = CreateDbContext();
        var seeded = SeedNadpcoCompany(db);
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance);

        await normalizer.NormalizeAsync(
            MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson),
            default);

        var statements = await db.FinancialStatements.ToListAsync();
        Assert.All(statements, statement =>
        {
            Assert.Equal(NadpcoCompanyId, statement.ExternalCompanyId);
            Assert.DoesNotContain("MissingData", statement.WarningsJson);
        });

        var company = await db.Companies.SingleAsync(c => c.Id == seeded.Id);
        Assert.Equal("NADPCO authoritative name", company.Name);
        Assert.Equal("NADPCO English", company.NameEnglish);
        Assert.Equal("NADPCO board", company.MarketBoard);
        Assert.Equal(0, await db.Companies.CountAsync(c => c.ProviderName == ProviderName));
    }

    [Fact]
    public async Task FinancialStatementNormalizer_WhenNadpcoLinkMissing_AttachesMissingDataWarning()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance);

        await normalizer.NormalizeAsync(
            MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson),
            default);

        var statements = await db.FinancialStatements.ToListAsync();
        Assert.All(statements, statement =>
        {
            Assert.Contains("MissingData", statement.WarningsJson);
            Assert.Equal("6a144b2e5fad5d3fae081f92", statement.ExternalCompanyId);
        });
        Assert.Equal(0, await db.Companies.CountAsync(c => c.ProviderName == ProviderName));
    }

    [Fact]
    public async Task MonthlyReportNormalizer_ProducesThreeReportRowsWithRevenueLineItem()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesMonthlyReportNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesMonthlyReportNormalizer>.Instance);
        var payload = MakePayload(ProviderDataset.MonthlyProductionSales, TickerDetailJson);

        var outcome = await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, outcome.ProcessedRecords);
        Assert.Equal(3, await db.MonthlyReports.CountAsync());

        var allItems = await db.MonthlyReportLineItems.ToListAsync();
        // 3 REVENUE line items (M0, M1, M12) + 1 AVG_12M line item (M0 only) = 4 total
        Assert.Equal(4, allItems.Count);
        Assert.Equal(3, allItems.Count(item => item.ProductCode == "REVENUE"));
        Assert.Equal(1, allItems.Count(item => item.ProductCode == "AVG_12M"));
    }

    [Fact]
    public async Task MonthlyReportNormalizer_CyclicalWavesPrecomputedMonthlyValues_AreStoredAsIs()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesMonthlyReportNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesMonthlyReportNormalizer>.Instance);

        await normalizer.NormalizeAsync(MakePayload(ProviderDataset.MonthlyProductionSales, TickerDetailJson), default);

        var m0 = await db.MonthlyReports.FirstAsync(report => report.ExternalReportId.EndsWith(":M0"));
        var items = await db.MonthlyReportLineItems
            .Where(item => item.MonthlyReportId == m0.Id)
            .ToDictionaryAsync(item => item.ProductCode);

        Assert.Equal(2297714000000m, items["REVENUE"].SalesAmount);
        Assert.Equal(57549286500000m, items["AVG_12M"].SalesAmount);

        var observations = await new MonthlySalesMetricInputSource(db)
            .LoadAsync("6a144b2e5fad5d3fae081f92", CancellationToken.None);

        Assert.Contains(observations, observation => observation.Value == 2297714000000m);
    }

    [Fact]
    public async Task CyclicalWavesSyncAndRecalculation_PersistsFullDerivedMetricSnapshot()
    {
        await using var db = CreateDbContext();
        SeedKchadCompany(db);
        await db.SaveChangesAsync();
        var statementNormalizer = new CyclicalWavesFinancialStatementNormalizer(
            db,
            NullCompanyResolverService.Instance,
            NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance);
        var monthlyNormalizer = new CyclicalWavesMonthlyReportNormalizer(
            db,
            NullCompanyResolverService.Instance,
            NullLogger<CyclicalWavesMonthlyReportNormalizer>.Instance);

        await statementNormalizer.NormalizeAsync(
            MakePayload(ProviderDataset.FinancialStatements, KchadTickerDetailJson),
            default);
        await monthlyNormalizer.NormalizeAsync(
            MakePayload(ProviderDataset.MonthlyProductionSales, KchadTickerDetailJson),
            default);
        var normalizedMonthlyReports = await db.MonthlyReports
            .Where(row => row.ExternalCompanyId == "3")
            .OrderBy(row => row.PeriodEnd)
            .Select(row => $"{row.ExternalReportId}|{row.PeriodEnd:yyyy-MM-dd}")
            .ToListAsync();
        Assert.Contains("cw-kchad-doc:M12|2025-05-31", normalizedMonthlyReports);
        var normalizedMonthlySalesInputs = await new MonthlySalesMetricInputSource(db)
            .LoadAsync("3", CancellationToken.None);
        Assert.Contains(normalizedMonthlySalesInputs, input =>
            input.Period.EndDate == new DateOnly(2025, 5, 31) &&
            input.Value == 69220219000000m);
        AddPendingRequest(db, ProviderDataset.FinancialStatements, "3", "fs");
        AddPendingRequest(db, ProviderDataset.MonthlyProductionSales, "3", "monthly");
        await db.SaveChangesAsync();

        var processor = NewCyclicalWavesProcessor(db);
        await processor.ProcessPendingAsync(10, CancellationToken.None);

        var metrics = await db.DerivedMetrics
            .Where(row => row.ExternalCompanyId == "3")
            .ToListAsync();
        var latestByCode = metrics
            .GroupBy(row => row.MetricCode)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(row => row.PeriodEnd).First());

        Assert.Equal(57549286500000m, latestByCode["AVG_12M_MONTHLY_SALES"].Value);
        Assert.Equal(90879722000000m, latestByCode["MONTHLY_SALES"].Value);
        Assert.Contains(metrics, row =>
            row.MetricCode == "MONTHLY_SALES" &&
            row.PeriodEnd == new DateOnly(2025, 5, 31) &&
            row.Value == 69220219000000m);
        Assert.Equal(249211279000000m, latestByCode["REVENUE"].Value);
        Assert.Equal(265915619500000m, latestByCode["AVG_4Q_REVENUE"].Value);
        Assert.Equal(75257854000000m, latestByCode["NET_PROFIT"].Value);
        Assert.Equal(62289927000000m, latestByCode["GROSS_PROFIT"].Value);
        Assert.Equal(54150691000000m, latestByCode["OPERATING_PROFIT"].Value);
        Assert.Equal(30.2m, latestByCode["NET_PROFIT_MARGIN"].Value);
        Assert.Equal(24.99m, latestByCode["GROSS_PROFIT_MARGIN"].Value);
        Assert.Equal(21.73m, latestByCode["OPERATING_PROFIT_MARGIN"].Value);
        Assert.Equal(9.73m, latestByCode["PE_TTM"].Value);
        Assert.Equal(2.14m, latestByCode["PS_TTM"].Value);

        var normalizedAvgMonthlySalesInputs = await new MonthlyAvgSaleMetricInputSource(db)
            .LoadAsync("3", CancellationToken.None);
        Assert.Contains(normalizedAvgMonthlySalesInputs, input =>
            input.Period.EndDate == new DateOnly(2025, 5, 31) &&
            input.Value == 50000000000000m);
        Assert.Contains(metrics, row =>
            row.MetricCode == "AVG_12M_MONTHLY_SALES" &&
            row.PeriodEnd == new DateOnly(2025, 5, 31) &&
            row.Value == 50000000000000m);
        Assert.All(metrics.Where(row => row.Value is not null), row =>
        {
            Assert.Contains("CyclicalWaves", row.SourceEvidenceJson);
            Assert.DoesNotContain("NoavaranCurrentApi", row.SourceEvidenceJson);
        });
        Assert.Contains("sourceUnit\":\"Rials", latestByCode["REVENUE"].SourceEvidenceJson);
        Assert.Contains("canonicalUnit\":\"Rials", latestByCode["REVENUE"].SourceEvidenceJson);
        Assert.Contains("cyclicalwaves-precomputed-rials-passthrough-v1", latestByCode["REVENUE"].SourceEvidenceJson);
        Assert.Contains("sourceUnit\":\"Ratio", latestByCode["PE_TTM"].SourceEvidenceJson);

        var narrowMonthlyOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AVG_12M_MONTHLY_SALES",
            "MONTHLY_SALES",
            "MONTHLY_SALES_GROWTH_MOM",
            "MONTHLY_SALES_GROWTH_YOY",
            "MONTHLY_SALES_QUANTITY",
            "MONTHLY_SALES_RATE",
            "MONTHLY_PRODUCTION_QUANTITY"
        };
        Assert.Contains(metrics, row => !narrowMonthlyOnly.Contains(row.MetricCode));
    }

    [Fact]
    public async Task Processor_SharedCyclicalWavesPayload_NormalizesFinancialAndMonthlyDatasets()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateDbContext();
        SeedKchadCompany(ingestionDb);
        await ingestionDb.SaveChangesAsync();
        var payload = MakePayload(ProviderDataset.FinancialStatements, KchadTickerDetailJson);
        var processor = new FinancialDataSyncProcessor(
            ingestionDb,
            new ProviderRawPayloadStore(providerDb),
            new ThrowingFinancialProvider(),
            new ThrowingFinancialProvider(),
            new ThrowingFinancialProvider(),
            [
                new CyclicalWavesFinancialStatementNormalizer(
                    ingestionDb,
                    NullCompanyResolverService.Instance,
                    NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance),
                new CyclicalWavesMonthlyReportNormalizer(
                    ingestionDb,
                    NullCompanyResolverService.Instance,
                    NullLogger<CyclicalWavesMonthlyReportNormalizer>.Instance)
            ],
            new StoredDerivedMetricRecalculationPublisher(ingestionDb),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-06-17T00:00:00Z")),
            NullLogger<FinancialDataSyncProcessor>.Instance);

        await processor.ProcessPayloadAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.FinancialStatements,
                "\u06a9\u0686\u0627\u062f",
                DateTimeOffset.Parse("2026-06-17T00:00:00Z"),
                "cw-shared-fs",
                ProviderName),
            payload,
            CancellationToken.None);
        await processor.ProcessPayloadAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.MonthlyProductionSales,
                "\u06a9\u0686\u0627\u062f",
                DateTimeOffset.Parse("2026-06-17T00:00:00Z"),
                "cw-shared-monthly",
                ProviderName),
            payload,
            CancellationToken.None);

        Assert.Single(await providerDb.ProviderRawPayloads.ToListAsync());
        Assert.Equal(3, await ingestionDb.FinancialStatements.CountAsync());
        Assert.Equal(3, await ingestionDb.MonthlyReports.CountAsync());
        var recalculationRequests = await ingestionDb.MetricRecalculationRequests.ToListAsync();
        Assert.Equal(2, recalculationRequests.Count);
        Assert.Contains(recalculationRequests, row =>
            row.SourceDataset == ProviderDataset.FinancialStatements.ToString() &&
            row.SourcePayloadChecksum == payload.Checksum);
        Assert.Contains(recalculationRequests, row =>
            row.SourceDataset == ProviderDataset.MonthlyProductionSales.ToString() &&
            row.SourcePayloadChecksum == payload.Checksum);
    }

    [Fact]
    public async Task FullSync_FetchesCyclicalWavesTickerDetailOnceAndProcessesBothDatasets()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = NadpcoProviderName,
            ExternalCompanyId = "3",
            CompanySymbol = "\u06a9\u0686\u0627\u062f",
            Ticker = "\u06a9\u0686\u0627\u062f",
            PrecedencyRight = 0,
            MarketId = NoavaranCompanyScope.BourseMarketId,
            LastSynchronizedAt = DateTimeOffset.Parse("2026-06-17T00:00:00Z")
        });
        await db.SaveChangesAsync();
        var payload = MakePayload(ProviderDataset.FinancialStatements, KchadTickerDetailJson);
        var provider = new CountingStatementProvider(payload);
        var processor = new RecordingSyncProcessor();
        var service = new CyclicalWavesFullSyncService(
            processor,
            provider,
            db,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-06-17T00:00:00Z")),
            NullLogger<CyclicalWavesFullSyncService>.Instance);

        var result = await service.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, provider.FetchCount);
        Assert.Equal(1, result.TickersSynced);
        Assert.Equal(2, processor.Processed.Count);
        Assert.Contains(processor.Processed, item =>
            item.Request.Dataset == ProviderDataset.FinancialStatements &&
            ReferenceEquals(payload, item.Payload));
        Assert.Contains(processor.Processed, item =>
            item.Request.Dataset == ProviderDataset.MonthlyProductionSales &&
            ReferenceEquals(payload, item.Payload));
    }

    [Fact]
    public async Task MonthlyReportNormalizer_IsIdempotent_NoDuplicatesOnSecondCall()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesMonthlyReportNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesMonthlyReportNormalizer>.Instance);
        var payload = MakePayload(ProviderDataset.MonthlyProductionSales, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);
        await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, await db.MonthlyReports.CountAsync());
    }

    [Fact]
    public async Task MonthlyReportNormalizer_WhenNadpcoLinkExists_UsesNadpcoExternalCompanyId()
    {
        await using var db = CreateDbContext();
        SeedNadpcoCompany(db);
        var normalizer = new CyclicalWavesMonthlyReportNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesMonthlyReportNormalizer>.Instance);

        await normalizer.NormalizeAsync(
            MakePayload(ProviderDataset.MonthlyProductionSales, TickerDetailJson),
            default);

        var reports = await db.MonthlyReports.ToListAsync();
        Assert.All(reports, report =>
        {
            Assert.Equal(NadpcoCompanyId, report.ExternalCompanyId);
            Assert.DoesNotContain("MissingData", report.WarningsJson);
        });
        Assert.Equal(0, await db.Companies.CountAsync(c => c.ProviderName == ProviderName));
    }

    [Fact]
    public async Task StatementNormalizer_AttachesStaleDataWarning()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db, NullCompanyResolverService.Instance, NullLogger<CyclicalWavesFinancialStatementNormalizer>.Instance);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);

        var statements = await db.FinancialStatements.ToListAsync();
        Assert.All(statements, s =>
        {
            Assert.NotEqual("[]", s.WarningsJson);
            Assert.Contains("StaleData", s.WarningsJson);
        });
    }
}
