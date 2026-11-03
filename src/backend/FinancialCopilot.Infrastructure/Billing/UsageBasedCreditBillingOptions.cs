using FinancialCopilot.Billing.Pricing;

namespace FinancialCopilot.Infrastructure.Billing;

// Feature 147: usage-based AI credit billing for chat turns. Legacy keeps the operation-based
// charge; Shadow computes the usage-based charge alongside it; Enforced commits it.
public sealed class UsageBasedCreditBillingOptions
{
    public const string SectionName = "Billing:UsageBasedCredits";

    public UsageBasedBillingMode Mode { get; set; } = UsageBasedBillingMode.Legacy;

    public string PolicyVersion { get; set; } = "usage-v1";

    public string Currency { get; set; } = "USD";

    public decimal CostPerCredit { get; set; } = 0.0125m;

    public decimal PricingMultiplier { get; set; } = 1m;

    public int MinimumCredits { get; set; } = 1;

    public int MaximumCreditsPerQuestion { get; set; } = 5;

    public int MissingUsageFallbackCredits { get; set; } = 1;

    // Only these operations are charged by usage; every other operation keeps its operation price.
    // No code default: the configuration binder appends to a pre-populated list.
    public List<string> OperationCodes { get; set; } = [];

    public List<ModelTokenPriceOptions> ModelPrices { get; set; } = [];

    public bool AppliesTo(string operationCode) =>
        Mode != UsageBasedBillingMode.Legacy &&
        OperationCodes.Contains(operationCode, StringComparer.OrdinalIgnoreCase);

    public UsageBasedCreditPolicy ToPolicy() =>
        new(
            PolicyVersion,
            Currency,
            CostPerCredit,
            PricingMultiplier,
            MinimumCredits,
            MaximumCreditsPerQuestion,
            MissingUsageFallbackCredits,
            ModelPrices
                .Where(price => price.EffectivePricingConfidence is
                    PricingConfidence.VERIFIED_CONTRACT or PricingConfidence.VERIFIED_PUBLIC_RATE)
                .Select(price => new ModelTokenPrice(
                    price.ProviderKey,
                    price.ModelKey,
                    price.InputPerMillionTokens,
                    price.CachedInputPerMillionTokens,
                    price.OutputPerMillionTokens,
                    price.CacheWrite30mInputMultiplier,
                    price.CacheWrite12hInputMultiplier,
                    price.LongContextThresholdInputTokens,
                    price.LongContextInputMultiplier,
                    price.LongContextOutputMultiplier,
                    price.CacheWriteUsageRequired,
                    price.CacheReadUsageRequired,
                    price.EffectivePricingConfidence))
                .ToArray());

    public bool IsValid()
    {
        if (Mode == UsageBasedBillingMode.Legacy)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(PolicyVersion) || PolicyVersion.Trim().Length > 64 ||
            OperationCodes.Count == 0 || OperationCodes.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        if (Mode == UsageBasedBillingMode.Enforced &&
            (ModelPrices.Count == 0 || ModelPrices.Any(price =>
                price.EffectivePricingConfidence != PricingConfidence.VERIFIED_CONTRACT)))
        {
            return false;
        }

        try
        {
            ToPolicy();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public sealed class ModelTokenPriceOptions
{
    public string ProviderKey { get; set; } = string.Empty;

    public string ModelKey { get; set; } = string.Empty;

    public decimal InputPerMillionTokens { get; set; }

    public decimal CachedInputPerMillionTokens { get; set; }

    public decimal OutputPerMillionTokens { get; set; }

    // A numeric value is not an approved tariff. Set only after confirming the exact provider
    // model identifier and tariff (or authorized contract rate); the safe default is unverified.
    public bool PriceVerified { get; set; }

    // An explicit confidence is required to distinguish public reference rates from account
    // contract rates. The legacy boolean alone can establish at most a verified public rate.
    public PricingConfidence PricingConfidence { get; set; } = PricingConfidence.UNVERIFIED;

    public PricingConfidence EffectivePricingConfidence => this.PricingConfidence !=
        global::FinancialCopilot.Billing.Pricing.PricingConfidence.UNVERIFIED
        ? this.PricingConfidence
        : PriceVerified
            ? global::FinancialCopilot.Billing.Pricing.PricingConfidence.VERIFIED_PUBLIC_RATE
            : global::FinancialCopilot.Billing.Pricing.PricingConfidence.UNVERIFIED;

    public decimal CacheWrite30mInputMultiplier { get; set; } = 1m;

    public decimal CacheWrite12hInputMultiplier { get; set; } = 1m;

    public bool CacheWriteUsageRequired { get; set; }

    public bool CacheReadUsageRequired { get; set; }

    public int? LongContextThresholdInputTokens { get; set; }

    public decimal LongContextInputMultiplier { get; set; } = 1m;

    public decimal LongContextOutputMultiplier { get; set; } = 1m;
}
