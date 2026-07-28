using System.Diagnostics;
using System.Diagnostics.Metrics;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Insights.Microstructure;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Insights;

public sealed class MarketMicrostructureOptions
{
    public const string SectionName = "MarketMicrostructure";

    public int BatchSize { get; set; } = 500;
    public int BaselineLookback { get; set; } = 20;
    public int MinimumBaselineObservations { get; set; } = 10;
    public int MaximumSourceAgeMinutes { get; set; } = 15;
    public decimal LargeTradeAbsoluteValue { get; set; } = 50_000_000_000m;
    public decimal LargeTradeRelativeToMedian { get; set; } = 0.05m;
    public decimal BuyerSellerPowerRatio { get; set; } = 1.5m;
    public decimal MoneyFlowAbsoluteValue { get; set; } = 20_000_000_000m;
    public decimal MoneyFlowRelativeToMedian { get; set; } = 0.02m;
    public decimal QueueMinimumValue { get; set; } = 10_000_000_000m;
    public int QueueMinimumDurationSeconds { get; set; } = 120;
    public decimal QueueChangeRatio { get; set; } = 0.20m;
    public decimal AnomalyRatio { get; set; } = 2m;
    public Dictionary<string, MarketMicrostructureThresholdOverride> SegmentOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MarketMicrostructureThresholdOverride
{
    public decimal? LargeTradeAbsoluteValue { get; set; }
    public decimal? MoneyFlowAbsoluteValue { get; set; }
    public decimal? QueueMinimumValue { get; set; }
    public decimal? AnomalyRatio { get; set; }
}

internal sealed class MarketMicrostructureInsightDetector(
    FinancialIngestionDbContext dbContext,
    IEnumerable<IMicrostructureSignalDetector> signalDetectors,
    IInsightScoringService scoringService,
    IMarketQuoteSourcePriority sourcePriority,
    IOptions<MarketMicrostructureOptions> options,
    ILogger<MarketMicrostructureInsightDetector> logger) : IInsightDetector
{
    private static readonly Meter Meter = new("FinancialCopilot.MarketMicrostructure", "1.0.0");
    private static readonly Counter<long> ObservationsCounter = Meter.CreateCounter<long>("microstructure.observations");
    private static readonly Counter<long> SignalsCounter = Meter.CreateCounter<long>("microstructure.signals");
    private static readonly Counter<long> SuppressionsCounter = Meter.CreateCounter<long>("microstructure.suppressions");
    private static readonly Counter<long> CorrectionsCounter = Meter.CreateCounter<long>("microstructure.corrections");
    private static readonly Counter<long> FailuresCounter = Meter.CreateCounter<long>("microstructure.failures");
    private static readonly Histogram<double> SourceLag = Meter.CreateHistogram<double>("microstructure.source.lag", "s");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("microstructure.detector.duration", "ms");
    private readonly MarketMicrostructureOptions _options = options.Value;

    public string DetectorName => "MarketMicrostructure";

    public async Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var observations = await ReadCanonicalObservationsAsync(context, cancellationToken);
        var events = new List<InsightEvent>();

        foreach (var observation in observations)
        {
            ObservationsCounter.Add(1, KeyValuePair.Create<string, object?>("provider", observation.ProviderName));
            SourceLag.Record(Math.Max(0, (context.DetectedAtUtc - observation.SourceSyncedAtUtc).TotalSeconds),
                KeyValuePair.Create<string, object?>("provider", observation.ProviderName));
            if (observation.IsCorrection)
                CorrectionsCounter.Add(1, KeyValuePair.Create<string, object?>("provider", observation.ProviderName));
            var policy = ResolvePolicy(observation.MarketSegment);
            foreach (var detector in signalDetectors)
            {
                MicrostructureDetectionOutcome outcome;
                try
                {
                    outcome = detector.Detect(observation, policy);
                }
                catch (Exception exception)
                {
                    FailuresCounter.Add(1, new("detector", detector.DetectorCode), new("disposition", "poison-input"));
                    logger.LogError(exception,
                        "Poison microstructure observation {SourceEventIdentity} was isolated for detector {DetectorCode}; other observations continue.",
                        observation.SourceEventIdentity, detector.DetectorCode);
                    continue;
                }
                if (outcome.SuppressionReason is not null)
                {
                    SuppressionsCounter.Add(1,
                        new("detector", detector.DetectorCode),
                        new("reason", outcome.SuppressionReason));
                    continue;
                }

                foreach (var signal in outcome.Signals)
                {
                    SignalsCounter.Add(1, new("detector", signal.DetectorCode), new("kind", signal.Kind.ToString()));
                    events.Add(Map(observation, signal, context.DetectedAtUtc));
                }
            }
        }

        stopwatch.Stop();
        Duration.Record(stopwatch.Elapsed.TotalMilliseconds);
        logger.LogInformation(
            "Market microstructure detector processed {ObservationCount} bounded observations and emitted {EventCount} events in {ElapsedMs} ms.",
            observations.Count, events.Count, stopwatch.Elapsed.TotalMilliseconds);
        return events;
    }

