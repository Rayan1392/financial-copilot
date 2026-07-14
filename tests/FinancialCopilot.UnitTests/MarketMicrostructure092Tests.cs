using FinancialCopilot.Domain.Financial.Insights.Microstructure;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Insights;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class MarketMicrostructure092Tests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 8, 30, 0, TimeSpan.Zero);
    private static readonly decimal[] Baseline = Enumerable.Repeat(10_000_000_000m, 20).ToArray();

    [Fact]
    public void LargeTrade_UsesAbsoluteAndRelativeThreshold_AndPreservesSideEvidence()
    {
        var observation = Observation() with
        {
            LargestTradeValue = 60_000_000_000m,
            LargestTradeVolume = 2_000_000m,
            LargestTradeSide = MicrostructureTradeSide.Buy,
            BaselineTradingValues = Baseline
        };

        var result = new LargeTradeSignalDetector().Detect(observation, MicrostructureDetectionPolicy.Default);

        var signal = Assert.Single(result.Signals);
        Assert.Equal(MicrostructureSignalKind.LargeTrade, signal.Kind);
        Assert.Contains(signal.Evidence, item => item.Label == "trade_side" && item.Value == "Buy");
        Assert.Contains("microstructure-v1", signal.DetectorVersion);
    }

    [Fact]
    public void LargeTrade_Suppresses_WhenCanonicalPerTradeEvidenceIsUnavailable()
    {
        var result = new LargeTradeSignalDetector().Detect(Observation(), MicrostructureDetectionPolicy.Default);

        Assert.Empty(result.Signals);
        Assert.Equal("missing-largest-trade-value", result.SuppressionReason);
    }

    [Fact]
    public void BuyerPower_UsesRealPersonAverageVolumes()
    {
        var observation = Observation() with
        {
            RealBuyVolume = 600m,
            RealBuyerCount = 2,
            RealSellVolume = 300m,
            RealSellerCount = 3
        };

        var signal = Assert.Single(new BuyerSellerPowerSignalDetector()
            .Detect(observation, MicrostructureDetectionPolicy.Default).Signals);

        Assert.Equal(MicrostructureSignalKind.BuyerPower, signal.Kind);
        Assert.Contains(signal.Evidence, item => item.Label == "buyer_power_ratio" && item.Value == "3");
    }

    [Fact]
    public void BuyerPower_Suppresses_ZeroOrIncompleteDenominators()
    {
        var observation = Observation() with
        {
            RealBuyVolume = 600m,
            RealBuyerCount = 0,
            RealSellVolume = 300m,
            RealSellerCount = 3
        };

        var result = new BuyerSellerPowerSignalDetector().Detect(observation, MicrostructureDetectionPolicy.Default);

        Assert.Empty(result.Signals);
        Assert.Equal("incomplete-real-person-count-or-volume", result.SuppressionReason);
    }

    [Theory]
    [InlineData(50_000_000_000, 10_000_000_000, MicrostructureSignalKind.RetailMoneyInflow)]
    [InlineData(10_000_000_000, 50_000_000_000, MicrostructureSignalKind.RetailMoneyOutflow)]
    public void RealMoneyFlow_ReportsDirectionWithoutSmartMoneyClaims(
        long buyValue,
        long sellValue,
        MicrostructureSignalKind expected)
    {
        var observation = Observation() with
        {
            RealBuyValue = buyValue,
            RealSellValue = sellValue,
            BaselineTradingValues = Baseline
        };

        var signal = Assert.Single(new RealMoneyFlowSignalDetector()
            .Detect(observation, MicrostructureDetectionPolicy.Default).Signals);

        Assert.Equal(expected, signal.Kind);
        Assert.Contains("no smart-money claim", signal.Reason);
    }

    [Fact]
    public void QueueDetector_DistinguishesFormationStrengtheningReleaseAndCollection()
    {
        var detector = new OrderQueueSignalDetector();
        var policy = MicrostructureDetectionPolicy.Default;
        var formation = Observation() with
        {
            AllowedMinimumPrice = 900m, AllowedMaximumPrice = 1_100m,
            BuyQueueValue = 15_000_000_000m, BuyQueueVolume = 1_000_000m,
            BuyQueueDurationSeconds = 180, PreviousBuyQueueValue = 0m
        };
        var strengthening = formation with { PreviousBuyQueueValue = 10_000_000_000m, BuyQueueValue = 13_000_000_000m };
        var release = formation with { PreviousBuyQueueValue = 15_000_000_000m, BuyQueueValue = 0m, BuyQueueDurationSeconds = 0 };
        var collection = release with { BuyQueueCollectionConfirmed = true };

        Assert.Equal(MicrostructureSignalKind.BuyQueueFormation, Assert.Single(detector.Detect(formation, policy).Signals).Kind);
        Assert.Equal(MicrostructureSignalKind.BuyQueueStrengthening, Assert.Single(detector.Detect(strengthening, policy).Signals).Kind);
        Assert.Equal(MicrostructureSignalKind.BuyQueueRelease, Assert.Single(detector.Detect(release, policy).Signals).Kind);
        Assert.Equal(MicrostructureSignalKind.BuyQueueCollection, Assert.Single(detector.Detect(collection, policy).Signals).Kind);
    }

    [Fact]
    public void QueueDetector_Suppresses_WhenAllowedPriceBoundsAreUnavailable()
    {
        var result = new OrderQueueSignalDetector().Detect(Observation(), MicrostructureDetectionPolicy.Default);

        Assert.Equal("missing-allowed-price-bounds", result.SuppressionReason);
        Assert.Empty(result.Signals);
    }

    [Fact]
    public void VolumeAnomaly_UsesMedianSoSingleHistoricalOutlierDoesNotHideSignal()
    {
        var baseline = Enumerable.Repeat(100m, 19).Append(100_000m).ToArray();
        var observation = Observation() with { Volume = 250m, BaselineVolumes = baseline };

        var signal = Assert.Single(new VolumeAnomalySignalDetector()
            .Detect(observation, MicrostructureDetectionPolicy.Default).Signals);

        Assert.Equal(MicrostructureSignalKind.VolumeAnomaly, signal.Kind);
        Assert.Contains(signal.Evidence, item => item.Label == "baseline_median" && item.Value == "100");
    }

    [Fact]
    public void TradingValueAnomaly_RequiresMinimumBaselineCoverage()
    {
        var observation = Observation() with
        {
            TradingValue = 100_000m,
            BaselineTradingValues = [10m, 20m]
        };

        var result = new TradingValueAnomalySignalDetector()
            .Detect(observation, MicrostructureDetectionPolicy.Default);

        Assert.Equal("insufficient-baseline", result.SuppressionReason);
        Assert.Empty(result.Signals);
    }

    [Fact]
    public void Detectors_SuppressStaleAndOutOfSessionEvidence()
    {
        var detector = new VolumeAnomalySignalDetector();
        var stale = Observation() with { SourceSyncedAtUtc = Now.AddHours(-1), Volume = 300m, BaselineVolumes = Baseline };
        var outsideSession = Observation() with { IsTradingSession = false, Volume = 300m, BaselineVolumes = Baseline };

        Assert.Equal("stale-source", detector.Detect(stale, MicrostructureDetectionPolicy.Default).SuppressionReason);
        Assert.Equal("outside-trading-session", detector.Detect(outsideSession, MicrostructureDetectionPolicy.Default).SuppressionReason);
    }

    [Fact]
    public void SameEvidence_ProducesReproducibleSignals()
    {
        var observation = Observation() with { Volume = 30_000_000_000m, BaselineVolumes = Baseline };
        var detector = new VolumeAnomalySignalDetector();

        var first = Assert.Single(detector.Detect(observation, MicrostructureDetectionPolicy.Default).Signals);
        var second = Assert.Single(detector.Detect(observation, MicrostructureDetectionPolicy.Default).Signals);

        Assert.Equal(first.DetectorCode, second.DetectorCode);
        Assert.Equal(first.DetectorVersion, second.DetectorVersion);
        Assert.Equal(first.Kind, second.Kind);
        Assert.Equal(first.MagnitudeScore, second.MagnitudeScore);
        Assert.Equal(first.Evidence.ToArray(), second.Evidence.ToArray());
    }

    [Fact]
    public void ChangedPolicyVersion_IsHistoricallyDistinguishable()
    {
        var observation = Observation() with { Volume = 30_000_000_000m, BaselineVolumes = Baseline };
        var detector = new VolumeAnomalySignalDetector();

        var v1 = Assert.Single(detector.Detect(observation, MicrostructureDetectionPolicy.Default).Signals);
        var v2 = Assert.Single(detector.Detect(
            observation,
            MicrostructureDetectionPolicy.Default with { Version = "microstructure-v2" }).Signals);

        Assert.NotEqual(v1.DetectorVersion, v2.DetectorVersion);
        Assert.Equal(v1.Evidence.Skip(1), v2.Evidence.Skip(1));
    }

    [Fact]
    public async Task Correction_ProducesSupersedingAuditEvidenceAndNewStableIdentity()
    {
        await using var db = new FinancialIngestionDbContext(
            new DbContextOptionsBuilder<FinancialIngestionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        var adapter = new MarketMicrostructureInsightDetector(
            db, [], new DeterministicInsightScoringService(), new FixedSourcePriority(), Options.Create(new MarketMicrostructureOptions()),
            NullLogger<MarketMicrostructureInsightDetector>.Instance);
        var detector = new VolumeAnomalySignalDetector();
        var originalObservation = Observation() with { Volume = 30_000_000_000m, BaselineVolumes = Baseline };
        var correctedObservation = originalObservation with
        {
            SourceEventIdentity = "source-2",
            IsCorrection = true,
            SupersedesSourceEventIdentity = originalObservation.SourceEventIdentity
        };
        var originalSignal = Assert.Single(detector.Detect(originalObservation, MicrostructureDetectionPolicy.Default).Signals);
        var correctedSignal = Assert.Single(detector.Detect(correctedObservation, MicrostructureDetectionPolicy.Default).Signals);

        var original = adapter.Map(originalObservation, originalSignal, Now);
        var corrected = adapter.Map(correctedObservation, correctedSignal, Now);

        Assert.NotEqual(original.DeduplicationKey, corrected.DeduplicationKey);
        Assert.Contains(corrected.Evidence, item => item.Label == "is_correction" && item.Value == "True");
        Assert.Contains(corrected.Evidence,
            item => item.Label == "supersedes_source_event_identity" && item.Value == "source-1");
    }

    [Fact]
    public async Task CanonicalAdapter_EmitsAndIdempotentlyPersistsAnomaliesInExistingInsightFeed()
    {
        await using var db = new FinancialIngestionDbContext(
            new DbContextOptionsBuilder<FinancialIngestionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        var companyId = Guid.NewGuid();
        var instrumentId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId, ProviderName = "canonical-provider", ExternalCompanyId = "company-1",
            Name = "Test Company", CompanySymbol = "TEST", LastSynchronizedAt = Now
        });
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId, ProviderName = "canonical-provider", ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 1234, InstrumentIsin = "IROTEST00001", Symbol = "TEST", Name = "Test Company",
            MarketCode = "TSE", InstrumentKind = "Stock", NormalizedCompanyId = companyId, IsActive = true,
            SourceChangedAt = Now, LastSynchronizedAt = Now
        });
        for (var day = 1; day <= 20; day++)
        {
            db.DailyInstrumentTrades.Add(new DailyInstrumentTradeRow
            {
                Id = Guid.NewGuid(), ProviderName = "canonical-provider", ExternalTradeId = Guid.NewGuid(),
                TradingInstrumentId = instrumentId, TradingDate = new DateOnly(2026, 7, 14).AddDays(-day),
                Volume = 100m, TotalCapital = 1_000m, SourceInsertedAt = Now.AddDays(-day)
            });
        }
        db.IntradayTradeSnapshots.Add(new IntradayTradeSnapshotRow
        {
            Id = Guid.NewGuid(), ProviderName = "canonical-provider", ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId, TradingDate = new DateOnly(2026, 7, 14), TradingTime = new TimeOnly(10, 30),
            Volume = 300m, TotalCapital = 3_000m, TotalTransactions = 25m, ReceivedAt = Now
        });
        await db.SaveChangesAsync();

        var detector = new MarketMicrostructureInsightDetector(
            db,
            [new VolumeAnomalySignalDetector(), new TradingValueAnomalySignalDetector()],
            new DeterministicInsightScoringService(),
            new FixedSourcePriority(),
            Options.Create(new MarketMicrostructureOptions()),
            NullLogger<MarketMicrostructureInsightDetector>.Instance);
        var repository = new InsightEventRepository(db, new FixedTimeProvider(Now));
        var context = new InsightDetectionContext(Now, Now.AddDays(-2));

        var first = await detector.DetectAsync(context);
        await repository.UpsertAsync(first);
        var second = await detector.DetectAsync(context);
        await repository.UpsertAsync(second);
        var feed = await repository.QueryAsync(new InsightFeedQuery(Symbol: "TEST", Take: 10));

        Assert.Equal(2, feed.TotalCount);
        Assert.Contains(feed.Items, item => item.InsightType == InsightType.TradingVolumeAnomaly);
        Assert.Contains(feed.Items, item => item.InsightType == InsightType.TradingValueAnomaly);
        Assert.All(feed.Items, item => Assert.Contains("MICROSTRUCTURE-V1", item.DeduplicationKey));
        Assert.All(feed.Items, item => Assert.Contains(item.Evidence, evidence => evidence.Label == "source_event_identity"));
        var volume = Assert.Single(feed.Items, item => item.InsightType == InsightType.TradingVolumeAnomaly);
        Assert.Contains(volume.Evidence, item => item.Label == "current_volume" && item.Value == "300");
        Assert.Contains(volume.Evidence, item => item.Label == "baseline_median" && item.Value == "100");
        Assert.Contains(volume.Evidence, item => item.Label == "threshold_ratio" && item.Value == "2");
        Assert.Contains(volume.Evidence, item => item.Label == "detector_version" && item.Value.Contains("microstructure-v1"));
    }

    [Fact]
    public async Task ConcurrentWorkers_PersistOneEventForTheSameStableIdentity()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"microstructure-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Default Timeout=30;Pooling=False";
        try
        {
            await using (var setup = new FinancialIngestionDbContext(
                             new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseSqlite(connectionString).Options))
                await setup.Database.EnsureCreatedAsync();

            var insight = new InsightEvent(
                Guid.NewGuid(), "company-1", "TEST", "CHEM", InsightType.TradingVolumeAnomaly,
                InsightSeverity.Important, 80m, 95m, "Volume anomaly", "Informational anomaly.",
                "Deterministic threshold crossed.", [new("ratio", "3", "canonical-provider")],
                "canonical-provider", InsightSourceEntityType.MarketMicrostructureObservation, "source-1",
                "2026-07-14/10:30", Now, Now.AddHours(24),
                "MM:VOLUME_ANOMALY:MICROSTRUCTURE-V1/VOLUME_ANOMALY/1:1234:20260714:10:30:SOURCE-1");

            async Task PersistAsync()
            {
                await using var context = new FinancialIngestionDbContext(
                    new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseSqlite(connectionString).Options);
                await new InsightEventRepository(context, new FixedTimeProvider(Now)).UpsertAsync([insight]);
            }

            await Task.WhenAll(PersistAsync(), PersistAsync());

            await using var verification = new FinancialIngestionDbContext(
                new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseSqlite(connectionString).Options);
            Assert.Equal(1, await verification.InsightEvents.CountAsync());
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static MarketMicrostructureObservation Observation() => new(
        ExternalCompanyId: "company-1",
        Symbol: "TEST",
        IndustryCode: "CHEM",
        MarketSegment: "TSE",
        ProviderName: "canonical-provider",
        InstrumentIdentity: "1234",
        SourceEventIdentity: "source-1",
        TradingDate: new DateOnly(2026, 7, 14),
        Window: "10:30",
        ObservedAtUtc: Now,
        SourceSyncedAtUtc: Now,
        EvaluatedAtUtc: Now,
        IsTradingSession: true);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedSourcePriority : IMarketQuoteSourcePriority
    {
        public string PrimarySourceName => "canonical-provider";
    }
}
