namespace FinancialCopilot.Application.FinancialData.Options;

/// <summary>Result of walking the order book for a quantity.</summary>
public readonly record struct OptionFill(OptionRowState State, decimal? Price, long AvailableVolume);

/// <summary>
/// Pure rules of the option comparison (Feature 168): order-book fill, the measures of each view
/// with fees, the annualization floor, the highlights and the freshness rule. No I/O; the web
/// card, the Telegram text, the API and the discount alert all go through here.
/// </summary>
public static class OptionComparisonPolicy
{
    // Iran Standard Time, fixed offset (no daylight saving).
    private static readonly TimeSpan MarketOffset = TimeSpan.FromHours(3.5);
    private const decimal DaysPerYear = 365m;

    /// <summary>
    /// Volume-weighted price for <paramref name="quantity"/> contracts over the levels, best
    /// first: the ask side for a buyer, the bid side for a writer.
    /// </summary>
    public static OptionFill Fill(IReadOnlyList<OptionOrderBookLevel> levels, int quantity, bool askSide)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);

        long available = 0;
        long remaining = quantity;
        decimal value = 0;
        foreach (var level in levels.OrderBy(level => level.Level))
        {
            var price = askSide ? level.AskPrice : level.BidPrice;
            var volume = askSide ? level.AskVolume : level.BidVolume;
            if (price is not { } levelPrice || volume is not { } levelVolume || levelVolume <= 0) continue;

            available += levelVolume;
            var taken = Math.Min(levelVolume, remaining);
            value += taken * levelPrice;
            remaining -= taken;
        }

        if (available == 0) return new OptionFill(OptionRowState.NoOrder, null, 0);
        return remaining > 0
            ? new OptionFill(OptionRowState.NotFillable, null, available)
            : new OptionFill(OptionRowState.Fillable, value / quantity, available);
    }

    /// <summary>
    /// The order book is current when its last successful sync is not older than the trading
    /// session of the underlying's latest quote. On a closed day the last session's book counts.
    /// </summary>
    public static bool IsOrderBookCurrent(DateTimeOffset? orderBookObservedAt, DateOnly underlyingTradingDate) =>
        orderBookObservedAt is { } observedAt &&
        DateOnly.FromDateTime(observedAt.ToOffset(MarketOffset).DateTime) >= underlyingTradingDate;

    public static OptionComparison Build(
        OptionMarketSnapshot snapshot,
        OptionComparisonQuery query,
        OptionAnswersOptions options,
        OptionTheoreticalInputs? theoreticalInputs)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Underlying is not { LastPrice: > 0 } underlying)
            throw new InvalidOperationException("A comparison needs a positive underlying price.");

        var fees = options.Fees;
        var underlyingClass = OptionUnderlyingClassifier.FromIsin(snapshot.UnderlyingIsin);
        var bookIsCurrent = IsOrderBookCurrent(snapshot.OrderBookObservedAt, underlying.TradingDate);
        var context = new RowContext(
            underlying.LastPrice, query.QuantityInContracts, fees, fees.TransferTaxFor(underlyingClass),
            options.AnnualizationFloorDays, bookIsCurrent, query.ContractSymbol, theoreticalInputs);

        var views = new[] { OptionComparisonView.Call, OptionComparisonView.Put, OptionComparisonView.CoveredCall }
            .Select(view => new OptionComparisonViewResult(view, BuildExpiries(snapshot, view, underlying.TradingDate, context)))
            .ToArray();

        return new OptionComparison(
            OptionComparison.CurrentVersion,
            snapshot.UnderlyingSymbol,
            underlyingClass,
            underlying.LastPrice,
            underlying.TradingDate,
            underlying.ObservedAt,
            query.QuantityInContracts,
            query.View,
            views,
            snapshot.AvailableExpiryDates,
            fees,
            bookIsCurrent,
            snapshot.OrderBookObservedAt,
            snapshot.OpenInterestObservedAt,
            theoreticalInputs);
    }

    private static OptionComparisonExpiry[] BuildExpiries(
        OptionMarketSnapshot snapshot, OptionComparisonView view, DateOnly tradingDate, RowContext context)
    {
        var side = view == OptionComparisonView.Put ? OptionSide.Put : OptionSide.Call;
        return snapshot.Contracts
            .Where(contract => contract.Side == side)
            .GroupBy(contract => contract.ExpiryDate)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var days = Math.Max(0, group.Key.DayNumber - tradingDate.DayNumber);
                var nearest = group.MinBy(contract => Math.Abs(contract.StrikePrice - context.UnderlyingPrice))?.Symbol;
                var rows = group
                    .OrderBy(contract => contract.StrikePrice)
                    .Select(contract => BuildRow(contract, view, days, contract.Symbol == nearest, context))
                    .ToArray();
                return new OptionComparisonExpiry(group.Key, days, ApplyHighlights(rows, view));
            })
            .ToArray();
    }

    private static OptionComparisonRow BuildRow(
        OptionMarketContract contract, OptionComparisonView view, int days, bool nearestToMoney, RowContext context)
    {
        var fill = context.BookIsCurrent
            ? Fill(contract.Levels, context.Quantity, askSide: view != OptionComparisonView.CoveredCall)
            : new OptionFill(OptionRowState.NoOrder, null, 0);
        var row = new OptionComparisonRow(
            contract.Symbol, contract.StrikePrice, contract.ContractSize, contract.OpenInterest,
            fill.State, fill.Price, fill.AvailableVolume, contract.LastPrice, nearestToMoney,
            context.RequestedContract is { } requested && string.Equals(requested, contract.Symbol, StringComparison.Ordinal),
            []);
        if (fill.Price is not { } price || price <= 0)
            return row;

        var spot = context.UnderlyingPrice;
        var strike = contract.StrikePrice;
        var annualize = days >= Math.Max(1, context.AnnualizationFloorDays) ? DaysPerYear / days : (decimal?)null;
        var theoretical = Theoretical(contract.Side, spot, strike, days, price, context.TheoreticalInputs);

        switch (view)
        {
            case OptionComparisonView.Call:
            {
                var cost = strike * (1 + context.Fees.Exercise) + price * (1 + context.Fees.Trade);
                var intrinsic = Math.Max(spot - strike, 0);
                var timeValue = price - intrinsic;
                var perContract = contract.ContractSize * context.Quantity;
                return row with
                {
                    EffectivePurchaseCost = cost,
                    BreakEvenDistance = (cost - spot) / spot,
                    IntrinsicValue = intrinsic,
                    TimeValue = timeValue,
                    // The price of deferring the strike payment; meaningful only when the call is in the money.
                    ImpliedAnnualRate = annualize is { } factor && spot > strike ? timeValue / strike * factor : null,
                    Leverage = spot / price,
                    CashNow = price * (1 + context.Fees.Trade) * perContract,
                    CashAtExpiry = strike * (1 + context.Fees.Exercise) * perContract,
                    TheoreticalPrice = theoretical.Price,
                    ImpliedVolatility = theoretical.ImpliedVolatility
                };
            }

            case OptionComparisonView.Put:
            {
                // The put holder delivers the underlying at exercise and pays the transfer tax on a share.
                var paid = price * (1 + context.Fees.Trade);
                var sale = strike * (1 - context.Fees.Exercise - context.TransferTax) - paid;
                return row with
                {
                    EffectiveSalePrice = sale,
                    DistanceFromMarket = (sale - spot) / spot,
                    ProtectionCost = paid / spot,
                    ProtectionCostAnnualized = annualize is { } factor ? paid / spot * factor : null,
                    TheoreticalPrice = theoretical.Price,
                    ImpliedVolatility = theoretical.ImpliedVolatility
                };
            }

            default:
            {
                // The call writer delivers the underlying if exercised.
                var premium = price * (1 - context.Fees.Trade);
                var ifExercised = (strike * (1 - context.Fees.Exercise - context.TransferTax) + premium - spot) / spot;
                var ifNotExercised = premium / spot;
                return row with
                {
                    PremiumIncome = premium,
                    YieldIfExercised = ifExercised,
                    YieldIfExercisedAnnualized = annualize is { } factor ? ifExercised * factor : null,
                    YieldIfNotExercised = ifNotExercised,
                    YieldIfNotExercisedAnnualized = annualize is { } rate ? ifNotExercised * rate : null,
                    DownsideCushion = premium / spot,
                    TheoreticalPrice = theoretical.Price,
                    ImpliedVolatility = theoretical.ImpliedVolatility
                };
            }
        }
    }

    private static OptionComparisonRow[] ApplyHighlights(OptionComparisonRow[] rows, OptionComparisonView view)
    {
        var labels = rows.ToDictionary(row => row.Symbol, _ => new List<string>(), StringComparer.Ordinal);

        void Mark(string label, Func<OptionComparisonRow, decimal?> measure, bool lowest)
        {
            var candidates = rows.Where(row => row.State == OptionRowState.Fillable && measure(row) is not null).ToArray();
            // A highlight compares; with a single fillable row there is nothing to compare.
            if (candidates.Length < 2) return;
            var ordered = lowest
                ? candidates.OrderBy(row => measure(row))
                : candidates.OrderByDescending(row => measure(row));
            var winner = ordered
                .ThenByDescending(row => row.AvailableVolume)
                .ThenBy(row => row.Symbol, StringComparer.Ordinal)
                .First();
            labels[winner.Symbol].Add(label);
        }

        switch (view)
        {
            case OptionComparisonView.Call:
                Mark(OptionHighlights.LowestEffectivePurchaseCost, row => row.EffectivePurchaseCost, lowest: true);
                Mark(OptionHighlights.LowestImpliedAnnualRate, row => row.ImpliedAnnualRate, lowest: true);
                break;
            case OptionComparisonView.Put:
                Mark(OptionHighlights.HighestEffectiveSalePrice, row => row.EffectiveSalePrice, lowest: false);
                break;
            default:
                Mark(OptionHighlights.HighestAnnualizedYieldIfExercised, row => row.YieldIfExercisedAnnualized, lowest: false);
                break;
        }

        return rows.Select(row => labels[row.Symbol].Count == 0 ? row : row with { Highlights = labels[row.Symbol] }).ToArray();
    }

    private static (decimal? Price, decimal? ImpliedVolatility) Theoretical(
        OptionSide side, decimal spot, decimal strike, int days, decimal marketPrice, OptionTheoreticalInputs? inputs)
    {
        if (inputs is null || days < 1) return (null, null);

        var years = days / 365d;
        var rate = Math.Log(1 + (double)inputs.RiskFreeAnnualRate);
        var price = BlackScholes.Price(side, (double)spot, (double)strike, years, rate, (double)inputs.HistoricalVolatility);
        var implied = BlackScholes.ImpliedVolatility(side, (double)spot, (double)strike, years, rate, (double)marketPrice);
        return (
            double.IsFinite(price) ? Math.Round((decimal)price, 2) : null,
            implied is { } value ? Math.Round((decimal)value, 4) : null);
    }

    private sealed record RowContext(
        decimal UnderlyingPrice,
        int Quantity,
        OptionFeeRates Fees,
        decimal TransferTax,
        int AnnualizationFloorDays,
        bool BookIsCurrent,
        string? RequestedContract,
        OptionTheoreticalInputs? TheoreticalInputs);
}

