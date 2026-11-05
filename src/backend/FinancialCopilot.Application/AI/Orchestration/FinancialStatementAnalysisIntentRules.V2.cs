using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Feature 154 V2-only analysis recognition. «تحلیل عملکرد مالی X» behaves exactly like «تحلیل صورت سود و زیان X»
/// (PD-21) because V2 canonicalizes the binding aliases before the shared rules run. The shared methods keep their
/// pre-Feature 154 behavior for the frozen V1 path.
/// </summary>
public static partial class FinancialStatementAnalysisIntentRules
{
    public static bool LooksLikeV2FinancialStatementAnalysisQuery(string? query) =>
        !string.IsNullOrWhiteSpace(query) &&
        LooksLikeFinancialStatementAnalysisQuery(FinancialStatementTableIntentRules.CanonicalizeV2(query));

    public static FinancialStatementAnalysisQuery BuildV2Query(string userMessage) =>
        BuildQuery(FinancialStatementTableIntentRules.CanonicalizeV2(userMessage)) with { UserMessage = userMessage };
}
