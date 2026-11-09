using System.Globalization;
using System.Text;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Plain-text form of the card, used by Telegram and stored as the message text.</summary>
public static class SymbolSnapshotTextComposer
{
    public static string Compose(SymbolSnapshotResult result)
    {
        if (!result.HasData())
            return result.IntroFa;

        var builder = new StringBuilder();
        builder.AppendLine(result.IntroFa);
        AppendPrice(builder, result.Price);
        AppendMonthly(builder, result.MonthlySales);
        AppendQuarterly(builder, result.QuarterlyReport);
        return builder.ToString().TrimEnd();
    }

    private static void AppendPrice(StringBuilder builder, SymbolSnapshotPrice price)
    {
        builder.AppendLine().AppendLine("آخرین قیمت");
        if (price.Status != SymbolSnapshotSectionStatus.Available)
        {
            builder.AppendLine(price.NoticeFa);
            return;
        }

        builder.Append(Number(price.LatestPrice)).Append(" ریال");
        if (price.DailyChangePercent is { } change)
            builder.Append(" (").Append(Percent(change)).Append(')');
        builder.AppendLine();
        builder.Append("تاریخ معامله: ").AppendLine(Digits(price.TradingDateJalali ?? string.Empty));
    }

    private static void AppendMonthly(StringBuilder builder, SymbolSnapshotMonthlySales monthly)
    {
        builder.AppendLine();
        if (monthly.Status != SymbolSnapshotSectionStatus.Available)
        {
            builder.AppendLine("فروش ماهانه").AppendLine(monthly.NoticeFa);
            return;
        }

        builder.Append("فروش ماهانه — ").AppendLine(Digits(monthly.PeriodLabelFa ?? string.Empty));
        builder.Append(Number(monthly.SalesAmount)).Append(' ').AppendLine(monthly.UnitLabelFa);
        if (monthly.MonthOverMonthPercent is { } monthOverMonth)
            builder.Append("نسبت به ماه قبل: ").AppendLine(Percent(monthOverMonth));
        if (monthly.YearOverYearPercent is { } yearOverYear)
            builder.Append("نسبت به ماه مشابه سال قبل: ").AppendLine(Percent(yearOverYear));
    }

    private static void AppendQuarterly(StringBuilder builder, SymbolSnapshotQuarterlyReport report)
    {
        builder.AppendLine();
        if (report.Status != SymbolSnapshotSectionStatus.Available)
        {
            builder.AppendLine("گزارش فصلی").AppendLine(report.NoticeFa);
            return;
        }

        builder.Append("گزارش فصلی — ").Append(Digits(report.PeriodLabelFa ?? string.Empty));
        if (report.IsAudited is { } audited)
            builder.Append(audited ? " (حسابرسی‌شده)" : " (حسابرسی‌نشده)");
        builder.AppendLine();
        foreach (var row in report.Rows ?? [])
        {
            builder.Append(row.LabelFa).Append(": ");
            builder.Append(row.Value is null ? "—" : $"{Number(row.Value)} {row.UnitLabelFa}");
            if (row.YearOverYearPercent is { } growth)
                builder.Append(" (نسبت به فصل مشابه سال قبل ").Append(Percent(growth)).Append(')');
            builder.AppendLine();
        }
    }

    private static string Number(decimal? value) =>
        value is { } number
            ? Digits(number.ToString("#,##0.##", CultureInfo.InvariantCulture)).Replace(',', '٬').Replace('.', '٫')
            : "—";

    private static string Percent(decimal value)
    {
        var sign = value > 0 ? "+" : value < 0 ? "-" : string.Empty;
        var magnitude = Digits(Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture)).Replace('.', '٫');
        return $"{sign}{magnitude}٪";
    }

    private static string Digits(string value) =>
        string.Concat(value.Select(character => character is >= '0' and <= '9' ? (char)('۰' + character - '0') : character));
}
