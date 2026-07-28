using System.Security.Cryptography;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramAssistantResponseRenderer089Tests
{
    [Fact]
    public void Single_symbol_price_lookup_is_rendered_once_as_a_stock_card()
    {
        var response = CreatePriceResponse();

        var messages = CreateRenderer().Render(response, "fa-IR");
        var text = string.Join("\n", messages.Select(message => message.Text));

        Assert.Single(messages);
        Assert.Contains("شگل", text);
        Assert.Contains("گلتاش", text);
        Assert.Contains("آخرین قیمت: ۴٬۲۵۰", text);
        Assert.Contains("تغییر روزانه: \\+۲٫۹۱%", text);
        Assert.Contains("تاریخ معامله: ۱۴۰۵/۰۴/۲۹", text);
        Assert.Contains("منبع: معاملات روزانه", text);
        Assert.Contains("اطمینان پاسخ: ۹۵%", text);
        Assert.Contains("اعتبار: ۱ مصرف شد \\| ۹۹۸۵۶۹ باقی‌مانده", text);
        Assert.DoesNotContain("آخرین قیمت نماد شگل برابر است", text);
        Assert.DoesNotContain("LATEST_PRICE", text);
        Assert.DoesNotContain("DAILY_CHANGE_PCT", text);
        Assert.DoesNotContain("IntradayToday", text);
    }

    [Fact]
    public void Multi_symbol_lookup_preserves_rows_and_uses_localized_labels()
    {
        var columns = new[]
        {
            new ScannerTableColumn("SYMBOL", "SYMBOL", ScannerColumnType.Symbol),
            new ScannerTableColumn("PE_TTM", "PE_TTM", ScannerColumnType.Metric, "PE_TTM"),
            new ScannerTableColumn("MONTHLY_SALES", "MONTHLY_SALES", ScannerColumnType.Metric, "MONTHLY_SALES"),
            new ScannerTableColumn("MONTHLY_SALES_YTD", "MONTHLY_SALES_YTD", ScannerColumnType.Metric, "MONTHLY_SALES_YTD")
        };
        var rows = new[]
        {
            new ScannerTableRow("شگل", "گلتاش", new Dictionary<string, ScannerTableCell>
            {
                ["PE_TTM"] = new(5.4m, "5.4", CellFreshnessStatus.Persisted, null),
                ["MONTHLY_SALES"] = new(59407883m, "59,407,883", CellFreshnessStatus.Persisted, null),
                ["MONTHLY_SALES_YTD"] = new(152099615m, "152,099,615", CellFreshnessStatus.Persisted, null)
            }, 1, []),
            new ScannerTableRow("فملی", "ملی صنایع مس ایران", new Dictionary<string, ScannerTableCell>
            {
                ["PE_TTM"] = new(6.1m, "6.1", CellFreshnessStatus.Persisted, null),
                ["MONTHLY_SALES"] = new(545287525m, "545,287,525", CellFreshnessStatus.Persisted, null),
                ["MONTHLY_SALES_YTD"] = new(816689257m, "816,689,257", CellFreshnessStatus.Persisted, null)
            }, 1, [])
        };
        var table = new SymbolLookupTableResult(
            Guid.NewGuid(),
            columns,
            rows,
            new ScannerExecutionFacts(DateTimeOffset.UtcNow, TimeSpan.Zero, 2, 2, false),
            [],
            [],
            ["PE_TTM"]);
        var response = new AiQueryResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.SymbolLookup,
            ScannerPlan: null,
            ScannerTable: null,
            SymbolLookupTable: table,
            ExplainableAnswer: null,
            ConfidenceScore: null,
            TextAnswer: null,
            ClarificationRequired: false,
            ClarificationMessage: null,
            Usage: null);

        var text = string.Join("\n", CreateRenderer().Render(response, "fa-IR").Select(message => message.Text));

        Assert.Contains("شگل", text);
        Assert.Contains("فملی", text);
        Assert.Contains("P/E: ۵٫۴", text);
        Assert.Contains("P/E: ۶٫۱", text);
        Assert.Contains("فروش ماهانه", text);
        Assert.Contains("فروش تجمیعی از ابتدای سال", text);
        Assert.Contains("میلیون ریال", text);
        Assert.DoesNotContain("MONTHLY_SALES", text);
        Assert.DoesNotContain("MONTHLY_SALES_YTD", text);
        Assert.DoesNotContain("PE_TTM", text);
    }

    [Fact]
    public void Single_symbol_average_12_month_sales_uses_localized_telegram_label()
    {
        var response = CreateAverage12MonthSalesResponse();

        var text = string.Join("\n", CreateRenderer().Render(response, "fa-IR").Select(message => message.Text));

        Assert.Contains("سکرد", text);
        Assert.Contains("سیمان کردستان", text);
        Assert.Contains("متوسط فروش ۱۲ ماهه: ۱٬۴۲۱٬۳۶۳ میلیون ریال", text);
        Assert.Contains("منبع: نوآوران امین", text);
        Assert.Contains("تازگی: ذخیره‌شده", text);
        Assert.DoesNotContain("AVG_12M_MONTHLY_SALES", text);
        Assert.DoesNotContain("AVG\\_12M\\_MONTHLY\\_SALES", text);
    }

    [Fact]
    public void Monthly_trend_is_rendered_as_a_deterministic_png_with_a_concise_caption()
    {
        var response = CreateMonthlyTrendResponse();
        var renderer = CreateRenderer();

        var first = Assert.Single(renderer.Render(response, "fa-IR"));
        var second = Assert.Single(renderer.Render(response, "fa-IR"));

        Assert.Equal("MarkdownV2", first.ParseMode);
        Assert.Contains("۱٬۹۲۰٬۰۸۰", first.Text.Replace("\\\\", string.Empty));
        Assert.Contains("میلیون ریال", first.Text);
        Assert.DoesNotContain("میلیارد تومان", first.Text);
        Assert.Contains("روند فروش ماهانه", first.Text);
        Assert.Contains("۱٬۹۲۰٬۰۸۰", first.Text);
        Assert.Contains("\\+۵۱٫۱٪", first.Text);
        Assert.Contains("نوآوران امین", first.Text);
        Assert.Contains("۱۴۰۵/۰۴/۱۶", first.Text);
        Assert.DoesNotContain("NoavaranCurrentApi", first.Text);
        Assert.DoesNotContain("۲۰۲۶/۰۷/۰۷", first.Text);
        Assert.DoesNotContain("داده نمودار", first.Text);
        Assert.DoesNotContain("\\| ماه \\|", first.Text);
        Assert.True(first.Text.Length <= 1024);

        var media = Assert.IsType<TelegramAssistantMediaAttachment>(first.Media);
        Assert.Equal("photo", media.Kind);
        Assert.Equal("image/png", media.ContentType);
        Assert.Equal("monthly-trend-chart-v4", media.RenderVersion);
        var bytes = Convert.FromBase64String(media.ContentBase64);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.InRange(bytes.Length, 1, 5 * 1024 * 1024);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), media.Sha256);
        Assert.Equal(media.ContentBase64, second.Media?.ContentBase64);
        Assert.Equal(media.Sha256, second.Media?.Sha256);
    }

    [Theory]
    [InlineData("آخرین گزارش: ۱۴۰۵/۰۳ | واحد: میلیارد تومان", "۱۴۰۵/۰۳")]
    [InlineData("سال قبل ۱۴۰۴", "۱۴۰۴")]
    [InlineData("سال جاری ۱۴۰۵", "۱۴۰۵")]
    [InlineData("میانگین ۱۲ ماهه", "۱۲")]
    [InlineData("فروش ماهانه نسبت به ماه مشابه سال قبل +۲۰۵٫۷٪ رشد داشته است.", "+۲۰۵٫۷٪")]
    public void Monthly_chart_directional_layout_preserves_numeric_sequence_order(
        string value,
        string expectedNumericRun)
    {
        var numericRuns = TelegramMonthlyTrendChartRenderer.SplitDirectionalRuns(value)
            .Where(run => run.IsNumeric)
            .Select(run => run.Text)
            .ToArray();

        Assert.Contains(expectedNumericRun, numericRuns);
        Assert.DoesNotContain(new string(expectedNumericRun.Reverse().ToArray()), numericRuns);
    }

    [Theory]
    [InlineData("241.566", "۲۴۲")]
    [InlineData("603.111", "۶۰۳")]
    [InlineData("49.5", "۵۰")]
    [InlineData("49.49", "۴۹")]
    public void Monthly_chart_bar_values_are_rounded_without_decimals(string value, string expected)
    {
        Assert.Equal(expected, TelegramMonthlyTrendChartRenderer.FormatBarValue(decimal.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Monthly_trend_image_failure_returns_the_same_concise_text_profile_without_a_table()
    {
        var renderer = new TelegramAssistantResponseRenderer(
            new ThrowingMonthlyTrendChartRenderer(),
            NullLogger<TelegramAssistantResponseRenderer>.Instance);

        var message = Assert.Single(renderer.Render(CreateMonthlyTrendResponse(), "fa-IR"));

        Assert.Null(message.Media);
        Assert.Contains("روند فروش ماهانه", message.Text);
        Assert.DoesNotContain("داده نمودار", message.Text);
        Assert.DoesNotContain("\\| ماه \\|", message.Text);
    }

    [Fact]
    public void Disclosure_listing_is_rendered_as_compact_numbered_rows_with_pagination_metadata()
    {
        var item = new CompanyDisclosureFeedItem(
            "d-1", "l-1", CompanyDisclosureType.MonthlyProductionSales, "NoavaranCurrentApi", "559",
            null, "فولاژ", "فولاد آلیاژی ایران", "گزارش تولید و فروش ماهانه", new DateOnly(2026, 7, 20), null,
            new DateTimeOffset(2026, 7, 21, 10, 0, 0, TimeSpan.FromHours(3.5)), "source-1", 1, false,
            DisclosureCoverageStatus.Complete, "PersistedNormalizedData");
        var result = new DisclosureListingResult(
            [item],
            new DisclosureListingAppliedFilters([CompanyDisclosureType.MonthlyProductionSales], null, [], null, null, null, null, DisclosureConsolidationScope.NonConsolidated),
            1, 8, false, true, 9, 2, DateTimeOffset.UtcNow, DisclosureCoverageStatus.Complete, "PersistedNormalizedData");
        var response = new AiQueryResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.DisclosureListing,
            null, null, null, null, null, null, false, null, null, DisclosureListingResult: result);

        var message = Assert.Single(CreateRenderer().Render(response, "fa-IR"));

        Assert.Contains("فهرست اطلاعیه", message.Text.Replace("\\", string.Empty));
        Assert.Contains("فولاژ", message.Text);
        Assert.Contains("نتایج بیشتری", message.Text);
    }

    private static TelegramAssistantResponseRenderer CreateRenderer() =>
        new(
            new TelegramMonthlyTrendChartRenderer(),
            NullLogger<TelegramAssistantResponseRenderer>.Instance);

    private static AiQueryResponse CreateMonthlyTrendResponse()
    {
        var monthNames = new[]
        {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
        };
        var previous = new decimal[]
        {
            86.824m, 122.276m, 127.095m, 97.413m, 143.045m, 150.601m,
            157.167m, 137.605m, 105.646m, 49.93m, 94.237m, 149.318m
        };
        var current = new decimal?[]
        {
            166.21m, 262.457m, 192.008m, null, null, null,
            null, null, null, null, null, null
        };
        var points = Enumerable.Range(0, 12)
            .Select(index => new MonthlyActivityTrendChartPoint(
                index + 1,
                monthNames[index],
                1404,
                previous[index],
                1405,
                current[index],
                142.136m,
                current[index] is not null,
                true))
            .ToArray();
        var trend = new MonthlyActivityTrendResponse(
            "سکرد",
            "سیمان کردستان",
            1405,
            3,
            "میلیارد تومان",
            192.008m,
            127.095m,
            142.136m,
            51.1m,
            35.1m,
            620.675m,
            428.667m,
            points,
            [new MonthlyActivityTrendInsight(MonthlyActivityTrendInsightKind.YoYGrowth, "فروش ماهانه نسبت به ماه مشابه سال قبل ۵۱٫۱٪ رشد داشته است.")],
            [],
            "NoavaranCurrentApi",
            new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero));

        return new AiQueryResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.MonthlyActivityTrend,
            ScannerPlan: null,
            ScannerTable: null,
            SymbolLookupTable: null,
            ExplainableAnswer: null,
            ConfidenceScore: null,
            TextAnswer: "**داده نمودار ماهانه:**\n| ماه | فروش 1404 | فروش 1405 |",
            ClarificationRequired: false,
            ClarificationMessage: null,
            Usage: new UsageAccountingResult("AiQuery.MonthlyActivityTrend", "Completed", 1, 998565, "v1", false),
            MonthlyActivityTrendResult: trend);
    }

    private sealed class ThrowingMonthlyTrendChartRenderer : ITelegramMonthlyTrendChartRenderer
    {
        public TelegramAssistantMediaAttachment Render(MonthlyActivityTrendResponse trend) =>
            throw new InvalidOperationException("Synthetic chart renderer failure.");
    }

    private static AiQueryResponse CreatePriceResponse()
    {
        var columns = new[]
        {
            new ScannerTableColumn("SYMBOL", "SYMBOL", ScannerColumnType.Symbol),
            new ScannerTableColumn("COMPANY_NAME", "COMPANY_NAME", ScannerColumnType.CompanyName),
            new ScannerTableColumn("LATEST_PRICE", "LATEST_PRICE", ScannerColumnType.LatestPrice, "LATEST_PRICE"),
            new ScannerTableColumn("DAILY_CHANGE_PCT", "DAILY_CHANGE_PCT", ScannerColumnType.DailyChangePercent, "DAILY_CHANGE_PCT")
        };
        var row = new ScannerTableRow("شگل", "گلتاش", new Dictionary<string, ScannerTableCell>
        {
            ["LATEST_PRICE"] = new(
                4250m,
                "4,250",
                CellFreshnessStatus.Live,
                DateTimeOffset.UtcNow,
                new DateOnly(2026, 7, 20),
                "1405/04/29",
                "IntradayToday"),
            ["DAILY_CHANGE_PCT"] = new(2.91m, "+2.91%", CellFreshnessStatus.Live, DateTimeOffset.UtcNow)
        }, 1, []);
        var table = new SymbolLookupTableResult(
            Guid.NewGuid(),
            columns,
            [row],
            new ScannerExecutionFacts(DateTimeOffset.UtcNow, TimeSpan.Zero, 1, 1, false),
            [],
            [],
            ["LATEST_PRICE"]);

        return new AiQueryResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.SymbolLookup,
            ScannerPlan: null,
            ScannerTable: null,
            SymbolLookupTable: table,
            ExplainableAnswer: null,
            ConfidenceScore: new ConfidenceScoreResult(
                0.95,
                new ConfidenceFactors(1, 1, 1, 0),
                "test-v1"),
            TextAnswer: "آخرین قیمت نماد شگل برابر است با 4,250.",
            ClarificationRequired: false,
            ClarificationMessage: null,
            Usage: new UsageAccountingResult("AiQuery.StockAnalysis", "Completed", 1, 998569, "v1", false));
    }

    private static AiQueryResponse CreateAverage12MonthSalesResponse()
    {
        var columns = new[]
        {
            new ScannerTableColumn("SYMBOL", "SYMBOL", ScannerColumnType.Symbol),
            new ScannerTableColumn("COMPANY_NAME", "COMPANY_NAME", ScannerColumnType.CompanyName),
            new ScannerTableColumn(
                "AVG_12M_MONTHLY_SALES",
                "AVG_12M_MONTHLY_SALES",
                ScannerColumnType.Metric,
                "AVG_12M_MONTHLY_SALES")
        };
        var row = new ScannerTableRow("سکرد", "سیمان کردستان", new Dictionary<string, ScannerTableCell>
        {
            ["AVG_12M_MONTHLY_SALES"] = new(
                1_421_363m,
                "1,421,363",
                CellFreshnessStatus.Persisted,
                DateTimeOffset.UtcNow,
                SourceLabel: "NoavaranCurrentApi")
        }, 1, []);
        var table = new SymbolLookupTableResult(
            Guid.NewGuid(),
            columns,
            [row],
            new ScannerExecutionFacts(DateTimeOffset.UtcNow, TimeSpan.Zero, 1, 1, false),
            [],
            [],
            ["AVG_12M_MONTHLY_SALES"]);

        return new AiQueryResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.SymbolLookup,
            ScannerPlan: null,
            ScannerTable: null,
            SymbolLookupTable: table,
            ExplainableAnswer: null,
            ConfidenceScore: new ConfidenceScoreResult(
                0.92,
                new ConfidenceFactors(1, 1, 1, 0),
                "test-v1"),
            TextAnswer: "متوسط فروش ۱۲ ماهه نماد سکرد برابر با 1,421,363 میلیون ریال است.",
            ClarificationRequired: false,
            ClarificationMessage: null,
            Usage: new UsageAccountingResult("AiQuery.SymbolLookup", "Completed", 1, 998499, "v1", false));
    }
}
