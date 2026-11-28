using FinancialCopilot.Application.FinancialData.Options;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Options;

/// <summary>
/// Feature 168: the stored market state behind the option comparison — contracts with all five
/// order-book levels for several expiries, contract lookup by symbol, the underlyings that have
/// options, and the underlying's daily closes. Read-only; a fixed number of queries per call.
/// </summary>
public sealed partial class EfCoreOptionChainReader : IOptionMarketSnapshotReader
{
    public async Task<OptionMarketSnapshot?> GetAsync(
        string underlyingSymbol, DateOnly? expiryDate, int maxExpiries, CancellationToken cancellationToken)
    {
        var wanted = PersianSymbolNormalizer.Normalize(underlyingSymbol);
        if (wanted.Length == 0) return null;
        if (await ResolveUnderlyingAsync(wanted, cancellationToken) is not { } underlying) return null;

        var today = Today();
        var underlyingIds = underlying.InstrumentIds;
        var contracts = await dbContext.OptionContracts.AsNoTracking()
            .Where(row => underlyingIds.Contains(row.UnderlyingInstrumentId) && row.ExpiryDate >= today)
            .ToListAsync(cancellationToken);
        if (contracts.Count == 0) return null;

        var expiryDates = contracts.Select(row => row.ExpiryDate).Distinct().OrderBy(date => date).ToArray();
        var selectedExpiries = expiryDate is { } requested
            ? expiryDates.Where(date => date == requested).ToArray()
            : expiryDates.Take(Math.Max(1, maxExpiries)).ToArray();
        var selected = contracts.Where(row => selectedExpiries.Contains(row.ExpiryDate)).ToArray();
        var contractIds = selected.Select(row => row.TradingInstrumentId).ToArray();

        var symbols = await dbContext.TradingInstruments.AsNoTracking()
            .Where(row => contractIds.Contains(row.Id))
            .Select(row => new { row.Id, row.Symbol })
            .ToDictionaryAsync(row => row.Id, row => row.Symbol, cancellationToken);
        var levels = (await dbContext.OptionOrderBookLevels.AsNoTracking()
                .Where(row => contractIds.Contains(row.TradingInstrumentId))
                .ToListAsync(cancellationToken))
            .ToLookup(row => row.TradingInstrumentId);

        var quoteIds = contractIds.Concat(underlyingIds).ToArray();
        var quotes = (await dbContext.LatestMarketQuotes.AsNoTracking()
                .Where(row => quoteIds.Contains(row.TradingInstrumentId))
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.TradingInstrumentId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.AsOf).First());
        var underlyingIsin = await dbContext.TradingInstruments.AsNoTracking()
            .Where(row => underlyingIds.Contains(row.Id) && row.InstrumentIsin != string.Empty)
            .OrderByDescending(row => row.IsActive)
            .Select(row => row.InstrumentIsin)
            .FirstOrDefaultAsync(cancellationToken);
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

        var marketContracts = selected
            .Select(contract => new OptionMarketContract(
                symbols.GetValueOrDefault(contract.TradingInstrumentId) ?? string.Empty,
                Enum.Parse<OptionSide>(contract.Side),
                contract.StrikePrice,
                contract.ExpiryDate,
                contract.ContractSize,
                contract.OpenInterest,
                quotes.GetValueOrDefault(contract.TradingInstrumentId)?.LatestPrice,
                levels[contract.TradingInstrumentId].OrderBy(row => row.Level).Select(ToLevel).OfType<OptionOrderBookLevel>().ToArray()))
            .ToArray();

