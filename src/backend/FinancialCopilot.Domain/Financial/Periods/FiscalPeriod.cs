namespace FinancialCopilot.Domain.Financial.Periods;

public enum FiscalPeriodType
{
    Monthly,
    ThreeMonths,
    SixMonths,
    NineMonths,
    TwelveMonths,
    LatestQuarter,
    LatestMonth,
    TrailingTwelveMonths
}

public sealed record FiscalPeriod
{
    private FiscalPeriod(FiscalPeriodType type, DateOnly? startDate, DateOnly? endDate)
    {
        Type = type;
        StartDate = startDate;
        EndDate = endDate;
    }

    public FiscalPeriodType Type { get; }

    public DateOnly? StartDate { get; }

    public DateOnly? EndDate { get; }

    public bool IsLatestSelection => Type is FiscalPeriodType.LatestMonth or FiscalPeriodType.LatestQuarter;

    public static FiscalPeriod Closed(
        FiscalPeriodType type,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (type is FiscalPeriodType.LatestMonth or FiscalPeriodType.LatestQuarter)
        {
            throw new ArgumentException(
                "Latest-period selections must be resolved to a closed period before use.",
                nameof(type));
        }

        if (endDate < startDate)
        {
            throw new ArgumentException("Fiscal period end date must not precede its start date.", nameof(endDate));
        }

        return new FiscalPeriod(type, startDate, endDate);
    }

    public static FiscalPeriod LatestMonth() => new(FiscalPeriodType.LatestMonth, null, null);

    public static FiscalPeriod LatestQuarter() => new(FiscalPeriodType.LatestQuarter, null, null);

    internal FiscalPeriod ShiftMonths(int months)
    {
        if (StartDate is null || EndDate is null)
        {
            throw new InvalidOperationException(
                "A latest-period selection must be resolved before it can be compared.");
        }

        var shiftedStart = StartDate.Value.AddMonths(months);
        var durationMonths = Type switch
        {
            FiscalPeriodType.Monthly => 1,
            FiscalPeriodType.ThreeMonths => 3,
            FiscalPeriodType.SixMonths => 6,
            FiscalPeriodType.NineMonths => 9,
            FiscalPeriodType.TwelveMonths or FiscalPeriodType.TrailingTwelveMonths => 12,
            _ => 0
        };
        var isCompleteCalendarPeriod = durationMonths > 0 &&
            EndDate.Value == StartDate.Value.AddMonths(durationMonths).AddDays(-1);
        var shiftedEnd = isCompleteCalendarPeriod
            ? shiftedStart.AddMonths(durationMonths).AddDays(-1)
            : EndDate.Value.AddMonths(months);

        return Closed(Type, shiftedStart, shiftedEnd);
    }
}
