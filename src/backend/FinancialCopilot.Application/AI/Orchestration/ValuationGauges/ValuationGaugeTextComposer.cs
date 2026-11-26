using System.Globalization;
using System.Numerics;
using System.Text;
using FinancialCopilot.Application.FinancialData.ValuationGauges;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// The one text rendering of <see cref="ValuationGaugeCardsResult"/>: the web reply text, the persisted message text
/// and the Telegram reply all come from here, so every channel shows the same symbol, facts, source and gaps.
/// Display rule (change «valuation-gauge-cards-visual-refresh»): P/E and P/S with exactly two decimals, prices and
/// equilibrium bounds in whole rials, both rounded half away from zero from the exact provider string; the payload
/// keeps the exact values. The provider is named تحلیل‌اپ and its fetch time is shown as a date only.
/// </summary>
public static class ValuationGaugeTextComposer
{
    private static readonly TimeSpan TehranOffset = TimeSpan.FromHours(3.5);

    public static string Compose(ValuationGaugeCardsResult result)
    {
        var builder = new StringBuilder();
        var usable = result.Cards.Where(card => card.IsDisplayable()).ToArray();
        var title = result.RequestedKinds.Count == 1
            ? $"{KindTitle(result.RequestedKinds[0])} {result.Symbol}"
            : $"گیج‌های ارزش‌گذاری {result.Symbol}";
        if (usable.Length == 0)
        {
            builder.Append("برای ").Append(title).Append(" دادهٔ قابل نمایش وجود ندارد.");
            foreach (var card in result.Cards)
                builder.AppendLine().Append("• ").Append(KindName(card.Kind)).Append(": ").Append(UnavailableText(card));
            return builder.ToString();
        }

        builder.Append(title);
        foreach (var card in usable)
        {
            builder.AppendLine().AppendLine();
            if (card.Ratio is { } ratio)
                AppendRatio(builder, card, ratio);
            else if (card.Equilibrium is { } equilibrium)
                AppendEquilibrium(builder, card, equilibrium);
        }

        var missing = result.Cards.Where(card => !card.IsDisplayable()).ToArray();
        if (missing.Length > 0)
        {
            builder.AppendLine().AppendLine().Append("در دسترس نیست: ")
                .Append(string.Join("؛ ", missing.Select(card => $"{KindName(card.Kind)} ({UnavailableText(card)})")));
        }

        return builder.ToString();
    }

    private static void AppendRatio(StringBuilder builder, ValuationGaugeCard card, ValuationRatioGaugeFacts ratio)
    {
        builder.Append(KindName(card.Kind)).Append(": ");
        builder.Append(ratio.Current is null ? "نسبت فعلی ثبت نشده است" : $"نسبت فعلی {Ratio2(ratio.Current)}");
        builder.Append(" — ").Append(PositionText(ratio.Position));
        if (ratio.CurrentSide == ValuationGaugeNeedleSide.PinnedBelow)
            builder.Append(" (کمتر از ابتدای محور)");
        else if (ratio.CurrentSide == ValuationGaugeNeedleSide.PinnedAbove)
            builder.Append(" (بیشتر از انتهای محور)");
        builder.AppendLine();
        builder.Append("حد پایین ").Append(Ratio2(ratio.Min))
            .Append("، میانگین ").Append(Ratio2(ratio.Average))
            .Append("، حد بالا ").Append(Ratio2(ratio.Max))
            .Append(" (محور از ").Append(Ratio2(ratio.Start)).Append(" تا ").Append(Ratio2(ratio.End)).Append(')');
        if (card.Insights?.DistanceToAveragePercent is { } distance)
            builder.AppendLine().Append("فاصله تا میانگین ").Append(Percent(distance, signed: true));
        if (ratio.DayPercentages is { Count: 6 } percentages)
        {
            builder.AppendLine().Append("سهم روزهای تاریخی در شش بازه (از کم به زیاد): ")
                .Append(string.Join("، ", percentages.Select(value => Exact(value) + "٪")));
        }
        else
        {
            builder.AppendLine().Append("توزیع روزهای تاریخی ثبت نشده است.");
        }

        if (card.Insights is { DaysBelowBandPercent: { } below, DaysInBandPercent: { } within })
        {
            builder.AppendLine().Append("در ").Append(Percent(below)).Append(" روزها ").Append(KindName(card.Kind))
                .Append(" پایین‌تر از بازهٔ امروز بوده و ").Append(Percent(within)).Append(" روزها در همین بازه.");
        }

        if (card.ReasonCode == ValuationGaugeReasonCodes.AxisNotRenderable)
            builder.AppendLine().Append("مرزهای محور برای رسم گیج معتبر نیست؛ فقط ارقام نمایش داده می‌شود.");
        AppendSource(builder, card);
    }

