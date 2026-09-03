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
    private const int MonthlyProductComparisonRowLimit = 20;

    public string Version => "telegram-render-v3";

    public IReadOnlyList<TelegramAssistantRenderedMessage> Render(
        AiQueryResponse response,
        string locale)
    {
        if (response.DisclosureListingResult is not null)
        {
            return RenderDisclosureListing(response, response.DisclosureListingResult);
        }

        if (response.MonthlyActivityTrendResult is not null)
        {
            return RenderMonthlyTrend(response, response.MonthlyActivityTrendResult);
        }

        if (response.MonthlyProductComparisonResult is not null)
        {
            return RenderMonthlyProductComparison(response.MonthlyProductComparisonResult);
        }

        if (response.ScannerTable?.SalesGrowthMetadata is not null)
        {
            return RenderSalesGrowthScanner(response, response.ScannerTable);
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
            builder.AppendLine(TryFormatIndustryComparison(response.TextAnswer, out var comparison)
                ? comparison
                : response.TextAnswer);
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
        AppendSuggestedActions(builder, response);

        var text = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "پاسخ دستیار آماده شد، اما متن قابل نمایش برای تلگرام تولید نشد.";
        }

        return Split(EscapeMarkdownV2(text));
    }

    private static IReadOnlyList<TelegramAssistantRenderedMessage> RenderMonthlyProductComparison(MonthlyProductComparisonResponse result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"مقایسه فروش محصولات: {result.CompanyText}");
        sb.AppendLine($"دوره جاری: {result.CurrentPeriod?.ToString() ?? "—"} | دوره مقایسه: {result.ComparisonPeriod?.ToString() ?? "—"}");
        if (!string.IsNullOrWhiteSpace(result.ClarificationMessage)) sb.AppendLine(result.ClarificationMessage);
        if (result.Totals is not null)
        {
            sb.AppendLine($"فروش جاری: {result.Totals.Current:N2} | مقایسه: {result.Totals.Comparison:N2}");
            sb.AppendLine($"تغییر: {result.Totals.Change:N2} | درصد: {(result.Totals.ChangePercent.HasValue ? result.Totals.ChangePercent.Value.ToString("N2") + "٪" : "—")}");
        }
        if (result.Products.Count > 0)
        {
            sb.AppendLine("محصولات:");
            foreach (var product in result.Products.Take(MonthlyProductComparisonRowLimit))
            {
                sb.AppendLine(
                    $"- {product.DisplayTitle} | جاری: {FormatProductComparisonDecimal(product.Current?.SalesAmount)} | " +
                    $"مقایسه: {FormatProductComparisonDecimal(product.Comparison?.SalesAmount)} | " +
                    $"تغییر: {FormatProductComparisonDecimal(product.SalesChange)} | واحد: {product.RawUnit ?? product.Current?.Unit ?? product.Comparison?.Unit ?? "—"}");

                if (product.Warnings.Count > 0)
                    sb.AppendLine($"  هشدار محصول: {string.Join("، ", product.Warnings)}");

                if (product.ProductionSalesDifference.HasValue)
                    sb.AppendLine($"  اختلاف استنباطی تولید و فروش: {FormatProductComparisonDecimal(product.ProductionSalesDifference)}");
            }
        }
        if (result.Warnings.Count > 0) sb.AppendLine($"هشدار: {string.Join("، ", result.Warnings)}");
        if (result.Evidence.Count > 0) sb.AppendLine("منبع: نوآوران امین");
        return Split(EscapeMarkdownV2(sb.ToString().Trim()));
    }

    private static string FormatProductComparisonDecimal(decimal? value) =>
        value.HasValue ? value.Value.ToString("N2", CultureInfo.InvariantCulture) : "—";

    private static bool TryFormatIndustryComparison(string text, out string formatted)
    {
        formatted = string.Empty;
        if (!text.Contains("مقایسه نماد با صنعت", StringComparison.Ordinal) || !text.Contains('|'))
            return false;

        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        var tableStart = lines.FindIndex(line => line.StartsWith('|'));
        if (tableStart < 0 || tableStart + 2 >= lines.Count)
            return false;

        var headers = SplitTableLine(lines[tableStart]);
        if (headers.Length < 2 || !headers[0].Equals("نماد", StringComparison.Ordinal))
            return false;

        var rows = new List<(string Symbol, string[] Values)>();
        for (var index = tableStart + 2; index < lines.Count; index++)
        {
            var cells = SplitTableLine(lines[index]);
            if (cells.Length != headers.Length)
                continue;
            if (cells[0].Equals("میانگین صنعت", StringComparison.Ordinal))
            {
                var benchmark = cells.Skip(1).ToArray();
                var cards = rows.Select(row => FormatIndustryCard(row.Symbol, row.Values, headers, benchmark));
                var prefix = lines.Take(tableStart)
                    .Where(line => !line.StartsWith("###", StringComparison.Ordinal))
                    .Select(line => line.Replace("**", string.Empty, StringComparison.Ordinal));
                formatted = string.Join(Environment.NewLine, prefix.Append(string.Empty).Concat(cards));
                return true;
            }

            rows.Add((cells[0], cells.Skip(1).ToArray()));
        }

        return false;
    }

    private static string FormatIndustryCard(string symbol, string[] values, IReadOnlyList<string> headers, string[] benchmark)
    {
        var builder = new StringBuilder($"📌 {symbol}");
        for (var index = 0; index < values.Length && index + 1 < headers.Count; index++)
        {
            var value = values[index];
            var comparison = CompareIndustryValues(value, benchmark.ElementAtOrDefault(index));
            builder.AppendLine();
            builder.Append($"{headers[index + 1]}: {value} {comparison}");
        }
        return builder.ToString();
    }

    private static string CompareIndustryValues(string value, string? benchmark)
    {
        if (!TryParsePersianPercent(value, out var current) || !TryParsePersianPercent(benchmark, out var industry))
            return "⚪ قابل مقایسه نیست";
        if (current == industry)
            return "⚪ برابر با میانگین صنعت";
        return current < industry
            ? $"🟢 کمتر از میانگین صنعت ({benchmark})"
            : $"🔴 بیشتر از میانگین صنعت ({benchmark})";
    }

    private static bool TryParsePersianPercent(string? value, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().TrimEnd('٪', '%')
            .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
            .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
            .Replace('٫', '.').Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static string[] SplitTableLine(string line) =>
        line.Trim().Trim('|').Split('|', StringSplitOptions.None).Select(cell => cell.Trim()).ToArray();

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

    private static IReadOnlyList<TelegramAssistantRenderedMessage> RenderDisclosureListing(
        AiQueryResponse response,
        DisclosureListingResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("فهرست اطلاعیه‌های منتشرشده");
        if (result.Items.Count == 0)
        {
            builder.AppendLine("اطلاعیه‌ای با فیلترهای درخواستی یافت نشد.");
        }
        else
        {
            foreach (var (item, index) in result.Items.Select((item, index) => (item, index)))
            {
                var company = item.Symbol ?? item.CompanyName ?? "—";
                var published = item.PublishedAt is null ? "نامشخص" : FormatJalaliDateOnly(item.PublishedAt.Value);
                var received = FormatJalaliReceipt(item.ReceivedAt);
                var qualifiers = string.Join("، ", new[]
                {
                    item.IsRevised ? "اصلاحی" : null,
                    item.IsComposing ? "تلفیقی" : null
                }.Where(value => value is not null));
                builder.AppendLine($"{ToPersianDigits((index + 1).ToString(CultureInfo.InvariantCulture))}. {company}");
                builder.AppendLine($"{DisclosureTypeLabel(item.Type)} — {item.Title}");
                builder.AppendLine($"انتشار: {published} | دریافت: {received}{(qualifiers.Length > 0 ? $" | {qualifiers}" : string.Empty)}");
                builder.AppendLine($"منبع: {ProviderSources.GetDisplayName(item.ProviderName)}");
            }
        }

        if (result.CoverageStatus != DisclosureCoverageStatus.Complete)
            builder.AppendLine("هشدار پوشش: بخشی از اطلاعیه‌ها هنوز به شرکت یا نماد نگاشت نشده‌اند.");
        if (result.FreshnessReasonCode.Contains("stale", StringComparison.OrdinalIgnoreCase))
            builder.AppendLine("هشدار تازگی: بخشی از داده‌ها ممکن است به‌روز نباشند.");
        builder.AppendLine($"صفحه {ToPersianDigits(result.Page.ToString(CultureInfo.InvariantCulture))} از {ToPersianDigits(Math.Max(result.TotalPages, 1).ToString(CultureInfo.InvariantCulture))}");
        if (result.HasNextPage)
            builder.AppendLine("نتایج بیشتری وجود دارد.");
        builder.AppendLine("این فهرست صرفاً اطلاع‌رسانی است و توصیهٔ خرید یا فروش نیست.");

        AppendUsage(builder, response);
        return Split(EscapeMarkdownV2(builder.ToString().Trim()));
    }

    private static string DisclosureTypeLabel(CompanyDisclosureType type) => type switch
    {
        CompanyDisclosureType.MonthlyProductionSales => "تولید و فروش ماهانه",
        CompanyDisclosureType.IncomeStatement => "صورت سود و زیان",
        CompanyDisclosureType.BalanceSheet => "ترازنامه",
        CompanyDisclosureType.CashFlowStatement => "جریان وجه نقد",
        _ => type.ToString()
    };

    private static string FormatJalaliDateOnly(DateOnly value) =>
        ShamsiMonthCalculator.FormatJalaliDate(new DateTimeOffset(
            value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3.5)));

    private static string FormatJalaliReceipt(DateTimeOffset value)
    {
        var tehran = value.ToOffset(TimeSpan.FromHours(3.5));
        return $"{ShamsiMonthCalculator.FormatJalaliDate(tehran)} {ToPersianDigits(tehran.ToString("HH:mm", CultureInfo.InvariantCulture))} (تهران)";
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

    private static void AppendSuggestedActions(StringBuilder builder, AiQueryResponse response)
    {
        if (response.SuggestedActions is not { Count: > 0 }) return;
        builder.AppendLine();
        builder.AppendLine(response.ReplyLanguage == "fa" ? "پیشنهادها:" : "Suggestions:");
        var index = 1;
        foreach (var action in response.SuggestedActions.Take(4))
            builder.AppendLine($"{index++}. {action.LocalizedLabel} — {action.Message}");
    }

    private static IReadOnlyList<TelegramAssistantRenderedMessage> RenderSalesGrowthScanner(
        AiQueryResponse response,
        ScannerTableResult table)
    {
        var metadata = table.SalesGrowthMetadata!;
        var builder = new StringBuilder();
        builder.AppendLine("📈 نتایج رشد فروش ماهانه:");
        builder.AppendLine($"دوره هدف: {FormatPeriod(metadata.TargetCommonPeriod)} | پوشش: {ToPersianDigits($"{metadata.CoverageNumerator}/{metadata.CoverageDenominator}")} ({ToPersianDigits(metadata.CoveragePercent.ToString("0.##", CultureInfo.InvariantCulture))}٪)");

        foreach (var row in table.Rows)
        {
            builder.AppendLine();
            builder.AppendLine($"{row.SymbolCode}{(string.IsNullOrWhiteSpace(row.CompanyName) ? string.Empty : $" — {row.CompanyName}")}");
            AppendSalesGrowthCells(builder, row, table.Columns);
        }

        var firstMetadata = table.Rows.Select(row => row.SalesGrowthMetadata).FirstOrDefault(value => value is not null);
        if (firstMetadata is not null)
        {
            var baseline = firstMetadata.BaselinePeriod is not null
                ? FormatPeriod(firstMetadata.BaselinePeriod.Value)
                : firstMetadata.BaselineWindow.Count > 0
                    ? $"{FormatPeriod(firstMetadata.BaselineWindow.First())} تا {FormatPeriod(firstMetadata.BaselineWindow.Last())}"
                    : "نامشخص";
            builder.AppendLine();
            builder.AppendLine($"مقایسه با: {baseline} | منبع تازگی: {firstMetadata.FreshnessSource ?? "ذخیره‌شده"}");
            if (firstMetadata.LatestObservedAtUtc is not null)
                builder.AppendLine($"آخرین مشاهده: {FormatPeriod(firstMetadata.LatestObservedAtUtc.Value)}");
        }

        builder.AppendLine();
        builder.AppendLine($"وضعیت داده: {SalesGrowthStatusLabel(metadata.SelectionStatus)} | صفحه {ToPersianDigits(metadata.CoverageDenominator > 0 ? table.ExecutionFacts.Page.ToString(CultureInfo.InvariantCulture) : "1")} از {ToPersianDigits(Math.Max(table.ExecutionFacts.TotalPages, 1).ToString(CultureInfo.InvariantCulture))}");
        if (response.ConversationId != Guid.Empty)
            builder.AppendLine($"جدول کامل در وب: /c/{response.ConversationId:N}");
        foreach (var warning in table.MissingDataWarnings.Take(3))
            builder.AppendLine($"هشدار: {warning}");
        if (table.ExecutionFacts.TotalPages > table.ExecutionFacts.Page)
            builder.AppendLine("صفحه بعدی با همان فیلتر و تازه‌سازی شواهد قابل دریافت است.");

        AppendUsage(builder, response);
        return Split(EscapeMarkdownV2(builder.ToString().Trim()));
    }

    private static void AppendSalesGrowthCells(
        StringBuilder builder,
        ScannerTableRow row,
        IReadOnlyCollection<ScannerTableColumn> columns)
    {
        var ordered = new[]
        {
            "MONTHLY_SALES",
            "MONTHLY_SALES_BASELINE_PREVIOUS_MONTH",
            "MONTHLY_SALES_BASELINE_SAME_MONTH_PREVIOUS_YEAR",
            "MONTHLY_SALES_BASELINE_AVERAGE_PREVIOUS_12_MONTHS",
            "MONTHLY_SALES_GROWTH_PERCENT",
            "MONTHLY_SALES_GROWTH_MULTIPLE"
        };

        foreach (var identifier in ordered)
        {
            var column = columns.FirstOrDefault(item =>
                string.Equals(item.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
            if (column is null || !row.Cells.TryGetValue(column.Identifier, out var cell))
                continue;

            builder.AppendLine($"{SalesGrowthLabel(identifier)}: {FormatSalesGrowthCell(cell, identifier)}");
        }
    }

    private static string SalesGrowthLabel(string identifier) => identifier switch
    {
        "MONTHLY_SALES" => "فروش فعلی",
        "MONTHLY_SALES_BASELINE_PREVIOUS_MONTH" => "فروش ماه قبل",
        "MONTHLY_SALES_BASELINE_SAME_MONTH_PREVIOUS_YEAR" => "فروش ماه مشابه سال قبل",
        "MONTHLY_SALES_BASELINE_AVERAGE_PREVIOUS_12_MONTHS" => "میانگین فروش ۱۲ ماه قبل",
        "MONTHLY_SALES_GROWTH_PERCENT" => "درصد رشد",
        "MONTHLY_SALES_GROWTH_MULTIPLE" => "نسبت فروش",
        _ => identifier
    };

    private static string FormatSalesGrowthCell(ScannerTableCell cell, string identifier)
    {
        if (cell.FreshnessStatus == CellFreshnessStatus.Missing)
            return "در دسترس نیست";

        var value = cell.FormattedValue is not null
            ? ToPersianDigits(cell.FormattedValue)
            : ToPersianDigits(cell.Value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—");
        return identifier is "MONTHLY_SALES_GROWTH_PERCENT" ? $"{value}٪" : value;
    }

    private static string FormatPeriod(DateOnly period) =>
        ToPersianDigits($"{period.Year:0000}/{period.Month:00}");

    private static string FormatPeriod(DateTimeOffset timestamp) =>
        ToPersianDigits(timestamp.ToOffset(TimeSpan.FromHours(3.5)).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));

    private static string SalesGrowthStatusLabel(SalesGrowthCommonPeriodSelectionStatus status) => status switch
    {
        SalesGrowthCommonPeriodSelectionStatus.Available => "قابل استفاده",
        SalesGrowthCommonPeriodSelectionStatus.Partial => "ناقص",
        _ => "در دسترس نیست"
    };

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
