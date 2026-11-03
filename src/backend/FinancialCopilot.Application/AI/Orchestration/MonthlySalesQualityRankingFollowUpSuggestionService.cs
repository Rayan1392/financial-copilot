using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Creates company-specific monthly sales trend actions from the ranked companies already
/// returned to the user. The suggestions never execute a capability or make a new ranking query.
/// </summary>
public interface IMonthlySalesQualityRankingFollowUpSuggestionService
{
    IReadOnlyCollection<SuggestedAction> Build(MonthlySalesQualityRankingResponse result);
}

public sealed class MonthlySalesQualityRankingFollowUpSuggestionService(
    IConversationalCapabilityRegistry registry,
    ICapabilityInterpreter interpreter) : IMonthlySalesQualityRankingFollowUpSuggestionService
{
    public const string RelevanceReason = "monthly_sales_quality_ranking_follow_up";
    private const string CapabilityCode = "monthly_activity_trend";
    private const int MaximumSymbolLength = 120;

    public IReadOnlyCollection<SuggestedAction> Build(MonthlySalesQualityRankingResponse result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (registry.Find(CapabilityCode) is not { Enabled: true }) return [];

        var actions = new List<SuggestedAction>(3);
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in result.Items.Take(3))
        {
            var symbol = item.Symbol?.Trim();
            if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > MaximumSymbolLength || !symbols.Add(Canonical(symbol)))
                continue;

            var message = $"\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647 {symbol} \u0631\u0627 \u0646\u0634\u0627\u0646 \u0628\u062f\u0647";
            if (!RoundTrips(message, symbol)) continue;

            var idInput = string.Join('\n', RelevanceReason, CapabilityCode, registry.Version,
                result.ReportYear, result.ReportMonth, result.Scope, result.Direction, Canonical(symbol));
            var id = "monthly-ranking:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idInput))).ToLowerInvariant();
            actions.Add(new SuggestedAction(
                id,
                SuggestedActionKind.RunRelatedCapability,
                message,
                message,
                CapabilityCode,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["symbol"] = symbol },
                RelevanceReason,
                registry.Version));
        }

        return actions;
    }

    private bool RoundTrips(string message, string symbol)
    {
        if (!MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(message) ||
            MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(message) ||
            Canonical(MonthlyActivityTrendIntentRules.ExtractCompanySymbol(message)) != Canonical(symbol))
            return false;

        try
        {
            var interpretation = interpreter.Interpret(message);
            return interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode == CapabilityCode &&
                   interpretation.MissingSlots.Count == 0 &&
                   interpretation.EntityMentions.Count == 1 &&
                   Canonical(interpretation.EntityMentions[0].Text) == Canonical(symbol);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string Canonical(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : QueryNormalization.Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\u200c", string.Empty, StringComparison.Ordinal);
}
