using System.Diagnostics;

namespace FinancialCopilot.Application.FinancialData.ValuationGauges;

/// <summary>
/// Builds standalone valuation gauge cards for one security from Feature 148 snapshots and the canonical quote.
/// Expected read timeouts become <see cref="ValuationGaugeCardState.TemporarilyUnavailable"/> cards (or a range-only
/// equilibrium card for a quote timeout); caller cancellation is always rethrown. No LLM, vendor HTTP, legacy table
/// or cross-provider fallback is involved.
/// </summary>
public sealed class ValuationGaugeCardsUseCase(
    IValuationGaugeSnapshotReader snapshots,
    IValuationGaugeQuoteReader quotes,
    ValuationGaugeCardsOptions options,
    TimeProvider timeProvider,
    IValuationGaugeObserver? observer = null) : IValuationGaugeCardsUseCase
{
    private readonly IValuationGaugeObserver _observer = observer ?? NullValuationGaugeObserver.Instance;

    public async Task<ValuationGaugeCardsResult> ExecuteAsync(ValuationGaugeCardsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var started = Stopwatch.GetTimestamp();
        var kinds = Normalize(query.Kinds);
        var now = timeProvider.GetUtcNow();
        var maxAge = TimeSpan.FromHours(options.MaxFetchAgeHours);

        ValuationGaugeSnapshotSet? set;
        try
        {
            set = await WithBudget(
                token => snapshots.ReadAsync(query.Security, kinds, token),
                options.ReadTimeoutMilliseconds,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            set = null;
        }

        List<ValuationGaugeCard> cards;
        if (set is null)
        {
            cards = kinds.Select(kind => ValuationGaugeProjector.Unavailable(
                kind, ValuationGaugeCardState.TemporarilyUnavailable, ValuationGaugeReasonCodes.ReadTimedOut)).ToList();
        }
        else if (set.Match != ValuationGaugeMatchOutcome.Matched)
        {
            var reason = set.Match == ValuationGaugeMatchOutcome.Ambiguous
                ? ValuationGaugeReasonCodes.AmbiguousMatch
                : ValuationGaugeReasonCodes.CoverageMiss;
            cards = kinds.Select(kind => ValuationGaugeProjector.Unavailable(kind, ValuationGaugeCardState.NoData, reason)).ToList();
        }
        else
        {
            cards = [];
            foreach (var kind in kinds)
                cards.Add(await ProjectAsync(kind, set, now, maxAge, cancellationToken));
        }

        foreach (var card in cards)
            _observer.CardProjected(card.Kind, card.State, card.ReasonCode, card.FetchedAtUtc is { } fetched ? now - fetched : null);
        var result = new ValuationGaugeCardsResult(
            ValuationGaugeCardsResult.CurrentVersion,
            query.Security.DisplaySymbol,
            query.Security.CompanyName,
            set?.VendorTicker,
            kinds,
            cards,
            now,
            options.MaxFetchAgeHours);
        _observer.Completed(
            Stopwatch.GetElapsedTime(started),
            cards.Count(card => card.IsDisplayable()),
            set is null ? "timeout" : set.Match.ToString().ToLowerInvariant());
        return result;
    }

    private async Task<ValuationGaugeCard> ProjectAsync(
        ValuationGaugeKind kind,
        ValuationGaugeSnapshotSet set,
        DateTimeOffset now,
        TimeSpan maxAge,
        CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case ValuationGaugeKind.Pe or ValuationGaugeKind.Ps:
            {
                var listed = kind == ValuationGaugeKind.Pe ? set.Catalog?.HasPe : set.Catalog?.HasPs;
                if (listed != true)
                    return ValuationGaugeProjector.Unavailable(kind, ValuationGaugeCardState.NoData, ValuationGaugeReasonCodes.NotInCatalog);
                var snapshot = kind == ValuationGaugeKind.Pe ? set.Pe : set.Ps;
                return snapshot is null
                    ? ValuationGaugeProjector.Unavailable(kind, ValuationGaugeCardState.NoData, ValuationGaugeReasonCodes.SnapshotMissing)
                    : ValuationGaugeProjector.ProjectRatio(kind, snapshot, now, maxAge, options.PercentageDecimals);
            }
            default:
            {
                var range = set.Equilibrium;
                var usableRange = range is { HasRange: true } && !ValuationGaugeProjector.IsStale(range.FetchedAtUtc, now, maxAge);
                ValuationGaugeQuote? quote = null;
                var quoteState = ValuationGaugeQuoteState.Unavailable;
                if (usableRange && !string.IsNullOrWhiteSpace(set.Isin))
                {
                    try
                    {
                        quote = await WithBudget(
                            token => quotes.ReadAsync(set.Isin!, now, token),
                            options.QuoteTimeoutMilliseconds,
                            cancellationToken);
                        quoteState = quote is null ? ValuationGaugeQuoteState.Unavailable : ValuationGaugeQuoteState.Available;
                    }
                    catch (TimeoutException)
                    {
                        quoteState = ValuationGaugeQuoteState.TimedOut;
                    }
                }

                return ValuationGaugeProjector.ProjectEquilibrium(range, quote, quoteState, now, maxAge);
            }
        }
    }

    /// <summary>Fixed presentation order P/E, P/S, equilibrium; an empty request means all kinds.</summary>
    public static IReadOnlyList<ValuationGaugeKind> Normalize(IReadOnlyCollection<ValuationGaugeKind>? kinds) =>
        kinds is null || kinds.Count == 0
            ? [ValuationGaugeKind.Pe, ValuationGaugeKind.Ps, ValuationGaugeKind.Equilibrium]
            : kinds.Distinct().Order().ToArray();

    /// <summary>
    /// Runs a read with its own budget. A budget overrun becomes <see cref="TimeoutException"/>; caller cancellation
    /// propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    private static async Task<T> WithBudget<T>(Func<CancellationToken, Task<T>> read, int budgetMilliseconds, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMilliseconds(budgetMilliseconds));
        try
        {
            return await read(budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
        {
            throw new TimeoutException("The valuation gauge read exceeded its budget.");
        }
    }
}
