using System.Globalization;
using System.Text;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.Telegram;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramAssistantResponseRenderer(
    ITelegramMonthlyTrendChartRenderer monthlyTrendChartRenderer,
    ILogger<TelegramAssistantResponseRenderer> logger) : ITelegramAssistantResponseRenderer
{
    private const int TelegramMessageLimit = 3900;
    private const int TelegramPhotoCaptionLimit = 1024;

    public string Version => "telegram-render-v3";

    public IReadOnlyList<TelegramAssistantRenderedMessage> Render(
        AiQueryResponse response,
        string locale)
    {
        if (response.MonthlyActivityTrendResult is not null)
        {
            return RenderMonthlyTrend(response, response.MonthlyActivityTrendResult);
        }

        var builder = new StringBuilder();
        var isSingleSymbolLookup = response.SymbolLookupTable is { Rows.Count: 1 } &&
                                   response.ScannerTable is null;

        if (response.ClarificationRequired && !string.IsNullOrWhiteSpace(response.ClarificationMessage))
        {
            builder.AppendLine(response.ClarificationMessage);
        }
        else if (!isSingleSymbolLookup && !string.IsNullOrWhiteSpace(response.TextAnswer))
        {
            builder.AppendLine(response.TextAnswer);
        }

        if (isSingleSymbolLookup)
        {
            AppendStockCard(builder, response.SymbolLookupTable!.Rows.Single(), response.SymbolLookupTable.Columns);
        }
        else
        {
            AppendAnalysis(builder, response);
            AppendLookupRows(builder, response.SymbolLookupTable);
            AppendScannerRows(builder, response.ScannerTable);
        }

        AppendConfidence(builder, response);
        AppendUsage(builder, response);
        AppendCitations(builder, response);

        var text = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "پاسخ دستیار آماده شد، اما متن قابل نمایش برای تلگرام تولید نشد.";
        }

        return Split(EscapeMarkdownV2(text));
    }

    private IReadOnlyList<TelegramAssistantRenderedMessage> RenderMonthlyTrend(
        AiQueryResponse response,
        MonthlyActivityTrendResponse trend)
    {
        var builder = new StringBuilder();
        const string captionUnit = "میلیون ریال";
        var company = string.IsNullOrWhiteSpace(trend.CompanyName)
            ? trend.CompanySymbol
            : $"{trend.CompanyName} ({trend.CompanySymbol})";

        builder.AppendLine($"📊 روند فروش ماهانه — {company}");
        builder.AppendLine($"آخرین دوره: {ToPersianDigits($"{trend.LatestReportYear}/{trend.LatestReportMonth:00}")} | واحد: {captionUnit}");
        builder.AppendLine();
        builder.AppendLine($"فروش آخرین ماه: {FormatMillionRial(trend.LatestMonthlySalesAmount)} {captionUnit}");
        builder.AppendLine($"نسبت به ماه مشابه سال قبل: {FormatPercent(trend.SalesAmountYoYGrowthPercent)}");
        builder.AppendLine($"نسبت به میانگین ۱۲ ماهه: {FormatPercent(trend.SalesVsAverage12MonthPercent)}");
        builder.AppendLine();
        builder.AppendLine($"منبع: {ProviderSources.GetDisplayName(trend.SourceProviderName)} | محاسبه: {ToPersianDigits(ShamsiMonthCalculator.FormatJalaliDate(trend.CalculatedAtUtc))}");
        AppendUsage(builder, response);

        var caption = EscapeMarkdownV2(builder.ToString().Trim());
        TelegramAssistantMediaAttachment? media = null;
        try
        {
            media = monthlyTrendChartRenderer.Render(trend);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Telegram monthly trend image rendering failed for symbol {Symbol}; returning the concise text fallback.",
                trend.CompanySymbol);
        }

        var parts = SplitText(caption, TelegramPhotoCaptionLimit);
        return parts
            .Select((part, index) => new TelegramAssistantRenderedMessage(
                index + 1,
                parts.Count,
                part,
                Media: index == 0 ? media : null))
            .ToArray();
    }

    private static void AppendStockCard(
        StringBuilder builder,
        ScannerTableRow row,
        IReadOnlyCollection<ScannerTableColumn> columns)
    {
        builder.AppendLine($"📈 {row.SymbolCode}{(string.IsNullOrWhiteSpace(row.CompanyName) ? string.Empty : $" — {row.CompanyName}")}");
        builder.AppendLine();

        foreach (var column in columns.Where(column =>
                     column.ColumnType is not ScannerColumnType.Symbol and not ScannerColumnType.CompanyName))
        {
            if (!row.Cells.TryGetValue(column.Identifier, out var cell))
            {
                continue;
            }

            builder.AppendLine($"{GetLabel(column)}: {FormatCell(cell, column)}");
        }

        var evidenceCell = FindEvidenceCell(row, columns);
        if (evidenceCell is not null)
        {
            var evidence = new List<string>();
            if (!string.IsNullOrWhiteSpace(evidenceCell.TradingDatePersian))
            {
                evidence.Add($"تاریخ معامله: {ToPersianDigits(evidenceCell.TradingDatePersian)}");
            }
            else if (evidenceCell.TradingDate is not null)
            {
                evidence.Add($"تاریخ معامله: {ToPersianDigits(evidenceCell.TradingDate.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture))}");
            }

            if (!string.IsNullOrWhiteSpace(evidenceCell.SourceLabel))
            {
                evidence.Add($"منبع: {GetFriendlySource(evidenceCell.SourceLabel)}");
            }
            evidence.Add($"تازگی: {GetFreshnessLabel(evidenceCell.FreshnessStatus)}");

            if (evidence.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(string.Join(" | ", evidence));
            }
        }
    }

    private static void AppendLookupRows(StringBuilder builder, SymbolLookupTableResult? table)
    {
        if (table is null || table.Rows.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("📊 نمادها:");
        foreach (var row in table.Rows)
        {
            AppendCompactRow(builder, row, table.Columns);
        }
    }

    private static void AppendScannerRows(StringBuilder builder, ScannerTableResult? table)
    {
        if (table is null || table.Rows.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("📊 نتایج فیلتر:");
        foreach (var row in table.Rows.Take(8))
        {
            builder.AppendLine($"{row.SymbolCode}{(string.IsNullOrWhiteSpace(row.CompanyName) ? string.Empty : $" — {row.CompanyName}")}");
            AppendCells(builder, row, table.Columns);
        }

        if (table.ExecutionFacts.TotalPages > table.ExecutionFacts.Page)
        {
            builder.AppendLine($"صفحه {table.ExecutionFacts.Page} از {table.ExecutionFacts.TotalPages}؛ برای ادامه، نتیجه کامل را در وب مشاهده کنید.");
        }
    }

    private static void AppendCompactRow(
        StringBuilder builder,
        ScannerTableRow row,
        IReadOnlyCollection<ScannerTableColumn> columns)
    {
        builder.AppendLine($"• {row.SymbolCode}{(string.IsNullOrWhiteSpace(row.CompanyName) ? string.Empty : $" — {row.CompanyName}")}");
        AppendCells(builder, row, columns);
    }

    private static void AppendCells(
        StringBuilder builder,
        ScannerTableRow row,
        IReadOnlyCollection<ScannerTableColumn> columns)
    {
        var values = columns
            .Where(column => column.ColumnType is not ScannerColumnType.Symbol and not ScannerColumnType.CompanyName)
            .Where(column => row.Cells.ContainsKey(column.Identifier))
            .Take(8)
            .Select(column => $"{GetLabel(column)}: {FormatCell(row.Cells[column.Identifier], column)}");
        builder.AppendLine($"  {string.Join(" | ", values)}");
    }

    private static void AppendAnalysis(StringBuilder builder, AiQueryResponse response)
    {
        var items = response.ComprehensiveAnalysisResult?.Items;
        if (items is null || items.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("تحلیل‌های یافت‌شده:");
        foreach (var item in items.Take(3))
        {
            builder.AppendLine($"{item.Title} — {item.PersianCreatedAt}");
            builder.AppendLine(item.PlainTextSummary);
            builder.AppendLine($"منبع: ComprehensiveAnalyses | نویسنده: {item.AuthorName}");
            builder.AppendLine();
        }
    }

    private static void AppendConfidence(StringBuilder builder, AiQueryResponse response)
    {
        if (response.ConfidenceScore is not null)
        {
            builder.AppendLine();
            builder.AppendLine($"اطمینان پاسخ: {response.ConfidenceScore.Score:P0}");
        }
    }

    private static void AppendUsage(StringBuilder builder, AiQueryResponse response)
    {
        if (response.Usage is not null)
        {
            builder.AppendLine();
            builder.AppendLine($"اعتبار: {FormatDecimal(response.Usage.CreditsCharged)} مصرف شد | {FormatDecimal(response.Usage.RemainingSpendingCapacity)} باقی‌مانده");
        }
    }

    private static void AppendCitations(StringBuilder builder, AiQueryResponse response)
    {
        var citations = response.ExplainableAnswer?.DataCitations;
        if (citations is null || citations.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("استناد داده:");
        foreach (var citation in citations.Take(5))
        {
            builder.AppendLine($"- {citation.SymbolCode} / {GetMetricLabel(citation.MetricCode)}: {GetFriendlySource(citation.SourceProvider)}؛ {citation.FreshnessStatus}");
        }
    }

    private static ScannerTableCell? FindEvidenceCell(
        ScannerTableRow row,
        IReadOnlyCollection<ScannerTableColumn> columns)
    {
        foreach (var column in columns.OrderBy(column => column.ColumnType == ScannerColumnType.LatestPrice ? 0 : 1))
        {
            if (row.Cells.TryGetValue(column.Identifier, out var cell) &&
                (cell.TradingDate is not null ||
                 !string.IsNullOrWhiteSpace(cell.TradingDatePersian) ||
                 !string.IsNullOrWhiteSpace(cell.SourceLabel)))
            {
                return cell;
            }
        }

        return row.Cells.Values.FirstOrDefault();
    }

    private static string GetLabel(ScannerTableColumn column)
    {
        if (!string.IsNullOrWhiteSpace(column.MetricCode))
        {
            return GetMetricLabel(column.MetricCode);
        }

        if (column.ColumnType == ScannerColumnType.LatestPrice)
        {
            return "آخرین قیمت";
        }

        if (column.ColumnType == ScannerColumnType.DailyChangePercent)
        {
            return "تغییر روزانه";
        }

        return column.DisplayName switch
        {
            "SYMBOL" => "نماد",
            "COMPANY_NAME" => "شرکت",
            "LATEST_PRICE" => "آخرین قیمت",
            "DAILY_CHANGE_PCT" => "تغییر روزانه",
            _ => column.DisplayName
        };
    }

    private static string GetMetricLabel(string metricCode) => metricCode switch
    {
        "LATEST_PRICE" => "آخرین قیمت",
        "DAILY_CHANGE_PCT" => "تغییر روزانه",
        "PE_TTM" => "P/E",
        "PS_TTM" => "P/S",
        "EPS" => "EPS",
        "MONTHLY_SALES" => "فروش ماهانه",
        "MONTHLY_SALES_GROWTH_YOY" => "رشد فروش ماهانه نسبت به سال قبل",
        "MONTHLY_PRODUCTION_QUANTITY" => "تولید ماهانه",
        "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH" => "فروش ماه مشابه سال قبل",
        "AVG_12M_MONTHLY_SALES" => "متوسط فروش ۱۲ ماهه",
        "MONTHLY_SALES_YTD" => "فروش تجمیعی از ابتدای سال",
        "MONTHLY_SALES_YTD_PREVIOUS_MONTH" => "فروش تجمیعی تا ماه قبل",
        _ => metricCode
    };

    private static string FormatCell(ScannerTableCell cell, ScannerTableColumn column)
    {
        var value = cell.FormattedValue is not null
            ? ToPersianDigits(cell.FormattedValue)
            : ToPersianDigits(cell.Value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—");

        return IsMonthlySalesMonetaryMetric(column) ? $"{value} میلیون ریال" : value;
    }

    private static bool IsMonthlySalesMonetaryMetric(ScannerTableColumn column)
    {
        var metricCode = column.MetricCode ?? column.Identifier;
        return metricCode is "MONTHLY_SALES"
            or "AVG_12M_MONTHLY_SALES"
            or "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH"
            or "MONTHLY_SALES_YTD"
            or "MONTHLY_SALES_YTD_PREVIOUS_MONTH";
    }

    private static string GetFreshnessLabel(CellFreshnessStatus status) => status switch
    {
        CellFreshnessStatus.Live => "زنده",
        CellFreshnessStatus.PreviousTradingDay => "آخرین روز معاملاتی",
        CellFreshnessStatus.Persisted => "ذخیره‌شده",
        CellFreshnessStatus.Missing => "در دسترس نیست",
        _ => status.ToString()
    };

    private static string GetFriendlySource(string? provider) => provider switch
    {
        null or "" => "منبع ثبت‌شده",
        "IntradayToday" => "معاملات روزانه",
        _ => ProviderSources.GetDisplayName(provider)
    };

    private static IReadOnlyList<TelegramAssistantRenderedMessage> Split(string text)
    {
        var parts = SplitText(text, TelegramMessageLimit).ToList();

        if (parts.Count == 0)
        {
            parts.Add(EscapeMarkdownV2("پاسخی برای نمایش وجود ندارد."));
        }

        return parts
            .Select((part, index) => new TelegramAssistantRenderedMessage(
                index + 1,
                parts.Count,
                parts.Count == 1 ? part : EscapeMarkdownV2($"بخش {index + 1}/{parts.Count}") + "\n" + part))
            .ToArray();
    }

    private static IReadOnlyList<string> SplitText(string text, int limit)
    {
        var parts = new List<string>();
        var remaining = text;
        while (remaining.Length > limit)
        {
            var splitAt = remaining.LastIndexOf('\n', limit);
            if (splitAt < limit / 2)
            {
                splitAt = limit;
            }

            if (splitAt > 0 && remaining[splitAt - 1] == '\\')
            {
                splitAt--;
            }

            parts.Add(remaining[..splitAt].Trim());
            remaining = remaining[splitAt..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            parts.Add(remaining);
        }

        return parts;
    }

    private static string EscapeMarkdownV2(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '_' or '*' or '[' or ']' or '(' or ')' or '~' or '`' or '>' or '#' or '+' or '-' or '=' or '|' or '{' or '}' or '.' or '!')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string FormatDecimal(decimal value) =>
        ToPersianDigits(value.ToString("0.##", CultureInfo.InvariantCulture));

    private static string FormatNullableDecimal(decimal? value) =>
        value is null
            ? "—"
            : ToPersianDigits(value.Value.ToString("N3", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.'));

    private static string FormatMillionRial(decimal? billionTooman) =>
        billionTooman is null
            ? "—"
            : ToPersianDigits((billionTooman.Value * 10_000m)
                .ToString("N0", CultureInfo.InvariantCulture));

    private static string FormatPercent(decimal? value)
    {
        if (value is null)
        {
            return "—";
        }

        var sign = value.Value > 0 ? "+" : string.Empty;
        return ToPersianDigits($"{sign}{value.Value.ToString("0.#", CultureInfo.InvariantCulture)}٪");
    }

    private static string ToPersianDigits(string value) =>
        value
            .Replace(',', '٬')
            .Replace('.', '٫')
            .Replace('0', '۰')
            .Replace('1', '۱')
            .Replace('2', '۲')
            .Replace('3', '۳')
            .Replace('4', '۴')
            .Replace('5', '۵')
            .Replace('6', '۶')
            .Replace('7', '۷')
            .Replace('8', '۸')
            .Replace('9', '۹');
}
