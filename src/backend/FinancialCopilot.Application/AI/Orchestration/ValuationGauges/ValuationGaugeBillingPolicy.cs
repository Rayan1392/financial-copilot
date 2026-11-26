using System.Globalization;
using FinancialCopilot.Application.FinancialData.ValuationGauges;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Change «valuation-gauge-per-card-credits» (owner, 2026-11-26): every usable gauge card costs one credit. A usable
/// answer settles with <c>ValuationGaugesAnswered.&lt;n&gt;</c>, where n is the number of usable cards, and the billing
/// hook charges that fixed amount in every usage-based mode. A card without an answer is free (OQ-138); a range-only
/// equilibrium card is usable (OQ-139). Before execution the hold is raised to the requested cards (OQ-140).
/// </summary>
public static class ValuationGaugeBillingPolicy
{
    public const decimal CreditsPerCard = 1m;

    public const string AnsweredStatusPrefix = "ValuationGaugesAnswered.";

    private static readonly int MaxCards = Enum.GetValues<ValuationGaugeKind>().Length;

    /// <summary>The cards the user is charged for: Available and Partial ones.</summary>
    public static int ChargedCards(ValuationGaugeCardsResult result) => result.Cards.Count(card => card.IsDisplayable());

    public static string AnsweredStatus(int usableCards)
    {
        if (usableCards < 1 || usableCards > MaxCards)
            throw new ArgumentOutOfRangeException(nameof(usableCards), usableCards, "A gauge answer has 1 to 3 usable cards.");
        return AnsweredStatusPrefix + usableCards.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryParseAnsweredStatus(string? completionStatus, out int usableCards)
    {
        usableCards = 0;
        if (completionStatus is null || !completionStatus.StartsWith(AnsweredStatusPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(completionStatus.AsSpan(AnsweredStatusPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture,
                out usableCards)
            && usableCards >= 1 && usableCards <= MaxCards;
    }

    /// <summary>The fixed amount for a gauge-answer status; null for every other status.</summary>
    public static decimal? FixedCredits(string? completionStatus) =>
        TryParseAnsweredStatus(completionStatus, out var cards) ? cards * CreditsPerCard : null;

    /// <summary>The hold before execution: the requested kinds, where an empty kinds slot means all three.</summary>
    public static decimal RequiredCredits(ValidatedQueryFrame frame) =>
        ValuationGaugeCardsUseCase.Normalize(ValuationGaugeIntentRules.ParseKinds(frame.Value(QuerySlotType.MetricSet))).Count
        * CreditsPerCard;
}
