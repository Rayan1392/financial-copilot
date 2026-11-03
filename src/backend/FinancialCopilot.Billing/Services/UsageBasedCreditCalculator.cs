using FinancialCopilot.Billing.Pricing;

namespace FinancialCopilot.Billing.Services;

// Converts measured model usage into an integer credit charge. Provider-specific cache and
// context multipliers live on each model price rather than being applied to all providers.
//   RawCredits = ActualCost × PricingMultiplier / CostPerCredit
//   Credits    = clamp(Ceiling(RawCredits), MinimumCredits, MaximumCreditsPerQuestion)
// Incomplete calls are retained as observations, excluded from complete cost conclusions, and
// trigger MissingUsageFallbackCredits instead of being priced as zero.
public sealed class UsageBasedCreditCalculator
{
    private const decimal TokensPerPriceUnit = 1_000_000m;

    // Removes decimal division artifacts (for example 2.0000000000000000000000000001)
    // before Ceiling; a real overage of one millionth of a credit or more still rounds up.
    private const int RawCreditPrecision = 6;

    public UsageBasedCreditCharge Calculate(
        UsageBasedCreditPolicy policy,
        IReadOnlyCollection<ModelCallUsage> calls)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(calls);

        var actualCost = 0m;
        var unmeasuredCalls = 0;
        var costComplete = true;
        var confidence = PricingConfidence.VERIFIED_CONTRACT;

        foreach (var call in calls)
        {
            var result = TryPrice(policy, call);
            confidence = LessCertain(confidence, result.Confidence);
            if (result.Cost is { } callCost)
            {
                actualCost += callCost;
            }
            else
            {
                unmeasuredCalls++;
            }

            costComplete &= result.CostComplete;
        }

        var rawCredits = decimal.Round(
            actualCost * policy.PricingMultiplier / policy.CostPerCredit,
            RawCreditPrecision,
            MidpointRounding.AwayFromZero);
        var credits = Math.Max(policy.MinimumCredits, (int)Math.Min(decimal.Ceiling(rawCredits), int.MaxValue));
        var fallbackApplied = unmeasuredCalls > 0;

        if (fallbackApplied)
        {
            credits = Math.Max(credits, policy.MissingUsageFallbackCredits);
        }

        var capped = credits > policy.MaximumCreditsPerQuestion;

