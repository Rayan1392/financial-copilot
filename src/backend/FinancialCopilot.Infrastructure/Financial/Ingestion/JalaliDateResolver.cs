using System.Globalization;
using FinancialCopilot.Application.FinancialData.Providers;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

internal static class JalaliDateResolver
{
    public static bool TryResolveDate(string value, out DateOnly date)
    {
        date = default;
        var parts = value.Replace('/', '-').Split('-');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var year)
            || !int.TryParse(parts[1], out var month)
            || !int.TryParse(parts[2], out var day))
            return false;

        try
        {
            date = DateOnly.FromDateTime(Calendar.ToDateTime(year, month, day, 0, 0, 0, 0));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static (DateOnly PeriodStart, DateOnly PeriodEnd) ResolveMonth(int jalaliYear, byte jalaliMonth)
    {
        try
        {
            var firstDay = Calendar.ToDateTime(jalaliYear, jalaliMonth, 1, 0, 0, 0, 0);
            var daysInMonth = Calendar.GetDaysInMonth(jalaliYear, jalaliMonth);
            var lastDay = Calendar.ToDateTime(jalaliYear, jalaliMonth, daysInMonth, 0, 0, 0, 0);
            return (DateOnly.FromDateTime(firstDay), DateOnly.FromDateTime(lastDay));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                $"Jalali month '{jalaliYear}/{jalaliMonth}' is not valid.",
                exception);
        }
    }

    private static readonly PersianCalendar Calendar = new();
}
