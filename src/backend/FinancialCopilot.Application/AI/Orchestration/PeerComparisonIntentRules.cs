namespace FinancialCopilot.Application.AI.Orchestration;

public static class PeerComparisonIntentRules
{
    public static bool ShouldRouteEarly(AiQueryRequest request, IChatCapabilityAvailability? availability) =>
        request.Context?.InsightEventId is null &&
        availability?.EvaluateForV2(CapabilityExecutionRoutes.IndustryRelativeValuation,
            ChatCapabilityAvailability.PeerComparisonCapabilityCode).IsAvailable == true &&
        IsExplicitComparison(request.Message);

    public static bool IsExplicitComparison(string message)
    {
        var text = QueryNormalization.Normalize(message);
        return Contains("مقایسه", "نسبت به", "compare", "versus", "against") &&
            Contains("هم گروه", "همگروه", "هم صنعت", "همصنعت", "صنعت خودش", "صنعتش", "با صنعت",
                "شرکت های صنعت", "its industry", "its sector", "its peers", "industry peers", "sector peers") &&
            !Contains("دو نماد", "two symbols", "two stocks", "کدام سهام", "کدام نمادها", "چه سهامی", "سهامی که",
                "which stocks", "stocks with");

        bool Contains(params string[] phrases) => phrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }
}
