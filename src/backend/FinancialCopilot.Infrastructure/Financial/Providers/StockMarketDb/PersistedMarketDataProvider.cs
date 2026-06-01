using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;

public sealed class PersistedMarketDataProvider(
    FinancialIngestionDbContext dbContext,
    IOptions<StockMarketDbProviderOptions> options) : IMarketDataProvider
{
    private readonly string _providerName = options.Value.ProviderName;

    public async Task<BatchMarketQuoteResult> GetLatestQuotesAsync(
        IReadOnlyCollection<SymbolCode> symbols,
        CancellationToken cancellationToken)
    {
        var codes = symbols.Select(symbol => symbol.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = await (
            from quote in dbContext.LatestMarketQuotes.AsNoTracking()
            join instrument in dbContext.TradingInstruments.AsNoTracking()
                on quote.TradingInstrumentId equals instrument.Id
            join company in dbContext.Companies.AsNoTracking()
                on instrument.NormalizedCompanyId equals company.Id
            join symbol in dbContext.Symbols.AsNoTracking()
                on company.Id equals symbol.CompanyId
            where quote.ProviderName == _providerName && codes.Contains(symbol.SymbolCode)
            select new { quote, symbol.SymbolCode })
            .ToListAsync(cancellationToken);

        var observations = rows
            .GroupBy(row => row.SymbolCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.quote.AsOf).First())
            .Select(item => new MarketQuoteObservation(
                new SymbolCode(item.SymbolCode),
                item.quote.LatestPrice,
                item.quote.PriceChangePercentage,
                item.quote.AsOf,
                item.quote.SourceKind == "Intraday" ? MarketQuoteSource.LiveQuote : MarketQuoteSource.PreviousTradingDay,
                new FinancialSourceEvidence(_providerName, item.quote.AsOf, item.quote.AsOf)))
            .ToArray();
        var available = observations.Select(item => item.SymbolCode.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unavailable = symbols.Where(item => !available.Contains(item.Value)).ToArray();
        return new BatchMarketQuoteResult(observations, unavailable);
    }
}

