using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;

/// <summary>
/// Phase 3 parallel validation: compares LatestMarketQuote rows between StockMarketDb
/// (bridge) and TsetmcWebService (direct feed) for matching TradingInstruments (same
/// InstrumentCode). Divergences beyond <see cref="RelativeTolerance"/> are persisted in
/// MarketQuoteMismatches for DataAdmin review.
/// </summary>
public sealed class TsetmcValidationService(
    FinancialIngestionDbContext dbContext,
    IOptions<StockMarketDbProviderOptions> bridgeOptions,
    IOptions<TsetmcWebServiceOptions> directOptions,
    TimeProvider timeProvider,
    ILogger<TsetmcValidationService> logger) : ITsetmcValidationService
{
    // Relative tolerance below which a difference is not flagged (0.1 % default).
    private const decimal RelativeTolerance = 0.001m;

    private readonly string _bridgeProvider = bridgeOptions.Value.ProviderName;
    private readonly string _directProvider = directOptions.Value.ProviderName;

    public bool CanValidate =>
        bridgeOptions.Value.UsePersistedMarketQuotes &&
        directOptions.Value.Enabled &&
        !string.IsNullOrWhiteSpace(directOptions.Value.UserName);

    public async Task<TsetmcValidationResult> ValidateLatestQuotesAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();

        // Load instruments that have quotes from both providers.
        // Join on InstrumentCode (= TSETMC InsCode as long) which is provider-agnostic.
        var bridgeQuotes = await (
                from q in dbContext.LatestMarketQuotes.AsNoTracking()
                join i in dbContext.TradingInstruments.AsNoTracking()
                    on q.TradingInstrumentId equals i.Id
                where q.ProviderName == _bridgeProvider
                select new { q, i.InstrumentCode, i.Symbol, InstrumentId = i.Id })
            .ToListAsync(cancellationToken);

        var directQuotes = await (
                from q in dbContext.LatestMarketQuotes.AsNoTracking()
                join i in dbContext.TradingInstruments.AsNoTracking()
                    on q.TradingInstrumentId equals i.Id
                where q.ProviderName == _directProvider
                select new { q, i.InstrumentCode, InstrumentId = i.Id })
            .ToListAsync(cancellationToken);

        var directByCode = directQuotes
            .GroupBy(r => r.InstrumentCode)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.q.AsOf).First());

        var now = timeProvider.GetUtcNow();
        var mismatches = new List<MarketQuoteMismatchRow>();
        var compared = 0;

        foreach (var bridge in bridgeQuotes)
        {
            if (!directByCode.TryGetValue(bridge.InstrumentCode, out var direct)) continue;
            compared++;

            var symbol = bridge.Symbol ?? string.Empty;
            CompareField("LatestPrice",
                bridge.q.LatestPrice, direct.q.LatestPrice,
                bridge.q.SourceKind, direct.q.SourceKind,
                bridge.InstrumentId, symbol, now, mismatches);

            CompareField("PriceChangePercentage",
                bridge.q.PriceChangePercentage, direct.q.PriceChangePercentage,
                bridge.q.SourceKind, direct.q.SourceKind,
                bridge.InstrumentId, symbol, now, mismatches);
        }

        if (mismatches.Count > 0)
        {
            dbContext.MarketQuoteMismatches.AddRange(mismatches);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "TSETMC validation: compared {Compared} instruments, found {Mismatches} mismatches, persisted {Persisted}.",
            compared, mismatches.Count, mismatches.Count);

        return new TsetmcValidationResult(compared, mismatches.Count, mismatches.Count, timeProvider.GetUtcNow() - started);
    }

    private static void CompareField(
        string field,
        decimal bridgeVal, decimal directVal,
        string bridgeKind, string directKind,
        Guid instrumentId, string symbol,
        DateTimeOffset now,
        List<MarketQuoteMismatchRow> mismatches)
    {
        if (bridgeVal == 0 && directVal == 0) return;
        var abs = Math.Abs(bridgeVal - directVal);
        var baseline = Math.Max(Math.Abs(bridgeVal), Math.Abs(directVal));
        if (baseline == 0) return;
        var rel = abs / baseline;
        if (rel <= RelativeTolerance) return;

        mismatches.Add(new MarketQuoteMismatchRow
        {
            Id = Guid.NewGuid(),
            ComparedAt = now,
            TradingInstrumentId = instrumentId,
            Symbol = symbol,
            Field = field,
            BridgeValue = bridgeVal,
            DirectValue = directVal,
            AbsoluteDiff = abs,
            RelativeDiffPercent = rel * 100,
            BridgeSourceKind = bridgeKind,
            DirectSourceKind = directKind
        });
    }
}

/// <summary>Reads persisted mismatch summaries for DataAdmin (Phase 3).</summary>
public sealed class MarketQuoteMismatchReader(FinancialIngestionDbContext dbContext) : IMarketQuoteMismatchReader
{
    public async Task<IReadOnlyCollection<MarketQuoteMismatchSummary>> GetSummaryAsync(
        int recentDays,
        CancellationToken cancellationToken)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, recentDays));
        var rows = await dbContext.MarketQuoteMismatches.AsNoTracking()
            .Where(r => r.ComparedAt >= since)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.Field)
            .Select(g => new MarketQuoteMismatchSummary(
                Field: g.Key,
                MismatchCount: g.Count(),
                AvgRelativeDiffPercent: g.Average(r => r.RelativeDiffPercent),
                MaxRelativeDiffPercent: g.Max(r => r.RelativeDiffPercent),
                LastComparedAt: g.Max(r => (DateTimeOffset?)r.ComparedAt)))
            .OrderByDescending(s => s.MismatchCount)
            .ToArray();
    }
}
