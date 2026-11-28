using System.Diagnostics;
using System.Diagnostics.Metrics;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.FinancialData.ValuationGauges;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// Read-only adapter over the Feature 148 latest-state gauge tables. A security matches a vendor ticker by its
/// CyclicalWaves identity (<c>cw2:</c> external id) or by a unique active ticker with the same normalized ISIN;
/// anything else is a coverage miss. Rows are only exposed for kinds listed in the vendor catalog.
/// </summary>
public sealed class ValuationGaugeSnapshotReader(FinancialIngestionDbContext db) : IValuationGaugeSnapshotReader
{
    public async Task<ValuationGaugeSnapshotSet> ReadAsync(
        ValuationGaugeSecurity security,
        IReadOnlyCollection<ValuationGaugeKind> kinds,
        CancellationToken cancellationToken)
    {
        var (match, ticker) = await ValuationGaugeTickerMatcher.MatchAsync(db, security, cancellationToken);
        if (match != ValuationGaugeMatchOutcome.Matched || ticker is null)
            return new ValuationGaugeSnapshotSet(match);

        var isin = NormalizeIsin(ticker.Enticker);
        var wantsRatios = kinds.Any(kind => kind is ValuationGaugeKind.Pe or ValuationGaugeKind.Ps);
        ValuationGaugeCatalogFlags? catalog = null;
        RatioGaugeSnapshot? pe = null;
        RatioGaugeSnapshot? ps = null;
        if (wantsRatios)
        {
            var availability = await db.CyclicalWavesV2GaugeAvailability.AsNoTracking()
                .Where(row => row.TickerId == ticker.Id)
                .Select(row => new { row.HasPe, row.HasPs })
                .FirstOrDefaultAsync(cancellationToken);
            catalog = availability is null ? new ValuationGaugeCatalogFlags(false, false) : new ValuationGaugeCatalogFlags(availability.HasPe, availability.HasPs);
            var types = kinds.Where(kind => kind is ValuationGaugeKind.Pe or ValuationGaugeKind.Ps)
                .Select(kind => kind == ValuationGaugeKind.Pe ? "PE" : "PS").ToArray();
            var details = await db.CyclicalWavesV2GaugeDetails.AsNoTracking()
                .Where(row => row.TickerId == ticker.Id && types.Contains(row.GaugeType))
                .ToListAsync(cancellationToken);
            pe = catalog.HasPe ? Ratio(details.FirstOrDefault(row => row.GaugeType == "PE")) : null;
            ps = catalog.HasPs ? Ratio(details.FirstOrDefault(row => row.GaugeType == "PS")) : null;
        }

        EquilibriumRangeSnapshot? equilibrium = null;
        if (kinds.Contains(ValuationGaugeKind.Equilibrium))
        {
            var range = await db.CyclicalWavesV2EquilibriumRanges.AsNoTracking()
                .Where(row => row.TickerId == ticker.Id)
                .FirstOrDefaultAsync(cancellationToken);
            equilibrium = range is null
                ? null
                : new EquilibriumRangeSnapshot(range.HasRange, Text(range.Min), Text(range.Avg), Text(range.Max), range.DetailsFetchedAtUtc);
        }

        return new ValuationGaugeSnapshotSet(
            ValuationGaugeMatchOutcome.Matched,
            ticker.Ticker,
            isin.Length == 0 ? null : isin,
            catalog,
            pe,
            ps,
            equilibrium);
    }

    private static RatioGaugeSnapshot? Ratio(CyclicalWavesV2GaugeDetailRow? row) => row is null
        ? null
        : new RatioGaugeSnapshot(
            Text(row.Close),
            row.Start.ToString(),
            row.Min.ToString(),
            row.Avg.ToString(),
            row.Max.ToString(),
            row.End.ToString(),
            [row.A, row.B, row.C, row.D, row.E, row.F],
            row.Status,
            row.FetchedAtUtc,
            Text(row.Forward));

    private static string? Text(ExactDecimal? value) => value?.ToString();

