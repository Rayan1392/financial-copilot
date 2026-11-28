using System.Globalization;
using System.Text;
using FinancialCopilot.Application.FinancialData.Options;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Every Persian label, note and sentence of the option comparison answer (OQ-175). One owner, so
/// the rule "compare, never recommend" can be checked: none of these strings may contain
/// «بهترین», «مناسب», «پیشنهاد» or «فرصت».
/// </summary>
public static class OptionComparisonWording
{
    public const string CallTitle = "مقایسهٔ هزینهٔ خرید از مسیر اختیار خرید";
    public const string PutTitle = "مقایسهٔ قیمت فروش از مسیر اختیار فروش";
    public const string CoveredCallTitle = "مقایسهٔ بازده فروش اختیار خرید روی سهم";

    public const string LowestEffectivePurchaseCost = "کمترین هزینهٔ مؤثر";
    public const string LowestImpliedAnnualRate = "کمترین نرخ ضمنی";
    public const string HighestEffectiveSalePrice = "بیشترین قیمت فروش مؤثر";
    public const string HighestAnnualizedYieldIfExercised = "بیشترین بازده سالانه در صورت اعمال";

    public const string CallNote =
        "اعمال اختیار خرید یعنی پرداخت قیمت اعمال برای هر سهم در سررسید. اگر قیمت سهم در سررسید زیر قیمت اعمال باشد، مبلغ پرداختی بابت اختیار از دست می‌رود.";
    public const string PutNote =
        "اعمال اختیار فروش یعنی تحویل سهم در سررسید؛ بدون داشتن سهم، اعمال فیزیکی ممکن نیست.";
    public const string CoveredCallNote =
        "برای این موقعیت باید سهم را در اختیار داشته باشید. اگر قیمت سهم از قیمت اعمال بالاتر برود، سود شما به قیمت اعمال محدود می‌شود.";
    public const string VolumeNote = "قیمت‌ها فقط برای حجم نمایش‌داده‌شده در سفارش‌های فعلی معتبرند.";
    public const string AdviceNote = "این مقایسه بر پایهٔ دادهٔ بازار است؛ پیش‌بینی قطعی و توصیهٔ سرمایه‌گذاری نیست.";
    public const string OrderBookNotCurrent =
        "عمق سفارش به‌روز نیست؛ فقط آخرین قیمت معامله و موقعیت‌های باز نمایش داده می‌شود.";
    public const string UnknownUnderlyingClassNote =
        "نوع دارایی پایه مشخص نشد؛ مالیات نقل‌وانتقال مانند سهم محاسبه شده است.";
    public const string TheoreticalNote = "قیمت تئوریک با مدل بلک‌شولز محاسبه شده و قیمت هدف نیست.";

    public static IReadOnlyList<string> All { get; } =
    [
        CallTitle, PutTitle, CoveredCallTitle, LowestEffectivePurchaseCost, LowestImpliedAnnualRate,
        HighestEffectiveSalePrice, HighestAnnualizedYieldIfExercised, CallNote, PutNote, CoveredCallNote,
        VolumeNote, AdviceNote, OrderBookNotCurrent, UnknownUnderlyingClassNote, TheoreticalNote
    ];

    public static string Title(OptionComparisonView view) => view switch
    {
        OptionComparisonView.Put => PutTitle,
        OptionComparisonView.CoveredCall => CoveredCallTitle,
        _ => CallTitle
    };

    public static string ViewNote(OptionComparisonView view) => view switch
    {
        OptionComparisonView.Put => PutNote,
        OptionComparisonView.CoveredCall => CoveredCallNote,
        _ => CallNote
    };

    public static string Highlight(string code) => code switch
    {
        OptionHighlights.LowestEffectivePurchaseCost => LowestEffectivePurchaseCost,
        OptionHighlights.LowestImpliedAnnualRate => LowestImpliedAnnualRate,
        OptionHighlights.HighestEffectiveSalePrice => HighestEffectiveSalePrice,
        OptionHighlights.HighestAnnualizedYieldIfExercised => HighestAnnualizedYieldIfExercised,
        _ => code
    };
}

/// <summary>What the capability returns when it has no comparison to show; rendered as one sentence.</summary>
public sealed record OptionComparisonUnavailable(
    string Symbol,
    OptionComparisonFailure Failure,
    IReadOnlyList<DateOnly> ListedExpiryDates);