/// <summary>
/// Black–Scholes price of a European option without dividends, and the volatility implied by a
/// market price. Iranian equity options are exercised at expiry, so the European model applies.
/// </summary>
public static class BlackScholes
{
    private const double MinVolatility = 0.01;
    private const double MaxVolatility = 5.0;
    private const int MaxIterations = 100;
    private const double PriceTolerance = 1e-4;

    public static double Price(OptionSide side, double spot, double strike, double years, double rate, double volatility)
    {
        if (spot <= 0 || strike <= 0 || years <= 0 || volatility <= 0) return double.NaN;

        var root = volatility * Math.Sqrt(years);
        var d1 = (Math.Log(spot / strike) + (rate + volatility * volatility / 2) * years) / root;
        var d2 = d1 - root;
        var discountedStrike = strike * Math.Exp(-rate * years);
        return side == OptionSide.Call
            ? spot * NormalCdf(d1) - discountedStrike * NormalCdf(d2)
            : discountedStrike * NormalCdf(-d2) - spot * NormalCdf(-d1);
    }

    /// <summary>
    /// Bisection between 1% and 500%. Null when the market price lies outside what any volatility
    /// in that range can produce (for example below intrinsic value) or an input is invalid.
    /// </summary>
    public static double? ImpliedVolatility(OptionSide side, double spot, double strike, double years, double rate, double marketPrice)
    {
        if (spot <= 0 || strike <= 0 || years <= 0 || marketPrice <= 0) return null;

        var low = MinVolatility;
        var high = MaxVolatility;
        if (marketPrice < Price(side, spot, strike, years, rate, low) ||
            marketPrice > Price(side, spot, strike, years, rate, high))
            return null;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var middle = (low + high) / 2;
            var difference = Price(side, spot, strike, years, rate, middle) - marketPrice;
            if (Math.Abs(difference) < PriceTolerance) return middle;
            if (difference > 0) high = middle;
            else low = middle;
        }

