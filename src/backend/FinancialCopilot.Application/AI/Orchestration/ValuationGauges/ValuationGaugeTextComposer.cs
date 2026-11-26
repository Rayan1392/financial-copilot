using System.Globalization;
using System.Text;
using FinancialCopilot.Application.FinancialData.ValuationGauges;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// The one text rendering of <see cref="ValuationGaugeCardsResult"/>: the web reply text, the persisted message text
/// and the Telegram reply all come from here, so every channel shows the same symbol, facts, source and gaps.
/// Provider values are printed exactly (Persian digits and separators only).
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
        builder.Append(ratio.Current is null ? "نسبت فعلی ثبت نشده است" : $"نسبت فعلی {Exact(ratio.Current)}");
        builder.Append(" — ").Append(PositionText(ratio.Position));
        if (ratio.CurrentSide == ValuationGaugeNeedleSide.PinnedBelow)
            builder.Append(" (کمتر از ابتدای محور)");
        else if (ratio.CurrentSide == ValuationGaugeNeedleSide.PinnedAbove)
            builder.Append(" (بیشتر از انتهای محور)");
        builder.AppendLine();
        builder.Append("حد پایین ").Append(Exact(ratio.Min))
            .Append(" · میانگین ").Append(Exact(ratio.Average))
            .Append(" · حد بالا ").Append(Exact(ratio.Max))
            .Append(" (محور از ").Append(Exact(ratio.Start)).Append(" تا ").Append(Exact(ratio.End)).Append(')');
        if (ratio.DayPercentages is { Count: 6 } percentages)
        {
            builder.AppendLine().Append("سهم روزهای تاریخی در شش بازه (از کم به زیاد): ")
                .Append(string.Join("، ", percentages.Select(value => Exact(value) + "٪")));
        }
        else
        {
            builder.AppendLine().Append("توزیع روزهای تاریخی ثبت نشده است.");
        }

        if (card.ReasonCode == ValuationGaugeReasonCodes.AxisNotRenderable)
            builder.AppendLine().Append("مرزهای محور برای رسم گیج معتبر نیست؛ فقط ارقام نمایش داده می‌شود.");
        AppendSource(builder, card);
    }

    private static void AppendEquilibrium(StringBuilder builder, ValuationGaugeCard card, ValuationEquilibriumFacts facts)
    {
        builder.Append("بازهٔ قیمت تعادلی: حد پایین ").Append(Exact(facts.Lower))
            .Append(" · میانه ").Append(Exact(facts.Midpoint))
            .Append(" · حد بالا ").Append(Exact(facts.Upper)).Append(" ریال");
        builder.AppendLine();
        if (facts.Quote is { } quote)
        {
            builder.Append("قیمت بازار ").Append(Exact(quote.Price)).Append(" ریال");
            if (facts.Position is { } position)
                builder.Append(" — ").Append(EquilibriumPositionText(position));
            builder.AppendLine().Append("قیمت: ").Append(QuoteSourceText(quote.SourceLabel))
                .Append("، ").Append(quote.ProviderName).Append("، ").Append(DateTime(quote.AsOf));
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
        builder.AppendLine().Append("منبع: CyclicalWaves");
        if (card.FetchedAtUtc is { } fetched)
            builder.Append(" · زمان دریافت داده: ").Append(DateTime(fetched));
    }

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
            ? $"دادهٔ منبع قدیمی است؛ آخرین دریافت {DateTime(fetched)}"
            : "دادهٔ منبع قدیمی است",
        ValuationGaugeCardState.TemporarilyUnavailable => "موقتاً در دسترس نیست",
        _ => card.ReasonCode switch
        {
            ValuationGaugeReasonCodes.CoverageMiss => "این نماد در پوشش دادهٔ گیج CyclicalWaves نیست",
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