/// <summary>
/// The one text rendering of <see cref="OptionComparison"/>: the web reply text, the persisted
/// message text and the Telegram reply all come from here. It shows the asked view for the
/// nearest expiry (or the asked one), at most eight fillable rows nearest to the money.
/// </summary>
public static class OptionComparisonTextComposer
{
    public const int MaxRows = 8;
    private static readonly TimeSpan TehranOffset = TimeSpan.FromHours(3.5);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Compose(OptionComparison comparison)
    {
        var view = comparison.ViewOrDefault(comparison.RequestedView)
            ?? comparison.Views.FirstOrDefault(item => item.Expiries.Any(expiry => expiry.Rows.Count > 0));
        var builder = new StringBuilder();
        if (view is null)
            return Unavailable(new OptionComparisonUnavailable(comparison.UnderlyingSymbol, OptionComparisonFailure.NoOptionContracts, []));

        var expiry = view.Expiries.FirstOrDefault(item => item.Rows.Any(row => row.IsRequestedContract))
            ?? view.Expiries.First(item => item.Rows.Count > 0);
        builder.Append(OptionComparisonWording.Title(view.View)).Append(" — ").Append(comparison.UnderlyingSymbol).AppendLine();
        builder.Append("قیمت سهم: ").Append(Money(comparison.UnderlyingPrice)).Append(" ریال (")
            .Append(JalaliDate(comparison.UnderlyingTradingDate)).Append(')').AppendLine();
        builder.Append("سررسید ").Append(JalaliDate(expiry.ExpiryDate)).Append(" (").Append(expiry.DaysToExpiry.ToString(Invariant))
            .Append(" روز)، برای ").Append(comparison.QuantityInContracts.ToString(Invariant)).Append(" قرارداد").AppendLine();

        if (!comparison.OrderBookIsCurrent)
        {
            builder.AppendLine().Append(OptionComparisonWording.OrderBookNotCurrent).AppendLine();
            foreach (var row in NearestToMoney(expiry.Rows, comparison.UnderlyingPrice))
            {
                builder.Append("• ").Append(row.Symbol).Append(" | اعمال ").Append(Money(row.StrikePrice))
                    .Append(" | آخرین معامله ").Append(row.LastPrice is { } last ? Money(last) : "—")
                    .Append(" | موقعیت باز ").Append(row.OpenInterest.ToString("N0", Invariant)).AppendLine();
            }
        }
        else
        {
            var fillable = expiry.Rows.Where(row => row.State == OptionRowState.Fillable).ToArray();
            builder.AppendLine();
            if (fillable.Length == 0)
                builder.Append("در این سررسید سفارشی با حجم کافی برای این تعداد قرارداد وجود ندارد.").AppendLine();
            foreach (var row in NearestToMoney(fillable, comparison.UnderlyingPrice))
                builder.Append("• ").Append(RowText(row, view.View)).AppendLine();

            foreach (var row in expiry.Rows.Where(row => row.Highlights.Count > 0))
            foreach (var highlight in row.Highlights)
                builder.Append(OptionComparisonWording.Highlight(highlight)).Append(": ").Append(row.Symbol).AppendLine();

            var withoutOrder = expiry.Rows.Count - fillable.Length;
            if (withoutOrder > 0)
                builder.Append("بدون سفارش یا با حجم ناکافی: ").Append(withoutOrder.ToString(Invariant)).Append(" قرارداد").AppendLine();
        }

        builder.AppendLine();
        builder.Append(OptionComparisonWording.ViewNote(view.View)).AppendLine();
        builder.Append(OptionComparisonWording.VolumeNote).AppendLine();
        if (comparison.UnderlyingClass == OptionUnderlyingClass.Unknown && view.View != OptionComparisonView.Call)
            builder.Append(OptionComparisonWording.UnknownUnderlyingClassNote).AppendLine();
        if (comparison.TheoreticalInputs is { } inputs && comparison.OrderBookIsCurrent)
            builder.Append(OptionComparisonWording.TheoreticalNote)
                .Append(" نرخ بدون ریسک ").Append(Percent(inputs.RiskFreeAnnualRate))
                .Append("، نوسان تاریخی ").Append(Percent(inputs.HistoricalVolatility)).Append('.').AppendLine();
        builder.Append("کارمزدها: معامله ").Append(Percent(comparison.Fees.Trade, 2))
            .Append("، اعمال ").Append(Percent(comparison.Fees.Exercise, 2));
        if (view.View != OptionComparisonView.Call)
            builder.Append("، مالیات نقل‌وانتقال ").Append(Percent(comparison.Fees.TransferTaxFor(comparison.UnderlyingClass), 2));
        builder.Append('.').AppendLine();
        if (comparison.OrderBookObservedAt is { } observedAt)
            builder.Append("زمان عمق سفارش: ").Append(JalaliDateTime(observedAt)).AppendLine();
        builder.Append(OptionComparisonWording.AdviceNote);
        return builder.ToString();
    }

