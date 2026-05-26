namespace FinancialCopilot.Billing.Pricing;

public sealed record UsageUnit(string Code, decimal Quantity);

public sealed record ComputeCost(decimal Amount, string Unit);

public sealed record OperationCost(string OperationCode, decimal Credits);

public sealed record ProviderCost(string ProviderKey, string ModelKey, decimal? Amount, string? Currency);

public sealed record PricingPolicy(
    string Version,
    IReadOnlyDictionary<string, decimal> OperationCredits,
    decimal CachedMultiplier,
    IReadOnlySet<string> ZeroChargeStatuses);

public sealed record UsageChargeRequest(
    string OperationCode,
    string PricingPolicyVersion,
    bool Cached,
    string CompletionStatus,
    IReadOnlyCollection<UsageUnit> UsageUnits,
    IReadOnlyCollection<ProviderCost> ProviderCosts);

public sealed record UsageChargeResult(
    decimal CreditsCharged,
    string PricingPolicyVersion,
    bool Cached);
