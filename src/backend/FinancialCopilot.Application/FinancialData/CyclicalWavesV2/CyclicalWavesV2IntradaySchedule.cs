using System.Globalization;

namespace FinancialCopilot.Application.FinancialData.CyclicalWavesV2;

/// <summary>
/// The market-hours slots of the gauge and equilibrium sync: window start plus a whole number of intervals,
/// up to and including the window end, on the trading days of the Iranian week (Saturday to Wednesday).
/// Times are Tehran local time, a fixed UTC+03:30 (Iran has no daylight saving time). Public holidays are
/// not known here; a run on a closed day finds unchanged data.
/// </summary>
public sealed class CyclicalWavesV2IntradaySchedule
{
    public const int MinimumIntervalMinutes = 5;
    public const int MaximumIntervalMinutes = 240;
    public static readonly TimeSpan TehranOffset = TimeSpan.FromHours(3.5);

    private readonly TimeSpan _windowStart;
    private readonly TimeSpan _windowEnd;

    private CyclicalWavesV2IntradaySchedule(TimeSpan windowStart, TimeSpan windowEnd, TimeSpan interval)
    {
        _windowStart = windowStart;
        _windowEnd = windowEnd;
        Interval = interval;
    }

    public TimeSpan Interval { get; }

    public static bool IsValid(string? windowStartTehran, string? windowEndTehran, int intervalMinutes) =>
        TryCreate(windowStartTehran, windowEndTehran, intervalMinutes, out _);

    public static CyclicalWavesV2IntradaySchedule Create(string windowStartTehran, string windowEndTehran, int intervalMinutes) =>
        TryCreate(windowStartTehran, windowEndTehran, intervalMinutes, out var schedule)
            ? schedule!
            : throw new FormatException(
                "The market-hours window needs HH:mm start and end times with the start before the end, " +
                $"and an interval of {MinimumIntervalMinutes} to {MaximumIntervalMinutes} minutes.");

    public static bool TryCreate(
        string? windowStartTehran,
        string? windowEndTehran,
        int intervalMinutes,
        out CyclicalWavesV2IntradaySchedule? schedule)
    {
        schedule = null;
        if (!TimeOnly.TryParseExact(windowStartTehran, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(windowEndTehran, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
            start >= end ||
            intervalMinutes is < MinimumIntervalMinutes or > MaximumIntervalMinutes)
        {
            return false;
        }

        schedule = new CyclicalWavesV2IntradaySchedule(start.ToTimeSpan(), end.ToTimeSpan(), TimeSpan.FromMinutes(intervalMinutes));
        return true;
    }

    /// <summary>The first slot strictly after <paramref name="afterUtc"/>, in UTC.</summary>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset afterUtc)
    {
        var localDate = afterUtc.ToOffset(TehranOffset).Date;
        // Eight days always contain a trading day whose first slot is after the given time.
        for (var dayOffset = 0; dayOffset <= 7; dayOffset++)
        {
            var date = localDate.AddDays(dayOffset);
            if (!IsTradingDay(date.DayOfWeek))
            {
                continue;
            }

            for (var slot = _windowStart; slot <= _windowEnd; slot += Interval)
            {
                var candidate = new DateTimeOffset(date + slot, TehranOffset);
                if (candidate > afterUtc)
                {
                    return candidate.ToUniversalTime();
                }
            }
        }

        throw new InvalidOperationException("The market-hours schedule has no occurrence in the next eight days.");
    }

    public static bool IsTradingDay(DayOfWeek day) =>
        day is DayOfWeek.Saturday or DayOfWeek.Sunday or DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday;
}