    public static string Unavailable(OptionComparisonUnavailable unavailable) => unavailable.Failure switch
    {
        OptionComparisonFailure.ExpiryNotListed when unavailable.ListedExpiryDates.Count > 0 =>
            $"سررسید خواسته‌شده برای {unavailable.Symbol} وجود ندارد. سررسیدهای موجود: " +
            string.Join("، ", unavailable.ListedExpiryDates.Select(JalaliDate)) + ".",
        OptionComparisonFailure.NoUnderlyingPrice =>
            $"قیمت {unavailable.Symbol} در دسترس نیست و مقایسهٔ اختیارها انجام نشد.",
        _ => $"برای {unavailable.Symbol} قرارداد اختیار معاملهٔ فعالی ثبت نشده است."
    };

    private static string RowText(OptionComparisonRow row, OptionComparisonView view)
    {
        var builder = new StringBuilder();
        builder.Append(row.Symbol).Append(" | اعمال ").Append(Money(row.StrikePrice));
        switch (view)
        {
            case OptionComparisonView.Call:
                builder.Append(" | عرضه ").Append(Money(row.FillPrice))
                    .Append(" | هزینهٔ مؤثر ").Append(Money(row.EffectivePurchaseCost))
                    .Append(" (").Append(SignedPercent(row.BreakEvenDistance)).Append(" نسبت به قیمت سهم)")
                    .Append(" | ارزش زمانی ").Append(Money(row.TimeValue));
                if (row.ImpliedAnnualRate is { } rate)
                    builder.Append(" | نرخ ضمنی سالانه ").Append(Percent(rate, 0));
                builder.Append(" | اهرم ").Append((row.Leverage ?? 0).ToString("0.0", Invariant));
                break;
            case OptionComparisonView.Put:
                builder.Append(" | عرضه ").Append(Money(row.FillPrice))
                    .Append(" | قیمت فروش مؤثر ").Append(Money(row.EffectiveSalePrice))
                    .Append(" (").Append(SignedPercent(row.DistanceFromMarket)).Append(" نسبت به قیمت سهم)")
                    .Append(" | هزینهٔ پوشش ").Append(Percent(row.ProtectionCost ?? 0));
                break;
            default:
                builder.Append(" | تقاضا ").Append(Money(row.FillPrice))
                    .Append(" | بازده در صورت اعمال ").Append(Percent(row.YieldIfExercised ?? 0));
                if (row.YieldIfExercisedAnnualized is { } annualized)
                    builder.Append(" (سالانه ").Append(Percent(annualized, 0)).Append(')');
                builder.Append(" | بازده بدون اعمال ").Append(Percent(row.YieldIfNotExercised ?? 0));
                break;
        }

        if (row.TheoreticalPrice is { } theoretical)
            builder.Append(" | تئوریک ").Append(Money(theoretical));
        builder.Append(" | حجم ").Append(row.AvailableVolume.ToString("N0", Invariant));
        return builder.ToString();
    }

    private static IEnumerable<OptionComparisonRow> NearestToMoney(IEnumerable<OptionComparisonRow> rows, decimal underlyingPrice) =>
        rows.OrderBy(row => Math.Abs(row.StrikePrice - underlyingPrice))
            .Take(MaxRows)
            .OrderBy(row => row.StrikePrice);

    private static string Money(decimal? value) =>
        value is { } amount ? Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("N0", Invariant) : "—";

    private static string Percent(decimal fraction, int decimals = 1) =>
        Math.Round(fraction * 100, decimals, MidpointRounding.AwayFromZero).ToString("F" + decimals, Invariant) + "٪";

    private static string SignedPercent(decimal? fraction)
    {
        if (fraction is not { } value) return "—";
        var text = Percent(Math.Abs(value));
        return value > 0 ? text + " بالاتر" : value < 0 ? text + " پایین‌تر" : text;
    }

    private static string JalaliDate(DateOnly date)
    {
        var calendar = new PersianCalendar();
        var value = date.ToDateTime(TimeOnly.MinValue);
        return string.Create(Invariant, $"{calendar.GetYear(value):0000}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}");
    }

    private static string JalaliDateTime(DateTimeOffset instant)
    {
        var local = instant.ToOffset(TehranOffset);
        return JalaliDate(DateOnly.FromDateTime(local.DateTime)) + " " + local.ToString("HH:mm", Invariant);
    }
}
