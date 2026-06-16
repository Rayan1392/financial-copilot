using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.ValueObjects;
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
            where quote.ProviderName == _providerName &&
                company.TseSymbol != null && codes.Contains(company.TseSymbol)
            select new { quote, SymbolCode = company.TseSymbol! };

        var directTicker =
            from quote in dbContext.LatestMarketQuotes.AsNoTracking()
            join instrument in dbContext.TradingInstruments.AsNoTracking()
                on quote.TradingInstrumentId equals instrument.Id
            where quote.ProviderName == _providerName && codes.Contains(instrument.Symbol)
            select new { quote, SymbolCode = instrument.Symbol };

        var rows = await companyLinked
            .Concat(directTicker)
            .ToListAsync(cancellationToken);

        // A quote is "live" only when it is an intraday observation for the current trading day.
        // An intraday snapshot left over from a previous session must surface as
        // PreviousTradingDay so the answer never labels stale data as live.
        // TradingDate values from TSETMC always use Iran Standard Time (IRST = UTC+3:30, no DST).
        // Use the same offset here to avoid mismatches when the server runs in UTC or another zone.
        var irstOffset = TimeSpan.FromHours(3.5);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(irstOffset).DateTime);
        var observations = rows
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
                new FinancialSourceEvidence(_providerName, item.quote.AsOf, item.quote.AsOf)))
            .ToArray();
        var available = observations.Select(item => item.SymbolCode.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unavailable = symbols.Where(item => !available.Contains(item.Value)).ToArray();
        return new BatchMarketQuoteResult(observations, unavailable);
    }
}

