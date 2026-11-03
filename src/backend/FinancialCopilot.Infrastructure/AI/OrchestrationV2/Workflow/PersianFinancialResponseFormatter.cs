using System.Globalization;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;

internal static class PersianFinancialResponseFormatter
{
    private const string PersianDigits = "۰۱۲۳۴۵۶۷۸۹";

    private static readonly string[] JalaliMonthNames =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    public static string FormatBillionToman(decimal? amountMillionRial)
    {
        if (amountMillionRial is null)
            return "—";

        // 10,000 million rials equal one billion tomans. Keep two decimals for
        // normal-sized values, as the Feature 141 answers do for the same fact,
        // and enough precision to avoid turning small non-zero amounts into zero.
        var billionToman = amountMillionRial.Value / 10_000m;
        var format = Math.Abs(billionToman) >= 1m ? "#,##0.##" : "#,##0.####";
        var formatted = billionToman.ToString(format, CultureInfo.InvariantCulture)
            .Replace(',', '٬')
            .Replace('.', '٫');
        return ToPersianDigits(formatted);
    }

    public static string ProductSalesValue(
        string companySymbol,
        string productTitle,
        int reportYear,
        int reportMonth,
        decimal? amountMillionRial)
    {
        var amount = FormatBillionToman(amountMillionRial);
        var period = reportMonth is >= 1 and <= 12
            ? $"{JalaliMonthNames[reportMonth - 1]} {ToPersianDigits($"{reportYear:0000}")}"
            : ToPersianDigits($"{reportYear:0000}/{reportMonth:00}");
        return $"فروش «{productTitle}» شرکت {companySymbol} در {period} حدود {amount} میلیارد تومان بوده است.";
    }

    private static string ToPersianDigits(string value) => string.Concat(
        value.Select(character => character is >= '0' and <= '9'
            ? PersianDigits[character - '0']
            : character));
}
