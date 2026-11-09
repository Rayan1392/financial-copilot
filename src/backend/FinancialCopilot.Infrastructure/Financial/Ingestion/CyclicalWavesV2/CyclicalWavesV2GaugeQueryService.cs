using System.Text;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>Read-only Chat projection for the approved CyclicalWaves-only gauge/range capabilities.</summary>
public sealed class CyclicalWavesV2GaugeQueryService(FinancialIngestionDbContext db)
{
    public async Task<string> AnswerAsync(string query, CancellationToken cancellationToken)
    {
        var normalizedQuery = FinancialCopilot.Domain.Financial.Services.PersianSymbolNormalizer.Normalize(query);
        var tickers = await db.CyclicalWavesV2Tickers.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var matches = tickers.Where(x => normalizedQuery.Contains(x.NormalizedTicker, StringComparison.Ordinal))
            .OrderByDescending(x => x.NormalizedTicker.Length).ToArray();
        var equilibrium = ContainsAny(query, "تعادلی", "قیمت تعادل", "equilibrium");
        var isPe = ContainsAny(query, "p/e", "pe gauge", "گیج پی ای", "گیج پی‌ای", "گیج نسبت قیمت به سود", "نسبت قیمت به سود", "زیر حداقل تاریخی");
        var isPs = ContainsAny(query, "p/s", "ps gauge", "گیج پی اس", "گیج پی‌اس", "گیج نسبت قیمت به فروش", "نسبت قیمت به فروش", "زیر میانگین تاریخی");
        var isScreen = ContainsAny(query, "کدام نماد", "کدام سهم", "which stocks", "which symbols");
        if (isScreen && !equilibrium)
        {
            var type = isPs ? "PS" : "PE";
            var availability = await db.CyclicalWavesV2GaugeAvailability.AsNoTracking()
                .Where(x => isPs ? x.HasPs : x.HasPe).Select(x => x.TickerId).ToListAsync(cancellationToken);
            var gauges = await db.CyclicalWavesV2GaugeDetails.AsNoTracking()
                .Where(x => availability.Contains(x.TickerId) && x.GaugeType == type).ToListAsync(cancellationToken);
            var candidates = gauges.Where(x => isPs
                    ? x.Close is not null && x.Close.Value < x.Avg
                    : x.Status == 1)
                .Select(x => new { x.TickerId, x.Close, x.Avg })
                .ToDictionary(x => x.TickerId);
            var names = tickers.Where(x => candidates.ContainsKey(x.Id)).Select(x => x.Ticker).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (names.Length == 0) return $"نمادی با داده کامل برای شرط درخواستی پیدا نشد؛ نمادهای دارای مقدار ناموجود حذف شدند.";
            return isPs
                ? $"نمادهای دارای P/S فعلی کمتر از میانگین تاریخی: {string.Join("، ", names)}. نمادهای با مقدار فعلی ناموجود حذف شدند."
                : $"نمادهای دارای وضعیت P/E پایین‌تر از حداقل تاریخی (status 1): {string.Join("، ", names)}.";
        }
        if (!isPe && !isPs && !equilibrium) isPe = true;
        if (matches.Length == 0) return "نماد موردنظر در کاتالوگ CyclicalWaves پیدا نشد؛ داده‌ای برای این درخواست در دسترس نیست.";

        var answer = new StringBuilder();
        foreach (var ticker in matches.Take(10))
        {
            answer.AppendLine($"نماد {ticker.Ticker} (منبع: CyclicalWaves v2)");
            if (equilibrium)
            {
                var row = await db.CyclicalWavesV2EquilibriumRanges.AsNoTracking().SingleOrDefaultAsync(x => x.TickerId == ticker.Id, cancellationToken);
                if (row is not { HasRange: true, Min: not null, Avg: not null, Max: not null }) answer.AppendLine("بازه قیمت تعادلی در داده‌های فعلی در دسترس نیست.");
                else answer.AppendLine($"بازه قیمت تعادلی (ریال): حداقل {row.Min}، میانگین {row.Avg}، حداکثر {row.Max}. این بازه تضمین قیمت آینده نیست.");
                continue;
            }

            var type = isPs ? "PS" : "PE";
            var label = isPs ? "P/S" : "P/E";
            var has = await db.CyclicalWavesV2GaugeAvailability.AsNoTracking().Where(x => x.TickerId == ticker.Id)
                .Select(x => isPs ? x.HasPs : x.HasPe).SingleOrDefaultAsync(cancellationToken);
            var gauge = await db.CyclicalWavesV2GaugeDetails.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TickerId == ticker.Id && x.GaugeType == type, cancellationToken);
            if (!has || gauge is null) { answer.AppendLine($"گیج {label} در داده‌های فعلی در دسترس نیست."); continue; }
            answer.AppendLine($"گیج تاریخی {label}: مقدار فعلی {Format(gauge.Close)}, کمینه {gauge.Min}، میانگین {gauge.Avg}، بیشینه {gauge.Max}؛ محور {gauge.Start} تا {gauge.End}.");
            answer.AppendLine(gauge.Status switch
            {
                1 => "وضعیت: کمتر از کمینه تاریخی.",
                2 => "وضعیت: بین کمینه و میانگین تاریخی.",
                3 => "وضعیت: بین میانگین و بیشینه تاریخی.",
                4 => "وضعیت: بیشتر از بیشینه تاریخی.",
                _ => "وضعیت رتبه‌بندی نشده است؛ مقدار فعلی ممکن است موجود نباشد یا روی مرزها باشد."
            });
            answer.AppendLine($"تعداد روزها در شش بخش از کمترین به بیشترین: {gauge.A}، {gauge.B}، {gauge.C}، {gauge.D}، {gauge.E}، {gauge.F}.");
        }
        return answer.ToString().Trim();
    }

    private static string Format(FinancialCopilot.Domain.Financial.CyclicalWavesV2.ExactDecimal? value) => value?.ToString() ?? "ناموجود";
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Recognizes explicit requests for the approved historical gauge and equilibrium data.</summary>
public static class CyclicalWavesV2GaugeIntentRules
{
    public static bool LooksLikeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var hasPe = ContainsAny(query, "p/e", "pe", "نسبت قیمت به سود");
        var hasPs = ContainsAny(query, "p/s", "ps", "نسبت قیمت به فروش");
        var isEquilibrium = ContainsAny(query, "قیمت تعادلی", "بازۀ تعادلی", "بازه تعادلی", "equilibrium");
        if (isEquilibrium) return true;
        if (!hasPe && !hasPs) return false;

        var explicitGauge = ContainsAny(query, "گیج", "gauge", "عقربه");
        var historicalComparison = ContainsAny(query, "تاریخی", "تاریخچه", "محدوده", "میانگین", "حداقل", "حداکثر", "جایگاه", "کجا", "زیر");
        return explicitGauge || historicalComparison;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
