using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Telegram;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramMonthlyTrendChartRenderer : ITelegramMonthlyTrendChartRenderer
{
    internal const string ChartRenderVersion = "monthly-trend-chart-v12-browser-text";
    internal const string ProductRevenueMixRenderVersion = "product-revenue-mix-table-v1";
    internal const int Width = 1800;
    private const int Padding = 90;
    private const int PlotTop = 230;
    private const int PlotHeight = 680;
    private const int MaximumPhotoBytes = 5 * 1024 * 1024;
    private const string RegularFontResource = "FinancialCopilot.Assets.Samim.ttf";
    private const string BoldFontResource = "FinancialCopilot.Assets.Samim-Bold.ttf";

    private static readonly SKColor Background = SKColor.Parse("#090D10");
    private static readonly SKColor Surface = SKColor.Parse("#0D1115");
    private static readonly SKColor Border = SKColor.Parse("#252B31");
    private static readonly SKColor Grid = SKColor.Parse("#20262C");
    private static readonly SKColor Foreground = SKColor.Parse("#F3F4F6");
    private static readonly SKColor Muted = SKColor.Parse("#9CA3AF");
    private static readonly SKColor PreviousYear = SKColor.Parse("#6366F1");
    private static readonly SKColor CurrentYear = SKColor.Parse("#10B981");
    private static readonly SKColor Average = SKColor.Parse("#F59E0B");

    // Keep the monthly trend image aligned with the light web export palette.
    // The dark palette above remains in use by the other Telegram image profiles.
    private static readonly SKColor TrendCurrentYear = SKColor.Parse("#047857");
    private static readonly SKColor TrendPreviousYear = SKColor.Parse("#4338CA");
    private static readonly SKColor TrendAverage = SKColor.Parse("#B45309");
    private static readonly SKColor TrendForeground = SKColor.Parse("#18181B");
    private static readonly SKColor TrendMuted = SKColor.Parse("#3F3F46");
    private static readonly SKColor TrendGrid = new(63, 63, 70, 71);
    private static readonly SKColor TrendSurface = SKColor.Parse("#FAFAFA");
    private static readonly SKColor TrendWatermark = new(39, 39, 42, 46);

    public TelegramAssistantMediaAttachment Render(MonthlyActivityTrendResponse trend)
    {
        ArgumentNullException.ThrowIfNull(trend);
        if (trend.ChartPoints.Count == 0)
        {
            throw new InvalidOperationException("Monthly trend chart points are required for Telegram image rendering.");
        }

        using var regularTypeface = LoadTypeface(RegularFontResource);
        using var boldTypeface = LoadTypeface(BoldFontResource);
        var height = CalculateExportHeight(trend);
        using var surface = SKSurface.Create(new SKImageInfo(Width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Unable to allocate the Telegram monthly trend image surface.");

        Draw(surface.Canvas, trend, regularTypeface, boldTypeface, height);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 92)
            ?? throw new InvalidOperationException("Unable to encode the Telegram monthly trend image as PNG.");
        var points = trend.ChartPoints.OrderBy(point => point.FiscalMonthIndex).Take(12).ToArray();
        var company = string.IsNullOrWhiteSpace(trend.CompanyName)
            ? trend.CompanySymbol : $"{trend.CompanyName} ({trend.CompanySymbol})";
        var lines = BuildExportExplanationLines(trend);
        if (lines.Count == 0)
            lines = [new ExportExplanationLine("دادهٔ گم‌شده‌ای گزارش نشده است.", null, "", ExportExplanationTone.Neutral)];
        var content = new MonthlyTrendBrowserText.Content(
            $"روند فروش ماهانه — {company}", $"واحد: {trend.UnitLabelFa}",
            BuildExportLegend(points),
            lines.Select(line => new MonthlyTrendBrowserText.Explanation(
                line.BeforeValue, line.ValueLabel, line.AfterValue,
                line.Tone == ExportExplanationTone.Positive ? "#047857" :
                line.Tone == ExportExplanationTone.Negative ? "#BE123C" : "#3F3F46")).ToArray());
        var bytes = MonthlyTrendBrowserText.Render(encoded.ToArray(), Width, height, content);
        if (bytes.Length == 0 || bytes.Length > MaximumPhotoBytes)
        {
            throw new InvalidOperationException($"Telegram monthly trend PNG size {bytes.Length} is outside the allowed range.");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new TelegramAssistantMediaAttachment(
            "photo",
            "image/png",
            $"monthly-trend-{SanitizeFileName(trend.CompanySymbol)}.png",
            Convert.ToBase64String(bytes),
            hash,
            ChartRenderVersion);
    }

    public TelegramAssistantMediaAttachment Render(ProductRevenueMixResponse result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Products.Count == 0)
            throw new InvalidOperationException("Product revenue mix rows are required for Telegram image rendering.");

        using var regularTypeface = LoadTypeface(RegularFontResource);
        using var boldTypeface = LoadTypeface(BoldFontResource);
        var height = 220 + Math.Min(result.Products.Count, 20) * 68;
        using var surface = SKSurface.Create(new SKImageInfo(Width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Unable to allocate the Telegram product mix image surface.");

        DrawProductRevenueMix(surface.Canvas, result, regularTypeface, boldTypeface, height);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 92)
            ?? throw new InvalidOperationException("Unable to encode the Telegram product mix image as PNG.");
        var bytes = encoded.ToArray();
        if (bytes.Length == 0 || bytes.Length > MaximumPhotoBytes)
            throw new InvalidOperationException($"Telegram product mix PNG size {bytes.Length} is outside the allowed range.");

        return new TelegramAssistantMediaAttachment(
            "photo", "image/png", $"product-revenue-mix-{SanitizeFileName(result.CompanySymbol)}.png",
            Convert.ToBase64String(bytes), Convert.ToHexStringLower(SHA256.HashData(bytes)), ProductRevenueMixRenderVersion);
    }

    public TelegramAssistantMediaAttachment RenderIndustryComparison(string markdown)
    {
        var lines = markdown.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        var tableStart = Array.FindIndex(lines, line => line.StartsWith('|'));
        if (tableStart < 0 || tableStart + 2 >= lines.Length)
            throw new InvalidOperationException("Industry comparison table is missing.");

        var headers = SplitTableLine(lines[tableStart]);
        var rows = lines.Skip(tableStart + 2).Select(SplitTableLine)
            .Where(cells => cells.Length == headers.Length)
            .ToArray();
        if (headers.Length != 4 || rows.Length == 0)
            throw new InvalidOperationException("Industry comparison table shape is invalid.");

        var group = lines.FirstOrDefault(line => line.StartsWith("**گروه صنعتی:**", StringComparison.Ordinal))
            ?.Replace("**گروه صنعتی:**", "", StringComparison.Ordinal).Trim() ?? "مقایسه نماد با صنعت";
        var size = lines.FirstOrDefault(line => line.StartsWith("**اندازه گروه:**", StringComparison.Ordinal))
            ?.Replace("**اندازه گروه:**", "", StringComparison.Ordinal).Trim() ?? "—";

        using var regularTypeface = LoadTypeface(RegularFontResource);
        using var boldTypeface = LoadTypeface(BoldFontResource);
        var height = 250 + rows.Length * 58;
        using var surface = SKSurface.Create(new SKImageInfo(Width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Unable to allocate the industry comparison image surface.");
        DrawIndustryComparison(surface.Canvas, group, size, headers, rows, regularTypeface, boldTypeface, height);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 94)
            ?? throw new InvalidOperationException("Unable to encode the industry comparison image as PNG.");
        var bytes = encoded.ToArray();
        if (bytes.Length == 0 || bytes.Length > MaximumPhotoBytes)
            throw new InvalidOperationException($"Telegram industry comparison PNG size {bytes.Length} is outside the allowed range.");

        return new TelegramAssistantMediaAttachment(
            "photo", "image/png", "industry-comparison.png", Convert.ToBase64String(bytes),
            Convert.ToHexStringLower(SHA256.HashData(bytes)), "industry-comparison-table-v1");
    }

    private static void DrawIndustryComparison(SKCanvas canvas, string group, string size, string[] headers,
        string[][] rows, SKTypeface regularTypeface, SKTypeface boldTypeface, int height)
    {
        var background = SKColor.Parse("#F7F8FA");
        var surface = SKColor.Parse("#FFFFFF");
        var header = SKColor.Parse("#EEF1F5");
        var border = SKColor.Parse("#D6DCE5");
        var foreground = SKColor.Parse("#26364A");
        var muted = SKColor.Parse("#5E6877");
        var positiveCell = SKColor.Parse("#CFF7E7");
        var negativeCell = SKColor.Parse("#FFE0E4");
        var averageRow = SKColor.Parse("#E8EEF7");

        canvas.Clear(background);
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = border, StrokeWidth = 1 };
        using var text = new SKPaint { IsAntialias = true, Color = foreground };
        using var regularShaper = new SKShaper(regularTypeface);
        using var boldShaper = new SKShaper(boldTypeface);
        using var regular20 = new SKFont(regularTypeface, 20);
        using var bold24 = new SKFont(boldTypeface, 24);
        using var bold20 = new SKFont(boldTypeface, 20);

        fill.Color = surface;
        canvas.DrawRoundRect(new SKRect(24, 20, Width - 24, height - 20), 26, 26, fill);
        canvas.DrawRoundRect(new SKRect(24, 20, Width - 24, height - 20), 26, 26, stroke);
        DrawRtlTextWithNumbers(canvas, boldShaper, bold24, text, "مقایسه نماد با صنعت", Width - 58, 62);
        text.Color = Muted;
        DrawRtlTextWithNumbers(canvas, regularShaper, regular20, text, $"گروه: {group}", Width - 58, 98);
        DrawRtlTextWithNumbers(canvas, regularShaper, regular20, text, $"اندازه گروه: {size}", Width - 58, 130);

        var headerY = 182;
        fill.Color = header;
        canvas.DrawRect(48, headerY - 30, Width - 48, headerY + 18, fill);
        text.Color = foreground;
        var x = new[] { 1160f, 850f, 600f, 300f };
        for (var index = 0; index < headers.Length; index++)
        {
            if (index is 1 or 2)
                canvas.DrawText(headers[index], x[index], headerY, SKTextAlign.Center, bold20, text);
            else
                DrawRtlTextWithNumbers(canvas, boldShaper, bold20, text, headers[index], x[index], headerY);
        }

        var rowY = 232;
        foreach (var row in rows)
        {
            if (row[0].Equals("میانگین صنعت", StringComparison.Ordinal))
                fill.Color = SKColor.Parse("#20352E");
            else
                fill.Color = rowY / 58 % 2 == 0 ? SKColor.Parse("#111A20") : Surface;
            var isAverage = ReferenceEquals(row, rows[^1]);
            fill.Color = isAverage
                ? averageRow
                : rowY / 58 % 2 == 0 ? SKColor.Parse("#F8FAFC") : surface;
            canvas.DrawRect(48, rowY - 29, Width - 48, rowY + 25, fill);
            text.Color = foreground;
            for (var index = 0; index < row.Length; index++)
            {
                if (!isAverage && index > 0 &&
                    CompareIndustryValues(row[index], rows[^1].ElementAtOrDefault(index)) != 0)
                {
                    fill.Color = CompareIndustryValues(row[index], rows[^1].ElementAtOrDefault(index)) < 0
                        ? positiveCell
                        : negativeCell;
                    var left = index switch
                    {
                        1 => 728f,
                        2 => 478f,
                        3 => 48f,
                        _ => 0f
                    };
                    var right = index switch
                    {
                        1 => 972f,
                        2 => 722f,
                        3 => 422f,
                        _ => 0f
                    };
                    canvas.DrawRect(left, rowY - 29, right, rowY + 25, fill);
                }
                if (index == 0)
                    DrawRtlTextWithNumbers(canvas, regularShaper, regular20, text, row[index], x[index], rowY);
                else
                    DrawNumericText(canvas, row[index], x[index], rowY, SKTextAlign.Center, regular20, text);
            }
            rowY += 58;
        }
    }

    private static int CompareIndustryValues(string value, string? benchmark)
    {
        if (!TryParsePercent(value, out var current) || !TryParsePercent(benchmark, out var average))
            return 0;
        return current.CompareTo(average);
    }

    private static bool TryParsePercent(string? value, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().Trim('*').TrimEnd('٪', '%')
            .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
            .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
            .Replace('٫', '.').Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static string[] SplitTableLine(string line) =>
        line.Trim().Trim('|').Split('|', StringSplitOptions.None)
            .Select(cell => cell.Trim().Replace("**", string.Empty, StringComparison.Ordinal)).ToArray();

    private static void DrawProductRevenueMix(
        SKCanvas canvas,
        ProductRevenueMixResponse result,
        SKTypeface regularTypeface,
        SKTypeface boldTypeface,
        int height)
    {
        canvas.Clear(Background);
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = Border, StrokeWidth = 2 };
        using var text = new SKPaint { IsAntialias = true, Color = Foreground };
        using var regular18 = new SKFont(regularTypeface, 18);
        using var regular22 = new SKFont(regularTypeface, 22);
        using var bold28 = new SKFont(boldTypeface, 28);
        using var bold20 = new SKFont(boldTypeface, 20);
        using var regularShaper = new SKShaper(regularTypeface);
        using var boldShaper = new SKShaper(boldTypeface);

        fill.Color = Surface;
        canvas.DrawRoundRect(new SKRect(24, 20, Width - 24, height - 20), 26, 26, fill);
        canvas.DrawRoundRect(new SKRect(24, 20, Width - 24, height - 20), 26, 26, stroke);

        var company = string.IsNullOrWhiteSpace(result.CompanyName)
            ? result.CompanySymbol
            : $"{result.CompanyName} \u2066({result.CompanySymbol})\u2069";
        DrawRtlTextWithNumbers(canvas, boldShaper, bold28, text,
            $"ترکیب درآمد محصولات — {company}", Width - 58, 64);
        text.Color = Muted;
        DrawRtlTextWithNumbers(canvas, regularShaper, regular22, text,
            $"دوره: {ToPersianDigits($"{result.ReportYear}/{result.ReportMonth:00}")}", Width - 58, 100);

        var headerY = 142;
        fill.Color = SKColor.Parse("#182229");
        canvas.DrawRect(48, headerY - 31, Width - 48, headerY + 18, fill);
        text.Color = Foreground;
        DrawRtlTextWithNumbers(canvas, boldShaper, bold20, text, "غالب", 1160, headerY);
        DrawRtlTextWithNumbers(canvas, boldShaper, bold20, text, "سهم (٪)", 930, headerY);
        DrawRtlTextWithNumbers(canvas, boldShaper, bold20, text, "فروش (تومان)", 700, headerY);
        DrawRtlTextWithNumbers(canvas, boldShaper, bold20, text, "محصول", 390, headerY);
        DrawRtlTextWithNumbers(canvas, boldShaper, bold20, text, "ردیف", 100, headerY);

        var rowY = 190;
        foreach (var product in result.Products.Take(20))
        {
            if (product.Rank % 2 == 0)
            {
                fill.Color = SKColor.Parse("#111A20");
                canvas.DrawRect(48, rowY - 29, Width - 48, rowY + 27, fill);
            }

            text.Color = product.IsDominantProduct ? CurrentYear : Foreground;
            DrawRtlTextWithNumbers(canvas, regularShaper, regular18, text,
                product.IsDominantProduct ? "✓" : "", 1160, rowY);
            text.Color = Foreground;
            DrawRtlTextWithNumbers(canvas, regularShaper, regular18, text,
                $"{ToPersianDigits(product.RevenueSharePercentage.ToString("0.0", CultureInfo.InvariantCulture))}٪", 930, rowY);
            DrawRtlTextWithNumbers(canvas, regularShaper, regular18, text,
                ToPersianDigits((product.SalesAmount * 100_000m).ToString("N0", CultureInfo.InvariantCulture)), 700, rowY);
            DrawRtlTextWithNumbers(canvas, regularShaper, regular18, text, product.ProductName, 390, rowY);
            DrawRtlTextWithNumbers(canvas, regularShaper, regular18, text,
                ToPersianDigits(product.Rank.ToString(CultureInfo.InvariantCulture)), 100, rowY);
            rowY += 68;
        }
    }

    private static void Draw(
        SKCanvas canvas,
        MonthlyActivityTrendResponse trend,
        SKTypeface regularTypeface,
        SKTypeface boldTypeface,
        int height)
    {
        canvas.Clear(TrendSurface);
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var text = new SKPaint { IsAntialias = true, Color = TrendForeground };
        using var regular20 = new SKFont(regularTypeface, 20);
        using var regular22 = new SKFont(regularTypeface, 22);
        using var regular24 = new SKFont(regularTypeface, 24);
        using var bold24 = new SKFont(boldTypeface, 24);
        using var bold34 = new SKFont(boldTypeface, 34);
        using var regularShaper = new SKShaper(regularTypeface);
        using var boldShaper = new SKShaper(boldTypeface);

        var points = trend.ChartPoints.OrderBy(point => point.FiscalMonthIndex).Take(12).ToArray();
        var values = points.SelectMany(point => new[]
            {
                point.CurrentFiscalYearSalesAmount,
                point.PreviousFiscalYearSalesAmount,
                point.Average12MonthSalesAmount
            })
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
        var maximum = Math.Max(values.DefaultIfEmpty(1m).Max(), 1m) * 1.12m;
        const float plotLeft = Padding + 80;
        const float plotRight = Width - Padding;
        const float plotBottom = PlotTop + PlotHeight;
        var slotWidth = (plotRight - plotLeft) / Math.Max(points.Length, 1);
        var barWidth = Math.Min(30f, slotWidth * 0.25f);

        stroke.Color = TrendGrid;
        stroke.StrokeWidth = 2;
        stroke.PathEffect = null;
        for (var tick = 0; tick <= 4; tick++)
        {
            var y = PlotTop + PlotHeight * tick / 4f;
            canvas.DrawLine(plotLeft, y, plotRight, y, stroke);
        }

        using var averagePath = new SKPath();
        var averageStarted = false;

        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            var centerX = plotLeft + slotWidth * index + slotWidth / 2;

            if (point.IsPreviousYearReported && point.PreviousFiscalYearSalesAmount is not null)
            {
                var previousValue = point.PreviousFiscalYearSalesAmount.Value;
                var previousHeight = (float)(previousValue / maximum) * PlotHeight;
                var previousX = centerX - barWidth - 5;
                fill.Color = TrendPreviousYear;
                canvas.DrawRect(previousX, plotBottom - previousHeight, barWidth, previousHeight, fill);
                DrawExportBarValue(canvas, regular22, text, previousValue,
                    previousX + barWidth / 2, plotBottom - previousHeight);
            }

            if (point.IsCurrentYearReported && point.CurrentFiscalYearSalesAmount is not null)
            {
                var currentValue = point.CurrentFiscalYearSalesAmount.Value;
                var currentHeight = (float)(currentValue / maximum) * PlotHeight;
                var currentX = centerX + 5;
                fill.Color = TrendCurrentYear;
                canvas.DrawRect(currentX, plotBottom - currentHeight, barWidth, currentHeight, fill);
                DrawExportBarValue(canvas, regular22, text, currentValue,
                    currentX + barWidth / 2, plotBottom - currentHeight);
            }

            if (point.Average12MonthSalesAmount is not null)
            {
                var averageY = plotBottom - (float)(point.Average12MonthSalesAmount.Value / maximum) * PlotHeight;
                if (averageStarted)
                {
                    averagePath.LineTo(centerX, averageY);
                }
                else
                {
                    averagePath.MoveTo(centerX, averageY);
                    averageStarted = true;
                }
            }
            else
            {
                averageStarted = false;
            }

            text.Color = TrendForeground;
            canvas.DrawShapedText(regularShaper, point.FiscalMonthNameFa, centerX, plotBottom + 38,
                SKTextAlign.Center, regular22, text);
        }

        stroke.Color = TrendAverage;
        stroke.StrokeWidth = 4;
        stroke.PathEffect = null;
        canvas.DrawPath(averagePath, stroke);

        text.Color = TrendWatermark;
        DrawLeftAlignedRtlText(canvas, regularShaper, regular22, text,
            "ساپیو - دستیار هوشمند بازار", Padding, plotBottom + 148);
    }

    private static int CalculateExportHeight(MonthlyActivityTrendResponse trend)
    {
        var explanationCount = BuildExportExplanationLines(trend).Count;
        var explanationHeight = Math.Max(140, explanationCount * 44 + 90);
        return PlotTop + PlotHeight + explanationHeight + Padding;
    }

    internal static MonthlyTrendBrowserText.Label[] BuildExportLegend(
        IReadOnlyList<MonthlyActivityTrendChartPoint> points)
    {
        var previousYear = points.FirstOrDefault(point => point.PreviousFiscalYear is not null)?.PreviousFiscalYear;
        var currentYear = points.FirstOrDefault(point => point.CurrentFiscalYear is not null)?.CurrentFiscalYear;
        var previousTotal = SumReportedSales(points, useCurrentYear: false);
        var currentTotal = SumReportedSales(points, useCurrentYear: true);
        var currentLabel = FormatExportYearLegend(currentYear, currentTotal, "سال جاری");
        if (currentTotal is not null && previousTotal is not null && previousTotal != 0)
        {
            var percentage = ToPersianDigits(
                ((currentTotal.Value / previousTotal.Value) * 100m).ToString("0.00", CultureInfo.InvariantCulture));
            // Formatting only: the browser receives this logical string unchanged.
            currentLabel += $" ({percentage}٪ از {ToPersianDigits((previousYear ?? 0).ToString(CultureInfo.InvariantCulture))})";
        }

        var average = points.FirstOrDefault(point => point.Average12MonthSalesAmount is not null)
            ?.Average12MonthSalesAmount;
        return [
            new(currentLabel, "#047857"),
            new(FormatExportYearLegend(previousYear, previousTotal, "سال قبل"), "#4338CA"),
            new($"میانگین ۱۲ ماهه{FormatExportAmountSuffix(average)}", "#B45309")
        ];
    }

    private static string FormatExportYearLegend(int? year, decimal? total, string fallback)
    {
        var label = year is not null
            ? ToPersianDigits(year.Value.ToString(CultureInfo.InvariantCulture))
            : fallback;
        return total is null ? label : $"{label}: {FormatExportAmount(total.Value)}";
    }

    private static string FormatExportAmountSuffix(decimal? amount) =>
        amount is null ? string.Empty : $": {FormatExportAmount(amount.Value)}";

    private static string FormatExportAmount(decimal amount) =>
        ToPersianDigits(decimal.Round(amount, 0, MidpointRounding.AwayFromZero)
            .ToString("N0", CultureInfo.InvariantCulture));

    private static decimal? SumReportedSales(
        IEnumerable<MonthlyActivityTrendChartPoint> points,
        bool useCurrentYear)
    {
        var values = points
            .Select(point => useCurrentYear
                ? point.IsCurrentYearReported ? point.CurrentFiscalYearSalesAmount : null
                : point.IsPreviousYearReported ? point.PreviousFiscalYearSalesAmount : null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
        return values.Length == 0 ? null : values.Sum();
    }

    private static IReadOnlyList<ExportExplanationLine> BuildExportExplanationLines(
        MonthlyActivityTrendResponse trend)
    {
        var lines = trend.Insights
            .Select(insight => FormatExportInsight(trend, insight))
            .ToList();
        lines.AddRange(trend.MissingDataPoints.Select(point => new ExportExplanationLine(
            $"⚠ {point.ReasonFa}", null, string.Empty, ExportExplanationTone.Neutral)));
        return lines;
    }

    private static ExportExplanationLine FormatExportInsight(
        MonthlyActivityTrendResponse trend,
        MonthlyActivityTrendInsight insight)
    {
        // Convert digits in prose without converting the sentence-ending full stop
        // into the Arabic decimal separator used by numeric labels.
        var text = ToPersianDigitsOnly(insight.TextFa);
        var match = Regex.Match(text, @"[+-]?\s*[0-9۰-۹٠-٩]+(?:[.,٫][0-9۰-۹٠-٩]+)?\s*[%٪]");
        var percentage = insight.Kind switch
        {
            MonthlyActivityTrendInsightKind.YoYGrowth => trend.SalesAmountYoYGrowthPercent,
            MonthlyActivityTrendInsightKind.VsAverage12Month => trend.SalesVsAverage12MonthPercent,
            _ => match.Success ? ParsePersianPercentage(match.Value) : null
        };
        if (!match.Success || percentage is null)
        {
            return new ExportExplanationLine(text, null, string.Empty, ExportExplanationTone.Neutral);
        }

        var valueLabel = FormatSignedPercentage(percentage.GetValueOrDefault());
        var tone = percentage > 0
            ? ExportExplanationTone.Positive
            : percentage < 0 ? ExportExplanationTone.Negative : ExportExplanationTone.Neutral;
        return new ExportExplanationLine(
            text[..match.Index],
            valueLabel,
            text[(match.Index + match.Length)..],
            tone);
    }

    private static decimal? ParsePersianPercentage(string value)
    {
        var normalized = value
            .Replace("۰", "0", StringComparison.Ordinal)
            .Replace("۱", "1", StringComparison.Ordinal)
            .Replace("۲", "2", StringComparison.Ordinal)
            .Replace("۳", "3", StringComparison.Ordinal)
            .Replace("۴", "4", StringComparison.Ordinal)
            .Replace("۵", "5", StringComparison.Ordinal)
            .Replace("۶", "6", StringComparison.Ordinal)
            .Replace("۷", "7", StringComparison.Ordinal)
            .Replace("۸", "8", StringComparison.Ordinal)
            .Replace("۹", "9", StringComparison.Ordinal)
            .Replace("٠", "0", StringComparison.Ordinal)
            .Replace("١", "1", StringComparison.Ordinal)
            .Replace("٢", "2", StringComparison.Ordinal)
            .Replace("٣", "3", StringComparison.Ordinal)
            .Replace("٤", "4", StringComparison.Ordinal)
            .Replace("٥", "5", StringComparison.Ordinal)
            .Replace("٦", "6", StringComparison.Ordinal)
            .Replace("٧", "7", StringComparison.Ordinal)
            .Replace("٨", "8", StringComparison.Ordinal)
            .Replace("٩", "9", StringComparison.Ordinal)
            .Replace('٫', '.')
            .Replace(',', '.')
            .Replace("٪", string.Empty, StringComparison.Ordinal)
            .Replace("%", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatSignedPercentage(decimal value)
    {
        var absolute = ToPersianDigits(Math.Abs(value).ToString("0.0", CultureInfo.InvariantCulture));
        return value > 0 ? $"+{absolute}٪" : value < 0 ? $"({absolute}٪)" : $"{absolute}٪";
    }

    private static void DrawExportBarValue(
        SKCanvas canvas,
        SKFont font,
        SKPaint text,
        decimal value,
        float centerX,
        float top)
    {
        text.Color = TrendForeground;
        DrawNumericText(canvas, FormatExportAmount(value), centerX, top - 14,
            SKTextAlign.Center, font, text);
    }

    private static void DrawExportExplanationLine(
        SKCanvas canvas,
        ExportExplanationLine line,
        float right,
        float baseline,
        SKFont font,
        SKShaper shaper,
        SKPaint text)
    {
        text.Color = TrendMuted;
        var cursor = right - DrawRtlTextWithNumbers(canvas, shaper, font, text, line.BeforeValue, right, baseline);
        if (line.ValueLabel is not null)
        {
            text.Color = line.Tone switch
            {
                ExportExplanationTone.Positive => TrendCurrentYear,
                ExportExplanationTone.Negative => SKColor.Parse("#BE123C"),
                _ => TrendMuted
            };
            var width = font.MeasureText(line.ValueLabel, text);
            DrawNumericText(canvas, line.ValueLabel, cursor, baseline, SKTextAlign.Right, font, text);
            cursor -= width;
        }

        text.Color = TrendMuted;
        DrawRtlTextWithNumbers(canvas, shaper, font, text, line.AfterValue, cursor, baseline);
    }

    private static void DrawLeftAlignedRtlText(
        SKCanvas canvas,
        SKShaper shaper,
        SKFont font,
        SKPaint paint,
        string value,
        float left,
        float baseline) =>
        canvas.DrawShapedText(shaper, value, left, baseline, SKTextAlign.Left, font, paint);

    private enum ExportExplanationTone
    {
        Neutral,
        Positive,
        Negative
    }

    private sealed record ExportExplanationLine(
        string BeforeValue,
        string? ValueLabel,
        string AfterValue,
        ExportExplanationTone Tone);

    private static void DrawGrid(
        SKCanvas canvas,
        SKShaper shaper,
        SKFont font,
        SKPaint text,
        SKPaint stroke,
        float left,
        float right,
        float top,
        float bottom,
        decimal yMaximum)
    {
        stroke.Color = Grid;
        stroke.StrokeWidth = 1;
        stroke.PathEffect = SKPathEffect.CreateDash([4, 5], 0);
        for (var tick = 0; tick <= 4; tick++)
        {
            var ratio = tick / 4f;
            var y = bottom - (bottom - top) * ratio;
            canvas.DrawLine(left, y, right, y, stroke);
            text.Color = Muted;
            var value = yMaximum * tick / 4m;
            DrawNumericText(canvas, FormatAxis(value), left - 16, y + 6,
                SKTextAlign.Right, font, text);
        }

        stroke.PathEffect = null;
    }

    private static void DrawBar(
        SKCanvas canvas,
        SKPaint paint,
        float x,
        float bottom,
        float width,
        float top,
        SKColor color)
    {
        paint.Color = color;
        canvas.DrawRoundRect(new SKRect(x, top, x + width, bottom), 5, 5, paint);
    }

    private static void DrawBarValue(
        SKCanvas canvas,
        SKFont font,
        SKPaint text,
        decimal value,
        float centerX,
        float barTop,
        float plotTop)
    {
        text.Color = Foreground;
        var baseline = Math.Max(plotTop + font.Size, barTop - 6);
        DrawNumericText(canvas, FormatBarValue(value), centerX, baseline,
            SKTextAlign.Center, font, text);
    }

    private static void DrawLegend(
        SKCanvas canvas,
        MonthlyActivityTrendResponse trend,
        IReadOnlyList<MonthlyActivityTrendChartPoint> points,
        SKShaper shaper,
        SKFont font,
        SKPaint text,
        SKPaint fill)
    {
        var previousYear = points.FirstOrDefault(point => point.PreviousFiscalYear is not null)?.PreviousFiscalYear;
        var currentYear = points.FirstOrDefault(point => point.CurrentFiscalYear is not null)?.CurrentFiscalYear;
        var items = new[]
        {
            ($"سال قبل {ToPersianDigits(previousYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}", PreviousYear),
            ($"سال جاری {ToPersianDigits(currentYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}", CurrentYear),
            ("میانگین ۱۲ ماهه", Average)
        };

        var x = Width - 58f;
        foreach (var item in items)
        {
            text.Color = Muted;
            var labelWidth = DrawRtlTextWithNumbers(canvas, shaper, font, text, item.Item1, x, 590);
            x -= labelWidth + 12;
            fill.Color = item.Item2;
            canvas.DrawRoundRect(new SKRect(x - 24, 574, x, 592), 4, 4, fill);
            x -= 54;
        }
    }

    private static SKTypeface LoadTypeface(string resourceName)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                using var data = SKData.Create(stream);
                var embedded = SKTypeface.FromData(data);
                if (embedded is not null) return embedded;
            }
        }
        catch (Exception)
        {
            // A system fallback below keeps chart delivery working when a container
            // was built without the optional embedded font resource.
        }

        return SKTypeface.FromFamilyName("sans-serif") ?? SKTypeface.Default;
    }

    private static decimal FindMaximum(IEnumerable<MonthlyActivityTrendChartPoint> points) =>
        points.SelectMany(point => new[]
            {
                point.PreviousFiscalYearSalesAmount,
                point.CurrentFiscalYearSalesAmount,
                point.Average12MonthSalesAmount
            })
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .DefaultIfEmpty(0m)
            .Max();

    private static decimal NiceMaximum(decimal value)
    {
        var magnitude = (decimal)Math.Pow(10, Math.Max(0, Math.Floor(Math.Log10((double)value)) - 1));
        return Math.Ceiling(value / magnitude) * magnitude;
    }

    private static float ScaleY(decimal value, decimal maximum, float top, float bottom) =>
        bottom - (float)(value / maximum) * (bottom - top);

    private static string FormatAxis(decimal value) =>
        ToPersianDigits(value.ToString(value >= 1000 ? "0,.#K" : "0.#", CultureInfo.InvariantCulture));

    internal static string FormatBarValue(decimal value) =>
        ToPersianDigits(decimal.Round(value, 0, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture));

    private static float DrawRtlTextWithNumbers(
        SKCanvas canvas,
        SKShaper shaper,
        SKFont font,
        SKPaint paint,
        string value,
        float right,
        float baseline)
    {
        // Shape the complete logical string with HarfBuzz so Persian glyphs join
        // correctly. Do not inject directional controls, split runs, or reconstruct
        // visual order; the legend and all other labels use the same raw string.
        var width = shaper.Shape(value, font).Width;
        canvas.DrawShapedText(shaper, value, right, baseline,
            SKTextAlign.Right, font, paint);
        return width;
    }

    private static void DrawNumericText(
        SKCanvas canvas,
        string value,
        float x,
        float baseline,
        SKTextAlign align,
        SKFont font,
        SKPaint paint) =>
        canvas.DrawText(value, x, baseline, align, font, paint);

    private static string ToPersianDigits(string value) =>
        value
            .Replace(',', '٬')
            .Replace('.', '٫')
            .Replace('0', '۰').Replace('1', '۱').Replace('2', '۲').Replace('3', '۳').Replace('4', '۴')
            .Replace('5', '۵').Replace('6', '۶').Replace('7', '۷').Replace('8', '۸').Replace('9', '۹');

    private static string ToPersianDigitsOnly(string value) =>
        string.Concat(value.Select(character => character is >= '0' and <= '9'
            ? (char)('\u06F0' + character - '0')
            : character));

    private static string SanitizeFileName(string value)
    {
        var safe = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "symbol" : safe;
    }

    private static string Bounded(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 1)] + "…";
}
