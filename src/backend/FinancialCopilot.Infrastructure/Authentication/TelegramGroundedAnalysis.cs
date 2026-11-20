using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.Telegram;

namespace FinancialCopilot.Infrastructure.Authentication;

/// <summary>
/// Change «sol-deep-analysis-web-search» (T13, design-proposal §6 and §8): how Telegram shows the analysis price and a
/// 163 offer. A discussable report gets one price line at the defaults (OQ-111 a: no settings buttons). A 163 offer
/// gets one inline button per tier, labelled with its credits, and «انصراف». Callback data is
/// <c>ga1:{offerId:N}:{s0|s1|d0|d1|x}</c> (39 bytes, under Telegram's 64); the adapter turns it into the backend
/// offer action <c>ga1:{offerId}:confirm|cancel</c> plus the tier as analysis options. Prices, balance, expiry and
/// single use are always checked by the backend; these buttons only carry the user's choice.
/// </summary>
internal static class TelegramGroundedAnalysis
{
    private const string CancelCode = "x";
    public const string ConfirmLabel = "تحلیل کن";
    public const string CancelLabel = "انصراف";
    public const string InvalidCallbackText = "این دکمه معتبر نیست. برای تحلیل، پرسش را دوباره بفرستید.";
    private const string UnlimitedText = "با اشتراک Max اعتباری کسر نمی‌شود.";

    public static string ChoiceCallbackData(Guid offerId, AnalysisChoice choice) =>
        $"{GroundedAnalysisOfferActions.Prefix}{offerId:N}:{(choice.Depth == AnalysisDepth.Simple ? 's' : 'd')}{(choice.WebSearch ? '1' : '0')}";

    public static string CancelCallbackData(Guid offerId) => $"{GroundedAnalysisOfferActions.Prefix}{offerId:N}:{CancelCode}";

    /// <summary>Parses a Telegram offer button; <paramref name="choice"/> is null for cancel. False for anything else.</summary>
    public static bool TryParse(string? data, out Guid offerId, out AnalysisChoice? choice)
    {
        offerId = Guid.Empty;
        choice = null;
        if (data is null || !data.StartsWith(GroundedAnalysisOfferActions.Prefix, StringComparison.Ordinal)) return false;
        var parts = data[GroundedAnalysisOfferActions.Prefix.Length..].Split(':');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out offerId)) return false;
        choice = parts[1] switch
        {
            "s0" => new AnalysisChoice(AnalysisDepth.Simple, false),
            "s1" => new AnalysisChoice(AnalysisDepth.Simple, true),
            "d0" => new AnalysisChoice(AnalysisDepth.Deep, false),
            "d1" => new AnalysisChoice(AnalysisDepth.Deep, true),
            _ => null
        };
        return choice is not null || parts[1] == CancelCode;
    }

    /// <summary>The plain-text line under a discussable report or a 162 answer; null when there is nothing to show.</summary>
    public static string? PriceLine(GroundedAnalysisHint? hint)
    {
        if (hint is not { Available: true, Offer: null }) return null;
        var tier = AnalysisLabels.Tier(hint.Defaults.Depth, hint.Defaults.WebSearch);
        if (hint.Unlimited) return $"پرسش تحلیلی درباره‌ی این گزارش: {tier}؛ {UnlimitedText}";
        var price = PriceOf(hint, hint.Defaults);
        return price is null ? null : $"پرسش تحلیلی درباره‌ی این گزارش: {tier}، {AnalysisLabels.Credits(price.Credits)}";
    }

    /// <summary>
    /// The buttons of a 163 offer and, on a short balance, the note under it. As on the web, a short balance blocks the
    /// default tier and dearer ones; cheaper tiers stay, and the backend refuses any tier the balance cannot cover.
    /// </summary>
    public static (string? Note, IReadOnlyList<TelegramAssistantAction> Actions) Offer(GroundedAnalysisHint? hint)
    {
        if (hint?.Offer is not { } offer) return (null, []);
        var defaultPrice = PriceOf(hint, hint.Defaults);
        var shortBalance = !hint.Unlimited && !offer.SufficientBalance;
        var tiers = hint.Prices
            .Where(price => !shortBalance || (defaultPrice is not null && price.Credits < defaultPrice.Credits))
            .Select(price => new TelegramAssistantAction(
                hint.Unlimited
                    ? $"{ConfirmLabel} — {AnalysisLabels.Tier(price.Depth, price.WebSearch)}"
                    : $"{ConfirmLabel} — {AnalysisLabels.Tier(price.Depth, price.WebSearch)} ({AnalysisLabels.Credits(price.Credits)})",
                ChoiceCallbackData(offer.OfferId, new AnalysisChoice(price.Depth, price.WebSearch))));
        IReadOnlyList<TelegramAssistantAction> actions =
            [.. tiers, new TelegramAssistantAction(CancelLabel, CancelCallbackData(offer.OfferId))];
        var note = shortBalance
            ? $"موجودی برای تحلیل {AnalysisLabels.Tier(hint.Defaults.Depth, hint.Defaults.WebSearch)} کافی نیست. برای خرید اعتبار /plans را بفرستید."
            : hint.Unlimited ? UnlimitedText : null;
        return (note, actions);
    }

    private static GroundedAnalysisPrice? PriceOf(GroundedAnalysisHint hint, AnalysisChoice choice) =>
        hint.Prices.FirstOrDefault(price => price.Depth == choice.Depth && price.WebSearch == choice.WebSearch);
}
