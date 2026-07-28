using System.Globalization;
using FinancialCopilot.Application.FinancialData.Providers;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

internal static class JalaliDateResolver
{
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
