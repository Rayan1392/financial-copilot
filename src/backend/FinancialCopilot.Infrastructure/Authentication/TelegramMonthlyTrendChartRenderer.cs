using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Telegram;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramMonthlyTrendChartRenderer : ITelegramMonthlyTrendChartRenderer
{
    internal const string ChartRenderVersion = "monthly-trend-chart-v4";
    internal const string ProductRevenueMixRenderVersion = "product-revenue-mix-table-v1";
    internal const int Width = 1280;
    internal const int Height = 720;
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

    public TelegramAssistantMediaAttachment Render(MonthlyActivityTrendResponse trend)
    {
        ArgumentNullException.ThrowIfNull(trend);
        if (trend.ChartPoints.Count == 0)
        {
            throw new InvalidOperationException("Monthly trend chart points are required for Telegram image rendering.");
        }

        using var regularTypeface = LoadTypeface(RegularFontResource);
        using var boldTypeface = LoadTypeface(BoldFontResource);
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Unable to allocate the Telegram monthly trend image surface.");

        Draw(surface.Canvas, trend, regularTypeface, boldTypeface);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 92)
            ?? throw new InvalidOperationException("Unable to encode the Telegram monthly trend image as PNG.");
        var bytes = encoded.ToArray();
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
            : $"{result.CompanyName} ({result.CompanySymbol})";
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
        SKTypeface boldTypeface)
    {
        canvas.Clear(Background);
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var text = new SKPaint { IsAntialias = true, Color = Foreground };
        using var regular18 = new SKFont(regularTypeface, 18);
        using var regular20 = new SKFont(regularTypeface, 20);
        using var regular22 = new SKFont(regularTypeface, 22);
        using var regular14 = new SKFont(regularTypeface, 14);
        using var bold24 = new SKFont(boldTypeface, 24);
        using var bold30 = new SKFont(boldTypeface, 30);
        using var regularShaper = new SKShaper(regularTypeface);
        using var boldShaper = new SKShaper(boldTypeface);

        fill.Color = Surface;
        canvas.DrawRoundRect(new SKRect(24, 20, Width - 24, Height - 20), 26, 26, fill);
        stroke.Color = Border;
        stroke.StrokeWidth = 2;
        canvas.DrawRoundRect(new SKRect(24, 20, Width - 24, Height - 20), 26, 26, stroke);

        var company = string.IsNullOrWhiteSpace(trend.CompanyName)
            ? trend.CompanySymbol
            : $"{trend.CompanyName} ({trend.CompanySymbol})";
        text.Color = Foreground;
        DrawRtlTextWithNumbers(canvas, boldShaper, bold30, text,
            $"روند فروش ماهانه — {company}", Width - 58, 68);
        text.Color = Muted;
        DrawRtlTextWithNumbers(canvas, regularShaper, regular20, text,
            $"آخرین گزارش: {ToPersianDigits($"{trend.LatestReportYear}/{trend.LatestReportMonth:00}")}  |  واحد: {trend.UnitLabelFa}",
            Width - 58, 108);

        const float plotLeft = 112;
        const float plotRight = 1218;
        const float plotTop = 146;
        const float plotBottom = 510;
        var points = trend.ChartPoints.OrderBy(point => point.FiscalMonthIndex).Take(12).ToArray();
        var maximum = FindMaximum(points);
        var yMaximum = maximum <= 0 ? 1m : NiceMaximum(maximum * 1.15m);

        DrawGrid(canvas, regularShaper, regular18, text, stroke, plotLeft, plotRight, plotTop, plotBottom, yMaximum);

        var slotWidth = (plotRight - plotLeft) / Math.Max(12, points.Length);
        const float barWidth = 26;
        using var averagePath = new SKPath();
        var averageStarted = false;

        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            var centerX = plotLeft + slotWidth * index + slotWidth / 2;

            if (point.IsPreviousYearReported && point.PreviousFiscalYearSalesAmount is not null)
            {
                var previousTop = ScaleY(point.PreviousFiscalYearSalesAmount.Value, yMaximum, plotTop, plotBottom);
                var previousX = centerX - barWidth - 7;
                DrawBar(canvas, fill, previousX, plotBottom, barWidth, previousTop, PreviousYear);
                DrawBarValue(canvas, regular14, text, point.PreviousFiscalYearSalesAmount.Value,
                    previousX + barWidth / 2, previousTop, plotTop);
            }

            if (point.IsCurrentYearReported && point.CurrentFiscalYearSalesAmount is not null)
            {
                var currentTop = ScaleY(point.CurrentFiscalYearSalesAmount.Value, yMaximum, plotTop, plotBottom);
                var currentX = centerX + 7;
                DrawBar(canvas, fill, currentX, plotBottom, barWidth, currentTop, CurrentYear);
                DrawBarValue(canvas, regular14, text, point.CurrentFiscalYearSalesAmount.Value,
                    currentX + barWidth / 2, currentTop, plotTop);
            }

            if (point.Average12MonthSalesAmount is not null)
            {
                var averageY = ScaleY(point.Average12MonthSalesAmount.Value, yMaximum, plotTop, plotBottom);
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

            text.Color = Muted;
            canvas.DrawShapedText(regularShaper, point.FiscalMonthNameFa, centerX, 544,
                SKTextAlign.Center, regular18, text);
        }

        if (averageStarted)
        {
            stroke.Color = Average;
            stroke.StrokeWidth = 4;
            stroke.PathEffect = null;
            canvas.DrawPath(averagePath, stroke);
        }

        DrawLegend(canvas, trend, points, regularShaper, regular20, text, fill);

        text.Color = Muted;
        DrawRtlTextWithNumbers(canvas, regularShaper, regular18, text,
            $"منبع: {ProviderSources.GetDisplayName(trend.SourceProviderName)}  |  محاسبه: {ToPersianDigits(ShamsiMonthCalculator.FormatJalaliDate(trend.CalculatedAtUtc))}",
            Width - 58, 674);

        if (trend.Insights.Count > 0)
        {
            text.Color = Foreground;
            var insight = Bounded(trend.Insights[0].TextFa, 92);
            DrawRtlTextWithNumbers(canvas, regularShaper, regular22, text,
                insight, Width - 58, 638);
        }
    }

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
        var cursor = right;
        foreach (var run in SplitDirectionalRuns(value))
        {
            float width;
            if (run.IsNumeric)
            {
                width = font.MeasureText(run.Text, paint);
                DrawNumericText(canvas, run.Text, cursor, baseline, SKTextAlign.Right, font, paint);
            }
            else
            {
                width = shaper.Shape(run.Text, font).Width;
                canvas.DrawShapedText(shaper, run.Text, cursor, baseline,
                    SKTextAlign.Right, font, paint);
            }

            cursor -= width;
        }

        return right - cursor;
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

    internal static IReadOnlyList<DirectionalTextRun> SplitDirectionalRuns(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var runs = new List<DirectionalTextRun>();
        var start = 0;
        var numeric = IsNumericTextCharacter(value[0]);
        for (var index = 1; index < value.Length; index++)
        {
            var nextNumeric = IsNumericTextCharacter(value[index]);
            if (nextNumeric == numeric)
            {
                continue;
            }

            runs.Add(new DirectionalTextRun(value[start..index], numeric));
            start = index;
            numeric = nextNumeric;
        }

        runs.Add(new DirectionalTextRun(value[start..], numeric));
        return runs;
    }

    private static bool IsNumericTextCharacter(char character) =>
        char.IsDigit(character) ||
        character is '.' or ',' or '/' or '%' or '٪' or '٫' or '٬' or '+' or '-' or '−' or 'K' or 'k';

    internal readonly record struct DirectionalTextRun(string Text, bool IsNumeric);

    private static string ToPersianDigits(string value) =>
        value
            .Replace(',', '٬')
            .Replace('.', '٫')
            .Replace('0', '۰').Replace('1', '۱').Replace('2', '۲').Replace('3', '۳').Replace('4', '۴')
            .Replace('5', '۵').Replace('6', '۶').Replace('7', '۷').Replace('8', '۸').Replace('9', '۹');

    private static string SanitizeFileName(string value)
    {
        var safe = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "symbol" : safe;
    }

    private static string Bounded(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 1)] + "…";
}
