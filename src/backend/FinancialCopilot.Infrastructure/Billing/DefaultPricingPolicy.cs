using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Billing.Pricing;

namespace FinancialCopilot.Infrastructure.Billing;

/// <summary>
/// The operation-based pricing policy the composition root registers. Kept as a factory so tests
/// can pin which completion statuses are free without starting the whole infrastructure.
/// </summary>
internal static class DefaultPricingPolicy
{
    public static PricingPolicy Create() => new(
        "v1",
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["AiQuery.Scanner"] = 1m,
            ["AiQuery.StockAnalysis"] = 3m,
            ["AiQuery.CachedResponse"] = 0.2m,
            ["AiQuery.FinancialComparison"] = 3m,
            ["AiQuery.DeepResearch"] = 15m,
            ["AiQuery.PortfolioAnalysis"] = 6m,
            ["AiQuery.CodalAnalysis"] = 8m,
            ["AiQuery.Summarization"] = 4m,
            ["AiQuery.PersonalDigest"] = 4m,
            ["AiQuery.Embeddings"] = 0.5m,
            ["AiQuery.RagSearch"] = 2m,
            ["AiQuery.BackgroundJob"] = 6m
        },
        CachedMultiplier: 0.2m,
        // A turn that gave the user no answer is free: rejected input, a clarification question,
        // a supported request with no usable rows, a provider failure, or a cancellation.
        ZeroChargeStatuses: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ValidationFailed",
            "ClarificationRequired",
            SemanticBillingCompletionStatus.NoData,
            // An answered turn, free by owner decision OQ-017: the fixed greeting and help reply.
            SemanticBillingCompletionStatus.InformationalReply,
            "ProviderFailed",
            "CancelledBeforeExecution",
            "TimedOutBeforeExecution"
        },
        CompletionMultipliers: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["PartiallyCompleted"] = 0.5m
        });
}
