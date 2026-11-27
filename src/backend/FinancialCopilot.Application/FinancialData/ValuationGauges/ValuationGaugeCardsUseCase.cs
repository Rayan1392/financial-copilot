using System.Diagnostics;

namespace FinancialCopilot.Application.FinancialData.ValuationGauges;

/// <summary>
/// Builds standalone valuation gauge cards for one security from Feature 148 snapshots and the canonical quote.
/// Expected read timeouts become <see cref="ValuationGaugeCardState.TemporarilyUnavailable"/> cards (or a range-only
/// equilibrium card for a quote timeout); caller cancellation is always rethrown. No LLM, vendor HTTP, legacy table
/// or cross-provider fallback is involved. When <see cref="ValuationGaugeForwardOptions.Enabled"/> is on and a P/E or
/// P/S card is displayable, Sapio's forward estimate is added to those cards; its input read has its own budget and a
/// timeout or failure drops only the estimate. The reader is optional so the flag-off path needs no registration.
/// </summary>
public sealed class ValuationGaugeCardsUseCase(
    IValuationGaugeSnapshotReader snapshots,
    IValuationGaugeQuoteReader quotes,
    ValuationGaugeCardsOptions options,
    TimeProvider timeProvider,
    IValuationGaugeObserver? observer = null,
    IForwardRatioInputsReader? forwardInputs = null) : IValuationGaugeCardsUseCase
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
            if (options.ForwardEstimate.Enabled && forwardInputs is not null)
                cards = await AddForwardEstimatesAsync(query.Security, set.VendorTicker, cards, now, cancellationToken);
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

    /// <summary>
    /// Reads the estimator inputs once for the P/E and P/S cards that can carry an estimate (displayable, with a current
    /// value and a rendered dial) and attaches each ratio's estimate. Every ratio card gets one observer call.
    /// </summary>
    private async Task<List<ValuationGaugeCard>> AddForwardEstimatesAsync(
        ValuationGaugeSecurity security,
        string? vendorTicker,
        List<ValuationGaugeCard> cards,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ratioCards = cards.Where(card => card.Kind is ValuationGaugeKind.Pe or ValuationGaugeKind.Ps).ToArray();
        if (ratioCards.Length == 0) return cards;
        var eligible = ratioCards.Where(card => ForwardBlocker(card) is null).ToArray();
        if (eligible.Length == 0 || string.IsNullOrWhiteSpace(vendorTicker))
        {
            foreach (var card in ratioCards)
                _observer.ForwardEstimated(card.Kind, false, ForwardBlocker(card) ?? ForwardRatioReasonCodes.CompanyUnmatched, null);
            return cards;
        }

        var started = Stopwatch.GetTimestamp();
        ForwardRatioInputs? inputs = null;
        string? readFailure = null;
        try
        {
            inputs = await WithBudget(
                token => forwardInputs!.ReadAsync(security, vendorTicker, token),
                options.ForwardEstimate.ReadTimeoutMilliseconds,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            readFailure = ForwardRatioReasonCodes.ReadTimedOut;
            _observer.ForwardReadFailed(readFailure, Stopwatch.GetElapsedTime(started), null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            readFailure = ForwardRatioReasonCodes.ReadFailed;
            _observer.ForwardReadFailed(readFailure, Stopwatch.GetElapsedTime(started), exception);
        }

        var readElapsed = Stopwatch.GetElapsedTime(started);
        ForwardRatioEstimate? estimate = null;
        if (inputs is not null)
        {
            var currentPe = CurrentValue(cards, ValuationGaugeKind.Pe);
            var currentPs = CurrentValue(cards, ValuationGaugeKind.Ps);
            estimate = ForwardRatioEstimator.Estimate(
                inputs, currentPe, currentPs, options.ForwardEstimate.Thresholds(), DateOnly.FromDateTime(now.UtcDateTime));
        }

        var result = new List<ValuationGaugeCard>(cards.Count);
        foreach (var card in cards)
        {
            if (card.Kind is not (ValuationGaugeKind.Pe or ValuationGaugeKind.Ps))
            {
                result.Add(card);
                continue;
            }

            var (value, reason) = ForwardBlocker(card) is { } blocker ? (null, blocker)
                : readFailure is not null ? (null, readFailure)
                : estimate is null ? (null, ForwardRatioReasonCodes.CompanyUnmatched)
                : card.Kind == ValuationGaugeKind.Pe ? (estimate.ForwardPe, estimate.PeReasonCode)
                : (estimate.ForwardPs, estimate.PsReasonCode);
            var projected = value is { } forward ? ValuationGaugeProjector.WithForward(card, forward, estimate!) : card;
            _observer.ForwardEstimated(card.Kind, projected.Forward is not null, reason, readElapsed);
            result.Add(projected);
        }

        return result;
    }

    /// <summary>Why a ratio card cannot carry an estimate before any input is read; null when it can.</summary>
    private static string? ForwardBlocker(ValuationGaugeCard card) =>
        !card.IsDisplayable() || card.Ratio?.Current is null ? ForwardRatioReasonCodes.CardNotDisplayable
        : card.Geometry is null ? ForwardRatioReasonCodes.AxisNotRenderable
        : null;

    private static decimal? CurrentValue(IEnumerable<ValuationGaugeCard> cards, ValuationGaugeKind kind) =>
        cards.FirstOrDefault(card => card.Kind == kind && ForwardBlocker(card) is null)?.Ratio?.Current is { } text &&
        decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

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
