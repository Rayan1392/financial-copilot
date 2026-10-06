using System.Globalization;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;

internal static class PersianFinancialResponseFormatter
{
    private const string PersianDigits = "۰۱۲۳۴۵۶۷۸۹";

    public static string FormatBillionToman(decimal? amountMillionRial)
    {
        if (amountMillionRial is null)
            return "—";

        // 10,000 million rials equal one billion tomans. Keep one decimal for
        // normal-sized values and enough precision to avoid turning small
        // non-zero amounts into zero.
        var billionToman = amountMillionRial.Value / 10_000m;
        var format = Math.Abs(billionToman) >= 1m ? "#,##0.#" : "#,##0.####";
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
        return $"فروش «{productTitle}» شرکت {companySymbol} در دوره {ToPersianDigits($"{reportYear:0000}/{reportMonth:00}")} حدود {amount} میلیارد تومان بوده است.";
    }

    private static string ToPersianDigits(string value) => string.Concat(
        value.Select(character => character is >= '0' and <= '9'
            ? PersianDigits[character - '0']
            : character));
}