        return new OptionMarketSnapshot(
            underlying.Symbol,
            underlyingIsin ?? string.Empty,
            underlyingQuote,
            expiryDates,
            marketContracts,
            orderBookObservedAt,
            selected.Length == 0 ? null : selected.Max(row => (DateTimeOffset?)row.OpenInterestObservedAt));
    }

    public async Task<OptionContractReference?> FindContractAsync(string contractSymbol, CancellationToken cancellationToken)
    {
        var wanted = PersianSymbolNormalizer.Normalize(contractSymbol);
        if (wanted.Length == 0) return null;

        var today = Today();
        // Unexpired contracts are a couple of thousand rows; symbols are compared after Persian
        // normalization because stored symbols use Arabic letter forms.
        var candidates = await (
                from contract in dbContext.OptionContracts.AsNoTracking()
                join instrument in dbContext.TradingInstruments.AsNoTracking()
                    on contract.TradingInstrumentId equals instrument.Id
                where contract.ExpiryDate >= today
                select new { instrument.Symbol, contract.UnderlyingInstrumentId, contract.Side, contract.ExpiryDate })
            .ToListAsync(cancellationToken);
        var match = candidates.FirstOrDefault(row => PersianSymbolNormalizer.Normalize(row.Symbol) == wanted);
        if (match is null) return null;

        var underlying = await (
                from instrument in dbContext.TradingInstruments.AsNoTracking()
                join company in dbContext.Companies.AsNoTracking()
                    on instrument.NormalizedCompanyId equals company.Id into companyRows
                from company in companyRows.DefaultIfEmpty()
                where instrument.Id == match.UnderlyingInstrumentId
                select new { instrument.Symbol, TseSymbol = company == null ? null : company.TseSymbol })
            .FirstOrDefaultAsync(cancellationToken);
        if (underlying is null) return null;

        return new OptionContractReference(
            match.Symbol,
            string.IsNullOrWhiteSpace(underlying.TseSymbol) ? underlying.Symbol : underlying.TseSymbol,
            Enum.Parse<OptionSide>(match.Side),
            match.ExpiryDate);
    }

    public async Task<IReadOnlyList<string>> ListUnderlyingSymbolsAsync(CancellationToken cancellationToken)
    {
        var today = Today();
        var rows = await (
                from instrument in dbContext.TradingInstruments.AsNoTracking()
                join company in dbContext.Companies.AsNoTracking()
                    on instrument.NormalizedCompanyId equals company.Id into companyRows
                from company in companyRows.DefaultIfEmpty()
                where dbContext.OptionContracts.Any(contract =>
                    contract.UnderlyingInstrumentId == instrument.Id && contract.ExpiryDate >= today)
                select new { instrument.Symbol, TseSymbol = company == null ? null : company.TseSymbol })
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => string.IsNullOrWhiteSpace(row.TseSymbol) ? row.Symbol : row.TseSymbol!)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<decimal>> GetUnderlyingDailyClosesAsync(
        string underlyingSymbol, int days, CancellationToken cancellationToken)
    {
        var wanted = PersianSymbolNormalizer.Normalize(underlyingSymbol);
        if (wanted.Length == 0 || days < 1) return [];
        if (await ResolveUnderlyingAsync(wanted, cancellationToken) is not { } underlying) return [];

        var underlyingIds = underlying.InstrumentIds;
        // A few rows per day at most (one per source); bounded by the window, newest first.
        var rows = await dbContext.DailyInstrumentTrades.AsNoTracking()
            .Where(row => underlyingIds.Contains(row.TradingInstrumentId) && row.ClosingPrice > 0)
            .OrderByDescending(row => row.TradingDate)
            .Select(row => new { row.TradingDate, row.ClosingPrice })
            .Take(days * 3)
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => row.TradingDate)
            .OrderByDescending(group => group.Key)
            .Take(days)
            .Select(group => group.First().ClosingPrice)
            .Reverse()
            .ToArray();
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(MarketOffset).DateTime);

    private static OptionOrderBookLevel? ToLevel(OptionOrderBookLevelRow row) =>
        OptionOrderBookLevel.TryCreate(
            row.Level,
            row.BidPrice ?? 0, row.BidVolume ?? 0, row.BidOrderCount ?? 0,
            row.AskPrice ?? 0, row.AskVolume ?? 0, row.AskOrderCount ?? 0,
            out var level)
            ? level
            : null;
}