        return (low + high) / 2;
    }

    /// <summary>
    /// Annualized volatility of daily log returns. Null with fewer than 20 returns or a
    /// non-positive close.
    /// </summary>
    public static double? HistoricalVolatility(IReadOnlyList<decimal> closesOldestFirst, int tradingDaysPerYear)
    {
        if (closesOldestFirst.Count < 21 || closesOldestFirst.Any(close => close <= 0)) return null;

        var returns = new double[closesOldestFirst.Count - 1];
        for (var index = 1; index < closesOldestFirst.Count; index++)
            returns[index - 1] = Math.Log((double)closesOldestFirst[index] / (double)closesOldestFirst[index - 1]);

        var mean = returns.Average();
        var variance = returns.Sum(value => (value - mean) * (value - mean)) / (returns.Length - 1);
        var volatility = Math.Sqrt(variance * tradingDaysPerYear);
        return volatility > 0 && double.IsFinite(volatility) ? volatility : null;
    }

    // Abramowitz and Stegun 7.1.26; absolute error below 1.5e-7.
    private static double NormalCdf(double x)
    {
        var t = 1 / (1 + 0.3275911 * Math.Abs(x) / Math.Sqrt(2));
        var poly = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
        var erf = 1 - poly * Math.Exp(-x * x / 2);
        return x >= 0 ? (1 + erf) / 2 : (1 - erf) / 2;
    }
}

