namespace FinancialCopilot.Billing.Pricing;

public enum UsageBasedBillingMode
{
    // The operation-based policy charges; usage is not evaluated.
    Legacy,

    // The operation-based policy charges; the usage-based charge is computed and reported only.
    Shadow,

    // The usage-based charge is committed, bounded by the reserved credits.
    Enforced
}

// Identifies what evidence supports a token rate or whether usage is complete enough to price.
// Public provider prices are useful for Shadow modeling, but are not account contract rates.
public enum PricingConfidence
{
    VERIFIED_CONTRACT,
    VERIFIED_PUBLIC_RATE,
    UNVERIFIED,
    INCOMPLETE_USAGE
}

// Prices are quoted per one million tokens in the policy currency.
public sealed record ModelTokenPrice(
    string ProviderKey,
    string ModelKey,
    decimal InputPerMillionTokens,
    decimal CachedInputPerMillionTokens,
    decimal OutputPerMillionTokens,
    decimal CacheWrite30mInputMultiplier = 1m,
    decimal CacheWrite12hInputMultiplier = 1m,
    int? LongContextThresholdInputTokens = null,
    decimal LongContextInputMultiplier = 1m,
    decimal LongContextOutputMultiplier = 1m,
    bool CacheWriteUsageRequired = false,
    bool CacheReadUsageRequired = false,
    PricingConfidence PricingConfidence = PricingConfidence.UNVERIFIED);

public sealed record UsageBasedCreditPolicy
{
    public UsageBasedCreditPolicy(
        string version,
        string currency,
        decimal costPerCredit,
        decimal pricingMultiplier,
        int minimumCredits,
        int maximumCreditsPerQuestion,
        int missingUsageFallbackCredits,
        IReadOnlyCollection<ModelTokenPrice> modelPrices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (costPerCredit <= 0 || pricingMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(costPerCredit),
                "Cost per credit and pricing multiplier must be positive.");
        }

        if (minimumCredits < 1 || maximumCreditsPerQuestion < minimumCredits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCreditsPerQuestion),
                "Credits must satisfy 1 <= minimum <= maximum.");
        }

        if (missingUsageFallbackCredits < minimumCredits || missingUsageFallbackCredits > maximumCreditsPerQuestion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(missingUsageFallbackCredits),
                "Missing-usage fallback credits must lie between the minimum and maximum credits.");
        }

        if (modelPrices.Any(price =>
                string.IsNullOrWhiteSpace(price.ProviderKey) ||
                string.IsNullOrWhiteSpace(price.ModelKey) ||
                price.InputPerMillionTokens < 0 ||
                price.CachedInputPerMillionTokens < 0 ||
                price.OutputPerMillionTokens < 0 ||
                price.CacheWrite30mInputMultiplier <= 0 ||
                price.CacheWrite12hInputMultiplier <= 0 ||
                price.LongContextThresholdInputTokens is <= 0 ||
                price.LongContextInputMultiplier <= 0 ||
                price.LongContextOutputMultiplier <= 0))
        {
            throw new ArgumentException(
                "Model prices require provider and model keys and non-negative token prices.",
                nameof(modelPrices));
        }

        Version = version.Trim();
        Currency = currency.Trim();
        CostPerCredit = costPerCredit;
        PricingMultiplier = pricingMultiplier;
        MinimumCredits = minimumCredits;
        MaximumCreditsPerQuestion = maximumCreditsPerQuestion;
        MissingUsageFallbackCredits = missingUsageFallbackCredits;
        ModelPrices = modelPrices;
    }

    public string Version { get; }

    public string Currency { get; }

    public decimal CostPerCredit { get; }

    public decimal PricingMultiplier { get; }

    public int MinimumCredits { get; }

    public int MaximumCreditsPerQuestion { get; }

    public int MissingUsageFallbackCredits { get; }

    public IReadOnlyCollection<ModelTokenPrice> ModelPrices { get; }

    public ModelTokenPrice? FindPrice(string providerKey, string modelKey) =>
        ModelPrices.FirstOrDefault(price =>
            string.Equals(price.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(price.ModelKey, modelKey, StringComparison.OrdinalIgnoreCase));
}

// One provider-reported model call. CachedInputTokens is the prompt-cache part of
// InputTokens, as hosted providers report it; it is never added on top of InputTokens.
public sealed record ModelCallUsage(
    string ProviderKey,
    string ModelKey,
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens,
    decimal? ProviderReportedCost = null,
    string? ProviderReportedCurrency = null,
    int? CacheWriteInputTokens = null,
    int? CacheWrite30mInputTokens = null,
    int? CacheWrite12hInputTokens = null);

public sealed record UsageBasedCreditCharge(
    int Credits,
    decimal ActualCost,
    decimal RawCredits,
    int ModelCalls,
    int UnmeasuredModelCalls,
    bool FallbackApplied,
    bool CappedAtMaximum,
    bool CostComplete = true,
    PricingConfidence PricingConfidence = PricingConfidence.UNVERIFIED);
