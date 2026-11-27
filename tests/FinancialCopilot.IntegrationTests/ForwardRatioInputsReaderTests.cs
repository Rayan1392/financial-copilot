using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.FinancialData.ValuationGauges;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Change «valuation-gauge-forward-ratio» T5: the estimator input reader at the seam. The same seed runs on the
/// in-memory store and, when a local PostgreSQL is configured, on a migrated PostgreSQL database (SQL translation of the
/// grouped monthly query).
/// </summary>
public sealed class ForwardRatioInputsReaderTests
{
    [Fact]
    public async Task ReadsTheMonthItselfFromAcceptedReportsAndTheLatestEightQuarters()
    {
        await using var db = InMemory();
        var seed = await ForwardRatioReaderSeed.SeedAsync(db);
        await ForwardRatioReaderSeed.AssertReadsAsync(new ForwardRatioInputsReader(db), seed);
    }

    [Fact]
    public async Task AnAmbiguousCompanyMatchGivesNoInputs()
    {
        await using var db = InMemory();
        var seed = await ForwardRatioReaderSeed.SeedAsync(db);
        db.Companies.Add(ForwardRatioReaderSeed.Company("noavaran-other", " iro1park0001 ", null));
        await db.SaveChangesAsync();

        Assert.Null(await new ForwardRatioInputsReader(db).ReadAsync(seed.Security, "شاراک", CancellationToken.None));
    }

    [Fact]
    public async Task AMissingCompanyMatchGivesNoInputs()
    {
        await using var db = InMemory();
        var seed = await ForwardRatioReaderSeed.SeedAsync(db);
        var ticker = await db.CyclicalWavesV2Tickers.SingleAsync(row => row.Ticker == "شاراک");
        // The vendor ticker is matched through its cw2 identity, but no monthly-report company has its ISIN.
        ticker.Enticker = "IRO1NONE0001";
        await db.SaveChangesAsync();
        var security = new ValuationGaugeSecurity("شاراک", null, CyclicalWavesV2CanonicalEntityResolver.ExternalIdPrefix + ticker.Id, null);

        Assert.Null(await new ForwardRatioInputsReader(db).ReadAsync(security, "شاراک", CancellationToken.None));
        Assert.NotNull(seed);
    }

    [Fact]
    public async Task ADifferentVendorTickerGivesNoInputs()
    {
        await using var db = InMemory();
        var seed = await ForwardRatioReaderSeed.SeedAsync(db);
        Assert.Null(await new ForwardRatioInputsReader(db).ReadAsync(seed.Security, "فولاد", CancellationToken.None));
    }

    [Fact]
    public async Task ACompanyWithoutIndustryHasNoIndustryId()
    {
        await using var db = InMemory();
        var seed = await ForwardRatioReaderSeed.SeedAsync(db);
        var company = await db.Companies.SingleAsync(row => row.Id == seed.CompanyId);
        company.IndustryId = null;
        await db.SaveChangesAsync();

        var inputs = await new ForwardRatioInputsReader(db).ReadAsync(seed.Security, "شاراک", CancellationToken.None);
        Assert.Null(inputs!.IndustryExternalId);
    }

    private static FinancialIngestionDbContext InMemory() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class ForwardRatioInputsReaderPostgreSqlTests(FinancialStatementValueSearch131PostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task TheReaderTranslatesToPostgreSqlWithTheSameResult()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        ForwardRatioReaderSeed.Seeded seed;
        await using (var db = database.CreateContext())
            seed = await ForwardRatioReaderSeed.SeedAsync(db);

        await using var context = database.CreateContext();
        await ForwardRatioReaderSeed.AssertReadsAsync(new ForwardRatioInputsReader(context), seed);
    }
}

internal static class ForwardRatioReaderSeed
{
    public sealed record Seeded(ValuationGaugeSecurity Security, Guid CompanyId);

    private const string ExternalCompanyId = "noavaran-sharak";
    private static readonly DateOnly LastMonth = new(2026, 10, 22);
    private static readonly DateOnly LastQuarter = new(2026, 9, 22);

