using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 154 T20 against an isolated PostgreSQL database (FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING).
/// Seeded with the §11.1 supplied fixtures and clearly labelled SYNTHETIC comparison data (§11.2).
/// </summary>
[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class BalanceSheetReport154PostgreSqlTests(FinancialStatementValueSearch131PostgreSqlFixture fixture)
{
    private static readonly Guid BankGroup = Guid.Parse("c96520f9-1d2e-444a-ab46-5bdffd3797d2");
    private static readonly Guid GeneralGroupA = Guid.Parse("97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c");
    private static readonly Guid GeneralGroupB = Guid.Parse("baab0118-18a2-4adf-b668-c4c0bb05527d");
    private static readonly Guid EligibleMarket = Guid.Parse("037c69ad-f519-419f-ae62-59003b6b2428");
    private static readonly PersianCalendar Jalali = new();

    private static DateOnly J(int year, int month, int day) => DateOnly.FromDateTime(Jalali.ToDateTime(year, month, day, 0, 0, 0, 0));

    /// <summary>Counts every command on every FinancialIngestionDbContext of the request (F154-R-11).</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Commands => Texts.Count;
        public System.Collections.Concurrent.ConcurrentQueue<string> Texts { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Texts.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Texts.Enqueue(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public void Reset() { while (Texts.TryDequeue(out _)) { } }

        public string Breakdown() => string.Join(", ", Texts.GroupBy(text =>
                text.Contains("\"FinancialStatementLineItems\"", StringComparison.Ordinal) ? "reader:line_items"
                : text.Contains("\"FinancialStatements\"", StringComparison.Ordinal) ? "reader:headers"
                : text.Contains("\"NoavaranEligibleCompanies\"", StringComparison.Ordinal) ? "classification"
                : text.Contains("\"Companies\"", StringComparison.Ordinal) ? "company_resolution"
                : "other")
            .Select(group => $"{group.Key}={group.Count()}"));
    }

    private sealed class StatementCountingReader(IBalanceSheetStatementReader inner) : IBalanceSheetStatementReader
    {
        public int LineItemStatements;

        public Task<BalanceSheetHeaderSet> ReadHeadersAsync(string providerName, string externalCompanyId, CancellationToken cancellationToken = default) =>
            inner.ReadHeadersAsync(providerName, externalCompanyId, cancellationToken);

        public Task<IReadOnlyList<BalanceSheetStatementHeader>> ReadHeadersAtDatesAsync(string providerName, string externalCompanyId,
            IReadOnlyCollection<DateOnly> periodEnds, CancellationToken cancellationToken = default) =>
            inner.ReadHeadersAtDatesAsync(providerName, externalCompanyId, periodEnds, cancellationToken);

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<BalanceSheetItemObservation>>> ReadLineItemsAsync(string providerName,
            IReadOnlyCollection<Guid> statementIds, IReadOnlyCollection<int> sourceItemIds, CancellationToken cancellationToken = default)
        {
            LineItemStatements += statementIds.Count;
            return inner.ReadLineItemsAsync(providerName, statementIds, sourceItemIds, cancellationToken);
        }

        public Task<bool> ConsolidatedExistsAsync(string providerName, string externalCompanyId, CancellationToken cancellationToken = default) =>
            inner.ConsolidatedExistsAsync(providerName, externalCompanyId, cancellationToken);
    }

    private static readonly int[] CatalogItems =
        BalanceSheetRowRegistry.RegisteredSourceItemIds.Concat([286, 60, 146, 160, 188]).Distinct().ToArray();

    [SkippableFact]
    public async Task SuppliedFixtures_SelectionTemplatesBaselinesBoundsAndReplay()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        Guid bankAnchorId;
        await using (var db = database.CreateContext())
        {
            db.Markets.Add(new NormalizedMarketRow { Id = EligibleMarket, ProviderName = "NoavaranCurrentApi", ExternalId = "1", Name = "بورس", LastSynchronizedAt = now });
            db.IndustryGroups.AddRange(
                new NormalizedIndustryGroupRow { Id = BankGroup, ProviderName = "NoavaranCurrentApi", ExternalId = "bank", Name = "بانک‌ها", LastSynchronizedAt = now },
                new NormalizedIndustryGroupRow { Id = GeneralGroupA, ProviderName = "NoavaranCurrentApi", ExternalId = "a", Name = "a", LastSynchronizedAt = now },
                new NormalizedIndustryGroupRow { Id = GeneralGroupB, ProviderName = "NoavaranCurrentApi", ExternalId = "b", Name = "b", LastSynchronizedAt = now });
            Company(db, "452", "وبملت", "بانک ملت", BankGroup, now);
            Company(db, "167", "شگل", "گلتاش", GeneralGroupA, now);
            Company(db, "290", "فاذر", "صنایع آذر آب", GeneralGroupB, now);
            Company(db, "900", "تلفیقیفقط", "فقط تلفیقی", GeneralGroupA, now);
            Company(db, "901", "ریالی", "واحد ریال", GeneralGroupA, now);

            var catalog = CatalogItems.ToDictionary(id => id, id => new FinancialStatementSourceItemCatalogRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", StatementType = "BalanceSheet", SourceItemId = id,
                TitleFa = $"bs-{id}", Unit = "N/A", LastSynchronizedAt = now
            });
            db.FinancialStatementSourceItems.AddRange(catalog.Values);
            // Income-statement item 143 (net profit) shares the id with balance-sheet 143 (total liabilities).
            var incomeCatalog143 = new FinancialStatementSourceItemCatalogRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", StatementType = "IncomeStatement", SourceItemId = 143,
                TitleFa = "سود خالص", Unit = "N/A", LastSynchronizedAt = now
            };
            db.FinancialStatementSourceItems.Add(incomeCatalog143);

            var anchorRow = Statement(db, catalog, "452", "409865", "BalanceSheet", "SixMonths", new DateOnly(2026, 9, 22), "1405/12/29",
                new DateOnly(2026, 10, 22), now, values: new()
                {
                    [1] = 409_287_621m, [57] = 3_382_000_000m, [291] = 45_209_234_799m, [223] = 27_590_264_477m, [170] = -181_354_538m,
                    [60] = 0m, [286] = 6m, [146] = 1_031_013_395m, [135] = 51_498_227_670m, [137] = 7_612_077_011m,
                    [139] = 52_501_759_193m, [140] = 126_830_069m, [138] = 59_110_304_681m, [143] = 52_628_589_262m,
                    [147] = 6_481_715_419m, [148] = 59_110_304_681m, [300] = 59_110_304_681m, [303] = 52_628_589_262m,
                    [304] = 6_481_715_419m, [305] = 59_110_304_681m
                });
            bankAnchorId = anchorRow.Id;
            // Same-date income statement and cash flow, and a later consolidated balance sheet, never affect selection.
            var income = Statement(db, catalog, "452", "409865", "IncomeStatement", "SixMonths", new DateOnly(2026, 9, 22), "1405/12/29", null, now, values: new());
            db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                { Id = Guid.NewGuid(), FinancialStatementId = income.Id, SourceItemCatalogId = incomeCatalog143.Id, Value = 999m });
            Statement(db, catalog, "452", "409866", "CashFlow", "SixMonths", new DateOnly(2026, 9, 22), "1405/12/29", null, now, values: new());
            Statement(db, catalog, "452", "419999", "BalanceSheet", "NineMonths", J(1405, 9, 30), "1405/12/29", null, now, composing: true,
                values: new() { [138] = 1m, [147] = 1m });
            // SYNTHETIC S-1 previous report 403733 (1405/03/31).
            Statement(db, catalog, "452", "403733", "BalanceSheet", "ThreeMonths", J(1405, 3, 31), "1405/12/29", new DateOnly(2026, 7, 21), now,
                values: new() { [138] = 50_000_000_000m, [143] = 44_523_000_000m, [147] = 5_477_000_000m, [148] = 50_000_000_000m, [57] = 3_382_000_000m });
            // SYNTHETIC S-3 fiscal-year end 1404/12/29 (audited and unaudited; audited ranks first).
            Statement(db, catalog, "452", "403749", "BalanceSheet", "TwelveMonths", J(1404, 12, 29), "1404/12/29", new DateOnly(2026, 7, 22), now, audited: true,
                values: new() { [138] = 48_000_000_000m, [143] = 42_000_000_000m, [147] = 6_000_000_000m, [148] = 48_000_000_000m });
            Statement(db, catalog, "452", "394247", "BalanceSheet", "TwelveMonths", J(1404, 12, 29), "1404/12/29", null, now,
                values: new() { [138] = 47_000_000_000m, [143] = 41_000_000_000m, [147] = 6_000_000_000m, [148] = 47_000_000_000m });
            Statement(db, catalog, "452", "385967", "BalanceSheet", "NineMonths", J(1404, 9, 30), "1404/12/29", null, now,
                values: new() { [138] = 35_000_000_000m, [143] = 30_000_000_000m, [147] = 5_000_000_000m, [148] = 35_000_000_000m });
            Statement(db, catalog, "452", "384526", "BalanceSheet", "SixMonths", J(1404, 6, 31), "1404/12/29", null, now,
                values: new() { [138] = 30_000_000_000m, [143] = 26_000_000_000m, [147] = 4_000_000_000m, [148] = 30_000_000_000m });
            Statement(db, catalog, "452", "377564", "BalanceSheet", "ThreeMonths", J(1404, 3, 31), "1404/12/29", null, now,
                values: new() { [138] = 28_000_000_000m, [143] = 24_000_000_000m, [147] = 4_000_000_000m, [148] = 28_000_000_000m });

            Statement(db, catalog, "167", "s-1405-06", "BalanceSheet", "NineMonths", new DateOnly(2026, 9, 22), "1405/09/30", null, now, values: new()
            {
                [1] = 3_263_057m, [57] = 6_149_600m, [18] = 10_266_826m, [47] = 21_347_261m, [51] = 0m, [58] = 10_850_400m,
                [306] = 26_549_068m, [293] = 26_549_068m, [307] = 16_116_006m, [308] = 16_116_006m,
                [138] = 63_522_534m, [143] = 41_014_336m, [147] = 22_508_198m, [148] = 63_522_534m
            });
            // SYNTHETIC S-2 non-Esfand fiscal-year end 1404/09/30.
            Statement(db, catalog, "167", "s-1404-09", "BalanceSheet", "TwelveMonths", J(1404, 9, 30), "1404/09/30", null, now,
                values: new() { [138] = 60_000_000m, [143] = 40_000_000m, [147] = 20_000_000m, [148] = 60_000_000m });
            Statement(db, catalog, "290", "f-1405-06", "BalanceSheet", "SixMonths", new DateOnly(2026, 9, 22), "1405/12/29", null, now, values: new()
            {
                [57] = 18_126_475m, [21] = 87_160_528m, [45] = 17_452_463m, [51] = 0m, [64] = 64_629_501m, [306] = 40_302_278m,
                [307] = 29_966_070m, [146] = 85_394_709m, [138] = 145_343_307m, [143] = 59_948_598m, [147] = 85_394_709m, [148] = 145_343_307m
            });
            Statement(db, catalog, "900", "c-only", "BalanceSheet", "TwelveMonths", J(1404, 12, 29), "1404/12/29", null, now, composing: true,
                values: new() { [138] = 1m });
            Statement(db, catalog, "901", "rials", "BalanceSheet", "TwelveMonths", J(1404, 12, 29), "1404/12/29", null, now, scale: "Rials",
                values: new() { [138] = 1m });
            await db.SaveChangesAsync();
        }

        var counter = new CommandCounter();
        var apiConfigPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/backend/FinancialCopilot.API"));
        var configuration = new ConfigurationBuilder().SetBasePath(apiConfigPath)
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:FinancialCopilot"] = database.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new BankingFinancialReportsConfiguration(BankGroup));
        services.AddFinancialCopilotInfrastructure(configuration);
        // Every FinancialIngestionDbContext of the request (company resolution, classification, reader) is counted.
        services.RemoveAll<DbContextOptions<FinancialIngestionDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<FinancialIngestionDbContext>>();
        services.AddDbContext<FinancialIngestionDbContext>(options => options.UseNpgsql(database.ConnectionString).AddInterceptors(counter));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var reader = new StatementCountingReader(new EfCoreBalanceSheetStatementReader(scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>()));
        var useCase = new BalanceSheetReportQueryUseCase(reader,
            scope.ServiceProvider.GetRequiredService<ICompanyResolutionOutcomeService>(),
            scope.ServiceProvider.GetRequiredService<IQuarterlyFinancialReportsRepository>(),
            new BankingFinancialReportsConfiguration(BankGroup), TimeProvider.System);

        // AC-X04: default report for وبملت with all four standard baselines resolvable; all database commands counted.
        counter.Reset();
        var report = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت"));
        var breakdown = counter.Breakdown();
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "feature154-pg-bounds.txt"),
            $"total_round_trips={counter.Commands} ({breakdown}); line_item_statements={reader.LineItemStatements}; snapshots={report.Snapshots.Count}; chart_points={report.Chart.Points.Count}");
        Assert.True(counter.Commands <= 6, $"total round trips {counter.Commands}: {breakdown}");
        Assert.True(reader.LineItemStatements <= 9, $"line-item statements: {reader.LineItemStatements}");
        Assert.True(report.Snapshots.Count <= 9);
        Assert.True(report.Chart.Points.Count <= 5);

        // AC-S01..S04, AC-M01, AC-M03
        Assert.Equal(BalanceSheetOutcomes.Available, report.Outcome);
        Assert.Equal(BalanceSheetTemplates.Banking, report.TemplateId);
        Assert.Equal(bankAnchorId.ToString("D"), report.AnchorStatementId);
        var anchor = report.Snapshots[report.AnchorStatementId!];
        Assert.Equal(("409865", "1405/06/31", 6, false, false), (anchor.Statement.ExternalStatementId, anchor.Statement.JalaliPeriodEnd,
            anchor.Statement.PeriodMonths, anchor.Statement.IsAudited, anchor.Statement.IsRepresented));
        Assert.Equal("1405/07/30", anchor.Statement.JalaliPublishedAt);
        var rows = BalanceSheetComparisonBuilder.AllRows(anchor).ToDictionary(row => row.RowId);
        Assert.Equal(409_287_621m, rows["cash"].Cell.Value);
        Assert.Equal(3_382_000_000m, rows["share_capital"].Cell.Value);
        Assert.Equal(45_209_234_799m, rows["bank_loans_granted"].Cell.Value);
        Assert.Equal(27_590_264_477m, rows["bank_deposits"].Cell.Value);
        Assert.Equal(-181_354_538m, rows["bank_treasury_shares"].Cell.Value);
        Assert.Equal(52_628_589_262m, rows["total_liabilities"].Cell.Value); // balance-sheet 143, never income 143 (999)
        Assert.DoesNotContain(rows.Values, row => row.Cell.Candidates.Any(candidate => candidate.SourceItemId is 60 or 286 or 146));
        Assert.StartsWith("10.96545763717478", anchor.Ratio.Value!.Value.ToString(CultureInfo.InvariantCulture));

        var candidates = report.Comparison!.Candidates.ToDictionary(item => item.Kind);
        Assert.Equal(J(1404, 12, 29), candidates[BalanceSheetBaselineKinds.FiscalYearEnd].BaselineDate);
        Assert.Equal("403749", report.Snapshots[candidates[BalanceSheetBaselineKinds.FiscalYearEnd].StatementId!].Statement.ExternalStatementId);
        Assert.Equal(J(1405, 3, 31), candidates[BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd].BaselineDate);
        Assert.Equal(J(1405, 3, 31), candidates[BalanceSheetBaselineKinds.PreviousReport].BaselineDate);
        Assert.Equal(J(1404, 6, 31), candidates[BalanceSheetBaselineKinds.SameStagePreviousYear].BaselineDate);
        Assert.Equal(0.01m, Math.Round(candidates[BalanceSheetBaselineKinds.PreviousReport].RatioChange!.PpDifference!.Value, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(9_110_304_681m, candidates[BalanceSheetBaselineKinds.PreviousReport].RowChanges["total_assets"].Amount);

        // AC-X01: serialization and replay without loss and without any database read.
        counter.Reset();
        var commandsBeforeReplay = counter.Commands;
        var replayed = JsonSerializer.Deserialize<BalanceSheetReportResult>(JsonSerializer.Serialize(report))!;
        Assert.Equal(JsonSerializer.Serialize(report), JsonSerializer.Serialize(replayed));
        Assert.Equal(commandsBeforeReplay, counter.Commands);

        // AC-M04 / AC-C03 non-Esfand General template.
        var shegol = await useCase.ExecuteAsync(new BalanceSheetReportRequest("شگل"));
        Assert.Equal(BalanceSheetTemplates.General, shegol.TemplateId);
        var shegolRows = BalanceSheetComparisonBuilder.AllRows(shegol.Snapshots[shegol.AnchorStatementId!]).ToDictionary(row => row.RowId);
        Assert.Equal(6_149_600m, shegolRows["share_capital"].Cell.Value);
        Assert.Equal(0m, shegolRows["long_term_borrowings"].Cell.Value);
        var shegolFiscal = shegol.Comparison!.Candidates.Single(item => item.Kind == BalanceSheetBaselineKinds.FiscalYearEnd);
        Assert.Equal(J(1404, 9, 30), shegolFiscal.BaselineDate);
        Assert.Equal(2.10m, Math.Round(shegolFiscal.RatioChange!.PpDifference!.Value, 2, MidpointRounding.AwayFromZero));
        var fazar = await useCase.ExecuteAsync(new BalanceSheetReportRequest("فاذر"));
        var fazarRows = BalanceSheetComparisonBuilder.AllRows(fazar.Snapshots[fazar.AnchorStatementId!]).ToDictionary(row => row.RowId);
        Assert.Equal(BalanceSheetCellStates.Missing, fazarRows["cash"].Cell.State);
        Assert.Equal(18_126_475m, fazarRows["share_capital"].Cell.Value);

        // AC-S09 single date.
        var single = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت", BalanceSheetViews.SingleDate, Dates: [J(1405, 3, 31)]));
        Assert.Equal("403733", single.Snapshots[single.AnchorStatementId!].Statement.ExternalStatementId);
        var missing = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت", BalanceSheetViews.SingleDate, Dates: [J(1405, 4, 31)]));
        Assert.Equal(BalanceSheetOutcomes.NoSnapshotAtDate, missing.Outcome);
        Assert.Contains("۱۴۰۵/۰۴/۳۱", missing.OutcomeMessage);

        // AC-S05 / E-6 and storage scale.
        var consolidatedOnly = await useCase.ExecuteAsync(new BalanceSheetReportRequest("تلفیقیفقط"));
        Assert.Equal((BalanceSheetOutcomes.NoStandaloneBalanceSheet, true), (consolidatedOnly.Outcome, consolidatedOnly.ConsolidatedAlternativeAvailable));
        var rials = await useCase.ExecuteAsync(new BalanceSheetReportRequest("ریالی"));
        Assert.Equal(BalanceSheetOutcomes.IneligibleStorageScale, rials.Outcome);
    }

    private static void Company(FinancialIngestionDbContext db, string externalId, string symbol, string name, Guid group, DateTimeOffset now) =>
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = externalId, Name = name,
            CompanySymbol = symbol, TseSymbol = symbol, Ticker = symbol, GroupId = group, MarketId = EligibleMarket,
            PrecedencyRight = 0, LastSynchronizedAt = now
        });

    private static NormalizedFinancialStatementRow Statement(FinancialIngestionDbContext db,
        IReadOnlyDictionary<int, FinancialStatementSourceItemCatalogRow> catalog, string company, string externalId, string type,
        string periodType, DateOnly periodEnd, string fiscalYearEnd, DateOnly? published, DateTimeOffset now,
        Dictionary<int, decimal?> values, bool audited = false, bool composing = false, string scale = "MillionRials")
    {
        var row = new NormalizedFinancialStatementRow
        {
            Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = company, ExternalStatementId = externalId,
            StatementType = type, PeriodType = periodType, PeriodStart = periodEnd.AddMonths(-6), PeriodEnd = periodEnd,
            SourcePayloadChecksum = $"checksum-{externalId}-{type}-{composing}", LastSynchronizedAt = now, IsAudited = audited,
            IsComposing = composing, PublishedAt = published, CompanyId = null,
            WarningsJson = $"[{{\"code\":\"NadpcoApiStatementSelection\",\"isComposing\":{(composing ? "true" : "false")},\"jalaliFiscalYearEnd\":\"{fiscalYearEnd}\",\"assumedScale\":\"{scale}\",\"sourceAmountUnit\":\"N/A\"}}]"
        };
        db.FinancialStatements.Add(row);
        foreach (var (id, value) in values)
            db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                { Id = Guid.NewGuid(), FinancialStatementId = row.Id, SourceItemCatalogId = catalog[id].Id, Value = value });
        return row;
    }
}