/// <summary>Use case: the option comparison of one underlying from the stored market state.</summary>
public sealed class OptionComparisonUseCase(
    IOptionMarketSnapshotReader reader,
    OptionAnswersOptions options) : IOptionComparisonUseCase
{
    public const int MaxQuantity = 100_000;

    public async Task<OptionComparisonOutcome> ExecuteAsync(OptionComparisonQuery query, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.QuantityInContracts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.QuantityInContracts, MaxQuantity);

        var snapshot = await reader.GetAsync(query.UnderlyingSymbol, query.ExpiryDate, options.MaxExpiries, cancellationToken);
        if (snapshot is null)
            return new OptionComparisonOutcome(null, OptionComparisonFailure.NoOptionContracts, []);
        if (snapshot.Contracts.Count == 0)
            return new OptionComparisonOutcome(null, OptionComparisonFailure.ExpiryNotListed, snapshot.AvailableExpiryDates);
        if (snapshot.Underlying is not { LastPrice: > 0 })
            return new OptionComparisonOutcome(null, OptionComparisonFailure.NoUnderlyingPrice, snapshot.AvailableExpiryDates);

        var theoretical = await TheoreticalInputsAsync(snapshot.UnderlyingSymbol, cancellationToken);
        var comparison = OptionComparisonPolicy.Build(snapshot, query, options, theoretical);
        return new OptionComparisonOutcome(comparison, OptionComparisonFailure.None, snapshot.AvailableExpiryDates);
    }

    private async Task<OptionTheoreticalInputs?> TheoreticalInputsAsync(string underlyingSymbol, CancellationToken cancellationToken)
    {
        if (options.RiskFreeAnnualRate is not { } rate) return null;

        var closes = await reader.GetUnderlyingDailyClosesAsync(underlyingSymbol, options.VolatilityWindowDays + 1, cancellationToken);
        return BlackScholes.HistoricalVolatility(closes, options.TradingDaysPerYear) is { } volatility
            ? new OptionTheoreticalInputs(rate, Math.Round((decimal)volatility, 4), closes.Count - 1)
            : null;
    }
}