    /// <summary>
    /// شاراک with 18 months of reports (the reader must keep the latest 16) and 10 statements (the latest 8). Each month
    /// has a month-itself product report with two lines (10 + month and 1 million rials), a fiscal-year cumulative report
    /// and a superseded revision (both excluded), and in the latest month a service report without an output type.
    /// </summary>
    public static async Task<Seeded> SeedAsync(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var industry = new NormalizedIndustryRow
        {
            Id = Guid.NewGuid(), ProviderName = ProviderSources.NoavaranCurrentApiName, ExternalId = "14", Name = "industry", LastSynchronizedAt = now
        };
        db.Industries.Add(industry);
        var company = Company(ExternalCompanyId, "IRO1PARK0001", industry.Id);
        db.Companies.Add(company);
        // Another company's report for the same month never leaks in.
        db.Companies.Add(Company("noavaran-other", "IRO1OTHR0001", industry.Id));

        for (var index = 0; index < 18; index++)
        {
            var monthEnd = LastMonth.AddMonths(index - 17);
            AddReport(db, ExternalCompanyId, monthEnd, "ProductSales", 0, true, [10m + index, 1m], now);
            AddReport(db, ExternalCompanyId, monthEnd, "ProductSales", 1, true, [9_999m], now);
            AddReport(db, ExternalCompanyId, monthEnd, "ProductSales", 0, false, [5_000m], now);
            AddReport(db, "noavaran-other", monthEnd, "ProductSales", 0, true, [7_777m], now);
        }

        AddReport(db, ExternalCompanyId, LastMonth, "ServiceSales", null, true, [3m], now);

        var ticker = new CyclicalWavesV2TickerRow
        {
            Id = Guid.NewGuid(), Ticker = "شاراک", NormalizedTicker = "شاراک", Enticker = "IRO1PARK0001", IsActive = true,
            CreatedAt = now, UpdatedAt = now, LastSeenInCatalogAt = now
        };
        db.CyclicalWavesV2Tickers.Add(ticker);
        foreach (var line in new[] { 44, 46, 66, 124, 412, 599, 85 })
            db.CyclicalWavesV2IncomeStatementLineDefinitions.Add(new CyclicalWavesV2IncomeStatementLineDefinitionRow
            {
                Id = line, NameEn = $"line {line}", NameFa = $"سطر {line}", UpdatedAt = now
            });
        for (var index = 0; index < 10; index++)
        {
            var statement = new CyclicalWavesV2IncomeStatementRow
            {
                Id = Guid.NewGuid(), TickerId = ticker.Id, PeriodEndingDate = LastQuarter.AddMonths(3 * (index - 9)),
                FiscalYear = 1405, FiscalMonth = 3 * (index % 4 + 1), CreatedAt = now, UpdatedAt = now
            };
            db.CyclicalWavesV2IncomeStatements.Add(statement);
            // Revenue is the first key with a value: 44, except in statement 5 where only 46 is printed.
            if (index == 5)
                Value(db, statement.Id, 46, 4_600m);
            else
                Value(db, statement.Id, 44, 1_000m + index);
            Value(db, statement.Id, 124, 99_999m);
            Value(db, statement.Id, 66, index == 8 ? null : 100m + index);
            Value(db, statement.Id, 85, 12m);
        }

        await db.SaveChangesAsync();
        return new Seeded(new ValuationGaugeSecurity("شاراک", company.Id, null, "پارس"), company.Id);
    }

    public static async Task AssertReadsAsync(ForwardRatioInputsReader reader, Seeded seed)
    {
        var inputs = await reader.ReadAsync(seed.Security, "شاراک", CancellationToken.None);

        Assert.NotNull(inputs);
        Assert.Equal("14", inputs!.IndustryExternalId);

        // The latest 16 of 18 months, oldest first; month itself only, in rials.
        Assert.Equal(16, inputs.MonthlySales.Count);
        Assert.Equal(LastMonth.AddMonths(-15), inputs.MonthlySales[0].MonthEnd);
        Assert.Equal(LastMonth, inputs.MonthlySales[^1].MonthEnd);
        Assert.Equal((10m + 2 + 1m) * 1_000_000m, inputs.MonthlySales[0].Sales);
        // The latest month adds the service report.
        Assert.Equal((10m + 17 + 1m + 3m) * 1_000_000m, inputs.MonthlySales[^1].Sales);

        // The latest 8 of 10 statements, oldest first.
        Assert.Equal(8, inputs.Quarters.Count);
        Assert.Equal(LastQuarter.AddMonths(-21), inputs.Quarters[0].PeriodEnd);
        Assert.Equal(LastQuarter, inputs.Quarters[^1].PeriodEnd);
        Assert.Equal(new ForwardQuarter(LastQuarter.AddMonths(-21), 1_002m, 102m), inputs.Quarters[0]);
        Assert.Equal(4_600m, inputs.Quarters[3].Revenue);
        Assert.Null(inputs.Quarters[6].NetProfit); // never filled
        Assert.Equal(new ForwardQuarter(LastQuarter, 1_009m, 109m), inputs.Quarters[^1]);
    }

    public static NormalizedCompanyRow Company(string externalId, string isin, Guid? industryId) => new()
    {
        Id = Guid.NewGuid(),
        ProviderName = ProviderSources.NoavaranCurrentApiName,
        ExternalCompanyId = externalId,
        Name = externalId,
        SymbolIsin = isin,
        IndustryId = industryId,
        LastSynchronizedAt = DateTimeOffset.UtcNow
    };

    private static void AddReport(
        FinancialIngestionDbContext db,
        string externalCompanyId,
        DateOnly monthEnd,
        string reportType,
        int? outputType,
        bool accepted,
        decimal[] amounts,
        DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        var fingerprint = id.ToString("N");
        db.MonthlyReports.Add(new NormalizedMonthlyReportRow
        {
            Id = id,
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalCompanyId = externalCompanyId,
            ExternalReportId = $"{externalCompanyId}-{monthEnd:yyyyMMdd}-{reportType}-{outputType}",
            PeriodStart = monthEnd.AddMonths(-1).AddDays(1),
            PeriodEnd = monthEnd,
            SourcePayloadChecksum = fingerprint,
            LastSynchronizedAt = now,
            LogicalReportKey = $"{externalCompanyId}-{monthEnd:yyyyMMdd}-{reportType}-{outputType}",
            RevisionFingerprint = fingerprint,
            IsAccepted = accepted,
            RevisionStatus = accepted ? "Accepted" : "Superseded",
            ReportType = reportType,
            OutputType = outputType
        });
        for (var line = 0; line < amounts.Length; line++)
            db.MonthlyReportLineItems.Add(new NormalizedMonthlyReportLineItemRow
            {
                Id = Guid.NewGuid(),
                MonthlyReportId = id,
                SourceRowFingerprint = $"{fingerprint}-{line}",
                SourcePayloadChecksum = fingerprint,
                ProductCode = $"P{line}",
                SalesAmount = amounts[line]
            });
    }

    private static void Value(FinancialIngestionDbContext db, Guid statementId, int line, decimal? quarter) =>
        db.CyclicalWavesV2IncomeStatementValues.Add(new CyclicalWavesV2IncomeStatementValueRow
        {
            StatementId = statementId, LineDefinitionId = line, SortOrder = line, Quarter = quarter, YearToDate = 123_456m
        });
}
