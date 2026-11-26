using System.Diagnostics;
using System.Diagnostics.Metrics;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.FinancialData.ValuationGauges;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.MarketViews;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using Microsoft.EntityFrameworkCore;
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
        var (match, ticker) = await MatchAsync(security, cancellationToken);
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

    private async Task<(ValuationGaugeMatchOutcome, CyclicalWavesV2TickerRow?)> MatchAsync(
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
        var isin = NormalizeIsin(symbolIsin);
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
            row.FetchedAtUtc);

    private static string? Text(ExactDecimal? value) => value?.ToString();

    internal static string NormalizeIsin(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
}

/// <summary>Canonical quote for the equilibrium needle; same market-session rules as relative valuation.</summary>
public sealed class ValuationGaugeQuoteReader(
    PersistedMarketDataProvider marketData,
    IOptions<MarketViewOptions> marketOptions) : IValuationGaugeQuoteReader
{
    public async Task<ValuationGaugeQuote?> ReadAsync(string isin, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var normalized = ValuationGaugeSnapshotReader.NormalizeIsin(isin);
        if (normalized.Length == 0) return null;
        var quotes = await marketData.GetCanonicalTradePricesAsync(
            [normalized], now, TimeSpan.FromMinutes(marketOptions.Value.StaleAfterMinutes), cancellationToken);
        var quote = quotes.FirstOrDefault(item => string.Equals(item.SymbolCode.Value, normalized, StringComparison.Ordinal));
        if (quote is null || quote.LatestPrice <= 0) return null;
        return new ValuationGaugeQuote(
            quote.LatestPrice,
            quote.SourceEvidence.SourceProvider,
            string.IsNullOrWhiteSpace(quote.SourceLabel)
                ? quote.Source == MarketQuoteSource.LiveQuote ? "IntradayToday" : "LatestDailyFallback"
                : quote.SourceLabel,
            quote.AsOf,
            quote.TradingDate == default ? null : quote.TradingDate);
    }
}

/// <summary>Low-cardinality metrics: per-kind outcomes, snapshot age, latency and coverage.</summary>
public sealed class ValuationGaugeTelemetry : IValuationGaugeObserver
{
    public const string MeterName = "FinancialCopilot.ValuationGauges";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Cards = Meter.CreateCounter<long>("valuation_gauge.cards", description: "Projected gauge cards by kind, state and reason.");
    private static readonly Histogram<double> FetchAge = Meter.CreateHistogram<double>("valuation_gauge.snapshot_age", unit: "h", description: "Vendor snapshot fetch age at answer time.");
    private static readonly Histogram<double> Latency = Meter.CreateHistogram<double>("valuation_gauge.latency", unit: "ms", description: "Card generation latency.");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("valuation_gauge.requests", description: "Gauge requests by vendor match outcome and usability.");

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
}
