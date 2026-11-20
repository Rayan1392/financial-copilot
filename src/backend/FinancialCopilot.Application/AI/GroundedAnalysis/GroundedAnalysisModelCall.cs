using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Application.AI.GroundedAnalysis;

/// <summary>
/// The one owner of how an analysis call (162 and 163) is sent (design-proposal §9, §10): the profile's model on its
/// provider only, its reasoning effort and output budget, structured output, and the hosted web search tool only when
/// the user's switch is on (PO-5). No retry; the caller applies <see cref="GroundedAnalysisProfile.TimeoutSeconds"/>.
/// </summary>
public static class GroundedAnalysisModelCall
{
    public static AiModelSelectionRequest Selection(
        GroundedAnalysisProfile profile, bool webSearch, Guid tenantId, string correlationId) =>
        new(
            tenantId,
            AiWorkloadKind.ExplanationGeneration,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput |
            (webSearch ? AiModelCapability.WebSearch : AiModelCapability.None),
            correlationId,
            RequiredProviderKey: profile.ProviderKey);

    public static AiModelRequest Request(
        GroundedAnalysisProfile profile,
        GroundedAnalysisWebSearchOptions webOptions,
        bool webSearch,
        AiStructuredOutputContract contract,
        string systemPrompt,
        string userContent,
        Guid tenantId,
        string correlationId) =>
        new(
            correlationId,
            tenantId,
            AiWorkloadKind.ExplanationGeneration,
            [
                new AiConversationMessage(AiMessageRole.System, systemPrompt),
                new AiConversationMessage(AiMessageRole.User, userContent)
            ],
            StructuredOutput: contract,
            MaxOutputTokens: profile.MaxOutputTokens,
            ModelKeyOverride: profile.Model,
            ReasoningEffort: profile.ReasoningEffort,
            WebSearch: webSearch ? WebSearchFor(profile, webOptions) : null);

    /// <summary>The hosted search options of one analysis: the profile's call cap and the configured search settings.</summary>
    public static AiWebSearchOptions WebSearchFor(GroundedAnalysisProfile profile, GroundedAnalysisWebSearchOptions options) =>
        new(
            profile.MaxWebSearchCalls,
            options.SearchContextSize,
            options.AllowedDomains.Count == 0 ? null : options.AllowedDomains.ToArray(),
            string.IsNullOrWhiteSpace(options.UserLocationCountry) ? null : options.UserLocationCountry);
}
