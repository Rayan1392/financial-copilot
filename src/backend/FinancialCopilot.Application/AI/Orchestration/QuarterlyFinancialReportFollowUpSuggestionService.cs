using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Creates symbol-specific follow-up actions from a quarterly report already returned to the user.
/// </summary>
public interface IQuarterlyFinancialReportFollowUpSuggestionService
{
    IReadOnlyCollection<SuggestedAction> Build(QuarterlyFinancialReportTableResult result);
}

public sealed class QuarterlyFinancialReportFollowUpSuggestionService(
    IConversationalCapabilityRegistry registry,
    ICapabilityInterpreter interpreter) : IQuarterlyFinancialReportFollowUpSuggestionService
{
    public const string RelevanceReason = "quarterly_financial_report_follow_up";

    public IReadOnlyCollection<SuggestedAction> Build(QuarterlyFinancialReportTableResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var symbol = result.CompanySymbol?.Trim();
        if (result.Outcome is not (QuarterlyFinancialReportOutcome.SuccessfulTable or QuarterlyFinancialReportOutcome.PartialTable) ||
            string.IsNullOrWhiteSpace(symbol) || symbol.Length > 120)
            return [];

        var actions = new List<SuggestedAction>(2);
        Add("monthly_activity_trend", $"\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647 {symbol} \u0631\u0627 \u0646\u0634\u0627\u0646 \u0628\u062f\u0647", MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery);
        Add("product_revenue_mix", $"\u062a\u0631\u06a9\u06cc\u0628 \u0641\u0631\u0648\u0634 \u0645\u062d\u0635\u0648\u0644\u0627\u062a {symbol}", ProductRevenueMixIntentRules.LooksLikeProductRevenueMixQuery);
        return actions;

        void Add(string capabilityCode, string message, Func<string, bool> looksLike)
        {
            if (registry.Find(capabilityCode) is not { Enabled: true } || !looksLike(message))
                return;

            try
            {
                var interpretation = interpreter.Interpret(message);
                if (interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode != capabilityCode ||
                    interpretation.MissingSlots.Count != 0 ||
                    interpretation.EntityMentions.Count(entity => entity.EntityType != "product") != 1 ||
                    !interpretation.EntityMentions.Any(entity => Canonical(entity.Text) == Canonical(symbol)))
                    return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            var idInput = string.Join('\n', RelevanceReason, capabilityCode, registry.Version,
                result.ExternalCompanyId, Canonical(symbol));
            var id = "quarterly-report:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idInput))).ToLowerInvariant();
            actions.Add(new SuggestedAction(
                id,
                SuggestedActionKind.RunRelatedCapability,
                message,
                message,
                capabilityCode,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["symbol"] = symbol },
                RelevanceReason,
                registry.Version));
        }
    }

    private static string Canonical(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : QueryNormalization.Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\u200c", string.Empty, StringComparison.Ordinal);
}