    internal static string NormalizeIsin(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
}

/// <summary>
/// The one vendor-ticker match for a gauge security, shared by the snapshot reader and the forward-estimate input
/// reader: the CyclicalWaves identity (<c>cw2:</c> external id), or a unique active ticker with the company's ISIN.
/// </summary>
internal static class ValuationGaugeTickerMatcher
{
    public static async Task<(ValuationGaugeMatchOutcome, CyclicalWavesV2TickerRow?)> MatchAsync(
        FinancialIngestionDbContext db,
        ValuationGaugeSecurity security,
        CancellationToken cancellationToken)
    {
        var external = security.ExternalCompanyId;
        if (external is not null &&
            external.StartsWith(CyclicalWavesV2CanonicalEntityResolver.ExternalIdPrefix, StringComparison.Ordinal) &&
            Guid.TryParse(external[CyclicalWavesV2CanonicalEntityResolver.ExternalIdPrefix.Length..], out var tickerId))
        {
            var byId = await db.CyclicalWavesV2Tickers.AsNoTracking()
                .Where(row => row.Id == tickerId && row.IsActive)
                .FirstOrDefaultAsync(cancellationToken);
            return byId is null ? (ValuationGaugeMatchOutcome.Missing, null) : (ValuationGaugeMatchOutcome.Matched, byId);
        }

        if (security.CanonicalId is not { } companyId)
            return (ValuationGaugeMatchOutcome.Missing, null);
        var symbolIsin = await db.Companies.AsNoTracking()
            .Where(row => row.Id == companyId)
            .Select(row => row.SymbolIsin)
            .FirstOrDefaultAsync(cancellationToken);
        var isin = ValuationGaugeSnapshotReader.NormalizeIsin(symbolIsin);
        if (isin.Length == 0)
            return (ValuationGaugeMatchOutcome.Missing, null);

        var candidates = await db.CyclicalWavesV2Tickers.AsNoTracking()
            .Where(row => row.IsActive && row.Enticker != null && row.Enticker.Trim().ToUpper() == isin)
            .Take(2)
            .ToListAsync(cancellationToken);
        return candidates.Count switch
        {
            1 => (ValuationGaugeMatchOutcome.Matched, candidates[0]),
            0 => (ValuationGaugeMatchOutcome.Missing, null),
            _ => (ValuationGaugeMatchOutcome.Ambiguous, null)
        };
    }
}

/// <summary>
/// Canonical quote for the equilibrium needle: today's intraday trade or the daily row, chosen by
/// <see cref="ValuationGaugeQuoteSelection"/> (change «equilibrium-intraday-price»). Unlike relative valuation, an intraday
/// trade of today has no freshness limit, because the intraday poll stops when the market closes.
/// </summary>
public sealed class ValuationGaugeQuoteReader(
    PersistedMarketDataProvider marketData,
    IOptions<ValuationGaugeCardsOptions> options) : IValuationGaugeQuoteReader
{
    public async Task<ValuationGaugeQuote?> ReadAsync(string isin, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var normalized = ValuationGaugeSnapshotReader.NormalizeIsin(isin);
        if (normalized.Length == 0) return null;
        // Options are validated at startup; the fallback only guards a host that skipped validation.
        var threshold = ValuationGaugeQuoteSelection.TryParseTehranTime(options.Value.DailyPriceReadyTehran, out var parsed)
            ? parsed
            : new TimeOnly(19, 0);
        var (intraday, daily) = await marketData.GetSessionTradeCandidatesAsync(normalized, now, cancellationToken);
        return ValuationGaugeQuoteSelection.Select(Map(intraday), Map(daily), now, threshold);
    }

    private static ValuationGaugeQuote? Map(MarketQuoteObservation? quote) =>
        quote is null || quote.LatestPrice <= 0
            ? null
            : new ValuationGaugeQuote(
                quote.LatestPrice,
                quote.SourceEvidence.SourceProvider,
                string.IsNullOrWhiteSpace(quote.SourceLabel)
                    ? quote.Source == MarketQuoteSource.LiveQuote ? "IntradayToday" : "LatestDailyFallback"
                    : quote.SourceLabel,
                quote.AsOf,
                quote.TradingDate == default ? null : quote.TradingDate);
}

/// <summary>
/// Low-cardinality metrics: per-kind outcomes, snapshot age, latency and coverage, and the forward-estimate outcome per
/// ratio card. A failed or timed-out estimator input read is logged once per request, without values.
/// </summary>
public sealed class ValuationGaugeTelemetry(ILogger<ValuationGaugeTelemetry>? logger = null) : IValuationGaugeObserver
{
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;
    public const string MeterName = "FinancialCopilot.ValuationGauges";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Cards = Meter.CreateCounter<long>("valuation_gauge.cards", description: "Projected gauge cards by kind, state and reason.");
    private static readonly Histogram<double> FetchAge = Meter.CreateHistogram<double>("valuation_gauge.snapshot_age", unit: "h", description: "Vendor snapshot fetch age at answer time.");
    private static readonly Histogram<double> Latency = Meter.CreateHistogram<double>("valuation_gauge.latency", unit: "ms", description: "Card generation latency.");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("valuation_gauge.requests", description: "Gauge requests by vendor match outcome and usability.");
    private static readonly Counter<long> Forward = Meter.CreateCounter<long>("valuation_gauge.forward_estimates", description: "Forward values on ratio cards by kind, shown, reason and source (provider or sapio).");
    private static readonly Histogram<double> ForwardLatency = Meter.CreateHistogram<double>("valuation_gauge.forward_read_latency", unit: "ms", description: "Forward-estimate input read latency.");

    public void CardProjected(ValuationGaugeKind kind, ValuationGaugeCardState state, string reasonCode, TimeSpan? fetchAge)
    {
        var tags = new TagList { { "kind", kind.ToString() }, { "state", state.ToString() }, { "reason", reasonCode } };
        Cards.Add(1, tags);
        if (fetchAge is { } age)
            FetchAge.Record(age.TotalHours, new TagList { { "kind", kind.ToString() } });
    }

    public void Completed(TimeSpan elapsed, int displayableCards, string matchOutcome)
    {
        Latency.Record(elapsed.TotalMilliseconds, new TagList { { "match", matchOutcome } });
        Requests.Add(1, new TagList { { "match", matchOutcome }, { "usable", displayableCards > 0 } });
    }

    public void ForwardEstimated(ValuationGaugeKind kind, bool shown, string reasonCode, TimeSpan? readElapsed)
    {
        Forward.Add(1, new TagList { { "kind", kind.ToString() }, { "shown", shown }, { "reason", reasonCode }, { "source", ValuationGaugeForwardSources.Sapio } });
        if (readElapsed is { } elapsed)
            ForwardLatency.Record(elapsed.TotalMilliseconds, new TagList { { "kind", kind.ToString() } });
    }

    public void ProviderForwardProjected(ValuationGaugeKind kind, bool shown, string reasonCode) =>
        Forward.Add(1, new TagList { { "kind", kind.ToString() }, { "shown", shown }, { "reason", reasonCode }, { "source", ValuationGaugeForwardSources.Provider } });

    public void ForwardReadFailed(string reasonCode, TimeSpan elapsed, Exception? error) =>
        _logger.LogWarning(error, "Valuation gauge forward estimate dropped: {ReasonCode} after {ElapsedMs} ms.", reasonCode, (long)elapsed.TotalMilliseconds);
}
