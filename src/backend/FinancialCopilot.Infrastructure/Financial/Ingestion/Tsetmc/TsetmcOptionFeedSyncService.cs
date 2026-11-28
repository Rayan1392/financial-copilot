using FinancialCopilot.Application.FinancialData.Options;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;

/// <summary>
/// Ingests listed option contracts from TSETMC (Feature 167): contract terms and open interest
/// from Option(), and the latest order book from BestLimitsAllIns().
/// <para>
/// This service never writes <c>TradingInstruments</c>; that table has a single writer
/// (<see cref="TsetmcDirectFeedSyncService"/>, spec 064). A contract whose own instrument or
/// underlying instrument is not in the catalog yet is counted as unresolved and picked up by a
/// later run, after the instruments sync has seen it.
/// </para>
/// <para>
/// A provider failure or an empty response leaves the stored data untouched and does not advance
/// the dataset watermark, so readers can tell how old the data is.
/// </para>
/// </summary>
public sealed class TsetmcOptionFeedSyncService(
    FinancialIngestionDbContext dbContext,
    ITsetmcOptionWebServiceClient client,
    IOptions<TsetmcWebServiceOptions> options,
    TimeProvider timeProvider,
    ILogger<TsetmcOptionFeedSyncService> logger) : IOptionMarketSyncService
{
    public const string ContractsDataset = "OptionContracts";
    public const string OrderBookDataset = "OptionOrderBook";
    public const string SyncStatePrefix = "Tsetmc_";

    private readonly TsetmcWebServiceOptions _options = options.Value;

    public bool IsOperational =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.UserName) &&
        !string.IsNullOrWhiteSpace(_options.Password);

    public Task<OptionMarketSyncResult> SynchronizeContractsAsync(CancellationToken cancellationToken) =>
        RunAsync(ContractsDataset, SynchronizeContractsCoreAsync, cancellationToken);

    public Task<OptionMarketSyncResult> SynchronizeOrderBookAsync(CancellationToken cancellationToken) =>
        RunAsync(OrderBookDataset, SynchronizeOrderBookCoreAsync, cancellationToken);

    private async Task<OptionMarketSyncResult> SynchronizeContractsCoreAsync(
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        var records = await client.GetOptionContractsAsync(cancellationToken);
        if (records.Count == 0)
            throw new FinancialProviderException(
                FinancialProviderErrorCode.RemoteUnavailable, "TSETMC Option returned no contracts.");

        var instrumentCodes = records.Select(record => record.InsCode)
            .Concat(records.Select(record => record.UnderlyingInsCode))
            .Distinct()
            .ToArray();
        var instrumentIds = await InstrumentIdsByCodeAsync(instrumentCodes, cancellationToken);
        var existing = await dbContext.OptionContracts.ToDictionaryAsync(row => row.TradingInstrumentId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        int persisted = 0, unchanged = 0, unresolved = 0, ignored = 0;
        var rejected = new List<(long InsCode, OptionContractRejection Reason)>();
        var seen = new HashSet<long>();
        foreach (var record in records)
        {
            if (!seen.Add(record.InsCode)) continue;

            // The provider lists other contract kinds here too (embedded puts and the like).
            // Only listed calls and puts form an option chain.
            if (TsetmcOptionIsin.SideOf(record.InstrumentId) is not { } listedSide)
            {
                ignored++;
                continue;
            }

            var rejection = OptionContractTerms.TryCreate(
                listedSide,
                record.StrikePrice, record.EndDate, record.BeginDate, record.ContractSize,
                out var terms);
            if (rejection != OptionContractRejection.None || terms is null)
            {
                rejected.Add((record.InsCode, rejection));
                continue;
            }

            if (!instrumentIds.TryGetValue(record.InsCode, out var instrumentId) ||
                !instrumentIds.TryGetValue(record.UnderlyingInsCode, out var underlyingId))
            {
                unresolved++;
                continue;
            }

            var side = terms.Side.ToString();
            var openInterest = Math.Max(0, record.OpenPositions);
            var previousOpenInterest = Math.Max(0, record.YesterdayOpenPositions);
            if (!existing.TryGetValue(instrumentId, out var row))
            {
                row = new OptionContractRow
                {
                    Id = Guid.NewGuid(),
                    TradingInstrumentId = instrumentId,
                    FirstSeenAt = now
                };
                dbContext.OptionContracts.Add(row);
                existing[instrumentId] = row;
                persisted++;
            }
            else
            {
                var termsChanged = row.UnderlyingInstrumentId != underlyingId || row.Side != side ||
                    row.StrikePrice != terms.StrikePrice || row.ExpiryDate != terms.ExpiryDate ||
                    row.BeginDate != terms.BeginDate || row.ContractSize != terms.ContractSize;
                if (termsChanged)
                    logger.LogWarning(
                        "TSETMC option contract {InsCode}: provider changed the terms (strike {OldStrike} -> {NewStrike}, expiry {OldExpiry} -> {NewExpiry}, size {OldSize} -> {NewSize}).",
                        record.InsCode, row.StrikePrice, terms.StrikePrice, row.ExpiryDate, terms.ExpiryDate,
                        row.ContractSize, terms.ContractSize);

                if (termsChanged || row.OpenInterest != openInterest || row.PreviousOpenInterest != previousOpenInterest)
                    persisted++;
                else
                    unchanged++;
            }

            row.UnderlyingInstrumentId = underlyingId;
            row.Side = side;
            row.StrikePrice = terms.StrikePrice;
            row.ExpiryDate = terms.ExpiryDate;
            row.BeginDate = terms.BeginDate;
            row.ContractSize = terms.ContractSize;
            row.OpenInterest = openInterest;
            row.PreviousOpenInterest = previousOpenInterest;
            row.OpenInterestObservedAt = now;
            row.LastSynchronizedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // The counts go into the per-run summary line; the sample is for diagnosis only, so a
        // row the provider keeps sending wrong does not raise a warning every few minutes.
        if (rejected.Count > 0)
            logger.LogDebug(
                "TSETMC option contracts: rejected rows sample: {Sample}.",
                string.Join(", ", rejected.Take(10).Select(item => $"{item.InsCode}:{item.Reason}")));

        return new OptionMarketSyncResult(
            ContractsDataset, records.Count, persisted, unchanged, rejected.Count, unresolved, 0, ignored,
            timeProvider.GetUtcNow() - started);
    }

    private async Task<OptionMarketSyncResult> SynchronizeOrderBookCoreAsync(
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        var records = await client.GetBestLimitsAsync(_options.OptionOrderBookFlow, cancellationToken);
        if (records.Count == 0)
            throw new FinancialProviderException(
                FinancialProviderErrorCode.RemoteUnavailable, "TSETMC BestLimitsAllIns returned no rows.");

        // Only instruments that are known option contracts are kept; the flow also carries
        // other derivative instruments.
        var contractInstruments = await (
                from contract in dbContext.OptionContracts.AsNoTracking()
                join instrument in dbContext.TradingInstruments.AsNoTracking()
                    on contract.TradingInstrumentId equals instrument.Id
                select new { instrument.InstrumentCode, instrument.Id })
            .ToListAsync(cancellationToken);
        var instrumentIdByCode = contractInstruments
            .GroupBy(row => row.InstrumentCode)
            .ToDictionary(group => group.Key, group => group.First().Id);

        var existing = (await dbContext.OptionOrderBookLevels.ToListAsync(cancellationToken))
            .ToDictionary(row => (row.TradingInstrumentId, row.Level));

        var now = timeProvider.GetUtcNow();
        int persisted = 0, unchanged = 0, rejected = 0, ignored = 0;
        var seen = new HashSet<(Guid, short)>();
        foreach (var record in records)
        {
            if (!instrumentIdByCode.TryGetValue(record.InsCode, out var instrumentId))
            {
                ignored++;
                continue;
            }

            if (!OptionOrderBookLevel.TryCreate(
                    record.Level, record.BidPrice, record.BidVolume, record.BidOrderCount,
                    record.AskPrice, record.AskVolume, record.AskOrderCount, out var level) || level is null)
            {
                rejected++;
                continue;
            }

            // A level with no order on either side is the same as no level.
            if (level.IsEmpty) continue;

            var key = (instrumentId, (short)level.Level);
            if (!seen.Add(key)) continue;

            if (!existing.TryGetValue(key, out var row))
            {
                row = new OptionOrderBookLevelRow { TradingInstrumentId = instrumentId, Level = key.Item2 };
                dbContext.OptionOrderBookLevels.Add(row);
            }
            else if (row.BidPrice == level.BidPrice && row.BidVolume == level.BidVolume &&
                     row.BidOrderCount == level.BidOrderCount && row.AskPrice == level.AskPrice &&
                     row.AskVolume == level.AskVolume && row.AskOrderCount == level.AskOrderCount &&
                     row.ProviderTime == record.Time)
            {
                unchanged++;
                continue;
            }

            row.BidPrice = level.BidPrice;
            row.BidVolume = level.BidVolume;
            row.BidOrderCount = level.BidOrderCount;
            row.AskPrice = level.AskPrice;
            row.AskVolume = level.AskVolume;
            row.AskOrderCount = level.AskOrderCount;
            row.ProviderTime = record.Time;
            row.ObservedAt = now;
            persisted++;
        }

        // The response is the whole book: a stored level the provider no longer reports is gone.
        var removed = existing.Where(pair => !seen.Contains(pair.Key)).Select(pair => pair.Value).ToArray();
        dbContext.OptionOrderBookLevels.RemoveRange(removed);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new OptionMarketSyncResult(
            OrderBookDataset, records.Count, persisted, unchanged, rejected, 0, removed.Length, ignored,
            timeProvider.GetUtcNow() - started);
    }

    private async Task<Dictionary<long, Guid>> InstrumentIdsByCodeAsync(
        IReadOnlyCollection<long> codes,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.TradingInstruments.AsNoTracking()
            .Where(row => codes.Contains(row.InstrumentCode))
            .Select(row => new { row.InstrumentCode, row.Id, row.LastSynchronizedAt })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => row.InstrumentCode)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.LastSynchronizedAt).First().Id);
    }

    /// <summary>
    /// Wraps one dataset sync with its sync-state row: started and completed are always stamped,
    /// the watermark only after success.
    /// </summary>
    private async Task<OptionMarketSyncResult> RunAsync(
        string dataset,
        Func<DateTimeOffset, CancellationToken, Task<OptionMarketSyncResult>> sync,
        CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        await StampAsync(dataset, state =>
        {
            state.LastRunStartedAt = started;
            state.LastRunCompletedAt = null;
        }, cancellationToken);

        OptionMarketSyncResult result;
        try
        {
            result = await sync(started, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Drop half-applied changes so the failure stamp is the only write.
            dbContext.ChangeTracker.Clear();
            await StampAsync(dataset, state => state.LastRunCompletedAt = timeProvider.GetUtcNow(), cancellationToken);
            throw;
        }

        var completed = timeProvider.GetUtcNow();
        await StampAsync(dataset, state =>
        {
            state.LastRunCompletedAt = completed;
            state.Watermark = completed;
        }, cancellationToken);

        logger.LogInformation(
            "TSETMC {Dataset} synced: {Fetched} fetched, {Persisted} persisted, {Unchanged} unchanged, {Rejected} rejected, {Unresolved} unresolved, {Removed} removed, {Ignored} ignored in {Duration}.",
            dataset, result.RowsFetched, result.RowsPersisted, result.RowsUnchanged, result.RowsRejected,
            result.RowsUnresolved, result.RowsRemoved, result.RowsIgnored, result.Duration);
        return result;
    }

    private async Task StampAsync(string dataset, Action<StockMarketSyncStateRow> apply, CancellationToken cancellationToken)
    {
        var key = SyncStatePrefix + dataset;
        var state = await dbContext.StockMarketSyncStates.SingleOrDefaultAsync(row => row.Dataset == key, cancellationToken);
        if (state is null)
        {
            state = new StockMarketSyncStateRow { Dataset = key };
            dbContext.StockMarketSyncStates.Add(state);
        }

        state.LogicalVendor = ProviderSources.TsetmcWebService.Vendor.ToString();
        state.PhysicalSource = ProviderSources.TsetmcWebService.Source.ToString();
        state.SourceMode = ProviderSources.TsetmcWebService.DefaultMode.ToString();
        apply(state);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Stand-in used when the TSETMC feed is disabled or has no credentials.</summary>
public sealed class NullOptionMarketSyncService : IOptionMarketSyncService
{
    public bool IsOperational => false;

    public Task<OptionMarketSyncResult> SynchronizeContractsAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("TsetmcWebService is not operational.");

    public Task<OptionMarketSyncResult> SynchronizeOrderBookAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("TsetmcWebService is not operational.");
}
