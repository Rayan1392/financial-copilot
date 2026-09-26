using System.Diagnostics;
using System.Text.Json;
using SkiaSharp;

namespace FinancialCopilot.Infrastructure.Authentication;

// Canvas 2D owns paragraph BiDi and shaping, as in the web image export.
// Never pass mixed-direction paragraphs directly to SKShaper (a single-run shaper).
internal static class MonthlyTrendBrowserText
{
    internal sealed record Label(string Text, string Color);
    internal sealed record Explanation(string BeforeValue, string? ValueLabel, string AfterValue, string Color);
    internal sealed record Content(string Title, string Unit, Label[] Legend, Explanation[] Explanations);

    private static readonly SemaphoreSlim Slots = new(2);

    internal static byte[] Render(byte[] background, int width, int height, Content content)
    {
        if (!Slots.Wait(TimeSpan.FromSeconds(20)))
            throw new TimeoutException("Monthly chart browser renderer is busy.");
        var directory = Directory.CreateTempSubdirectory("monthly-chart-");
        try
        {
            var page = Path.Combine(directory.FullName, "chart.html");
            var outputPath = Path.Combine(directory.FullName, "chart.png");
            File.WriteAllText(page, BuildHtml(background, width, height, content));
            var start = new ProcessStartInfo(FindBrowser())
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            // Only generated local HTML is loaded. CSP disallows network access;
            // all report text is JSON-encoded and all assets are embedded data URLs.
            foreach (var argument in new[] {
                "--headless", "--no-sandbox", "--disable-dev-shm-usage",
                "--disable-gpu", "--disable-background-networking", "--no-first-run",
                "--no-default-browser-check", "--disable-extensions",
                "--user-data-dir=" + Path.Combine(directory.FullName, "profile"),
                "--virtual-time-budget=5000", "--run-all-compositor-stages-before-draw",
                "--hide-scrollbars", "--window-size=" + width + "," + height,
                "--screenshot=" + outputPath, new Uri(page).AbsoluteUri })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Cannot start chart browser.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("Monthly chart browser rendering timed out.");
            }
            _ = stdout.GetAwaiter().GetResult();
            var errorText = errors.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !File.Exists(outputPath))
                throw new InvalidOperationException($"Monthly chart browser did not produce a PNG (exit={process.ExitCode}, stderr={errorText.Trim()}).");
            var bytes = File.ReadAllBytes(outputPath);
            if (bytes.Length < 8 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71)
                throw new InvalidOperationException("Monthly chart browser returned invalid image data.");
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null || bitmap.Width != width || bitmap.Height != height ||
                bitmap.GetPixel(0, 0) != new SKColor(250, 250, 250))
                throw new InvalidOperationException("Monthly chart browser returned an incomplete chart canvas.");
            return bytes;
        }
        finally
        {
            try { directory.Delete(recursive: true); }
            catch (IOException) { /* A browser child may still be releasing its profile. */ }
            catch (UnauthorizedAccessException) { }
            Slots.Release();
        }
    }

    private static string FindBrowser()
    {
        var configured = Environment.GetEnvironmentVariable("TELEGRAM_CHART_BROWSER");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        if (OperatingSystem.IsLinux())
        {
            if (File.Exists("/usr/bin/chromium-headless-shell")) return "/usr/bin/chromium-headless-shell";
            return "/usr/bin/chromium";
        }
        foreach (var path in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google/Chrome/Application/chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft/Edge/Application/msedge.exe") })
            if (File.Exists(path)) return path;
        throw new InvalidOperationException("Set TELEGRAM_CHART_BROWSER to an installed Chromium executable.");
    }

    internal static string BuildHtml(byte[] background, int width, int height, Content content)
    {
        static string Font(string name)
        {
            using var stream = typeof(MonthlyTrendBrowserText).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("Embedded chart font is missing.");
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return Convert.ToBase64String(bytes.ToArray());
        }
        var model = JsonSerializer.Serialize(new { width, height, content,
            background = Convert.ToBase64String(background),
            regular = Font("FinancialCopilot.Assets.Samim.ttf"),
            bold = Font("FinancialCopilot.Assets.Samim-Bold.ttf") });
        return """
            <!doctype html><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; font-src data:; img-src data:">
            <style>html,body{margin:0;padding:0;overflow:hidden;background:#FAFAFA}canvas{display:block}</style>
            <canvas id="chart"></canvas><script>
            (async () => {
            const model =
            """ + model + """
            ;
            try {
            const regular = new FontFace('Chart', `url(data:font/ttf;base64,${model.regular})`);
            const bold = new FontFace('Chart', `url(data:font/ttf;base64,${model.bold})`, {weight:'700'});
            await Promise.all([regular.load(), bold.load()]);
            document.fonts.add(regular); document.fonts.add(bold);
            const canvas = document.getElementById('chart');
            canvas.width = model.width; canvas.height = model.height;
            const ctx = canvas.getContext('2d');
            const background = new Image();
            background.src = `data:image/png;base64,${model.background}`;
            await background.decode();
            ctx.drawImage(background, 0, 0);
            // Raw logical text, native browser paragraph layout. No Unicode wrappers.
            function text(value, right, baseline, size, color, bold=false) {
              ctx.direction = 'rtl'; ctx.textAlign = 'right';
              ctx.font = `${bold ? '700' : '400'} ${size}px Chart`;
              ctx.fillStyle = color; ctx.fillText(value, right, baseline);
              return ctx.measureText(value).width;
            }
            const right = model.width - 90;
            const c = model.content;
            text(c.Title, right, 84, 34, '#18181b', true);
            text(c.Unit, right, 126, 24, '#3f3f46');
            let cursor = right;
            for (const entry of c.Legend) {
              ctx.fillStyle = entry.Color; ctx.fillRect(cursor-18, 155, 18, 18);
              const width = text(entry.Text, cursor-28, 170, 20, '#18181b');
              cursor -= width + 95;
            }
            text('توضیحات', right, 1015, 24, '#18181b', true);
            c.Explanations.forEach((line, index) => {
              const y = 1057 + 42 * index;
              let x = right - text(line.BeforeValue, right, y, 22, '#3f3f46');
              if (line.ValueLabel !== null) {
                ctx.direction = 'ltr'; ctx.fillStyle = line.Color;
                ctx.fillText(line.ValueLabel, x, y);
                x -= ctx.measureText(line.ValueLabel).width;
              }
              text(line.AfterValue, x, y, 22, '#3f3f46');
            });
            const result = document.createElement('pre'); result.id = 'result';
            result.textContent = canvas.toDataURL('image/png').split(',')[1];
            document.body.append(result);
            } catch (error) {
              document.body.textContent = String(error && error.stack ? error.stack : error);
            }
            })();
            </script>
            """;
    }
}
