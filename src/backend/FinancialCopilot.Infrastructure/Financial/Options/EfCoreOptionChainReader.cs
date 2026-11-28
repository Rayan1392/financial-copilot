using FinancialCopilot.Application.FinancialData.Options;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Options;

/// <summary>
/// Builds the option chain of one underlying from the stored contracts, order book and quotes
/// (Feature 167). Read-only; a fixed number of queries per call regardless of chain size.
/// </summary>
public sealed class EfCoreOptionChainReader(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider) : IOptionChainReader
{
    // Iran Standard Time, fixed offset (no daylight saving).
    private static readonly TimeSpan MarketOffset = TimeSpan.FromHours(3.5);
    private const string OrderBookSyncState =
        TsetmcOptionFeedSyncService.SyncStatePrefix + TsetmcOptionFeedSyncService.OrderBookDataset;

    public async Task<OptionChain?> GetChainAsync(
        string underlyingSymbol,
        DateOnly? expiryDate,
        CancellationToken cancellationToken)
    {
        var wanted = PersianSymbolNormalizer.Normalize(underlyingSymbol);
        if (wanted.Length == 0) return null;

        var underlying = await ResolveUnderlyingAsync(wanted, cancellationToken);
        if (underlying is null) return null;

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(MarketOffset).DateTime);
        var underlyingIds = underlying.Value.InstrumentIds;
        var contracts = await dbContext.OptionContracts.AsNoTracking()
            .Where(row => underlyingIds.Contains(row.UnderlyingInstrumentId) && row.ExpiryDate >= today)
            .ToListAsync(cancellationToken);
        if (contracts.Count == 0) return null;

        var expiryDates = contracts.Select(row => row.ExpiryDate).Distinct().OrderBy(date => date).ToArray();
        var expiry = expiryDate ?? expiryDates[0];
        var selected = contracts.Where(row => row.ExpiryDate == expiry).ToArray();
        if (selected.Length == 0) return null;

        var contractIds = selected.Select(row => row.TradingInstrumentId).ToArray();
        var instruments = await dbContext.TradingInstruments.AsNoTracking()
            .Where(row => contractIds.Contains(row.Id))
            .Select(row => new { row.Id, row.Symbol, row.Name })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var bestLevels = await dbContext.OptionOrderBookLevels.AsNoTracking()
            .Where(row => contractIds.Contains(row.TradingInstrumentId) && row.Level == OptionOrderBookLevel.MinLevel)
            .ToDictionaryAsync(row => row.TradingInstrumentId, cancellationToken);

        var quoteIds = contractIds.Concat(underlyingIds).ToArray();
        var quotes = (await dbContext.LatestMarketQuotes.AsNoTracking()
                .Where(row => quoteIds.Contains(row.TradingInstrumentId))
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.TradingInstrumentId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.AsOf).First());

        // Closing price comes from the last intraday snapshot of each contract's latest session.
        var contractQuoteDates = contractIds
            .Select(id => quotes.GetValueOrDefault(id))
            .OfType<LatestMarketQuoteRow>()
            .Select(row => row.TradingDate)
            .Distinct()
            .ToArray();
        var closingPrices = contractQuoteDates.Length == 0
            ? []
            : (await dbContext.IntradayTradeSnapshots.AsNoTracking()
                    .Where(row => contractIds.Contains(row.TradingInstrumentId) &&
                                  contractQuoteDates.Contains(row.TradingDate))
                    .Select(row => new { row.TradingInstrumentId, row.TradingDate, row.TradingTime, row.ClosingPrice })
                    .ToListAsync(cancellationToken))
                .GroupBy(row => (row.TradingInstrumentId, row.TradingDate))
                .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.TradingTime).First().ClosingPrice);

        var orderBookObservedAt = await dbContext.StockMarketSyncStates.AsNoTracking()
            .Where(row => row.Dataset == OrderBookSyncState)
            .Select(row => row.Watermark)
            .SingleOrDefaultAsync(cancellationToken);

        var underlyingQuote = underlyingIds
            .Select(id => quotes.GetValueOrDefault(id))
            .OfType<LatestMarketQuoteRow>()
            .OrderByDescending(row => row.AsOf)
            .Select(row => new OptionUnderlyingQuote(row.LatestPrice, row.TradingDate, row.AsOf))
            .FirstOrDefault();

        OptionQuote ToQuote(OptionContractRow contract)
        {
            var instrument = instruments.GetValueOrDefault(contract.TradingInstrumentId);
            var level = bestLevels.GetValueOrDefault(contract.TradingInstrumentId);
            var quote = quotes.GetValueOrDefault(contract.TradingInstrumentId);
            return new OptionQuote(
                Symbol: instrument?.Symbol ?? string.Empty,
                Name: instrument?.Name ?? string.Empty,
                Side: Enum.Parse<OptionSide>(contract.Side),
                StrikePrice: contract.StrikePrice,
                ExpiryDate: contract.ExpiryDate,
                ContractSize: contract.ContractSize,
                LastPrice: quote?.LatestPrice,
                ClosingPrice: quote is not null &&
                    closingPrices.TryGetValue((contract.TradingInstrumentId, quote.TradingDate), out var closing) && closing > 0
                        ? closing
                        : null,
                LastTradeDate: quote?.TradingDate,
                BidPrice: level?.BidPrice,
                BidVolume: level?.BidVolume,
                AskPrice: level?.AskPrice,
                AskVolume: level?.AskVolume,
                OpenInterest: contract.OpenInterest,
                OrderBookChangedAt: level?.ObservedAt);
        }

        var strikes = selected
            .GroupBy(row => row.StrikePrice)
            .OrderBy(group => group.Key)
            .Select(group => new OptionChainStrike(
                group.Key,
                group.Where(row => row.Side == nameof(OptionSide.Call)).Select(ToQuote).FirstOrDefault(),
                group.Where(row => row.Side == nameof(OptionSide.Put)).Select(ToQuote).FirstOrDefault()))
            .ToArray();

        return new OptionChain(
            underlying.Value.Symbol,
            underlyingQuote,
            expiry,
            expiryDates,
            strikes,
            orderBookObservedAt,
            selected.Max(row => (DateTimeOffset?)row.OpenInterestObservedAt));
    }

    /// <summary>
    /// Finds the underlying among the instruments that actually have option contracts (a few
    /// dozen), matching the requested symbol against the instrument symbol and the linked
    /// company's symbol after Persian normalization.
    /// </summary>
    private async Task<(string Symbol, Guid[] InstrumentIds)?> ResolveUnderlyingAsync(
        string wanted,
        CancellationToken cancellationToken)
    {
        var candidates = await (
                from instrument in dbContext.TradingInstruments.AsNoTracking()
                join company in dbContext.Companies.AsNoTracking()
                    on instrument.NormalizedCompanyId equals company.Id into companyRows
                from company in companyRows.DefaultIfEmpty()
                where dbContext.OptionContracts.Any(contract => contract.UnderlyingInstrumentId == instrument.Id)
                select new { instrument.Id, instrument.Symbol, TseSymbol = company == null ? null : company.TseSymbol })
            .ToListAsync(cancellationToken);

        var matches = candidates
            .Where(row => PersianSymbolNormalizer.Normalize(row.Symbol) == wanted ||
                          PersianSymbolNormalizer.Normalize(row.TseSymbol) == wanted)
            .ToArray();
        if (matches.Length == 0) return null;

        var symbol = matches.Select(row => row.TseSymbol).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? matches[0].Symbol;
        return (symbol, matches.Select(row => row.Id).Distinct().ToArray());
    }
}
