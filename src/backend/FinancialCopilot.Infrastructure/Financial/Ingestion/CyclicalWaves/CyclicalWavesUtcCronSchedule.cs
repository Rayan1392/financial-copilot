namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class CyclicalWavesUtcCronSchedule
{
    private readonly CronField _minute;
    private readonly CronField _hour;
    private readonly CronField _dayOfMonth;
    private readonly CronField _month;
    private readonly CronField _dayOfWeek;

    private CyclicalWavesUtcCronSchedule(
        CronField minute,
        CronField hour,
        CronField dayOfMonth,
        CronField month,
        CronField dayOfWeek)
    {
        _minute = minute;
        _hour = hour;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
    }

    public static bool IsValid(string? expression) => TryParse(expression, out _);

    public static CyclicalWavesUtcCronSchedule Parse(string expression)
    {
        if (!TryParse(expression, out var schedule))
        {
            throw new FormatException("Schedule must be a valid five-field UTC cron expression.");
        }

        return schedule!;
    }

    public DateTimeOffset GetNextOccurrence(DateTimeOffset afterUtc)
    {
        var utc = afterUtc.ToUniversalTime();
        var candidate = new DateTimeOffset(
                utc.Year,
                utc.Month,
                utc.Day,
                utc.Hour,
                utc.Minute,
                0,
                TimeSpan.Zero)
            .AddMinutes(1);
        var limit = candidate.AddYears(8);

        while (candidate < limit)
        {
            if (Matches(candidate))
            {
                return candidate;
            }

            candidate = candidate.AddMinutes(1);
        }

        throw new InvalidOperationException("UTC cron schedule has no occurrence in the supported range.");
    }

    private bool Matches(DateTimeOffset value)
    {
        if (!_minute.Contains(value.Minute) ||
            !_hour.Contains(value.Hour) ||
            !_month.Contains(value.Month))
        {
            return false;
        }

        var dayOfMonthMatches = _dayOfMonth.Contains(value.Day);
        var dayOfWeekMatches = _dayOfWeek.Contains((int)value.DayOfWeek);

        return (_dayOfMonth.IsWildcard, _dayOfWeek.IsWildcard) switch
        {
            (true, true) => true,
            (true, false) => dayOfWeekMatches,
            (false, true) => dayOfMonthMatches,
            _ => dayOfMonthMatches || dayOfWeekMatches
        };
    }

    private static bool TryParse(string? expression, out CyclicalWavesUtcCronSchedule? schedule)
    {
        schedule = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5 ||
            !CronField.TryParse(fields[0], 0, 59, false, out var minute) ||
            !CronField.TryParse(fields[1], 0, 23, false, out var hour) ||
            !CronField.TryParse(fields[2], 1, 31, false, out var dayOfMonth) ||
            !CronField.TryParse(fields[3], 1, 12, false, out var month) ||
            !CronField.TryParse(fields[4], 0, 7, true, out var dayOfWeek))
        {
            return false;
        }

        schedule = new CyclicalWavesUtcCronSchedule(
            minute!,
            hour!,
            dayOfMonth!,
            month!,
            dayOfWeek!);
        return true;
    }

    private sealed class CronField
    {
        private readonly HashSet<int> _values;

        private CronField(HashSet<int> values, bool isWildcard)
        {
            _values = values;
            IsWildcard = isWildcard;
        }

        public bool IsWildcard { get; }
        public bool Contains(int value) => _values.Contains(value);

        public static bool TryParse(
            string text,
            int minimum,
            int maximum,
            bool normalizeSunday,
            out CronField? field)
        {
            field = null;
            var values = new HashSet<int>();
            var isWildcard = text == "*";

            foreach (var segment in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var stepParts = segment.Split('/');
                if (stepParts.Length > 2 ||
                    (stepParts.Length == 2 &&
                     (!int.TryParse(stepParts[1], out var step) || step <= 0)))
                {
                    return false;
                }

                var increment = stepParts.Length == 2 ? int.Parse(stepParts[1]) : 1;
                var range = stepParts[0];
                int start;
                int end;

                if (range == "*")
                {
                    start = minimum;
                    end = maximum;
                }
                else
                {
                    var rangeParts = range.Split('-');
                    if (rangeParts.Length == 1 && int.TryParse(rangeParts[0], out var single))
                    {
                        start = single;
                        end = single;
                    }
                    else if (rangeParts.Length == 2 &&
                             int.TryParse(rangeParts[0], out start) &&
                             int.TryParse(rangeParts[1], out end))
                    {
                    }
                    else
                    {
                        return false;
                    }
                }

                if (start < minimum || end > maximum || start > end)
                {
                    return false;
                }

                for (var value = start; value <= end; value += increment)
                {
                    values.Add(normalizeSunday && value == 7 ? 0 : value);
                }
            }

            if (values.Count == 0)
            {
                return false;
            }

            field = new CronField(values, isWildcard);
            return true;
        }
    }
}