    private async Task<IReadOnlyList<MarketMicrostructureObservation>> ReadCanonicalObservationsAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(context.Take ?? _options.BatchSize, 1, 2_000);
        var snapshots = await dbContext.IntradayTradeSnapshots.AsNoTracking()
            .Where(row => row.ReceivedAt >= context.SinceUtc)
            .OrderByDescending(row => row.ReceivedAt)
            .Take(batchSize * 4)
            .ToListAsync(cancellationToken);

        var latest = snapshots
            .GroupBy(row => row.TradingInstrumentId)
            .Select(group => group.OrderByDescending(row => row.ReceivedAt).First())
            .Take(batchSize)
            .ToArray();
        if (latest.Length == 0) return [];

        var instrumentIds = latest.Select(row => row.TradingInstrumentId).Distinct().ToArray();
        var instruments = await dbContext.TradingInstruments.AsNoTracking()
            .Where(row => instrumentIds.Contains(row.Id) && row.NormalizedCompanyId != null)
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var companyIds = instruments.Values.Select(row => row.NormalizedCompanyId!.Value).Distinct().ToArray();
        var companies = await dbContext.Companies.AsNoTracking()
            .Where(row => companyIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        var oldestDate = latest.Min(row => row.TradingDate).AddDays(-Math.Max(60, _options.BaselineLookback * 4));
        var histories = await dbContext.DailyInstrumentTrades.AsNoTracking()
            .Where(row => instrumentIds.Contains(row.TradingInstrumentId) && row.TradingDate >= oldestDate)
            .OrderByDescending(row => row.TradingDate)
            .ToListAsync(cancellationToken);
        var historyByInstrument = histories
            .GroupBy(row => row.TradingInstrumentId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var result = new List<MarketMicrostructureObservation>(latest.Length);
        foreach (var snapshot in latest)
        {
            if (!instruments.TryGetValue(snapshot.TradingInstrumentId, out var instrument) ||
                !instrument.NormalizedCompanyId.HasValue ||
                !companies.TryGetValue(instrument.NormalizedCompanyId.Value, out var company))
                continue;
            if (context.ExternalCompanyIds is { Count: > 0 } scopedCompanies &&
                !scopedCompanies.Contains(company.ExternalCompanyId, StringComparer.Ordinal))
                continue;

            var history = historyByInstrument.GetValueOrDefault(instrument.Id, []);
            var prior = history
                .Where(row => row.TradingDate < snapshot.TradingDate)
                .Take(Math.Clamp(_options.BaselineLookback, 1, 120))
                .ToArray();
            var window = snapshot.TradingTime.HasValue
                ? snapshot.TradingTime.Value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                : "daily-cumulative";
            var isTradingSession = !snapshot.TradingTime.HasValue ||
                snapshot.TradingTime.Value >= new TimeOnly(8, 45) && snapshot.TradingTime.Value <= new TimeOnly(12, 45);

            result.Add(new MarketMicrostructureObservation(
                company.ExternalCompanyId,
                instrument.Symbol,
                null,
                instrument.MarketCode,
                snapshot.ProviderName,
                instrument.InstrumentCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                snapshot.ExternalSnapshotId.ToString("N"),
                snapshot.TradingDate,
                window,
                snapshot.ReceivedAt,
                snapshot.ReceivedAt,
                context.DetectedAtUtc,
                isTradingSession,
                Volume: snapshot.Volume,
                TradingValue: snapshot.TotalCapital,
                Transactions: snapshot.TotalTransactions,
                BaselineVolumes: prior.Select(row => row.Volume).ToArray(),
                BaselineTradingValues: prior.Select(row => row.TotalCapital).ToArray()));
        }

        return result
            .GroupBy(observation => observation.ExternalCompanyId, StringComparer.Ordinal)
            .SelectMany(group =>
            {
                var primary = group
                    .Where(observation => string.Equals(
                        observation.ProviderName,
                        sourcePriority.PrimarySourceName,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return primary.Length > 0 ? primary : group.ToArray();
            })
            .ToArray();
    }

    private MicrostructureDetectionPolicy ResolvePolicy(string segment)
    {
        _options.SegmentOverrides.TryGetValue(segment, out var segmentOverride);
        return new MicrostructureDetectionPolicy(
            Version: "microstructure-v1",
            EvidenceSchemaVersion: "microstructure-evidence-v1",
            MaximumSourceAge: TimeSpan.FromMinutes(Math.Clamp(_options.MaximumSourceAgeMinutes, 1, 1_440)),
            BaselineLookback: Math.Clamp(_options.BaselineLookback, 1, 120),
            MinimumBaselineObservations: Math.Clamp(_options.MinimumBaselineObservations, 2, 120),
            LargeTradeAbsoluteValue: segmentOverride?.LargeTradeAbsoluteValue ?? _options.LargeTradeAbsoluteValue,
            LargeTradeRelativeToMedian: _options.LargeTradeRelativeToMedian,
            BuyerSellerPowerRatio: _options.BuyerSellerPowerRatio,
            MoneyFlowAbsoluteValue: segmentOverride?.MoneyFlowAbsoluteValue ?? _options.MoneyFlowAbsoluteValue,
            MoneyFlowRelativeToMedian: _options.MoneyFlowRelativeToMedian,
            QueueMinimumValue: segmentOverride?.QueueMinimumValue ?? _options.QueueMinimumValue,
            QueueMinimumDurationSeconds: _options.QueueMinimumDurationSeconds,
            QueueChangeRatio: _options.QueueChangeRatio,
            AnomalyRatio: segmentOverride?.AnomalyRatio ?? _options.AnomalyRatio);
    }

    internal InsightEvent Map(MarketMicrostructureObservation observation, MicrostructureSignal signal, DateTimeOffset detectedAtUtc)
    {
        var freshnessMinutes = Math.Max(0m, (decimal)(detectedAtUtc - observation.SourceSyncedAtUtc).TotalMinutes);
        var freshness = Math.Max(0m, 100m - freshnessMinutes * 5m);
        var score = scoringService.Score(new InsightScoringInput(
            signal.MagnitudeScore, 95m, signal.EvidenceCompleteness, freshness, signal.RarityScore));
        var evidence = new List<InsightEvidenceItem>
        {
            new("detector_code", signal.DetectorCode, observation.ProviderName),
            new("detector_version", signal.DetectorVersion, observation.ProviderName),
            new("instrument_identity", observation.InstrumentIdentity, observation.ProviderName),
            new("trading_date", observation.TradingDate.ToString("yyyy-MM-dd"), observation.ProviderName),
            new("window", observation.Window, observation.ProviderName),
            new("source_event_identity", observation.SourceEventIdentity, observation.ProviderName),
            new("calculated_at_utc", detectedAtUtc.ToString("O"), observation.ProviderName),
            new("source_synced_at_utc", observation.SourceSyncedAtUtc.ToString("O"), observation.ProviderName),
            new("source_lag_seconds", Math.Max(0, (detectedAtUtc - observation.SourceSyncedAtUtc).TotalSeconds).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), observation.ProviderName),
            new("market_session_state", observation.IsTradingSession ? "Trading" : "OutsideTrading", observation.ProviderName),
            new("money_unit", "rial", observation.ProviderName),
            new("volume_unit", "share", observation.ProviderName),
            new("is_correction", observation.IsCorrection.ToString(), observation.ProviderName),
            new("supersedes_source_event_identity", observation.SupersedesSourceEventIdentity ?? "none", observation.ProviderName)
        };
        evidence.AddRange(signal.Evidence.Select(item =>
            new InsightEvidenceItem(item.Label, item.Value, observation.ProviderName,
                observation.TradingDate.ToString("yyyy-MM-dd"), observation.SourceSyncedAtUtc)));

        return new InsightEvent(
            Guid.NewGuid(), observation.ExternalCompanyId, observation.Symbol, observation.IndustryCode,
            MapInsightType(signal.Kind), score.Severity, score.ImportanceScore, score.ConfidenceScore,
            signal.Title, signal.Summary, signal.Reason, evidence, observation.ProviderName,
            InsightSourceEntityType.MarketMicrostructureObservation, observation.SourceEventIdentity,
            $"{observation.TradingDate:yyyy-MM-dd}/{observation.Window}", detectedAtUtc,
            detectedAtUtc.AddHours(24), BuildIdentity(observation, signal),
            [InsightAction.OpenSymbol, InsightAction.AskAiAboutThis]);
    }

    private static InsightType MapInsightType(MicrostructureSignalKind kind) => kind switch
    {
        MicrostructureSignalKind.LargeTrade => InsightType.LargeTradeDetected,
        MicrostructureSignalKind.BuyerPower or MicrostructureSignalKind.SellerPower => InsightType.BuyerSellerPowerChanged,
        MicrostructureSignalKind.RetailMoneyInflow or MicrostructureSignalKind.RetailMoneyOutflow => InsightType.RealMoneyFlowChanged,
        MicrostructureSignalKind.VolumeAnomaly => InsightType.TradingVolumeAnomaly,
        MicrostructureSignalKind.TradingValueAnomaly => InsightType.TradingValueAnomaly,
        _ => InsightType.OrderQueueChanged
    };

    private static string BuildIdentity(MarketMicrostructureObservation observation, MicrostructureSignal signal) =>
        string.Join(':', "MM", signal.DetectorCode, signal.DetectorVersion, observation.InstrumentIdentity,
            observation.TradingDate.ToString("yyyyMMdd"), observation.Window, observation.SourceEventIdentity)
            .ToUpperInvariant();
}

internal sealed class GenerateMarketMicrostructureInsightsUseCase(
    MarketMicrostructureInsightDetector detector,
    IInsightEventRepository repository,
    TimeProvider timeProvider) : IGenerateMarketMicrostructureInsightsUseCase
{
    public async Task<GenerateMarketInsightsResult> ExecuteAsync(
        GenerateMarketInsightsRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var lookbackDays = Math.Clamp(request.LookbackDays <= 0 ? 2 : request.LookbackDays, 1, 90);
        var events = await detector.DetectAsync(new InsightDetectionContext(now, now.AddDays(-lookbackDays)), cancellationToken);
        var distinct = events
            .GroupBy(item => item.DeduplicationKey, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.ImportanceScore).First())
            .ToArray();
        var persisted = await repository.UpsertAsync(distinct, cancellationToken);
        return new GenerateMarketInsightsResult(1, distinct.Length, persisted, now);
    }
}
