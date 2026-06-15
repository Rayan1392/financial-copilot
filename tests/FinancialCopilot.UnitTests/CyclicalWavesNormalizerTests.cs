using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
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
            "pe": 20.66,
            "ps": 0.42
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
        db.Symbols.Add(new NormalizedSymbolRow
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            ProviderName = NadpcoProviderName,
            ExternalSymbolId = externalCompanyId,
            SymbolCode = symbolIsin ?? ticker,
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });
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
    public async Task SymbolNormalizer_ParsesTickerArrayAndCreatesSymbolsWithoutCreatingCompanies()
    {
        await using var db = CreateDbContext();
        SeedTickerListCompanies(db);
        var normalizer = new CyclicalWavesSymbolNormalizer(db);
        var payload = MakePayload(ProviderDataset.Symbols, TickerListJson);

        var outcome = await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, outcome.ProcessedRecords);
        Assert.Equal(3, await db.Companies.CountAsync(c => c.ProviderName == NadpcoProviderName));
        Assert.Equal(0, await db.Companies.CountAsync(c => c.ProviderName == ProviderName));
        Assert.Equal(3, await db.Symbols.CountAsync(s => s.ProviderName == ProviderName));
    }

    [Fact]
    public async Task SymbolNormalizer_DoesNotOverwriteNadpcoCompanyMetadata()
    {
        await using var db = CreateDbContext();
        var seeded = SeedNadpcoCompany(db);
        var normalizer = new CyclicalWavesSymbolNormalizer(db);
        var payload = MakePayload(ProviderDataset.Symbols, $$"""["{{MainTicker}}"]""");

        await normalizer.NormalizeAsync(payload, default);

        var company = await db.Companies.SingleAsync(c => c.Id == seeded.Id);
        Assert.Equal("NADPCO authoritative name", company.Name);
        Assert.Equal("NADPCO English", company.NameEnglish);
        Assert.Equal("NADPCO board", company.MarketBoard);
        Assert.Equal("NADPCO registration", company.RegistrationNumber);
        Assert.Equal(0, await db.Companies.CountAsync(c => c.ProviderName == ProviderName));
    }

    [Fact]
    public async Task SymbolNormalizer_IsIdempotent_NoDuplicateRowsOnSecondCall()
    {
        await using var db = CreateDbContext();
        SeedTickerListCompanies(db);
        var normalizer = new CyclicalWavesSymbolNormalizer(db);
        var payload = MakePayload(ProviderDataset.Symbols, TickerListJson);

        await normalizer.NormalizeAsync(payload, default);
        await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, await db.Companies.CountAsync(c => c.ProviderName == NadpcoProviderName));
        Assert.Equal(0, await db.Companies.CountAsync(c => c.ProviderName == ProviderName));
        Assert.Equal(3, await db.Symbols.CountAsync(s => s.ProviderName == ProviderName));
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

        var symbol = await db.Symbols.SingleAsync(s => s.ProviderName == ProviderName);
        Assert.Equal(seeded.Id, symbol.CompanyId);
        Assert.Equal("IRO7SHLP0001", symbol.SymbolCode);

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
