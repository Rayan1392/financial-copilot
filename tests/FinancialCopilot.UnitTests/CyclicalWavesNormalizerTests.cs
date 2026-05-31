using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class CyclicalWavesNormalizerTests
{
    private const string ProviderName = "CyclicalWaves";

    private const string TickerListJson = """["شلرد","تاپیکو","فولاد"]""";

    private const string TickerDetailJson = """
        {
          "success": true,
          "data": {
            "_id": "6a144b2e5fad5d3fae081f92",
            "ticker": "شلرد",
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
            "شلرد",
            json,
            "checksum-" + Guid.NewGuid(),
            DateTimeOffset.UtcNow);

    // ── Symbol normalizer ──────────────────────────────────────────────────────

    [Fact]
    public async Task SymbolNormalizer_ParsesTickerArrayAndCreatesCompanyAndSymbolRows()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesSymbolNormalizer(db);
        var payload = MakePayload(ProviderDataset.Symbols, TickerListJson);

        var count = await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, count);
        Assert.Equal(3, await db.Companies.CountAsync());
        Assert.Equal(3, await db.Symbols.CountAsync());
    }

    [Fact]
    public async Task SymbolNormalizer_SetsCompanyNameToPersisnTicker()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesSymbolNormalizer(db);
        var payload = MakePayload(ProviderDataset.Symbols, TickerListJson);

        await normalizer.NormalizeAsync(payload, default);

        var company = await db.Companies.FirstAsync(c => c.ExternalCompanyId == "شلرد");
        Assert.Equal("شلرد", company.Name);
    }

    [Fact]
    public async Task SymbolNormalizer_IsIdempotent_NoDuplicateRowsOnSecondCall()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesSymbolNormalizer(db);
        var payload = MakePayload(ProviderDataset.Symbols, TickerListJson);

        await normalizer.NormalizeAsync(payload, default);
        await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, await db.Companies.CountAsync());
        Assert.Equal(3, await db.Symbols.CountAsync());
    }

    // ── Financial statement normalizer ────────────────────────────────────────

    [Fact]
    public async Task FinancialStatementNormalizer_ProducesThreeStatementRows()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        var count = await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, count);
        Assert.Equal(3, await db.FinancialStatements.CountAsync());
    }

    [Fact]
    public async Task FinancialStatementNormalizer_WritesIncomeStatementTypeAndThreeMonthsPeriodType()
    {
        // Spec 029 regression guard: CyclicalWaves writes only quarterly income data, so every row
        // must have StatementType=IncomeStatement and PeriodType=ThreeMonths.
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db);
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
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);

        var q0 = await db.FinancialStatements
            .FirstAsync(s => s.ExternalStatementId.EndsWith(":Q0"));
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
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db);
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
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);

        var q1 = await db.FinancialStatements.FirstAsync(s => s.ExternalStatementId.EndsWith(":Q1"));
        var items = await db.FinancialStatementLineItems
            .Where(i => i.FinancialStatementId == q1.Id)
            .Select(i => i.MetricCode)
            .ToListAsync();

        foreach (var code in new[] { "REVENUE", "NET_PROFIT", "GROSS_PROFIT", "OPERATING_PROFIT",
            "NET_PROFIT_MARGIN", "GROSS_PROFIT_MARGIN", "OPERATING_PROFIT_MARGIN" })
        {
            Assert.Contains(code, items);
        }
    }

    [Fact]
    public async Task FinancialStatementNormalizer_IsIdempotent_NoDuplicatesOnSecondCall()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db);
        var payload = MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);
        await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, await db.FinancialStatements.CountAsync());
    }

    [Fact]
    public async Task FinancialStatementNormalizer_EntickerOverwritesProvisionalSymbolCode()
    {
        await using var db = CreateDbContext();
        var symbolNormalizer = new CyclicalWavesSymbolNormalizer(db);
        await symbolNormalizer.NormalizeAsync(
            MakePayload(ProviderDataset.Symbols, """["شلرد"]"""),
            default);

        var statementNormalizer = new CyclicalWavesFinancialStatementNormalizer(db);
        await statementNormalizer.NormalizeAsync(
            MakePayload(ProviderDataset.FinancialStatements, TickerDetailJson),
            default);

        var symbol = await db.Symbols.FirstAsync(s => s.ProviderName == ProviderName);
        Assert.Equal("IRO7SHLP0001", symbol.SymbolCode);
    }

    // ── Monthly report normalizer ─────────────────────────────────────────────

    [Fact]
    public async Task MonthlyReportNormalizer_ProducesThreeReportRowsWithRevenueLineItem()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesMonthlyReportNormalizer(db);
        var payload = MakePayload(ProviderDataset.MonthlyProductionSales, TickerDetailJson);

        var count = await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, count);
        Assert.Equal(3, await db.MonthlyReports.CountAsync());

        var allItems = await db.MonthlyReportLineItems.ToListAsync();
        Assert.Equal(3, allItems.Count);
        Assert.All(allItems, item => Assert.Equal("REVENUE", item.ProductCode));
    }

    [Fact]
    public async Task MonthlyReportNormalizer_IsIdempotent_NoDuplicatesOnSecondCall()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesMonthlyReportNormalizer(db);
        var payload = MakePayload(ProviderDataset.MonthlyProductionSales, TickerDetailJson);

        await normalizer.NormalizeAsync(payload, default);
        await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(3, await db.MonthlyReports.CountAsync());
    }

    [Fact]
    public async Task StatementNormalizer_AttachesStaleDataWarning()
    {
        await using var db = CreateDbContext();
        var normalizer = new CyclicalWavesFinancialStatementNormalizer(db);
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
