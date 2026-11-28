using System.Text.Json;
using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.MarketViews;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Feature 148 T148-08.2: the v2 schema, its constraints and stores on real PostgreSQL.</summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class CyclicalWavesV2PostgreSqlIntegrationTests(PostgreSqlIntegrationFixture fixture)
{
    private const string FoundationMigration = "20261031045622_Feature148CyclicalWavesV2Foundation";
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Migration_CreatesOnlyTheCyclicalWavesSchemaAndRollsBackCleanly()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();

        Assert.Equal(14, await CountAsync(db, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'cyclicalwaves'"));
        Assert.Equal(29, await db.CyclicalWavesV2MetricDefinitions.CountAsync());
        Assert.Equal(30, await CountAsync(db, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'cyclicalwaves' AND table_name IN ('GaugeDetails', 'EquilibriumRanges') AND column_name ~ '(High|Middle|Low)$' AND data_type = 'numeric'"));
        Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'cyclicalwaves' AND table_name IN ('GaugeDetails', 'EquilibriumRanges') AND column_name ~ '(High|Middle|Low)$' AND data_type <> 'numeric'"));
        Assert.Equal(3, await CountAsync(db, "SELECT count(*) FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace WHERE n.nspname = 'cyclicalwaves' AND t.relname IN ('GaugeAvailability', 'GaugeDetails', 'EquilibriumRanges') AND c.contype = 'p'"));
        Assert.Equal(3, await CountAsync(db, "SELECT count(*) FROM information_schema.referential_constraints rc JOIN information_schema.table_constraints child ON child.constraint_name = rc.constraint_name AND child.constraint_schema = rc.constraint_schema JOIN information_schema.constraint_column_usage parent ON parent.constraint_name = rc.constraint_name AND parent.constraint_schema = rc.constraint_schema WHERE child.table_schema = 'cyclicalwaves' AND child.table_name IN ('GaugeAvailability', 'GaugeDetails', 'EquilibriumRanges') AND parent.table_schema = 'cyclicalwaves' AND parent.table_name = 'Tickers'"));
        // AC-23: no foreign key crosses into or out of the schema.
        Assert.Equal(0, await CountAsync(db, """
            SELECT count(*) FROM information_schema.referential_constraints rc
            JOIN information_schema.table_constraints child ON child.constraint_name = rc.constraint_name AND child.constraint_schema = rc.constraint_schema
            JOIN information_schema.table_constraints parent ON parent.constraint_name = rc.unique_constraint_name AND parent.constraint_schema = rc.unique_constraint_schema
            WHERE (child.table_schema = 'cyclicalwaves') <> (parent.table_schema = 'cyclicalwaves')
            """));

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(FoundationMigration);
        Assert.Equal(11, await CountAsync(db, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'cyclicalwaves'"));
        var originalTickerId = Guid.NewGuid();
        db.CyclicalWavesV2Tickers.Add(new CyclicalWavesV2TickerRow
        {
            Id = originalTickerId,
            Ticker = "FEATURE148VERIFY",
            NormalizedTicker = PersianSymbolNormalizer.Normalize("FEATURE148VERIFY"),
            Enticker = "IRO1VERIFY0001",
            IsActive = true,
            CreatedAt = Now,
            UpdatedAt = Now,
            LastSeenInCatalogAt = Now
        });
        await db.SaveChangesAsync();
        var originalMetricDefinition = await db.CyclicalWavesV2MetricDefinitions.AsNoTracking()
            .OrderBy(definition => definition.Id).FirstOrDefaultAsync();
        if (originalMetricDefinition is not null)
        {
            db.CyclicalWavesV2FinancialValues.Add(new CyclicalWavesV2FinancialValueRow
            {
                TickerId = originalTickerId,
                MetricDefinitionId = originalMetricDefinition.Id,
                NumericValue = 123.456m,
                UpdatedAt = Now
            });
            await db.SaveChangesAsync();
        }
        var originalMetricValueCount = await db.CyclicalWavesV2FinancialValues.CountAsync();
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync();
        Assert.Equal(14, await CountAsync(db, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'cyclicalwaves'"));
        Assert.Equal("FEATURE148VERIFY", await db.CyclicalWavesV2Tickers.AsNoTracking().Where(row => row.Id == originalTickerId).Select(row => row.Ticker).SingleAsync());
        Assert.Equal(originalMetricValueCount, await db.CyclicalWavesV2FinancialValues.CountAsync());
        await migrator.MigrateAsync(FoundationMigration);
        Assert.Equal(11, await CountAsync(db, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'cyclicalwaves'"));
        Assert.Equal("FEATURE148VERIFY", await db.CyclicalWavesV2Tickers.AsNoTracking().Where(row => row.Id == originalTickerId).Select(row => row.Ticker).SingleAsync());
        Assert.Equal(originalMetricValueCount, await db.CyclicalWavesV2FinancialValues.CountAsync());
        await migrator.MigrateAsync();
        Assert.Equal(14, await CountAsync(db, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'cyclicalwaves'"));
        Assert.Equal("FEATURE148VERIFY", await db.CyclicalWavesV2Tickers.AsNoTracking().Where(row => row.Id == originalTickerId).Select(row => row.Ticker).SingleAsync());
        Assert.Equal(originalMetricValueCount, await db.CyclicalWavesV2FinancialValues.CountAsync());

        var previous = db.Database.GetMigrations().TakeWhile(migration => migration != FoundationMigration).Last();
        await migrator.MigrateAsync(previous);
        Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'cyclicalwaves'"));
        Assert.True(await CountAsync(db, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'") > 50);

        await migrator.MigrateAsync();
        Assert.Equal(14, await CountAsync(db, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'cyclicalwaves'"));
    }

    [SkippableFact]
    public async Task GaugeStore_PersistsFullRangeExactValuesAndDoesNotDuplicateUnchangedRows()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var tickerId = Guid.NewGuid();
        db.CyclicalWavesV2Tickers.Add(new CyclicalWavesV2TickerRow
        {
            Id = tickerId, Ticker = "فولاد", NormalizedTicker = PersianSymbolNormalizer.Normalize("فولاد"),
            Enticker = "IRO1FOLD0001", IsActive = true, CreatedAt = Now, UpdatedAt = Now, LastSeenInCatalogAt = Now
        });
        await db.SaveChangesAsync();

        var store = new EfCoreCyclicalWavesV2GaugeStore(db);
        var catalog = await store.ApplyGaugeCatalogAsync(
            [new CyclicalWavesV2GaugeCatalogItem("فولاد", "iro1fold0001", true, true)], Now, CancellationToken.None);
        Assert.Equal(1, catalog.Matched);

        var maximum = new string('9', ExactDecimal.MaximumIntegerDigits) + "." + new string('8', ExactDecimal.MaximumScale);
        var large = "123456789012345678901234567890.123456789012345678901234567890";
        var negative = "-7232.1700000000000000002";
        var verySmall = "0.0000000000000000000000000000000000000000000000000000000000123";
        var gaugeJson = $$"""
            {"A":1,"B":2,"C":3,"D":4,"E":5,"F":6,"Close":null,"Start":{{maximum}},"End":{{large}},"Min":{{negative}},"Avg":{{verySmall}},"Max":{{large}},"Status":null}
            """;
        var gauge = JsonSerializer.Deserialize<CyclicalWavesV2Gauge>(gaugeJson)!;
        var details = new CyclicalWavesV2GaugeDetails("فولاد", "IRO1FOLD0001", gauge, null);

        Assert.Equal(CyclicalWavesV2ApplyOutcome.Updated, await store.ApplyGaugeDetailsAsync(details, Now, CancellationToken.None));
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Unchanged, await store.ApplyGaugeDetailsAsync(details, Now.AddMinutes(1), CancellationToken.None));
        var storedGauge = await db.CyclicalWavesV2GaugeDetails.AsNoTracking().SingleAsync();
        Assert.Equal(ExactDecimal.Parse(maximum), storedGauge.Start);
        Assert.Equal(ExactDecimal.Parse(large), storedGauge.End);
        Assert.Equal(ExactDecimal.Parse(negative), storedGauge.Min);
        Assert.Equal(ExactDecimal.Parse(verySmall), storedGauge.Avg);
        Assert.Equal(ExactDecimal.Parse(large), storedGauge.Max);
        Assert.Null(storedGauge.Close);
        Assert.Null(storedGauge.Status);
        Assert.Equal(6, storedGauge.F);
        Assert.Equal(1, await db.CyclicalWavesV2GaugeDetails.CountAsync());
        // BUG-052: an unchanged fetch still moves the fetch time; the readers age the gauge by it.
        Assert.Equal(Now.AddMinutes(1), storedGauge.FetchedAtUtc);

        var query = new CyclicalWavesV2GaugeQueryService(db);
        var queryTicker = await db.CyclicalWavesV2Tickers.Select(row => row.Ticker).SingleAsync();
        var peAnswer = await query.AnswerAsync($"P/E {queryTicker}", CancellationToken.None);
        var serializedPeAnswer = JsonSerializer.Serialize(peAnswer);
        Assert.Equal(peAnswer, JsonSerializer.Deserialize<string>(serializedPeAnswer));
        Assert.Contains("\u0646\u0627\u0645\u0648\u062c\u0648\u062f", peAnswer, StringComparison.Ordinal);
        Assert.Contains(negative, peAnswer, StringComparison.Ordinal);
        Assert.Contains(verySmall, peAnswer, StringComparison.Ordinal);
        Assert.Contains(large, peAnswer, StringComparison.Ordinal);
        Assert.Contains(maximum, peAnswer, StringComparison.Ordinal);

        var range = new CyclicalWavesV2EquilibriumRange(ExactDecimal.Parse("-" + verySmall), ExactDecimal.Parse(negative), ExactDecimal.Parse(large));
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Updated, await store.ApplyEquilibriumRangeAsync("iro1fold0001", range, Now, CancellationToken.None));
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Unchanged, await store.ApplyEquilibriumRangeAsync("IRO1FOLD0001", range, Now.AddMinutes(1), CancellationToken.None));
        var storedRange = await db.CyclicalWavesV2EquilibriumRanges.AsNoTracking().SingleAsync();
        Assert.Equal(ExactDecimal.Parse("-" + verySmall), storedRange.Min);
        var equilibriumAnswer = await query.AnswerAsync($"equilibrium {queryTicker}", CancellationToken.None);
        Assert.Contains("-" + verySmall, equilibriumAnswer, StringComparison.Ordinal);
        Assert.Contains(negative, equilibriumAnswer, StringComparison.Ordinal);
        Assert.Contains(large, equilibriumAnswer, StringComparison.Ordinal);
        Assert.Equal(-1, storedRange.Min!.Value.UnscaledValue.Sign);
        Assert.Equal(1, await db.CyclicalWavesV2EquilibriumRanges.CountAsync());
        Assert.Equal(0, await CountAsync(db, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'cyclicalwaves' AND table_name IN ('GaugeDetails', 'EquilibriumRanges') AND data_type <> 'numeric' AND column_name LIKE '%High'"));
    }

    /// <summary>
    /// BUG-052: on a closed market day the vendor returns the same gauges. The relative valuation inputs must stay
    /// usable after that sync, although the values were last changed more than the freshness window ago.
    /// </summary>
    [SkippableFact]
    public async Task GaugeStore_PersistsTheProviderForwardValueAndTreatsItsChangeAsAnUpdate()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        db.CyclicalWavesV2Tickers.Add(new CyclicalWavesV2TickerRow
        {
            Id = Guid.NewGuid(), Ticker = "فولاد", NormalizedTicker = PersianSymbolNormalizer.Normalize("فولاد"),
            Enticker = "IRO1FOLD0001", IsActive = true, CreatedAt = Now, UpdatedAt = Now, LastSeenInCatalogAt = Now
        });
        await db.SaveChangesAsync();
        var store = new EfCoreCyclicalWavesV2GaugeStore(db);
        await store.ApplyGaugeCatalogAsync(
            [new CyclicalWavesV2GaugeCatalogItem("فولاد", "IRO1FOLD0001", true, true)], Now, CancellationToken.None);
        var withoutForward = new CyclicalWavesV2Gauge(1, 2, 3, 4, 5, 6, ExactDecimal.Parse("4"),
            ExactDecimal.Parse("1"), ExactDecimal.Parse("6"), ExactDecimal.Parse("1"), ExactDecimal.Parse("2"), ExactDecimal.Parse("6"), 3);
        CyclicalWavesV2GaugeDetails Details(string? pe, string? ps) => new("فولاد", "IRO1FOLD0001",
            withoutForward with { Forward = pe is null ? null : ExactDecimal.Parse(pe) },
            withoutForward with { Forward = ps is null ? null : ExactDecimal.Parse(ps) });
        static (ExactDecimal? Pe, ExactDecimal? Ps, DateTimeOffset FetchedAt) Expected(string? pe, string? ps, DateTimeOffset at) =>
            (pe is null ? null : ExactDecimal.Parse(pe), ps is null ? null : ExactDecimal.Parse(ps), at);
        async Task<(ExactDecimal? Pe, ExactDecimal? Ps, DateTimeOffset FetchedAt)> StoredAsync()
        {
            var rows = await db.CyclicalWavesV2GaugeDetails.AsNoTracking().ToDictionaryAsync(row => row.GaugeType);
            return (rows["PE"].Forward, rows["PS"].Forward, rows["PE"].FetchedAtUtc);
        }

        // A gauge fetched before the provider sent the member has no forward value.
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Updated, await store.ApplyGaugeDetailsAsync(Details(null, null), Now, CancellationToken.None));
        Assert.Equal(Expected(null, null, Now), await StoredAsync());

        // The forward value alone is a change.
        var first = Now.AddHours(1);
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Updated,
            await store.ApplyGaugeDetailsAsync(Details("3.8198454018903374", "0.8630831034441018"), first, CancellationToken.None));
        Assert.Equal(Expected("3.8198454018903374", "0.8630831034441018", first), await StoredAsync());

        // Unchanged values only move the fetch time (BUG-052).
        var second = Now.AddHours(2);
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Unchanged,
            await store.ApplyGaugeDetailsAsync(Details("3.8198454018903374", "0.8630831034441018"), second, CancellationToken.None));
        Assert.Equal(second, (await StoredAsync()).FetchedAt);

        // A negative value is stored as received; a value that disappears is cleared, never kept or zeroed.
        var third = Now.AddHours(3);
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Updated,
            await store.ApplyGaugeDetailsAsync(Details("-12.5", null), third, CancellationToken.None));
        Assert.Equal(Expected("-12.5", null, third), await StoredAsync());
        Assert.Equal(1, await CountAsync(db, "SELECT count(*) FROM cyclicalwaves.\"GaugeDetails\" WHERE \"GaugeType\" = 'PS' AND \"ForwardHigh\" IS NULL AND \"ForwardMiddle\" IS NULL AND \"ForwardLow\" IS NULL AND \"ForwardSign\" IS NULL AND \"ForwardScale\" IS NULL"));
        Assert.Equal(2, await CountAsync(db, "SELECT count(*) FROM pg_constraint WHERE conname IN ('CK_CyclicalWavesV2GaugeDetails_ForwardParts', 'CK_CyclicalWavesV2GaugeDetails_ForwardRanges')"));
    }

    [SkippableFact]
    public async Task GaugeStore_UnchangedFetchKeepsRelativeValuationInputsFresh()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        db.CyclicalWavesV2Tickers.Add(new CyclicalWavesV2TickerRow
        {
            Id = Guid.NewGuid(), Ticker = "فولاد", NormalizedTicker = PersianSymbolNormalizer.Normalize("فولاد"),
            Enticker = "IRO1FOLD0001", IsActive = true, CreatedAt = Now, UpdatedAt = Now, LastSeenInCatalogAt = Now
        });
        await db.SaveChangesAsync();

        var store = new EfCoreCyclicalWavesV2GaugeStore(db);
        await store.ApplyGaugeCatalogAsync(
            [new CyclicalWavesV2GaugeCatalogItem("فولاد", "IRO1FOLD0001", true, true)], Now, CancellationToken.None);
        var gauge = JsonSerializer.Deserialize<CyclicalWavesV2Gauge>(
            """{"A":1,"B":2,"C":3,"D":4,"E":5,"F":6,"Close":4,"Start":1,"End":6,"Min":1,"Avg":2,"Max":6,"Status":null}""")!;
        var details = new CyclicalWavesV2GaugeDetails("فولاد", "IRO1FOLD0001", gauge, gauge);
        var lastTradingDaySync = Now;
        var closedDaySync = Now.AddHours(24);
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Updated, await store.ApplyGaugeDetailsAsync(details, lastTradingDaySync, CancellationToken.None));
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Unchanged, await store.ApplyGaugeDetailsAsync(details, closedDaySync, CancellationToken.None));

        var freshness = TimeSpan.FromHours(26);
        var reader = new CyclicalWavesV2RelativeValuationInputReader(
            db, new PersistedMarketDataProvider(db, TimeProvider.System), Options.Create(new MarketViewOptions()),
            NullLogger<CyclicalWavesV2RelativeValuationInputReader>.Instance);
        var companyId = Guid.NewGuid();
        var observations = await reader.ReadAsync(
            [new RelativeValuationSecurity(companyId, "IRO1FOLD0001")], lastTradingDaySync.AddHours(30), freshness, CancellationToken.None);

        Assert.Equal(
            [RelativeValuationMetric.Pe, RelativeValuationMetric.Ps],
            observations.Select(row => row.Fact.Metric).OrderBy(metric => metric).ToArray());
        Assert.All(observations, row =>
        {
            Assert.Equal(companyId, row.Fact.CompanyId);
            Assert.Equal(closedDaySync + freshness, row.ValidUntilUtc);
        });
    }

    [SkippableFact]
    public async Task Stores_UpsertLatestStateAndPeriodsAndPreventDuplicates()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var store = new EfCoreCyclicalWavesV2Store(db, NullLogger<EfCoreCyclicalWavesV2Store>.Instance);

        var catalog = await store.UpsertCatalogAsync(["فولاد", "شلرد", "وبملت"], Now, CancellationToken.None);
        Assert.Equal(3, catalog.Added);
        var second = await store.UpsertCatalogAsync(["فولاد", "شلرد"], Now.AddDays(1), CancellationToken.None);
        Assert.Equal((0, 1, 0), (second.Added, second.Deactivated, second.Reactivated));
        Assert.Equal(1, (await store.UpsertCatalogAsync(["فولاد", "شلرد", "وبملت"], Now.AddDays(2), CancellationToken.None)).Reactivated);
        Assert.Equal(3, await db.CyclicalWavesV2Tickers.CountAsync());

        var screener = Screener("فولاد", pe: 6.05m, ps: 1.78m);
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Updated, (await store.ApplyScreenerAsync(screener, Now, CancellationToken.None)).Outcome);
        Assert.Equal(29, await db.CyclicalWavesV2FinancialValues.CountAsync());
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Unchanged, (await store.ApplyScreenerAsync(screener, Now.AddDays(1), CancellationToken.None)).Outcome);
        var changed = await store.ApplyScreenerAsync(Screener("فولاد", pe: 6.10m, ps: 1.78m), Now.AddDays(2), CancellationToken.None);
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Updated, changed.Outcome);
        Assert.Equal(29, await db.CyclicalWavesV2FinancialValues.CountAsync());
        var pe = await db.CyclicalWavesV2FinancialValues.SingleAsync(row => row.MetricDefinitionId == 28);
        Assert.Equal(6.10m, pe.NumericValue);
        Assert.Equal(Now.AddDays(2), pe.UpdatedAt);
        Assert.Null((await db.CyclicalWavesV2FinancialValues.SingleAsync(row => row.MetricDefinitionId == 5)).NumericValue);
        Assert.Equal("IRO1FOLD0001", (await db.CyclicalWavesV2Tickers.SingleAsync(row => row.Ticker == "فولاد")).Enticker);
        Assert.True((await store.ApplyScreenerAsync(Screener("شلرد", 0m, 1m, enticker: "IRO1FOLD0001"), Now, CancellationToken.None)).EntickerConflict);

        await store.UpsertLineDefinitionsAsync([new(44, "Sales", "فروش"), new(85, "Earning Per Share", "سود هر سهم")], Now, CancellationToken.None);
        var statements = Statements("فولاد", 834_166_799_000_000m);
        var applied = await store.ApplyIncomeStatementsAsync(statements, Now, CancellationToken.None);
        Assert.Equal((2, 0, 1), (applied.InsertedPeriods, applied.UpdatedPeriods, applied.DuplicatePeriodsInResponse));
        Assert.Equal(CyclicalWavesV2ApplyOutcome.Unchanged, (await store.ApplyIncomeStatementsAsync(statements, Now, CancellationToken.None)).Outcome);
        var restated = await store.ApplyIncomeStatementsAsync(Statements("فولاد", 900_000_000_000_000m), Now, CancellationToken.None);
        Assert.Equal((0, 1), (restated.InsertedPeriods, restated.UpdatedPeriods));
        Assert.Equal(2, await db.CyclicalWavesV2IncomeStatements.CountAsync());
        Assert.True(await db.CyclicalWavesV2IncomeStatementLineDefinitions.AnyAsync(row => row.Id == 6220));
        Assert.Null((await db.CyclicalWavesV2IncomeStatementValues.SingleAsync(row => row.LineDefinitionId == 86 &&
            db.CyclicalWavesV2IncomeStatements.Any(statement => statement.Id == row.StatementId && statement.FiscalMonth == 6))).TrailingYear);

        // Older periods absent from a response are never deleted.
        await store.ApplyIncomeStatementsAsync(new CyclicalWavesV2IncomeStatementSet("فولاد", null, 1, [statements.Statements[0]]), Now, CancellationToken.None);
        Assert.Equal(2, await db.CyclicalWavesV2IncomeStatements.CountAsync());

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            db.CyclicalWavesV2Tickers.Add(new CyclicalWavesV2TickerRow
            {
                Id = Guid.NewGuid(), Ticker = "فولاد", NormalizedTicker = "فولاد", IsActive = true,
                CreatedAt = Now, UpdatedAt = Now, LastSeenInCatalogAt = Now
            });
            await db.SaveChangesAsync();
        });
    }

    [SkippableFact]
    public async Task AnalysisStore_DeduplicatesAndRetrievalMatchesTickerByTitleAndTag()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var store = new EfCoreCyclicalWavesV2AnalysisStore(db, new HtmlAgilityPackTextStripper());
        CyclicalWavesV2AnalysisArticle[] page =
        [
            new(55301, "شتوکا- گزارش عملکرد مهر 1405", "<p>رشد <b>۷۶٪</b></p>", Now.AddHours(-2), [new(11, "نیما آزادی")], []),
            new(55037, "خودرو- لیدر فشل بازار", "متن", Now.AddHours(-3), [new(11, "نیما آزادی")], [new(1814, "گزارش_فصلی"), new(9, "شتوکا")]),
            new(55280, "خزامیا", null, Now.AddHours(-1), [], [])
        ];

        Assert.Equal(new CyclicalWavesV2AnalysisApplyResult(3, 0, 0), await store.ApplyAnalysesPageAsync(page, Now, CancellationToken.None));
        Assert.Equal(new CyclicalWavesV2AnalysisApplyResult(0, 0, 3), await store.ApplyAnalysesPageAsync(page, Now, CancellationToken.None));
        var retitled = page.ToArray();
        retitled[1] = retitled[1] with { Tags = [new(1814, "گزارش_فصلی")] };
        Assert.Equal(new CyclicalWavesV2AnalysisApplyResult(0, 1, 2), await store.ApplyAnalysesPageAsync(retitled, Now, CancellationToken.None));
        Assert.Equal(3, await db.CyclicalWavesV2Analyses.CountAsync());
        Assert.Equal(1, await db.CyclicalWavesV2AnalysisTags.CountAsync(tag => tag.AnalysisId == 55037));
        Assert.Equal("رشد ۷۶٪", (await db.CyclicalWavesV2Analyses.SingleAsync(row => row.Id == 55301)).PlainTextSummary?.Trim());
        Assert.Equal(Now.AddHours(-1), await store.GetMaxPublishedAtAsync(CancellationToken.None));

        var query = new CyclicalWavesV2ComprehensiveAnalysisQuery(db);
        var byTitle = await query.ExecuteAsync(new ComprehensiveAnalysisQueryRequest(["شتوکا"], [], null, 5), CancellationToken.None);
        Assert.Equal([55301L], byTitle.Items.Select(item => item.AnalysisId));
        Assert.Equal("نیما آزادی", byTitle.Items[0].AuthorName);
        var byTopic = await query.ExecuteAsync(new ComprehensiveAnalysisQueryRequest([], ["گزارش_فصلی"], null, 5), CancellationToken.None);
        Assert.Equal([55037L], byTopic.Items.Select(item => item.AnalysisId));
        var emptySummary = await query.ExecuteAsync(new ComprehensiveAnalysisQueryRequest(["خزامیا"], [], null, 5), CancellationToken.None);
        Assert.Equal(string.Empty, Assert.Single(emptySummary.Items).PlainTextSummary);
        var unknown = await query.ExecuteAsync(new ComprehensiveAnalysisQueryRequest(["ناشناس"], [], null, 5), CancellationToken.None);
        Assert.Equal(["ناشناس"], unknown.UnresolvedSymbols);
    }

    [SkippableFact]
    public async Task RunStore_TheRunningIndexAllowsExactlyOneConcurrentStart()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var request = new CyclicalWavesV2SyncRequest(CyclicalWavesV2SyncScope.Full, null, null, CyclicalWavesV2SyncTrigger.Scheduled, "system");

        var starts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            await using var db = database.CreateContext();
            return await new EfCoreCyclicalWavesV2SyncRunStore(db).TryStartAsync(request, $"owner-{index}", Now, Now.AddMinutes(30), CancellationToken.None);
        }));

        Assert.Single(starts, start => start is not null);
        await using var verify = database.CreateContext();
        Assert.Equal(1, await verify.CyclicalWavesV2SyncRuns.CountAsync(run => run.Status == "Running"));
        Assert.Equal(7, await verify.CyclicalWavesV2SyncRuns.CountAsync(run => run.Status == "SkippedAlreadyRunning"));

        var runs = new EfCoreCyclicalWavesV2SyncRunStore(verify);
        var queued = await runs.RequestAsync(request, Now, CancellationToken.None);
        Assert.False(queued.Accepted);
        Assert.Equal(1, await runs.ExpireStaleLeasesAsync(Now.AddHours(1), CancellationToken.None));
        Assert.True((await runs.RequestAsync(request, Now.AddHours(1), CancellationToken.None)).Accepted);
        var claimed = await runs.ClaimNextRequestedAsync("worker", Now.AddHours(1), Now.AddHours(2), CancellationToken.None);
        Assert.NotNull(claimed);
        await runs.CompleteAsync(claimed!.RunId, CyclicalWavesV2SyncRunStatus.Succeeded, new(1, 1, 1, 0, 0), null, Now.AddHours(1), CancellationToken.None);
        Assert.Equal(Now.AddHours(1), await runs.GetLastCompletedFullRunStartAsync(CancellationToken.None));
    }

    [SkippableFact]
    public async Task RunStore_AnnouncementRefreshesQueueBehindAnActiveRunAndWaitForTheirDueTime()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var runs = new EfCoreCyclicalWavesV2SyncRunStore(db);
        var full = new CyclicalWavesV2SyncRequest(CyclicalWavesV2SyncScope.Full, null, null, CyclicalWavesV2SyncTrigger.Scheduled, "system");
        var fullRunId = await runs.TryStartAsync(full, "worker", Now, Now.AddMinutes(30), CancellationToken.None);
        Assert.NotNull(fullRunId);

        // Accepted while a run holds the lease; RequestAsync (Admin) is refused in the same state.
        Assert.Equal(CyclicalWavesV2AnnouncementEnqueueResult.Queued,
            await runs.EnqueueAnnouncementRefreshAsync("فولاد", "codal:MonthlyReport:-1001:42", Now, null, 500, CancellationToken.None));
        Assert.Equal(CyclicalWavesV2AnnouncementEnqueueResult.Queued,
            await runs.EnqueueAnnouncementRefreshAsync("فولاد", "codal:MonthlyReport:-1001:42", Now, Now.AddMinutes(30), 500, CancellationToken.None));
        Assert.False((await runs.RequestAsync(full, Now, CancellationToken.None)).Accepted);

        // One pending run per ticker and kind; another ticker is not a duplicate.
        Assert.Equal(CyclicalWavesV2AnnouncementEnqueueResult.Duplicate,
            await runs.EnqueueAnnouncementRefreshAsync("فولاد", "codal:FinancialStatement:-1001:43", Now, null, 500, CancellationToken.None));
        Assert.Equal(CyclicalWavesV2AnnouncementEnqueueResult.Duplicate,
            await runs.EnqueueAnnouncementRefreshAsync("فولاد", "codal:FinancialStatement:-1001:43", Now, Now.AddMinutes(45), 500, CancellationToken.None));
        Assert.Equal(CyclicalWavesV2AnnouncementEnqueueResult.Queued,
            await runs.EnqueueAnnouncementRefreshAsync("فملی", "codal:FinancialStatement:-1001:44", Now, null, 500, CancellationToken.None));
        // The cap counts every pending announcement run.
        Assert.Equal(CyclicalWavesV2AnnouncementEnqueueResult.QueueFull,
            await runs.EnqueueAnnouncementRefreshAsync("شپنا", "codal:MonthlyReport:-1001:45", Now, null, 3, CancellationToken.None));
        Assert.Equal(3, await db.CyclicalWavesV2SyncRuns.CountAsync(run => run.Status == "Requested" && run.TriggerSource == "CodalAnnouncement"));

        // Nothing is claimed while the Full run is running.
        Assert.Null(await runs.ClaimNextRequestedAsync("worker", Now.AddMinutes(1), Now.AddMinutes(31), CancellationToken.None));
        await runs.CompleteAsync(fullRunId!.Value, CyclicalWavesV2SyncRunStatus.Succeeded, new(1, 1, 1, 0, 0), null, Now.AddMinutes(19), CancellationToken.None);

        // The two immediate runs, oldest first; the deferred one stays queued until its due time.
        foreach (var expectedTicker in new[] { "فولاد", "فملی" })
        {
            var claimed = await runs.ClaimNextRequestedAsync("worker", Now.AddMinutes(20), Now.AddMinutes(50), CancellationToken.None);
            Assert.NotNull(claimed);
            Assert.Equal(expectedTicker, claimed!.Request.Ticker);
            Assert.Equal(CyclicalWavesV2SyncScope.TickerGaugeEquilibrium, claimed.Request.Scope);
            Assert.Equal(CyclicalWavesV2SyncTrigger.CodalAnnouncement, claimed.Request.Trigger);
            await runs.CompleteAsync(claimed.RunId, CyclicalWavesV2SyncRunStatus.Succeeded, new(2, 2, 2, 0, 0), null, Now.AddMinutes(20), CancellationToken.None);
        }

        Assert.Null(await runs.ClaimNextRequestedAsync("worker", Now.AddMinutes(29), Now.AddMinutes(59), CancellationToken.None));
        // A run that is not yet due does not block an Admin request, and that request is claimed first.
        var screener = new CyclicalWavesV2SyncRequest(CyclicalWavesV2SyncScope.Screener, null, null, CyclicalWavesV2SyncTrigger.Admin, "User:1");
        Assert.True((await runs.RequestAsync(screener, Now.AddMinutes(29), CancellationToken.None)).Accepted);
        var admin = await runs.ClaimNextRequestedAsync("worker", Now.AddMinutes(29), Now.AddMinutes(59), CancellationToken.None);
        Assert.Equal(CyclicalWavesV2SyncScope.Screener, admin!.Request.Scope);
        await runs.CompleteAsync(admin.RunId, CyclicalWavesV2SyncRunStatus.Succeeded, new(1, 1, 1, 0, 0), null, Now.AddMinutes(29), CancellationToken.None);

        var deferred = await runs.ClaimNextRequestedAsync("worker", Now.AddMinutes(30), Now.AddMinutes(60), CancellationToken.None);
        Assert.Equal("فولاد", deferred!.Request.Ticker);
        var history = await runs.GetRecentRunsAsync(20, CancellationToken.None);
        Assert.Equal(Now.AddMinutes(30), history.Single(run => run.RunId == deferred.RunId).NotBeforeUtc);
        Assert.Null(history.Single(run => run.RunId == admin.RunId).NotBeforeUtc);
    }

    [SkippableFact]
    public async Task Migration_AddsOnlyTheRunDueTimeAndRollsBackCleanly()
    {
        const string previousMigration = "20261128095025_Feature167OptionMarket";
        const string columnCount = "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'cyclicalwaves' AND table_name = 'SyncRuns'";
        const string dueTimeColumn = columnCount + " AND column_name = 'NotBeforeUtc' AND is_nullable = 'YES' AND data_type = 'timestamp with time zone'";
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        var withColumn = await CountAsync(db, columnCount);
        Assert.Equal(1, await CountAsync(db, dueTimeColumn));

        // A run stored before the migration reads as due at once after it.
        await migrator.MigrateAsync(previousMigration);
        Assert.Equal(withColumn - 1, await CountAsync(db, columnCount));
        Assert.Equal(0, await CountAsync(db, dueTimeColumn));
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO cyclicalwaves.\"SyncRuns\" (\"Id\", \"Scope\", \"TriggerSource\", \"Status\", \"RequestedAt\", \"TotalItems\", \"ProcessedItems\", \"UpdatedItems\", \"UnchangedItems\", \"FailedItems\") " +
            "VALUES ('11111111-1111-1111-1111-111111111111', 'Screener', 'Admin', 'Requested', now(), 0, 0, 0, 0, 0)");

        await migrator.MigrateAsync();
        Assert.Equal(withColumn, await CountAsync(db, columnCount));
        db.ChangeTracker.Clear();
        var claimed = await new EfCoreCyclicalWavesV2SyncRunStore(db)
            .ClaimNextRequestedAsync("worker", DateTimeOffset.UtcNow.AddMinutes(1), DateTimeOffset.UtcNow.AddMinutes(31), CancellationToken.None);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), claimed!.RunId);
    }

    [SkippableFact]
    public async Task FullRunThroughTheCoordinator_TouchesOnlyTheCyclicalWavesSchema()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var publicRowsBefore = await PublicRowCountAsync(db);
        var coordinator = new CyclicalWavesV2SyncCoordinator(
            new FixtureClient(),
            new EfCoreCyclicalWavesV2Store(db, NullLogger<EfCoreCyclicalWavesV2Store>.Instance),
            new EfCoreCyclicalWavesV2AnalysisStore(db, new HtmlAgilityPackTextStripper()),
            new EfCoreCyclicalWavesV2SyncRunStore(db),
            new CyclicalWavesV2SyncSettings(12, 100, 2, TimeSpan.Zero, TimeSpan.FromMinutes(30)),
            TimeProvider.System,
            new FixtureGaugeClient(),
            new EfCoreCyclicalWavesV2GaugeStore(db));

        var outcome = await coordinator.RunAsync(
            new CyclicalWavesV2SyncRequest(CyclicalWavesV2SyncScope.Full, null, null, CyclicalWavesV2SyncTrigger.Admin, "test"),
            "test",
            CancellationToken.None);

        Assert.Equal(CyclicalWavesV2SyncRunStatus.Succeeded, outcome.Status);
        Assert.Equal(2, await db.CyclicalWavesV2Tickers.CountAsync());
        Assert.Equal(58, await db.CyclicalWavesV2FinancialValues.CountAsync());
        Assert.Equal(1, await db.CyclicalWavesV2Analyses.CountAsync());
        Assert.Equal(publicRowsBefore, await PublicRowCountAsync(db));

        var repeated = await coordinator.RunAsync(
            new CyclicalWavesV2SyncRequest(CyclicalWavesV2SyncScope.Full, null, null, CyclicalWavesV2SyncTrigger.Admin, "test-repeat"),
            "test-repeat",
            CancellationToken.None);
        Assert.Equal(CyclicalWavesV2SyncRunStatus.Succeeded, repeated.Status);
        Assert.Equal(2, await db.CyclicalWavesV2Tickers.CountAsync());
        Assert.Equal(58, await db.CyclicalWavesV2FinancialValues.CountAsync());
        Assert.Equal(1, await db.CyclicalWavesV2Analyses.CountAsync());
        Assert.Equal(4, await db.CyclicalWavesV2GaugeDetails.CountAsync());
        Assert.Equal(2, await db.CyclicalWavesV2EquilibriumRanges.CountAsync());
        Assert.Equal(publicRowsBefore, await PublicRowCountAsync(db));
    }

    private static async Task<long> PublicRowCountAsync(FinancialIngestionDbContext db) =>
        await CountAsync(db, """SELECT (SELECT count(*) FROM "DerivedMetrics") + (SELECT count(*) FROM "Companies") + (SELECT count(*) FROM "MonthlyReports") + (SELECT count(*) FROM "FinancialStatements")""");

    private static async Task<long> CountAsync(FinancialIngestionDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static CyclicalWavesV2ScreenerSnapshot Screener(string ticker, decimal pe, decimal ps, string enticker = "IRO1FOLD0001")
    {
        var values = FinancialCopilot.Domain.Financial.CyclicalWavesV2.ScreenerMetricCatalog.All
            .ToDictionary(definition => definition.FieldKey, definition => (decimal?)definition.Id * 1_000_000m);
        values["pe"] = pe;
        values["ps"] = ps;
        values["lastQuarterNetProfit"] = null;
        return new CyclicalWavesV2ScreenerSnapshot(ticker, enticker, values, new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 22), Now);
    }

    private static CyclicalWavesV2IncomeStatementSet Statements(string ticker, decimal latestSales) =>
        new(ticker, "IRO1FOLD0001", 52,
        [
            new(new DateOnly(2026, 9, 22), 2026, 6, false, Now.AddDays(-9), "https://codal.ir/a",
            [
                new(44, "Sales", "فروش", latestSales, 2_509_422_184_000_000m, 4_569_548_192_000_000m),
                new(85, "Earning Per Share", "سود هر سهم", 243.55m, 306m, 720.99m),
                new(86, "Registered Capital", "سرمایه ثبت‌شده", 1_935_000_000_000_000m, 1_935_000_000_000_000m, null),
                new(6220, "Adjusted EPS", "سود هر سهم تعدیل‌شده", 243.55m, 305.75m, 692.88m)
            ]),
            // The same period published earlier: the later publication must win.
            new(new DateOnly(2026, 9, 22), 2026, 6, false, Now.AddDays(-20), "https://codal.ir/old",
                [new(44, "Sales", "فروش", 1m, 1m, 1m)]),
            new(new DateOnly(2026, 6, 21), 2026, 3, false, Now.AddDays(-90), "https://codal.ir/b",
                [new(44, "Sales", "فروش", 834_166_799_000_000m, 834_166_799_000_000m, 3_655_311_328_000_000m)])
        ]);

    private sealed class FixtureClient : ICyclicalWavesV2Client
    {
        public Task<IReadOnlyList<string>> GetTickersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["فولاد", "وبملت"]);

        public Task<CyclicalWavesV2ScreenerSnapshot?> GetScreenerAsync(string ticker, CancellationToken cancellationToken) =>
            Task.FromResult<CyclicalWavesV2ScreenerSnapshot?>(Screener(ticker, 5m, 1m, ticker == "فولاد" ? "IRO1FOLD0001" : "IRO1BMLT0001"));

        public Task<CyclicalWavesV2IncomeStatementSet?> GetIncomeStatementsAsync(string ticker, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<CyclicalWavesV2IncomeStatementSet?>(ticker == "فولاد" ? Statements(ticker, 1m) : null);

        public Task<IReadOnlyList<CyclicalWavesV2LineDefinition>> GetIncomeStatementLinesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CyclicalWavesV2LineDefinition>>([new(44, "Sales", "فروش")]);

        public Task<CyclicalWavesV2AnalysisPage> GetAnalysesPageAsync(int page, int pageSize, DateOnly? fromDate, CancellationToken cancellationToken) =>
            Task.FromResult(new CyclicalWavesV2AnalysisPage(
                [new CyclicalWavesV2AnalysisArticle(1, "فولاد- تحلیل", "خلاصه", Now, [], [])], 1, 1, 1));
    }

    private sealed class FixtureGaugeClient : ICyclicalWavesV2GaugeClient
    {
        private static readonly (string Ticker, string Enticker)[] Tickers =
        [
            ("ÙÙˆÙ„Ø§Ø¯", "IRO1FOLD0001"),
            ("ÙˆØ¨Ù…Ù„Øª", "IRO1BMLT0001")
        ];

        public Task<IReadOnlyList<CyclicalWavesV2GaugeCatalogItem>> GetGaugeCatalogAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CyclicalWavesV2GaugeCatalogItem>>(
                Tickers.Select(item => new CyclicalWavesV2GaugeCatalogItem(item.Ticker, item.Enticker, true, true)).ToArray());

        public Task<CyclicalWavesV2GaugeDetails?> GetGaugeDetailsAsync(string identifier, CancellationToken cancellationToken)
        {
            var ticker = Tickers.Single(item => item.Enticker == identifier);
            var gauge = new CyclicalWavesV2Gauge(
                1, 2, 3, 4, 5, 6, ExactDecimal.Parse("5.123456789012345678901234567890123"),
                ExactDecimal.Parse("10"), ExactDecimal.Parse("1"), ExactDecimal.Parse("2"),
                ExactDecimal.Parse("4"), ExactDecimal.Parse("8"), 2);
            return Task.FromResult<CyclicalWavesV2GaugeDetails?>(
                new CyclicalWavesV2GaugeDetails(ticker.Ticker, identifier, gauge, gauge));
        }

        public Task<IReadOnlyList<CyclicalWavesV2EquilibriumCatalogItem>> GetEquilibriumCatalogAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CyclicalWavesV2EquilibriumCatalogItem>>(
                Tickers.Select(item => new CyclicalWavesV2EquilibriumCatalogItem(item.Ticker, item.Enticker)).ToArray());

        public Task<CyclicalWavesV2EquilibriumRange?> GetEquilibriumRangeAsync(string identifier, CancellationToken cancellationToken) =>
            Task.FromResult<CyclicalWavesV2EquilibriumRange?>(new(
                ExactDecimal.Parse("-0.0000000000000000000000000000000000000000000000000000000000123"),
                ExactDecimal.Parse("-7232.1700000000000000002"),
                ExactDecimal.Parse("123456789012345678901234567890.123456789012345678901234567890")));
    }
}