    private static void AppendEquilibrium(StringBuilder builder, ValuationGaugeCard card, ValuationEquilibriumFacts facts)
    {
        builder.Append("بازهٔ قیمت تعادلی: حد پایین ").Append(Rial0(facts.Lower))
            .Append("، میانه ").Append(Rial0(facts.Midpoint))
            .Append("، حد بالا ").Append(Rial0(facts.Upper)).Append(" ریال");
        builder.AppendLine();
        if (facts.Quote is { } quote)
        {
            builder.Append("قیمت بازار ").Append(Rial0(quote.Price)).Append(" ریال");
            if (facts.Position is { } position)
                builder.Append(" — ").Append(EquilibriumPositionText(position));
            var comparisons = new List<string>();
            if (card.Insights?.PriceVsMidpointPercent is { } vsMid)
                comparisons.Add("تا میانهٔ بازه " + Percent(vsMid, signed: true));
            if (card.Insights?.PriceVsLowerPercent is { } vsLow)
                comparisons.Add("بالای حد پایین " + Percent(vsLow, signed: true));
            if (comparisons.Count > 0)
                builder.AppendLine().Append(string.Join("، ", comparisons));
            // No provider name for users (owner, 2026-11-26); the payload keeps it.
            builder.AppendLine().Append("قیمت: ").Append(QuoteSourceText(quote.SourceLabel))
                .Append("، ").Append(DateTime(quote.AsOf));
        }
        else
        {
            builder.Append(facts.QuoteState == ValuationGaugeQuoteState.TimedOut
                ? "قیمت بازار به‌موقع دریافت نشد؛ فقط بازه نمایش داده می‌شود."
                : "قیمت بازار در دسترس نیست؛ فقط بازه نمایش داده می‌شود.");
        }

        if (card.ReasonCode == ValuationGaugeReasonCodes.AxisNotRenderable)
            builder.AppendLine().Append("مرزهای بازه برای رسم گیج معتبر نیست؛ فقط ارقام نمایش داده می‌شود.");
        AppendSource(builder, card);
    }

    private static void AppendSource(StringBuilder builder, ValuationGaugeCard card)
    {
        builder.AppendLine().Append("منبع: ").Append(SourceName);
        if (card.FetchedAtUtc is { } fetched)
            builder.Append(" · دریافت ").Append(Date(fetched));
    }

    /// <summary>The provider's name for users; the payload keeps the internal source code.</summary>
    public const string SourceName = "تحلیل‌اپ";

    public static string KindName(ValuationGaugeKind kind) => kind switch
    {
        ValuationGaugeKind.Pe => "P/E",
        ValuationGaugeKind.Ps => "P/S",
        _ => "بازهٔ قیمت تعادلی"
    };

    private static string KindTitle(ValuationGaugeKind kind) => kind switch
    {
        ValuationGaugeKind.Pe => "گیج P/E",
        ValuationGaugeKind.Ps => "گیج P/S",
        _ => "گیج قیمت تعادلی"
    };

    public static string PositionText(ValuationRatioPosition position) => position switch
    {
        ValuationRatioPosition.BelowMin => "پایین‌تر از حد پایین تاریخی",
        ValuationRatioPosition.MinToAverage => "بین حد پایین و میانگین",
        ValuationRatioPosition.AverageToMax => "بین میانگین و حد بالا",
        ValuationRatioPosition.AboveMax => "بالاتر از حد بالای تاریخی",
        _ => "بدون رتبه‌بندی منبع"
    };

    public static string EquilibriumPositionText(ValuationEquilibriumPosition position) => position switch
    {
        ValuationEquilibriumPosition.BelowRange => "زیر بازهٔ تعادلی",
        ValuationEquilibriumPosition.LowerHalf => "در نیمهٔ پایین بازه",
        ValuationEquilibriumPosition.AtMidpoint => "روی میانهٔ بازه",
        ValuationEquilibriumPosition.UpperHalf => "در نیمهٔ بالای بازه",
        _ => "بالاتر از بازهٔ تعادلی"
    };

