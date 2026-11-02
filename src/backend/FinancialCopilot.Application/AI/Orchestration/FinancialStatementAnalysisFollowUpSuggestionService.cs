using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

public interface IFinancialStatementAnalysisFollowUpSuggestionService
{
    IReadOnlyCollection<SuggestedAction> Build(FinancialStatementAnalysisResponse result);
}

public sealed class FinancialStatementAnalysisFollowUpSuggestionService(
    IConversationalCapabilityRegistry registry,
    ICapabilityInterpreter interpreter) : IFinancialStatementAnalysisFollowUpSuggestionService
{
    public const string RelevanceReason = "financial_statement_analysis_follow_up";

    public IReadOnlyCollection<SuggestedAction> Build(FinancialStatementAnalysisResponse result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var symbol = result.CompanySymbol?.Trim();
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 120)
            return [];

        var actions = new List<SuggestedAction>(2);
        Add(
            "financial_statement_table",
            $"\u06af\u0632\u0627\u0631\u0634 \u0641\u0635\u0644\u06cc {symbol}",
            FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery);
        Add(
            "monthly_activity_trend",
            $"\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647 {symbol} \u0631\u0627 \u0646\u0634\u0627\u0646 \u0628\u062f\u0647",
            MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery);
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

            var identity = string.Join('\n', RelevanceReason, capabilityCode, registry.Version,
                result.SourceReferences.FirstOrDefault()?.StatementId, Canonical(symbol));
            var id = "financial-statement-analysis:" +
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
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
