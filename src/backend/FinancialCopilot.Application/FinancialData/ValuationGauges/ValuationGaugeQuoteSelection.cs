using System.Globalization;

namespace FinancialCopilot.Application.FinancialData.ValuationGauges;

/// <summary>
/// Which canonical trade price the equilibrium card uses (Feature 159 change «equilibrium-intraday-price»). TSETMC's daily
/// trade row for today is not ready before the configured Tehran time (19:00 by default, later on some Wednesdays), and
/// the intraday poll stops when the market closes, so:
/// before the threshold, today's latest intraday trade, however old; from the threshold, today's daily row when it exists,
/// else today's intraday trade; without an intraday trade today, the latest daily row.
/// </summary>
public static class ValuationGaugeQuoteSelection
{
    public static readonly TimeSpan TehranOffset = TimeSpan.FromHours(3.5);

    /// <param name="todayIntraday">Today's latest intraday trade (Tehran date), or null.</param>
    /// <param name="latestDaily">The latest daily trade up to today, or null.</param>
    public static ValuationGaugeQuote? Select(
        ValuationGaugeQuote? todayIntraday,
        ValuationGaugeQuote? latestDaily,
        DateTimeOffset now,
        TimeOnly dailyPriceReadyTehran)
    {
        var tehran = now.ToOffset(TehranOffset);
        var today = DateOnly.FromDateTime(tehran.DateTime);
        var dailyIsToday = latestDaily?.TradingDate == today;
        if (dailyIsToday && TimeOnly.FromDateTime(tehran.DateTime) >= dailyPriceReadyTehran)
            return latestDaily;
        return todayIntraday ?? latestDaily;
    }

    public static bool TryParseTehranTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
