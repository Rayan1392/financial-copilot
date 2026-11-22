using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.MarketViews;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

internal sealed record RelativeValuationSecurity(Guid CompanyId, string? Isin);

internal sealed record RelativeValuationObservation(RelativeValuationSourceFact Fact, DateTimeOffset ValidUntilUtc);

public sealed class CyclicalWavesV2RelativeValuationInputReader(
    FinancialIngestionDbContext db,
    PersistedMarketDataProvider marketData,
    IOptions<MarketViewOptions> marketOptions,
    ILogger<CyclicalWavesV2RelativeValuationInputReader> logger)
{
    public const string SourceVersion = "cyclicalwaves-v2";

    internal async Task<IReadOnlyList<RelativeValuationObservation>> ReadAsync(
        IReadOnlyList<RelativeValuationSecurity> securities, DateTimeOffset now,
        TimeSpan freshness, CancellationToken cancellationToken)
    {
        var tickers = await db.CyclicalWavesV2Tickers.AsNoTracking()
            .Where(row => row.IsActive && row.Enticker != null).ToListAsync(cancellationToken);
        var byIsin = tickers.ToLookup(row => NormalizeIsin(row.Enticker), StringComparer.Ordinal);
        var normalized = securities.Select(row => new { Security = row, Isin = NormalizeIsin(row.Isin) }).ToArray();
        var unique = normalized.Where(row => row.Isin.Length > 0)
            .GroupBy(row => row.Isin, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .Select(group => group.Single()).ToArray();
        var matched = unique.Where(row => byIsin[row.Isin].Count() == 1)
            .Select(row => (row.Security, row.Isin, Ticker: byIsin[row.Isin].Single())).ToArray();
        var missing = normalized.Count(row => row.Isin.Length == 0 || !byIsin[row.Isin].Any());
        logger.LogInformation("Relative valuation v2 identity coverage: matched {Matched}/{Eligible}; missing ISIN or ticker {Missing}; ambiguous ISIN {Ambiguous}.",
            matched.Length, securities.Count, missing, normalized.Length - matched.Length - missing);

        var tickerIds = matched.Select(row => row.Ticker.Id).ToArray();
        var availability = await db.CyclicalWavesV2GaugeAvailability.AsNoTracking()
            .Where(row => tickerIds.Contains(row.TickerId)).ToDictionaryAsync(row => row.TickerId, cancellationToken);
        var gauges = await db.CyclicalWavesV2GaugeDetails.AsNoTracking()
            .Where(row => tickerIds.Contains(row.TickerId)).ToListAsync(cancellationToken);
        var gaugeByTicker = gauges.ToLookup(row => row.TickerId);
        var ranges = await db.CyclicalWavesV2EquilibriumRanges.AsNoTracking()
            .Where(row => tickerIds.Contains(row.TickerId) && row.HasRange && row.DetailsFetchedAtUtc != null &&
                row.DetailsFetchedAtUtc > now - freshness && row.DetailsFetchedAtUtc <= now)
            .ToDictionaryAsync(row => row.TickerId, cancellationToken);

        var priceFreshness = TimeSpan.FromMinutes(marketOptions.Value.StaleAfterMinutes);
        // Use the normalized ISIN as the quote key so duplicate ticker text cannot conflate instruments.
        var quotes = await marketData.GetCanonicalTradePricesAsync(
            matched.Where(row => ranges.ContainsKey(row.Ticker.Id)).Select(row => row.Isin).ToArray(),
            now, priceFreshness, cancellationToken);
        var prices = quotes.ToDictionary(quote => quote.SymbolCode.Value, StringComparer.Ordinal);
        var local = now.ToOffset(TimeSpan.FromHours(3.5));
        var nextTehranDate = new DateTimeOffset(local.Date.AddDays(1), local.Offset).ToUniversalTime();

        var result = new List<RelativeValuationObservation>();
        foreach (var (security, isin, ticker) in matched)
        {
            if (availability.TryGetValue(ticker.Id, out var flags))
            {
                foreach (var gauge in gaugeByTicker[ticker.Id])
                {
                    var isPe = gauge.GaugeType == "PE";
                    if (isPe ? !flags.HasPe : !flags.HasPs) continue;
                    Add(security.CompanyId, isPe ? RelativeValuationMetric.Pe : RelativeValuationMetric.Ps,
                        AsDecimal(gauge.Close), AsDecimal(gauge.Avg), gauge.FetchedAtUtc,
                        $"{ticker.Id:D}/{gauge.GaugeType}", gauge.FetchedAtUtc + freshness, null);
                }
            }

            if (!ranges.TryGetValue(ticker.Id, out var range) || range.DetailsFetchedAtUtc is not { } fetchedAt ||
                !prices.TryGetValue(isin, out var price)) continue;
            var expires = fetchedAt + freshness;
            if (price.SourceLabel == "IntradayToday")
                expires = new[] { expires, price.AsOf + priceFreshness, nextTehranDate }.Min();
            var priceEvidence = JsonSerializer.Serialize(new
            {
                Kind = price.SourceLabel,
                price.TradingDate,
                price.AsOf,
                price.TradingInstrumentId,
                price.SourceEvidence.SourceProvider,
                Price = price.LatestPrice
            });
            Add(security.CompanyId, RelativeValuationMetric.Equilibrium, price.LatestPrice,
                AsDecimal(range.Avg), fetchedAt, $"{ticker.Id:D}/equilibrium", expires, priceEvidence);
        }
        logger.LogInformation("Relative valuation v2 input coverage: usable P/E {Pe}, P/S {Ps}, equilibrium {Equilibrium}; intraday prices {Intraday}, daily prices {Daily}.",
            result.Count(row => row.Fact.Metric == RelativeValuationMetric.Pe),
            result.Count(row => row.Fact.Metric == RelativeValuationMetric.Ps),
            result.Count(row => row.Fact.Metric == RelativeValuationMetric.Equilibrium),
            quotes.Count(quote => quote.Source == MarketQuoteSource.LiveQuote),
            quotes.Count(quote => quote.Source == MarketQuoteSource.PreviousTradingDay));
        return result;

        void Add(Guid companyId, RelativeValuationMetric metric, decimal? current, decimal? reference,
            DateTimeOffset fetchedAt, string observationId, DateTimeOffset expires, string? priceEvidence)
        {
            if (fetchedAt > now || expires <= now || current is not > 0m || reference is not > 0m) return;
            // Leave enough integer digits for the persisted IQR fences as well as the inputs and percent.
            if (current >= 100_000_000_000_000m || reference >= 100_000_000_000_000m ||
                current / reference >= 100_000_000_000m) return;
            var watermark = FormattableString.Invariant($"{current:G29}|{reference:G29}|{priceEvidence}");
            result.Add(new(new(companyId, metric, current, reference,
                SourceObservationTimestamp: fetchedAt, PersistedAtUtc: fetchedAt,
                SourceObservationId: observationId, SourceVersion: SourceVersion, SourceWatermark: watermark), expires));
        }
    }

    private static string NormalizeIsin(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static decimal? AsDecimal(ExactDecimal? value)
    {
        if (value is null) return null;
        var text = value.Value.ToString();
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ||
            !value.Value.Equals(ExactDecimal.Parse(number.ToString(CultureInfo.InvariantCulture))))
            return null;
        return decimal.Round(number, 14, MidpointRounding.AwayFromZero);
    }
}
