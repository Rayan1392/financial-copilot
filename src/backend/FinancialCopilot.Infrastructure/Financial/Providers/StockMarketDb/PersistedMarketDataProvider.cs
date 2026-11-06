using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;

public sealed class PersistedMarketDataProvider(
    FinancialIngestionDbContext dbContext,
    //IMarketQuoteSourcePriority sourcePriority,
    TimeProvider timeProvider) : IMarketDataProvider
{
    // PrimarySourceName is evaluated per-request so a live config change (Phase 4 cutover)
    // takes effect without restarting the process.
    //private string _providerName => sourcePriority.PrimarySourceName;

    public async Task<BatchMarketQuoteResult> GetLatestQuotesAsync(
        IReadOnlyCollection<SymbolCode> symbols,
        CancellationToken cancellationToken)
    {
        var codes = symbols.Select(symbol => symbol.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var irstOffset = TimeSpan.FromHours(3.5);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(irstOffset).DateTime);

        var observations = new Dictionary<string, MarketQuoteObservation>(StringComparer.OrdinalIgnoreCase);

        // Canonical trade tables, resolved for all symbols at once: a fixed handful of queries instead of
        // ~4 sequential round trips per symbol (the watchlist took ~1.2 s for four symbols).
        var instrumentIdsBySymbol = await ResolveInstrumentIdsAsync(
            symbols.Select(symbol => symbol.Value).Distinct().ToList(),
            cancellationToken);
        foreach (var observation in await ResolveFromCanonicalTradeTablesAsync(instrumentIdsBySymbol, today, cancellationToken))
        {
            observations[observation.SymbolCode.Value] = observation;
        }

        // Two resolution paths, unioned:
        // 1. Company-linked: quote -> instrument -> normalized company -> TseSymbol. This is the
        //    canonical normalized route (Spec 068: Symbols table removed; use company.TseSymbol).
        //    It depends on TradingInstruments.NormalizedCompanyId, which is null for most
        //    non-company instruments and can transiently point at the "other" provider-scoped
        //    duplicate of a company between instrument syncs.
        // 2. Direct ticker: the instrument's own TSE symbol equals the requested code. This keeps
        //    quotes reachable when the company linkage is absent or temporarily mismatched.
        var companyLinked =
            from quote in dbContext.LatestMarketQuotes.AsNoTracking()
            join instrument in dbContext.TradingInstruments.AsNoTracking()
                on quote.TradingInstrumentId equals instrument.Id
            join company in dbContext.Companies.AsNoTracking()
                on instrument.NormalizedCompanyId equals company.Id
            where company.TseSymbol != null && codes.Contains(company.TseSymbol)
            select new { quote, SymbolCode = company.TseSymbol! };

        var directTicker =
            from quote in dbContext.LatestMarketQuotes.AsNoTracking()
            join instrument in dbContext.TradingInstruments.AsNoTracking()
                on quote.TradingInstrumentId equals instrument.Id
            where codes.Contains(instrument.Symbol)
            select new { quote, SymbolCode = instrument.Symbol };

        var rows = await companyLinked
            .Concat(directTicker)
            .ToListAsync(cancellationToken);

        var projectionObservations = rows
            .GroupBy(row => row.SymbolCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.quote.AsOf).First())
            .Select(item => new MarketQuoteObservation(
                new SymbolCode(item.SymbolCode),
                item.quote.LatestPrice,
                item.quote.PriceChangePercentage,
                item.quote.AsOf,
                item.quote.SourceKind == "Intraday" && item.quote.TradingDate == today
                    ? MarketQuoteSource.LiveQuote
                    : MarketQuoteSource.PreviousTradingDay,
                new FinancialSourceEvidence(item.quote.ProviderName, item.quote.AsOf, item.quote.AsOf),
                item.quote.TradingDate,
                item.quote.SourceKind == "Intraday" && item.quote.TradingDate == today
                    ? "IntradayToday"
                    : "LatestDailyFallback"))
            .ToArray();

        foreach (var observation in projectionObservations)
        {
            observations.TryAdd(observation.SymbolCode.Value, observation);
        }

        var available = observations.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unavailable = symbols.Where(item => !available.Contains(item.Value)).ToArray();
        return new BatchMarketQuoteResult(observations.Values.ToArray(), unavailable);
    }

    /// <summary>
    /// Latest canonical trade per symbol, for all symbols in two aggregate queries: today's latest intraday
    /// snapshot per instrument, else the latest daily trade per instrument. Same precedence as before (intraday
    /// today wins; latest TradingTime/TradingDate, then latest ReceivedAt/SourceInsertedAt), but the "latest per
    /// instrument" is a GROUP BY MAX joined back, which uses the (TradingInstrumentId, TradingDate) indexes.
    /// </summary>
    private async Task<IReadOnlyList<MarketQuoteObservation>> ResolveFromCanonicalTradeTablesAsync(
        IReadOnlyDictionary<string, List<Guid>> instrumentIdsBySymbol,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var allIds = instrumentIdsBySymbol.Values.SelectMany(ids => ids).Distinct().ToList();
        if (allIds.Count == 0)
        {
            return [];
        }

        var todays = dbContext.IntradayTradeSnapshots.AsNoTracking()
            .Where(row => allIds.Contains(row.TradingInstrumentId) && row.TradingDate == today);
        var latestIntradayTimes = todays
            .GroupBy(row => row.TradingInstrumentId)
            .Select(group => new { InstrumentId = group.Key, TradingTime = group.Max(row => row.TradingTime) });
        var intradayRows = await (
                from row in todays
                join latest in latestIntradayTimes
                    on new { InstrumentId = row.TradingInstrumentId, row.TradingTime }
                    equals new { latest.InstrumentId, latest.TradingTime }
                select row)
            .ToListAsync(cancellationToken);

        var needDaily = instrumentIdsBySymbol
            .Where(entry => !intradayRows.Any(row => entry.Value.Contains(row.TradingInstrumentId)))
            .SelectMany(entry => entry.Value)
            .Distinct()
            .ToList();
        var dailyRows = new List<DailyInstrumentTradeRow>();
        if (needDaily.Count > 0)
        {
            var trades = dbContext.DailyInstrumentTrades.AsNoTracking()
                .Where(row => needDaily.Contains(row.TradingInstrumentId));
            var latestDates = trades
                .GroupBy(row => row.TradingInstrumentId)
                .Select(group => new { InstrumentId = group.Key, TradingDate = group.Max(row => row.TradingDate) });
            dailyRows = await (
                    from row in trades
                    join latest in latestDates
                        on new { InstrumentId = row.TradingInstrumentId, row.TradingDate }
                        equals new { latest.InstrumentId, latest.TradingDate }
                    select row)
                .ToListAsync(cancellationToken);
        }

        var result = new List<MarketQuoteObservation>();
        foreach (var (symbolCode, ids) in instrumentIdsBySymbol)
        {
            var intraday = intradayRows
                .Where(row => ids.Contains(row.TradingInstrumentId))
                .OrderByDescending(row => row.TradingTime)
                .ThenByDescending(row => row.ReceivedAt)
                .FirstOrDefault();
            if (intraday is not null)
            {
                result.Add(new MarketQuoteObservation(
                    new SymbolCode(symbolCode),
                    intraday.LastTradedPrice,
                    CalculatePriceChangePercentage(intraday.LastTradedPrice, intraday.PriceYesterday),
                    intraday.ReceivedAt,
                    MarketQuoteSource.LiveQuote,
                    new FinancialSourceEvidence(intraday.ProviderName, intraday.ReceivedAt, intraday.ReceivedAt),
                    intraday.TradingDate,
                    "IntradayToday"));
                continue;
            }

            var daily = dailyRows
                .Where(row => ids.Contains(row.TradingInstrumentId))
                .OrderByDescending(row => row.TradingDate)
                .ThenByDescending(row => row.SourceInsertedAt)
                .FirstOrDefault();
            if (daily is null)
            {
                continue;
            }

            result.Add(new MarketQuoteObservation(
                new SymbolCode(symbolCode),
                daily.LastTradedPrice,
                CalculatePriceChangePercentage(daily.LastTradedPrice, daily.PriceYesterday),
                daily.SourceInsertedAt,
                MarketQuoteSource.PreviousTradingDay,
                new FinancialSourceEvidence(daily.ProviderName, daily.SourceInsertedAt, daily.SourceInsertedAt),
                daily.TradingDate,
                "LatestDailyFallback"));
        }

        return result;
    }

    /// <summary>
    /// Instrument ids per requested symbol, same precedence as the former per-symbol resolver: eligible
    /// Noavaran companies first; only when they yield no instrument, all matching companies (by instrument
    /// code and normalized company link) plus instruments whose own ticker is the symbol. Symbols with no
    /// instrument are omitted. All symbols share each query.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, List<Guid>>> ResolveInstrumentIdsAsync(
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        static bool Matches(string symbol, string? ticker, string? tseSymbol, string? companySymbol) =>
            ticker == symbol || tseSymbol == symbol || companySymbol == symbol;

        var eligible = await NoavaranCompanyScope.EligibleCompanies(
                dbContext,
                NadpcoApiCompanyNormalizer.NadpcoApiProviderName)
            .Where(row =>
                row.InstrumentCode != null &&
                (codes.Contains(row.Ticker!) || codes.Contains(row.TseSymbol!) || codes.Contains(row.CompanySymbol!)))
            .Select(row => new { row.Ticker, row.TseSymbol, row.CompanySymbol, InstrumentCode = row.InstrumentCode! })
            .ToListAsync(cancellationToken);
        var eligibleIdsByCode = await ResolveInstrumentIdsByCodeAsync(
            eligible.Select(row => row.InstrumentCode).ToList(),
            cancellationToken);

        var result = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        foreach (var code in codes)
        {
            var ids = eligible
                .Where(row => Matches(code, row.Ticker, row.TseSymbol, row.CompanySymbol))
                .SelectMany(row => eligibleIdsByCode.TryGetValue(row.InstrumentCode, out var found) ? found : [])
                .Distinct()
                .ToList();
            if (ids.Count > 0) result[code] = ids;
            else unresolved.Add(code);
        }

        if (unresolved.Count == 0)
        {
            return result;
        }

        var companies = await dbContext.Companies.AsNoTracking()
            .Where(row =>
                codes.Contains(row.Ticker!) || codes.Contains(row.TseSymbol!) || codes.Contains(row.CompanySymbol!))
            .Select(row => new { row.Id, row.InstrumentCode, row.Ticker, row.TseSymbol, row.CompanySymbol })
            .ToListAsync(cancellationToken);
        companies = companies
            .Where(row => unresolved.Any(code => Matches(code, row.Ticker, row.TseSymbol, row.CompanySymbol)))
            .ToList();
        var companyIdsByCode = await ResolveInstrumentIdsByCodeAsync(
            companies.Where(row => row.InstrumentCode != null).Select(row => row.InstrumentCode!).ToList(),
            cancellationToken);
        var companyIds = companies.Select(row => row.Id).Distinct().ToList();
        var linkedInstruments = companyIds.Count == 0
            ? []
            : await dbContext.TradingInstruments.AsNoTracking()
                .Where(row => row.NormalizedCompanyId != null && companyIds.Contains(row.NormalizedCompanyId.Value))
                .Select(row => new { CompanyId = row.NormalizedCompanyId!.Value, row.Id })
                .ToListAsync(cancellationToken);
        var tickerInstruments = await dbContext.TradingInstruments.AsNoTracking()
            .Where(row => unresolved.Contains(row.Symbol))
            .Select(row => new { row.Symbol, row.Id })
            .ToListAsync(cancellationToken);

        foreach (var code in unresolved)
        {
            var matched = companies.Where(row => Matches(code, row.Ticker, row.TseSymbol, row.CompanySymbol)).ToList();
            var ids = matched
                .Where(row => row.InstrumentCode != null)
                .SelectMany(row => companyIdsByCode.TryGetValue(row.InstrumentCode!, out var found) ? found : [])
                .Concat(linkedInstruments.Where(row => matched.Any(company => company.Id == row.CompanyId)).Select(row => row.Id))
                .Concat(tickerInstruments.Where(row => row.Symbol == code).Select(row => row.Id))
                .Distinct()
                .ToList();
            if (ids.Count > 0) result[code] = ids;
        }

        return result;
    }

    /// <summary>Instrument ids per company instrument code (string, as stored on companies), in one query.</summary>
    private async Task<IReadOnlyDictionary<string, List<Guid>>> ResolveInstrumentIdsByCodeAsync(
        IReadOnlyCollection<string> instrumentCodes,
        CancellationToken cancellationToken)
    {
        var parsed = instrumentCodes
            .Distinct()
            .Select(code => (Code: code, Value: long.TryParse(code, out var value) ? value : (long?)null))
            .Where(item => item.Value is not null)
            .ToList();
        if (parsed.Count == 0)
        {
            return new Dictionary<string, List<Guid>>();
        }

        var values = parsed.Select(item => item.Value!.Value).Distinct().ToList();
        var rows = await dbContext.TradingInstruments.AsNoTracking()
            .Where(row => values.Contains(row.InstrumentCode))
            .Select(row => new { row.InstrumentCode, row.Id })
            .ToListAsync(cancellationToken);
        return parsed.ToDictionary(
            item => item.Code,
            item => rows.Where(row => row.InstrumentCode == item.Value).Select(row => row.Id).ToList());
    }

    // DAILY_CHANGE_PCT = (LastTradedPrice / PriceYesterday - 1) * 100.
    // ClosingPrice must not be used here; it is a separate metric if ever needed.
    private static decimal CalculatePriceChangePercentage(decimal lastTradedPrice, decimal priceYesterday) =>
        priceYesterday == 0 ? 0 : (lastTradedPrice / priceYesterday - 1m) * 100m;
}
