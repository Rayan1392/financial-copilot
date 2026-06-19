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
    IMarketQuoteSourcePriority sourcePriority,
    TimeProvider timeProvider) : IMarketDataProvider
{
    // PrimarySourceName is evaluated per-request so a live config change (Phase 4 cutover)
    // takes effect without restarting the process.
    private string _providerName => sourcePriority.PrimarySourceName;

    public async Task<BatchMarketQuoteResult> GetLatestQuotesAsync(
        IReadOnlyCollection<SymbolCode> symbols,
        CancellationToken cancellationToken)
    {
        var codes = symbols.Select(symbol => symbol.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var irstOffset = TimeSpan.FromHours(3.5);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(irstOffset).DateTime);

        var observations = new Dictionary<string, MarketQuoteObservation>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in symbols)
        {
            var observation = await ResolveFromCanonicalTradeTablesAsync(
                symbol.Value,
                today,
                cancellationToken);

            if (observation is not null)
            {
                observations[symbol.Value] = observation;
            }
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
                new FinancialSourceEvidence(_providerName, item.quote.AsOf, item.quote.AsOf),
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

    private async Task<MarketQuoteObservation?> ResolveFromCanonicalTradeTablesAsync(
        string symbolCode,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var instrumentIds = await ResolveInstrumentIdsAsync(symbolCode, cancellationToken);
        if (instrumentIds.Count == 0)
        {
            return null;
        }

        var intraday = await dbContext.IntradayTradeSnapshots.AsNoTracking()
            .Where(row =>
                instrumentIds.Contains(row.TradingInstrumentId) &&
                row.TradingDate == today)
            .OrderByDescending(row => row.TradingTime)
            .ThenByDescending(row => row.ReceivedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (intraday is not null)
        {
            return new MarketQuoteObservation(
                new SymbolCode(symbolCode),
                intraday.LastTradedPrice,
                CalculatePriceChangePercentage(intraday.LastTradedPrice, intraday.PriceYesterday),
                intraday.ReceivedAt,
                MarketQuoteSource.LiveQuote,
                new FinancialSourceEvidence(_providerName, intraday.ReceivedAt, intraday.ReceivedAt),
                intraday.TradingDate,
                "IntradayToday");
        }

        var daily = await dbContext.DailyInstrumentTrades.AsNoTracking()
            .Where(row =>
                instrumentIds.Contains(row.TradingInstrumentId))
            .OrderByDescending(row => row.TradingDate)
            .ThenByDescending(row => row.SourceInsertedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (daily is null)
        {
            return null;
        }

        return new MarketQuoteObservation(
            new SymbolCode(symbolCode),
            daily.LastTradedPrice,
            CalculatePriceChangePercentage(daily.LastTradedPrice, daily.PriceYesterday),
            daily.SourceInsertedAt,
            MarketQuoteSource.PreviousTradingDay,
            new FinancialSourceEvidence(_providerName, daily.SourceInsertedAt, daily.SourceInsertedAt),
            daily.TradingDate,
            "LatestDailyFallback");
    }

    private async Task<List<Guid>> ResolveInstrumentIdsAsync(
        string symbolCode,
        CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();

        ids.AddRange(await ResolveEligibleCompanyInstrumentIdsAsync(symbolCode, cancellationToken));
        if (ids.Count > 0)
        {
            return ids.Distinct().ToList();
        }

        ids.AddRange(await ResolveCompanyInstrumentIdsAsync(symbolCode, cancellationToken));
        ids.AddRange(await dbContext.TradingInstruments.AsNoTracking()
            .Where(row => row.Symbol == symbolCode)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken));

        return ids.Distinct().ToList();
    }

    private async Task<List<Guid>> ResolveEligibleCompanyInstrumentIdsAsync(
        string symbolCode,
        CancellationToken cancellationToken)
    {
        var instrumentCodes = await NoavaranCompanyScope.EligibleCompanies(
                dbContext,
                NadpcoApiCompanyNormalizer.NadpcoApiProviderName)
            .Where(row =>
                row.InstrumentCode != null &&
                (row.Ticker == symbolCode || row.TseSymbol == symbolCode || row.CompanySymbol == symbolCode))
            .Select(row => row.InstrumentCode!)
            .ToListAsync(cancellationToken);

        return await ResolveInstrumentIdsByCodeAsync(instrumentCodes, cancellationToken);
    }

    private async Task<List<Guid>> ResolveCompanyInstrumentIdsAsync(
        string symbolCode,
        CancellationToken cancellationToken)
    {
        var matchedCompanies = await dbContext.Companies.AsNoTracking()
            .Where(row =>
                (row.Ticker == symbolCode || row.TseSymbol == symbolCode || row.CompanySymbol == symbolCode))
            .Select(row => new { row.Id, row.InstrumentCode })
            .ToListAsync(cancellationToken);

        var ids = new List<Guid>();
        ids.AddRange(await ResolveInstrumentIdsByCodeAsync(
            matchedCompanies.Select(row => row.InstrumentCode!).ToList(),
            cancellationToken));

        var companyIds = matchedCompanies.Select(row => row.Id).Distinct().ToList();
        if (companyIds.Count > 0)
        {
            ids.AddRange(await dbContext.TradingInstruments.AsNoTracking()
                .Where(row => row.NormalizedCompanyId != null && companyIds.Contains(row.NormalizedCompanyId.Value))
                .Select(row => row.Id)
                .ToListAsync(cancellationToken));
        }

        return ids;
    }

    private async Task<List<Guid>> ResolveInstrumentIdsByCodeAsync(
        IReadOnlyCollection<string> instrumentCodes,
        CancellationToken cancellationToken)
    {
        var parsedCodes = instrumentCodes
            .Select(code => long.TryParse(code, out var parsed) ? parsed : (long?)null)
            .Where(code => code is not null)
            .Select(code => code!.Value)
            .Distinct()
            .ToList();

        if (parsedCodes.Count == 0)
        {
            return [];
        }

        return await dbContext.TradingInstruments.AsNoTracking()
            .Where(row => parsedCodes.Contains(row.InstrumentCode))
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
    }

    // DAILY_CHANGE_PCT = (LastTradedPrice / PriceYesterday - 1) * 100.
    // ClosingPrice must not be used here; it is a separate metric if ever needed.
    private static decimal CalculatePriceChangePercentage(decimal lastTradedPrice, decimal priceYesterday) =>
        priceYesterday == 0 ? 0 : (lastTradedPrice / priceYesterday - 1m) * 100m;
}
