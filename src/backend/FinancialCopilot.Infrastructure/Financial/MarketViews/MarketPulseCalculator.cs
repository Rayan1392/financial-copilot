using FinancialCopilot.Application.FinancialData.MarketViews;

namespace FinancialCopilot.Infrastructure.Financial.MarketViews;

internal static class MarketPulseCalculator
{
    private static readonly TimeSpan IranOffset = TimeSpan.FromHours(3.5);
    private static readonly TimeOnly MarketOpen = new(9, 0);
    private static readonly TimeOnly MarketClose = new(12, 30);

    internal sealed record Session(
        DateOnly TradingDate,
        MarketPulseSessionState State,
        string CadenceSlot);

    internal sealed record Quote(
        decimal ChangePercent,
        DateTimeOffset AsOf,
        string? IndustryCode,
        string? IndustryName);

    internal static decimal? CalculateTransactionValue(IEnumerable<decimal> totalCapitalValues)
    {
        var values = totalCapitalValues.ToArray();
        return values.Length == 0 ? null : values.Sum();
    }

    internal static Session ResolveSession(DateTimeOffset utcNow, int cadenceMinutes)
    {
        var iranNow = utcNow.ToOffset(IranOffset);
        var date = DateOnly.FromDateTime(iranNow.DateTime);
        var time = TimeOnly.FromDateTime(iranNow.DateTime);
        var day = iranNow.DayOfWeek;
        if (day is DayOfWeek.Thursday or DayOfWeek.Friday)
            return new Session(date, MarketPulseSessionState.Holiday, "holiday");

        if (time < MarketOpen)
            return new Session(date, MarketPulseSessionState.PreOpen, "pre-open");

        if (time <= MarketClose)
        {
            var minutes = Math.Max(0, (int)(time.ToTimeSpan() - MarketOpen.ToTimeSpan()).TotalMinutes);
            var slot = minutes / Math.Max(1, cadenceMinutes);
            return new Session(date, MarketPulseSessionState.Open, $"open-{slot:D3}");
        }

        var finalCutoff = day == DayOfWeek.Wednesday ? new TimeOnly(20, 0) : new TimeOnly(17, 0);
        return time < finalCutoff
            ? new Session(date, MarketPulseSessionState.Intermission, "post-close-settlement")
            : new Session(date, MarketPulseSessionState.Closed, "final");
    }

    internal static MarketPulseBreadth CalculateBreadth(
        IReadOnlyCollection<Quote> quotes,
        DateTimeOffset staleBefore,
        int excludedBeforeFreshness)
    {
        var fresh = quotes.Where(quote => quote.AsOf >= staleBefore).ToArray();
        var stale = quotes.Count - fresh.Length;
        if (fresh.Length == 0)
        {
            return new MarketPulseBreadth(
                null, null, null, 0, excludedBeforeFreshness + stale,
                quotes.Count == 0 ? MarketPulseFactStatus.Unavailable : MarketPulseFactStatus.Stale,
                quotes.Count == 0
                    ? "No canonical quotes were available at the snapshot cutoff."
                    : "All canonical quotes were older than the configured freshness threshold.");
        }

        return new MarketPulseBreadth(
            fresh.Count(quote => quote.ChangePercent > 0),
            fresh.Count(quote => quote.ChangePercent < 0),
            fresh.Count(quote => quote.ChangePercent == 0),
            fresh.Length,
            excludedBeforeFreshness + stale,
            stale == 0 && excludedBeforeFreshness == 0
                ? MarketPulseFactStatus.Available
                : MarketPulseFactStatus.Partial,
            stale == 0 && excludedBeforeFreshness == 0
                ? null
                : "Instruments without a fresh canonical quote were excluded.");
    }

    internal static (
        IReadOnlyCollection<MarketPulseIndustryDriver> Leading,
        IReadOnlyCollection<MarketPulseIndustryDriver> Lagging)
        CalculateIndustryDrivers(
            IReadOnlyCollection<Quote> quotes,
            DateTimeOffset staleBefore,
            int count)
    {
        var scored = quotes
            .Where(quote =>
                quote.AsOf >= staleBefore &&
                !string.IsNullOrWhiteSpace(quote.IndustryCode) &&
                !string.IsNullOrWhiteSpace(quote.IndustryName))
            .GroupBy(quote => new { Code = quote.IndustryCode!, Name = quote.IndustryName! })
            .Select(group => new MarketPulseIndustryDriver(
                group.Key.Code,
                group.Key.Name,
                decimal.Round(group.Average(item => item.ChangePercent), 4, MidpointRounding.AwayFromZero),
                group.Count()))
            .ToArray();

        var take = Math.Max(1, count);
        return (
            scored.OrderByDescending(item => item.ChangePercent)
                .ThenBy(item => item.IndustryCode, StringComparer.Ordinal)
                .Take(take).ToArray(),
            scored.OrderBy(item => item.ChangePercent)
                .ThenBy(item => item.IndustryCode, StringComparer.Ordinal)
                .Take(take).ToArray());
    }

    internal static MarketPulseComparison CalculateComparison(
        string window,
        int requiredSessions,
        int minimumSessions,
        decimal? currentValue,
        IReadOnlyCollection<decimal> completedSessionValues)
    {
        var values = completedSessionValues.Take(requiredSessions).ToArray();
        if (currentValue is null)
            return Unavailable(window, requiredSessions, values.Length, "Current transaction value is unavailable.");
        if (values.Length < minimumSessions)
            return Unavailable(window, requiredSessions, values.Length,
                $"At least {minimumSessions} completed trading sessions are required.");

        var average = values.Average();
        if (average == 0)
            return Unavailable(window, requiredSessions, values.Length, "The baseline average is zero.");

        return new MarketPulseComparison(
            window,
            requiredSessions,
            values.Length,
            decimal.Round(average, 2, MidpointRounding.AwayFromZero),
            decimal.Round(((currentValue.Value - average) / average) * 100m, 4, MidpointRounding.AwayFromZero),
            values.Length < requiredSessions ? MarketPulseFactStatus.Partial : MarketPulseFactStatus.Available,
            values.Length < requiredSessions ? "The comparison uses the available completed sessions." : null);
    }

    private static MarketPulseComparison Unavailable(
        string window,
        int requiredSessions,
        int availableSessions,
        string reason) =>
        new(window, requiredSessions, availableSessions, null, null, MarketPulseFactStatus.Unavailable, reason);
}
