using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.StockMarketDb;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class StockMarketDbSyncServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-01T12:40:00Z");
    private static readonly Guid InstrumentRef = Guid.Parse("935dc0fc-405d-4efd-ba38-af34f67d77c0");

    [Fact]
    public async Task Instruments_DoesNotWriteToTradingInstrumentsTable()
    {
        // spec 064: TradingInstruments is owned exclusively by TsetmcDirectFeedSyncService.
        // StockMarketDbSyncService must not insert or update instrument rows.
        await using var db = CreateDb();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = "CodalDb", ExternalCompanyId = "1",
            Name = "Company", InstrumentCode = "123", LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();
        var executor = new FakeExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.Instruments, true, CancellationToken.None);

        Assert.Equal(0, await db.TradingInstruments.CountAsync());
    }

    [Fact]
    public async Task Instruments_SyncCompletesWithoutErrorWhenInstrumentCodeMatchesMultipleCompanies()
    {
        // spec 064: StockMarketDb instruments sync is now a no-op for TradingInstruments.
        // It should still complete successfully even when duplicate company rows exist.
        await using var db = CreateDb();
        db.Companies.AddRange(
            new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranArchiveSql", ExternalCompanyId = "A",
                Name = "Archive", InstrumentCode = "123", LastSynchronizedAt = Now.AddYears(-1)
            },
            new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "C",
                Name = "Current", InstrumentCode = "123", LastSynchronizedAt = Now
            });
        await db.SaveChangesAsync();
        var executor = new FakeExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };

        // Must not throw; TradingInstruments remains empty.
        await Service(db, executor).SynchronizeAsync(StockMarketDataset.Instruments, true, CancellationToken.None);

        Assert.Equal(0, await db.TradingInstruments.CountAsync());
    }

    [Fact]
    public async Task Instruments_PersistCanonicalRawPayloadChecksum()
    {
        await using var db = CreateDb();
        var store = new FakeRawPayloadStore();
        var executor = new FakeExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };
        var service = new StockMarketDbSyncService(
            db, executor, store,
            Options.Create(new StockMarketDbProviderOptions { PageSize = 100 }),
            new NoOpScannerCache(), new NoOpMarketViewCache(), new FixedTimeProvider(Now));

        await service.SynchronizeAsync(StockMarketDataset.Instruments, true, CancellationToken.None);

        var payload = Assert.Single(store.Payloads);
        Assert.Equal(ProviderDataset.TradingInstruments, payload.Dataset);
        Assert.Equal(64, payload.Checksum.Length);
    }

    [Fact]
    public async Task Instruments_SyncRecordsRowsReadForWatermarkEvenWithoutInstrumentWrite()
    {
        // spec 064: StockMarketDb instruments sync still fetches and counts records for watermark
        // continuation, even though it no longer writes to TradingInstruments.
        await using var db = CreateDb();
        var executor = new FakeExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };

        var result = await Service(db, executor).SynchronizeAsync(StockMarketDataset.Instruments, true, CancellationToken.None);

        Assert.Equal(1, result.RowsRead);
        Assert.Equal(0, await db.TradingInstruments.CountAsync());
    }

    [Fact]
    public async Task Instruments_FullPageContinuesFromTimestampAndSourceId()
    {
        await using var db = CreateDb();
        var executor = new FakeExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };
        var service = new StockMarketDbSyncService(
            db, executor, new FakeRawPayloadStore(),
            Options.Create(new StockMarketDbProviderOptions { PageSize = 1, OverlapMinutes = 10 }),
            new NoOpScannerCache(), new NoOpMarketViewCache(), new FixedTimeProvider(Now));

        await service.SynchronizeAsync(StockMarketDataset.Instruments, false, CancellationToken.None);
        await service.SynchronizeAsync(StockMarketDataset.Instruments, false, CancellationToken.None);

        Assert.Equal(new StockMarketPageCursor(Now, LastGuidId: InstrumentRef), executor.InstrumentCursors[1]);
    }

    [Fact]
    public async Task IntradayTrade_UpdatesLatestQuoteAndPersistedProviderReadsIt()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.IntradayTrades, true, CancellationToken.None);
        var result = await Provider(db).GetLatestQuotesAsync([new SymbolCode("SYM")], CancellationToken.None);

        var quote = Assert.Single(result.Observations);
        Assert.Equal(110, quote.LatestPrice);
        Assert.Equal(10, quote.PriceChangePercentage);
        Assert.Equal(MarketQuoteSource.LiveQuote, quote.Source);
    }

    [Fact]
    public async Task PersistedQuote_ReachableByInstrumentTickerWhenCompanyLinkageIsMissing()
    {
        await using var db = CreateDb();
        // Instrument without a normalized company link (the common case for funds, rights, and
        // listings whose company row comes from another provider): the quote must still resolve
        // through the instrument's own TSE ticker.
        await SeedInstrumentAsync(db, companyId: null);
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.IntradayTrades, true, CancellationToken.None);
        var result = await Provider(db).GetLatestQuotesAsync([new SymbolCode("SYM")], CancellationToken.None);

        var quote = Assert.Single(result.Observations);
        Assert.Equal(110, quote.LatestPrice);
        Assert.Equal(MarketQuoteSource.LiveQuote, quote.Source);
        Assert.Empty(result.UnavailableSymbols);
    }

    [Fact]
    public async Task PersistedQuote_IntradayFromPreviousSessionIsNotReportedLive()
    {
        await using var db = CreateDb();
        await SeedInstrumentAsync(db, companyId: null);
        var instrumentId = await db.TradingInstruments.Select(row => row.Id).SingleAsync();
        db.LatestMarketQuotes.Add(new LatestMarketQuoteRow
        {
            Id = Guid.NewGuid(), ProviderName = "StockMarketDb", TradingInstrumentId = instrumentId,
            LatestPrice = 95, PriceChangePercentage = -1.5m, SourceKind = "Intraday",
            TradingDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-1), AsOf = Now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var result = await Provider(db).GetLatestQuotesAsync([new SymbolCode("SYM")], CancellationToken.None);

        var quote = Assert.Single(result.Observations);
        Assert.Equal(MarketQuoteSource.PreviousTradingDay, quote.Source);
    }

    [Fact]
    public async Task Provider_ResolvesEligibleCompanyToTradingInstrument_AndUsesLatestDailyFallback()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var instrumentId = Guid.NewGuid();

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
            ExternalCompanyId = "3",
            Name = "گل گهر",
            Ticker = "کگل",
            TseSymbol = "کگل",
            CompanySymbol = "کگل",
            InstrumentCode = "456",
            PrecedencyRight = 0,
            MarketId = NoavaranCompanyScope.BourseMarketId,
            LastSynchronizedAt = Now
        });
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId,
            ProviderName = "StockMarketDb",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 456,
            Symbol = "KGOL-TICKER",
            Name = "KGOL",
            IsActive = true,
            SourceChangedAt = Now,
            LastSynchronizedAt = Now
        });
        db.DailyInstrumentTrades.Add(new DailyInstrumentTradeRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "StockMarketDb",
            ExternalTradeId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId,
            TradingDate = new DateOnly(2026, 5, 31),
            ClosingPrice = 2025m,
            LastTradedPrice = 2110m,
            PriceChange = 110m,
            PriceYesterday = 2000m,
            SourceInsertedAt = Now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var result = await Provider(db).GetLatestQuotesAsync([new SymbolCode("کگل")], CancellationToken.None);

        var quote = Assert.Single(result.Observations);
        Assert.Equal(2110m, quote.LatestPrice);
        // DAILY_CHANGE_PCT = (LastTradedPrice / PriceYesterday - 1) * 100 = (2110/2000 - 1) * 100 = 5.5
        Assert.Equal(5.5m, quote.PriceChangePercentage);
        Assert.Equal(new DateOnly(2026, 5, 31), quote.TradingDate);
        Assert.Equal(MarketQuoteSource.PreviousTradingDay, quote.Source);
        Assert.Equal("LatestDailyFallback", quote.SourceLabel);
    }

    [Fact]
    public async Task Provider_PrefersTodayIntradayOverLatestDailyFallback()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var instrumentId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
            ExternalCompanyId = "4",
            Name = "چادرملو",
            Ticker = "کچاد",
            TseSymbol = "کچاد",
            CompanySymbol = "کچاد",
            InstrumentCode = "789",
            PrecedencyRight = 0,
            MarketId = NoavaranCompanyScope.BourseMarketId,
            LastSynchronizedAt = Now
        });
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId,
            ProviderName = "StockMarketDb",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 789,
            Symbol = "KCHAD-TICKER",
            Name = "KCHAD",
            IsActive = true,
            SourceChangedAt = Now,
            LastSynchronizedAt = Now
        });
        db.DailyInstrumentTrades.Add(new DailyInstrumentTradeRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "StockMarketDb",
            ExternalTradeId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId,
            TradingDate = today.AddDays(-1),
            ClosingPrice = 1000m,
            LastTradedPrice = 1010m,
            PriceChange = 10m,
            PriceYesterday = 990m,
            SourceInsertedAt = Now.AddDays(-1)
        });
        db.IntradayTradeSnapshots.Add(new IntradayTradeSnapshotRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "StockMarketDb",
            ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId,
            TradingDate = today,
            TradingTime = new TimeOnly(12, 30),
            ClosingPrice = 10050m,
            LastTradedPrice = 10115m,
            PriceChange = 115m,
            PriceYesterday = 10000m,
            ReceivedAt = Now
        });
        await db.SaveChangesAsync();

        var result = await Provider(db).GetLatestQuotesAsync([new SymbolCode("کچاد")], CancellationToken.None);

        var quote = Assert.Single(result.Observations);
        Assert.Equal(10115m, quote.LatestPrice);
        // DAILY_CHANGE_PCT = (LastTradedPrice / PriceYesterday - 1) * 100 = (10115/10000 - 1) * 100 = 1.15
        Assert.Equal(1.15m, quote.PriceChangePercentage);
        Assert.Equal(today, quote.TradingDate);
        Assert.Equal(MarketQuoteSource.LiveQuote, quote.Source);
        Assert.Equal("IntradayToday", quote.SourceLabel);
    }

    [Fact]
    public async Task IntradayTrade_MultipleSnapshotsInPageUpdateSingleLatestQuote()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now.AddMinutes(-1)),
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 6, 120, 1200,
                    120, 20, 120, 20, 100, 120, 100, 100, new TimeOnly(12, 31), Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.IntradayTrades, true, CancellationToken.None);

        Assert.Equal(2, await db.IntradayTradeSnapshots.CountAsync());
        Assert.Equal(120, (await db.LatestMarketQuotes.SingleAsync()).LatestPrice);
    }

    [Fact]
    public async Task IntradayTrade_DuplicateSourceIdInSamePageUpsertsSingleRow()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var snapshotId = Guid.NewGuid();
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(snapshotId, InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now.AddMinutes(-1)),
                new(snapshotId, InstrumentRef, new DateOnly(2026, 6, 1), 6, 120, 1200,
                    125, 25, 125, 25, 100, 125, 100, 100, new TimeOnly(12, 31), Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.IntradayTrades, true, CancellationToken.None);

        var row = Assert.Single(await db.IntradayTradeSnapshots.ToListAsync());
        Assert.Equal(125, row.LastTradedPrice);
    }

    [Fact]
    public async Task OlderDailyTrade_DoesNotOverwriteNewerIntradayQuote()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ],
            DailyTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 5, 31), 90, 90, 5, 100, 1000,
                    -10, 90, 100, 100, 100, 5000, Now.AddDays(-1))
            ]
        };
        var service = Service(db, executor);

        await service.SynchronizeAsync(StockMarketDataset.IntradayTrades, true, CancellationToken.None);
        await service.SynchronizeAsync(StockMarketDataset.DailyTrades, true, CancellationToken.None);

        Assert.Equal(110, (await db.LatestMarketQuotes.SingleAsync()).LatestPrice);
    }

    [Fact]
    public async Task IntradayIndex_LastObservationBecomesDailyClose()
    {
        await using var db = CreateDb();
        await SeedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            IntradayIndices =
            [
                new(Guid.NewGuid(), InstrumentRef, 123, new DateOnly(2026, 6, 1), new TimeOnly(12, 30), 2500, 1.25m, Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.IntradayIndices, true, CancellationToken.None);

        var daily = await db.DailyIndexSnapshots.SingleAsync();
        Assert.Equal(2500, daily.Value);
        Assert.Equal("IntradayClose", daily.SourceKind);
    }

    [Fact]
    public async Task HistoricalDailyIndex_PersistsNamedIndexCloseForTradingDay()
    {
        await using var db = CreateDb();
        await SeedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            HistoricalIndices =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1),
                    Value: 2_100_000, High: 2_150_000, Low: 2_050_000, ChangePercent: 0.75m, ChangeTime: Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(
            StockMarketDataset.HistoricalDailyIndices, true, CancellationToken.None);

        var daily = await db.DailyIndexSnapshots.SingleAsync();
        Assert.Equal(2_100_000, daily.Value);
        Assert.Equal(new DateOnly(2026, 6, 1), daily.TradingDate);
        Assert.Equal("HistoricalBackfill", daily.SourceKind);
    }

    [Fact]
    public void NamedIndexCatalog_ExposesTheSixDailyIndexInstrumentRefs()
    {
        var expected = new[]
        {
            Guid.Parse("36423CB8-D33B-47AD-89D4-06FA49592CBA"), // شاخص کل
            Guid.Parse("1B32B991-F48A-4F7E-9C0C-328D0B093EA5"), // شاخص کل فرابورس
            Guid.Parse("B27FA320-194F-4710-8D12-277E245D33C5"), // شاخص بازده نقدی و قیمت
            Guid.Parse("47CE7543-C052-4C44-BF0D-29281818FCA5"), // شاخص ۵۰ شرکت فعال‌تر
            Guid.Parse("42FCE63E-6CEB-405B-9179-78606C210D86"), // شاخص قیمت (هم‌وزن)
            Guid.Parse("D01F9D84-A1C8-46F3-A959-800DEF9E112F"), // شاخص کل (هم‌وزن)
        };

        Assert.Equal(expected, StockMarketNamedIndices.InstrumentRefs);
        Assert.Equal(expected.Length, StockMarketNamedIndices.InstrumentRefs.Distinct().Count());
        Assert.All(StockMarketNamedIndices.All, index => Assert.False(string.IsNullOrWhiteSpace(index.PersianName)));
    }

    [Fact]
    public async Task IntradayTrade_RepeatedOverlapReadIsIdempotentAndInvalidatesCache()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var snapshotId = Guid.NewGuid();
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(snapshotId, InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ]
        };
        var cache = new TrackingScannerCache();
        var service = Service(db, executor, cache);

        await service.SynchronizeAsync(StockMarketDataset.IntradayTrades, false, CancellationToken.None);
        await service.SynchronizeAsync(StockMarketDataset.IntradayTrades, false, CancellationToken.None);

        Assert.Single(await db.IntradayTradeSnapshots.ToListAsync());
        Assert.Single(await db.LatestMarketQuotes.ToListAsync());
        Assert.Equal([null, Now.AddMinutes(-10)], executor.IntradayTradeCursors.Select(item => item.After));
        Assert.Equal(2, cache.Invalidations.Count);
        Assert.All(cache.Invalidations, item => Assert.Equal("StockMarketDb.IntradayTrades", item.Reason));
    }

    [Fact]
    public async Task IntradayTrade_InvalidatesMarketViewCache()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ]
        };
        var marketCache = new TrackingMarketViewCache();

        await Service(db, executor, marketViewCache: marketCache)
            .SynchronizeAsync(StockMarketDataset.IntradayTrades, false, CancellationToken.None);

        Assert.Equal(1, marketCache.InvalidationCount);
    }

    [Fact]
    public async Task IntradayTrade_UnresolvedInstrumentDoesNotAdvanceWatermark()
    {
        await using var db = CreateDb();
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ]
        };

        var exception = await Assert.ThrowsAsync<StockMarketUnresolvedInstrumentException>(
            () => Service(db, executor).SynchronizeAsync(
                StockMarketDataset.IntradayTrades, false, CancellationToken.None));

        Assert.Equal(StockMarketDataset.IntradayTrades, exception.Dataset);
        Assert.Equal(1, exception.UnresolvedCount);
        Assert.Null((await db.StockMarketSyncStates.SingleAsync()).Watermark);
    }

    [Fact]
    public async Task Retention_DeletesOnlyExpiredIntradayHistory()
    {
        await using var db = CreateDb();
        await SeedInstrumentAsync(db);
        var instrumentId = await db.TradingInstruments.Select(row => row.Id).SingleAsync();
        db.IntradayTradeSnapshots.AddRange(
            IntradayTrade(instrumentId, Now.AddDays(-31), new DateOnly(2026, 5, 1)),
            IntradayTrade(instrumentId, Now.AddDays(-1), new DateOnly(2026, 5, 31)));
        db.IntradayIndexSnapshots.AddRange(
            IntradayIndex(instrumentId, Now.AddDays(-31), new DateOnly(2026, 5, 1)),
            IntradayIndex(instrumentId, Now.AddDays(-1), new DateOnly(2026, 5, 31)));
        await db.SaveChangesAsync();
        var service = new StockMarketHistoryRetentionService(
            db,
            Options.Create(new StockMarketDbProviderOptions
            {
                RetainIntradayTradeDays = 30,
                RetainIntradayIndexDays = 30
            }),
            new FixedTimeProvider(Now));

        var result = await service.DeleteExpiredAsync(CancellationToken.None);

        Assert.Equal(1, result.IntradayTradesDeleted);
        Assert.Equal(1, result.IntradayIndicesDeleted);
        Assert.Single(await db.IntradayTradeSnapshots.ToListAsync());
        Assert.Single(await db.IntradayIndexSnapshots.ToListAsync());
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Regression: provider-name mismatch must not suppress quote resolution
    // Spec 030 §17, §21: ProviderName is provenance metadata only; runtime
    // PrimarySourceName must not be used as a WHERE filter on canonical tables.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProviderNameMismatch_DailyInstrumentTrades_StillResolvesQuote()
    {
        // Arrange: API runtime config says StockMarketDb; row stored under TsetmcWebService.
        await using var db = CreateDb();
        var instrumentId = Guid.Parse("92990a92-e853-47e3-a682-bb8794b22999");

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
            ExternalCompanyId = "shgol",
            Name = "شگل",
            Ticker = "شگل",
            TseSymbol = "شگل",
            CompanySymbol = "شگل",
            InstrumentCode = "999",
            PrecedencyRight = 0,
            MarketId = NoavaranCompanyScope.BourseMarketId,
            LastSynchronizedAt = Now
        });
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId,
            ProviderName = "TsetmcWebService",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 999,
            Symbol = "SHGOL-TICKER",
            Name = "شگل",
            IsActive = true,
            SourceChangedAt = Now,
            LastSynchronizedAt = Now
        });
        // Row has ProviderName=TsetmcWebService, API config has PrimarySourceName=StockMarketDb.
        // PriceYesterday = 3820, LastTradedPrice = 3934 → (3934/3820 - 1)*100 ≈ 2.9843...
        db.DailyInstrumentTrades.Add(new DailyInstrumentTradeRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            ExternalTradeId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId,
            TradingDate = new DateOnly(2026, 6, 18),
            ClosingPrice = 3930m,
            LastTradedPrice = 3934m,
            PriceChange = 114m,
            PriceYesterday = 3820m,
            SourceInsertedAt = Now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        // Act: provider configured with StockMarketDb; data has TsetmcWebService.
        var provider = new PersistedMarketDataProvider(
            db,
            //new FixedMarketQuoteSourcePriority(ProviderSources.StockMarketDbName),
            new FixedTimeProvider(Now));
        var result = await provider.GetLatestQuotesAsync([new SymbolCode("شگل")], CancellationToken.None);

        // Assert: quote must be found regardless of ProviderName mismatch.
        var quote = Assert.Single(result.Observations);
        Assert.Equal(3934m, quote.LatestPrice);
        Assert.Empty(result.UnavailableSymbols);
        Assert.Equal(MarketQuoteSource.PreviousTradingDay, quote.Source);
        Assert.Equal("LatestDailyFallback", quote.SourceLabel);
    }

    [Fact]
    public async Task ProviderNameMismatch_DailyChangePercentage_UsesLastTradedPriceNotClosingPrice()
    {
        // Spec 030 §12, §18: DAILY_CHANGE_PCT = (LastTradedPrice/PriceYesterday - 1)*100.
        // ClosingPrice must not be used. Raw value 2.9842... must round to 2.98 (two decimals).
        await using var db = CreateDb();
        var instrumentId = Guid.NewGuid();

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
            ExternalCompanyId = "shgol2",
            Name = "شگل",
            Ticker = "شگل2",
            TseSymbol = "شگل2",
            CompanySymbol = "شگل2",
            InstrumentCode = "888",
            PrecedencyRight = 0,
            MarketId = NoavaranCompanyScope.BourseMarketId,
            LastSynchronizedAt = Now
        });
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId,
            ProviderName = "TsetmcWebService",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 888,
            Symbol = "SHGOL2-TICKER",
            Name = "شگل2",
            IsActive = true,
            SourceChangedAt = Now,
            LastSynchronizedAt = Now
        });
        db.DailyInstrumentTrades.Add(new DailyInstrumentTradeRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            ExternalTradeId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId,
            TradingDate = new DateOnly(2026, 6, 18),
            ClosingPrice = 3820m,       // deliberately different from LastTradedPrice
            LastTradedPrice = 3934m,    // canonical latest price
            PriceChange = 114m,
            PriceYesterday = 3820m,     // (3934/3820 - 1)*100 = 2.9843…
            SourceInsertedAt = Now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var result = await new PersistedMarketDataProvider(
                db,
                //new FixedMarketQuoteSourcePriority(ProviderSources.StockMarketDbName),
                new FixedTimeProvider(Now))
            .GetLatestQuotesAsync([new SymbolCode("شگل2")], CancellationToken.None);

        var quote = Assert.Single(result.Observations);
        // Raw = (3934/3820 - 1)*100 = 2.9842931937172800…
        // Two-decimal user display: 2.98 (not 0.00 which would result from ClosingPrice=PriceYesterday).
        var rawPct = quote.PriceChangePercentage;
        Assert.True(rawPct > 2.98m && rawPct < 2.99m,
            $"Expected ~2.98 but got {rawPct}; ClosingPrice must not be used for DAILY_CHANGE_PCT");
        // Formatted to two decimal places:
        Assert.Equal("2.98", rawPct.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ProviderNameMismatch_IntradayTradeSnapshots_StillResolvesLiveQuote()
    {
        // Spec 030 §17: IntradayTradeSnapshots must not filter by ProviderName.
        await using var db = CreateDb();
        var instrumentId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
            ExternalCompanyId = "kchad2",
            Name = "کچاد2",
            Ticker = "کچاد2",
            TseSymbol = "کچاد2",
            CompanySymbol = "کچاد2",
            InstrumentCode = "777",
            PrecedencyRight = 0,
            MarketId = NoavaranCompanyScope.BourseMarketId,
            LastSynchronizedAt = Now
        });
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId,
            ProviderName = "TsetmcWebService",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 777,
            Symbol = "KCHAD2-TICKER",
            Name = "کچاد2",
            IsActive = true,
            SourceChangedAt = Now,
            LastSynchronizedAt = Now
        });
        db.IntradayTradeSnapshots.Add(new IntradayTradeSnapshotRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",   // mismatch: API says StockMarketDb
            ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId,
            TradingDate = today,
            TradingTime = new TimeOnly(10, 0),
            ClosingPrice = 5000m,
            LastTradedPrice = 5050m,
            PriceChange = 50m,
            PriceYesterday = 5000m,
            ReceivedAt = Now
        });
        await db.SaveChangesAsync();

        var result = await new PersistedMarketDataProvider(
                db,
                //new FixedMarketQuoteSourcePriority(ProviderSources.StockMarketDbName),
                new FixedTimeProvider(Now))
            .GetLatestQuotesAsync([new SymbolCode("کچاد2")], CancellationToken.None);

        var quote = Assert.Single(result.Observations);
        Assert.Equal(5050m, quote.LatestPrice);
        Assert.Equal(MarketQuoteSource.LiveQuote, quote.Source);
        Assert.Equal("IntradayToday", quote.SourceLabel);
        Assert.Empty(result.UnavailableSymbols);
    }

    [Fact]
    public async Task FreshnessLabel_DailyFallback_IsNotMislabelledAsIntraday()
    {
        // Spec 030 §20: a daily-fallback quote must carry LatestDailyFallback, not IntradayToday.
        await using var db = CreateDb();
        var instrumentId = Guid.NewGuid();

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
            ExternalCompanyId = "labeltest",
            Name = "لیبل",
            Ticker = "لیبل",
            TseSymbol = "لیبل",
            CompanySymbol = "لیبل",
            InstrumentCode = "555",
            PrecedencyRight = 0,
            MarketId = NoavaranCompanyScope.BourseMarketId,
            LastSynchronizedAt = Now
        });
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId,
            ProviderName = "TsetmcWebService",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 555,
            Symbol = "LABEL-TICKER",
            Name = "لیبل",
            IsActive = true,
            SourceChangedAt = Now,
            LastSynchronizedAt = Now
        });
        // No intraday row for today — only a daily row for a previous date.
        db.DailyInstrumentTrades.Add(new DailyInstrumentTradeRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            ExternalTradeId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId,
            TradingDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-1),
            ClosingPrice = 1200m,
            LastTradedPrice = 1210m,
            PriceChange = 10m,
            PriceYesterday = 1200m,
            SourceInsertedAt = Now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var result = await new PersistedMarketDataProvider(
                db,
                //new FixedMarketQuoteSourcePriority(ProviderSources.StockMarketDbName),
                new FixedTimeProvider(Now))
            .GetLatestQuotesAsync([new SymbolCode("لیبل")], CancellationToken.None);

        var quote = Assert.Single(result.Observations);
        Assert.Equal("LatestDailyFallback", quote.SourceLabel);
        Assert.NotEqual("IntradayToday", quote.SourceLabel);
        Assert.Equal(MarketQuoteSource.PreviousTradingDay, quote.Source);
    }

    [Fact]
    public async Task DailyChangePercentage_ClosingPriceNotUsed_BothPaths()
    {
        // Spec 030 §12: verify ClosingPrice is NOT used for DAILY_CHANGE_PCT on either path.
        // We set ClosingPrice = PriceYesterday so if ClosingPrice were used the result would be 0.
        await using var db = CreateDb();
        var instrumentId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
            ExternalCompanyId = "closingtest",
            Name = "تست",
            Ticker = "تست",
            TseSymbol = "تست",
            CompanySymbol = "تست",
            InstrumentCode = "444",
            PrecedencyRight = 0,
            MarketId = NoavaranCompanyScope.BourseMarketId,
            LastSynchronizedAt = Now
        });
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId,
            ProviderName = "StockMarketDb",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 444,
            Symbol = "TEST-TICKER",
            Name = "تست",
            IsActive = true,
            SourceChangedAt = Now,
            LastSynchronizedAt = Now
        });

        // --- Intraday path: ClosingPrice == PriceYesterday, LastTradedPrice differs ---
        db.IntradayTradeSnapshots.Add(new IntradayTradeSnapshotRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "StockMarketDb",
            ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId,
            TradingDate = today,
            TradingTime = new TimeOnly(11, 0),
            ClosingPrice = 1000m,       // same as PriceYesterday → would yield 0 if used
            LastTradedPrice = 1030m,    // (1030/1000-1)*100 = 3.0
            PriceChange = 30m,
            PriceYesterday = 1000m,
            ReceivedAt = Now
        });
        await db.SaveChangesAsync();

        var resultIntraday = await new PersistedMarketDataProvider(
                db,
                //new FixedMarketQuoteSourcePriority(ProviderSources.StockMarketDbName),
                new FixedTimeProvider(Now))
            .GetLatestQuotesAsync([new SymbolCode("تست")], CancellationToken.None);

        var intradayQuote = Assert.Single(resultIntraday.Observations);
        Assert.Equal(1030m, intradayQuote.LatestPrice);
        Assert.Equal(3.0m, intradayQuote.PriceChangePercentage); // must be 3.0, not 0.0

        // --- Daily path: same pattern ---
        await using var db2 = CreateDb();
        db2.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
            ExternalCompanyId = "closingtest2",
            Name = "تست",
            Ticker = "تست",
            TseSymbol = "تست",
            CompanySymbol = "تست",
            InstrumentCode = "444",
            PrecedencyRight = 0,
            MarketId = NoavaranCompanyScope.BourseMarketId,
            LastSynchronizedAt = Now
        });
        var instrumentId2 = Guid.NewGuid();
        db2.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId2,
            ProviderName = "StockMarketDb",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 444,
            Symbol = "TEST-TICKER",
            Name = "تست",
            IsActive = true,
            SourceChangedAt = Now,
            LastSynchronizedAt = Now
        });
        db2.DailyInstrumentTrades.Add(new DailyInstrumentTradeRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "StockMarketDb",
            ExternalTradeId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId2,
            TradingDate = today.AddDays(-1),
            ClosingPrice = 1000m,       // same as PriceYesterday → would yield 0 if used
            LastTradedPrice = 1030m,    // (1030/1000-1)*100 = 3.0
            PriceChange = 30m,
            PriceYesterday = 1000m,
            SourceInsertedAt = Now.AddDays(-1)
        });
        await db2.SaveChangesAsync();

        var resultDaily = await new PersistedMarketDataProvider(
                db2,
                //new FixedMarketQuoteSourcePriority(ProviderSources.StockMarketDbName),
                new FixedTimeProvider(Now))
            .GetLatestQuotesAsync([new SymbolCode("تست")], CancellationToken.None);

        var dailyQuote = Assert.Single(resultDaily.Observations);
        Assert.Equal(1030m, dailyQuote.LatestPrice);
        Assert.Equal(3.0m, dailyQuote.PriceChangePercentage); // must be 3.0, not 0.0
    }

    private static StockMarketDbSyncService Service(
        FinancialIngestionDbContext db,
        FakeExecutor executor,
        IScannerCache? cache = null,
        IMarketViewCache? marketViewCache = null) =>
        new(db, executor, new FakeRawPayloadStore(), Options.Create(new StockMarketDbProviderOptions { PageSize = 100, OverlapMinutes = 10 }),
            cache ?? new NoOpScannerCache(), marketViewCache ?? new NoOpMarketViewCache(), new FixedTimeProvider(Now));

    private static PersistedMarketDataProvider Provider(FinancialIngestionDbContext db) =>
        new(db, /*new FixedMarketQuoteSourcePriority(ProviderSources.StockMarketDbName),*/ new FixedTimeProvider(Now));

    private sealed class FixedMarketQuoteSourcePriority(string sourceName) : IMarketQuoteSourcePriority
    {
        public string PrimarySourceName => sourceName;
    }

    private static async Task SeedLinkedInstrumentAsync(FinancialIngestionDbContext db)
    {
        var companyId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId, ProviderName = "CodalDb", ExternalCompanyId = "1", Name = "Company",
            InstrumentCode = "123", LastSynchronizedAt = Now
        });
        await SeedInstrumentAsync(db, companyId);
    }

    private static async Task SeedInstrumentAsync(FinancialIngestionDbContext db, Guid? companyId = null)
    {
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = Guid.NewGuid(), ProviderName = "StockMarketDb", ExternalInstrumentId = InstrumentRef,
            InstrumentCode = 123, InstrumentIsin = "IRO1TEST0001", Symbol = "SYM", Name = "Test",
            MarketCode = "NO", InstrumentKind = "A", NormalizedCompanyId = companyId,
            IsActive = true, SourceChangedAt = Now, LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IntradayTradeSnapshotRow IntradayTrade(
        Guid instrumentId,
        DateTimeOffset receivedAt,
        DateOnly tradingDate) =>
        new()
        {
            Id = Guid.NewGuid(), ProviderName = "StockMarketDb", ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId, TradingDate = tradingDate, ReceivedAt = receivedAt
        };

    private static IntradayIndexSnapshotRow IntradayIndex(
        Guid instrumentId,
        DateTimeOffset changedAt,
        DateOnly tradingDate) =>
        new()
        {
            Id = Guid.NewGuid(), ProviderName = "StockMarketDb", ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId, TradingDate = tradingDate, SourceChangedAt = changedAt
        };

    private sealed class FakeExecutor : IStockMarketDbQueryExecutor
    {
        public IReadOnlyList<StockMarketInstrumentRecord> Instruments { get; init; } = [];
        public IReadOnlyList<StockMarketIntradayTradeRecord> IntradayTrades { get; init; } = [];
        public IReadOnlyList<StockMarketDailyTradeRecord> DailyTrades { get; init; } = [];
        public IReadOnlyList<StockMarketIntradayIndexRecord> IntradayIndices { get; init; } = [];
        public IReadOnlyList<StockMarketHistoricalDailyIndexRecord> HistoricalIndices { get; init; } = [];
        public List<StockMarketPageCursor> InstrumentCursors { get; } = [];
        public List<StockMarketPageCursor> IntradayTradeCursors { get; } = [];
        public Task<IReadOnlyList<StockMarketInstrumentRecord>> QueryInstrumentsAsync(StockMarketPageCursor cursor, int take, CancellationToken cancellationToken)
        {
            InstrumentCursors.Add(cursor);
            return Task.FromResult(Instruments);
        }
        public Task<IReadOnlyList<StockMarketIntradayTradeRecord>> QueryIntradayTradesAsync(StockMarketPageCursor cursor, int take, CancellationToken cancellationToken)
        {
            IntradayTradeCursors.Add(cursor);
            return Task.FromResult(IntradayTrades);
        }
        public Task<IReadOnlyList<StockMarketDailyTradeRecord>> QueryDailyTradesAsync(StockMarketPageCursor cursor, int take, CancellationToken cancellationToken) => Task.FromResult(DailyTrades);
        public Task<IReadOnlyList<StockMarketIntradayIndexRecord>> QueryIntradayIndicesAsync(StockMarketPageCursor cursor, int take, CancellationToken cancellationToken) => Task.FromResult(IntradayIndices);
        public Task<IReadOnlyList<StockMarketHistoricalDailyIndexRecord>> QueryHistoricalDailyIndicesAsync(StockMarketPageCursor cursor, int take, CancellationToken cancellationToken) => Task.FromResult(HistoricalIndices);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        // Pin the local zone so "current trading day" derivation is deterministic on any machine.
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeRawPayloadStore : IProviderRawPayloadStore
    {
        public List<ProviderRawPayload> Payloads { get; } = [];
        public Task StoreAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
        {
            Payloads.Add(payload);
            return Task.CompletedTask;
        }
        public Task<ProviderRawPayload?> FindByChecksumAsync(string providerName, string checksum, CancellationToken cancellationToken) =>
            Task.FromResult(Payloads.FirstOrDefault(payload => payload.ProviderName == providerName && payload.Checksum == checksum));
    }

    private sealed class TrackingScannerCache : IScannerCache
    {
        public List<ScannerCacheInvalidation> Invalidations { get; } = [];
        public Task<string> GetDataVersionAsync(CancellationToken cancellationToken) => Task.FromResult("test");
        public Task<ScannerParseResult?> GetPlanAsync(ScannerCacheScope scope, string dataVersion, ScannerParseRequest request, CancellationToken cancellationToken) => Task.FromResult<ScannerParseResult?>(null);
        public Task SetPlanAsync(ScannerCacheScope scope, string dataVersion, ScannerParseRequest request, ScannerParseResult result, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ScannerTableResult?> GetResultAsync(ScannerCacheScope scope, string dataVersion, ScannerExecutionRequest request, CancellationToken cancellationToken) => Task.FromResult<ScannerTableResult?>(null);
        public Task SetResultAsync(ScannerCacheScope scope, string dataVersion, ScannerExecutionRequest request, ScannerTableResult result, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InvalidateAsync(ScannerCacheInvalidation invalidation, CancellationToken cancellationToken)
        {
            Invalidations.Add(invalidation);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpMarketViewCache : IMarketViewCache
    {
        public Task<MarketSummary?> GetSummaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<MarketSummary?>(null);

        public Task SetSummaryAsync(MarketSummary summary, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InvalidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TrackingMarketViewCache : IMarketViewCache
    {
        public int InvalidationCount { get; private set; }

        public Task<MarketSummary?> GetSummaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<MarketSummary?>(null);

        public Task SetSummaryAsync(MarketSummary summary, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InvalidateAsync(CancellationToken cancellationToken)
        {
            InvalidationCount++;
            return Task.CompletedTask;
        }
    }
}