    public static string UnavailableText(ValuationGaugeCard card) => card.State switch
    {
        ValuationGaugeCardState.Stale => card.FetchedAtUtc is { } fetched
            ? $"دادهٔ منبع قدیمی است؛ آخرین دریافت {Date(fetched)}"
            : "دادهٔ منبع قدیمی است",
        ValuationGaugeCardState.TemporarilyUnavailable => "موقتاً در دسترس نیست",
        _ => card.ReasonCode switch
        {
            ValuationGaugeReasonCodes.CoverageMiss => "این نماد در پوشش دادهٔ گیج تحلیل‌اپ نیست",
            ValuationGaugeReasonCodes.AmbiguousMatch => "تطبیق یکتای نماد با منبع گیج ممکن نشد",
            _ => "داده‌ای ثبت نشده است"
        }
    };

    private static string QuoteSourceText(string sourceLabel) => sourceLabel switch
    {
        "IntradayToday" => "معاملات امروز",
        "LatestDailyFallback" => "آخرین روز معاملاتی",
        _ => sourceLabel
    };

    /// <summary>Tehran local time in the Persian calendar, for example ۱۴۰۵/۰۹/۰۱ ۱۶:۱۰.</summary>
    public static string DateTime(DateTimeOffset value)
    {
        var local = value.ToOffset(TehranOffset).DateTime;
        var calendar = new PersianCalendar();
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{calendar.GetYear(local):0000}/{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00} {local:HH:mm}");
        return Digits(text);
    }

    /// <summary>Tehran local date in the Persian calendar, for example ۱۴۰۵/۰۹/۰۵.</summary>
    public static string Date(DateTimeOffset value)
    {
        var local = value.ToOffset(TehranOffset).DateTime;
        var calendar = new PersianCalendar();
        return Digits(string.Create(CultureInfo.InvariantCulture,
            $"{calendar.GetYear(local):0000}/{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00}"));
    }

    /// <summary>A P/E or P/S value with exactly two decimals.</summary>
    public static string Ratio2(string value) => Exact(RoundHalfAwayFromZero(value, 2));

    /// <summary>A price or equilibrium bound in whole rials.</summary>
    public static string Rial0(string value) => Exact(RoundHalfAwayFromZero(value, 0));

    /// <summary>A derived percentage («‎+۱۹٫۰٪»); the plus sign is shown only for signed distances.</summary>
    private static string Percent(string value, bool signed = false)
    {
        var text = Exact(value) + "٪";
        var positive = !value.TrimStart().StartsWith('-') && RoundHalfAwayFromZero(value, 1) != "0.0";
        return signed && positive ? "\u200e+" + text : text;
    }

    /// <summary>
    /// Rounds an invariant decimal string half away from zero without a precision limit (provider values may carry
    /// more digits than <see cref="decimal"/> holds). Strings that are not plain decimals are returned unchanged.
    /// </summary>
    public static string RoundHalfAwayFromZero(string value, int decimals)
    {
        var text = value.Trim();
        var negative = text.StartsWith('-');
        if (negative || text.StartsWith('+')) text = text[1..];
        var dot = text.IndexOf('.');
        var integer = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        if (integer.Length == 0) integer = "0";
        if (!integer.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit)) return value;

        var kept = fraction.Length >= decimals ? fraction[..decimals] : fraction.PadRight(decimals, '0');
        var scaled = BigInteger.Parse(integer + kept, CultureInfo.InvariantCulture);
        if (fraction.Length > decimals && fraction[decimals] >= '5') scaled += 1;
        var digits = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
        var rounded = decimals == 0 ? digits : digits[..^decimals] + "." + digits[^decimals..];
        return negative && !scaled.IsZero ? "-" + rounded : rounded;
    }

    /// <summary>An exact invariant decimal with Persian digits, thousands separator «٬» and decimal mark «٫».</summary>
    public static string Exact(string value)
    {
        var text = value.Trim();
        var sign = text.StartsWith('-') ? "−" : string.Empty;
        if (sign.Length > 0 || text.StartsWith('+')) text = text[1..];
        var dot = text.IndexOf('.');
        var integer = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        var grouped = new StringBuilder();
        for (var index = 0; index < integer.Length; index++)
        {
            if (index > 0 && (integer.Length - index) % 3 == 0) grouped.Append('٬');
            grouped.Append(integer[index]);
        }

        // A left-to-right mark keeps the minus sign attached to the number inside right-to-left text.
        var prefix = sign.Length > 0 ? "‎" + sign : string.Empty;
        return prefix + Digits(grouped + (fraction.Length > 0 ? "٫" + fraction : string.Empty));
    }

    private static string Digits(string value) =>
        string.Concat(value.Select(character => character is >= '0' and <= '9' ? (char)('۰' + character - '0') : character));
}