        return new UsageBasedCreditCharge(
            capped ? policy.MaximumCreditsPerQuestion : credits,
            actualCost,
            rawCredits,
            calls.Count,
            unmeasuredCalls,
            fallbackApplied,
            capped,
            costComplete,
            calls.Count == 0 ? PricingConfidence.UNVERIFIED : confidence);
    }

    private static PriceResult TryPrice(UsageBasedCreditPolicy policy, ModelCallUsage call)
    {
        var price = policy.FindPrice(call.ProviderKey, call.ModelKey);

        if (price is null)
        {
            // A provider-reported charge is a known cost only in the policy currency.
            if (call.ProviderReportedCost is >= 0 &&
                   string.Equals(call.ProviderReportedCurrency, policy.Currency, StringComparison.OrdinalIgnoreCase)
                )
            {
                return new PriceResult(call.ProviderReportedCost, true, PricingConfidence.UNVERIFIED);
            }

            return new PriceResult(null, false, PricingConfidence.UNVERIFIED);
        }

        if (call.InputTokens is not >= 0 || call.OutputTokens is not >= 0)
        {
            return new PriceResult(null, false, PricingConfidence.INCOMPLETE_USAGE);
        }

        var inputTokens = call.InputTokens.Value;
        var outputTokens = call.OutputTokens.Value;
        var writesRequired = price.CacheWriteUsageRequired ||
            price.CacheWrite30mInputMultiplier != 1m || price.CacheWrite12hInputMultiplier != 1m;
        if ((writesRequired || price.CacheReadUsageRequired) && call.CachedInputTokens is not >= 0)
        {
            return new PriceResult(null, false, PricingConfidence.INCOMPLETE_USAGE);
        }

        if (!TryResolveCacheWrites(call, price, out long write30m, out long write12h, out long totalWrites))
        {
            return new PriceResult(null, false, PricingConfidence.INCOMPLETE_USAGE);
        }

        var cachedTokens = call.CachedInputTokens ?? 0;
        if (cachedTokens < 0 || cachedTokens + totalWrites > inputTokens)
        {
            return new PriceResult(null, false, PricingConfidence.INCOMPLETE_USAGE);
        }

        var ordinaryInputTokens = inputTokens - cachedTokens - totalWrites;
        var inputMultiplier = price.LongContextThresholdInputTokens is { } threshold && inputTokens > threshold
            ? price.LongContextInputMultiplier
            : 1m;
        var outputMultiplier = price.LongContextThresholdInputTokens is { } outputThreshold && inputTokens > outputThreshold
            ? price.LongContextOutputMultiplier
            : 1m;

        var inputCost = ((decimal)ordinaryInputTokens * price.InputPerMillionTokens +
                (decimal)cachedTokens * price.CachedInputPerMillionTokens +
                (decimal)write30m * price.InputPerMillionTokens * price.CacheWrite30mInputMultiplier +
                (decimal)write12h * price.InputPerMillionTokens * price.CacheWrite12hInputMultiplier) * inputMultiplier;
        var outputCost = (decimal)outputTokens * price.OutputPerMillionTokens * outputMultiplier;
        return new PriceResult(
            (inputCost + outputCost) / TokensPerPriceUnit,
            true,
            price.PricingConfidence);
    }

    private static bool TryResolveCacheWrites(
        ModelCallUsage call,
        ModelTokenPrice price,
        out long write30m,
        out long write12h,
        out long totalWrites)
    {
        write30m = 0;
        write12h = 0;
        totalWrites = 0;
        var anyWriteField = call.CacheWriteInputTokens.HasValue ||
            call.CacheWrite30mInputTokens.HasValue || call.CacheWrite12hInputTokens.HasValue;
        var writesRequired = price.CacheWriteUsageRequired ||
            price.CacheWrite30mInputMultiplier != 1m || price.CacheWrite12hInputMultiplier != 1m;

        if (!anyWriteField)
        {
            return !writesRequired;
        }

        if (call.CacheWriteInputTokens is < 0 || call.CacheWrite30mInputTokens is < 0 || call.CacheWrite12hInputTokens is < 0)
        {
            return false;
        }

        if (call.CacheWrite30mInputTokens is { } reported30m && call.CacheWrite12hInputTokens is { } reported12h)
        {
            write30m = reported30m;
            write12h = reported12h;
            totalWrites = write30m + write12h;
            return call.CacheWriteInputTokens is not { } reportedTotal || reportedTotal == totalWrites;
        }

        if (call.CacheWriteInputTokens is { } aggregate)
        {
            totalWrites = aggregate;
            if (aggregate == 0)
            {
                if (call.CacheWrite30mInputTokens is > 0 || call.CacheWrite12hInputTokens is > 0)
                {
                    return false;
                }

                return true;
            }

            // If both retention classes have the same price, the aggregate is sufficient for
            // monetary cost. Otherwise an aggregate cannot identify the correct rate.
            if (price.CacheWrite30mInputMultiplier == price.CacheWrite12hInputMultiplier)
            {
                write30m = aggregate;
                return true;
            }

            return false;
        }

        return false;
    }

    private static PricingConfidence LessCertain(PricingConfidence left, PricingConfidence right)
    {
        static int Rank(PricingConfidence value) => value switch
        {
            PricingConfidence.VERIFIED_CONTRACT => 0,
            PricingConfidence.VERIFIED_PUBLIC_RATE => 1,
            PricingConfidence.UNVERIFIED => 2,
            _ => 3
        };

        return Rank(left) >= Rank(right) ? left : right;
    }

    private sealed record PriceResult(decimal? Cost, bool CostComplete, PricingConfidence Confidence);
}
