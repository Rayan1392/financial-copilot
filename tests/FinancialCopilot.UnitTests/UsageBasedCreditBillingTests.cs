using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Infrastructure.AI.ModelProviders;
using FinancialCopilot.Infrastructure.Billing;

namespace FinancialCopilot.UnitTests;

// Feature 147: deterministic token counts and prices; one credit costs 0.01 USD.
public sealed class UsageBasedCreditBillingTests
{
    private static readonly UsageBasedCreditPolicy Policy = new(
        "usage-test",
        "USD",
        costPerCredit: 0.01m,
        pricingMultiplier: 1m,
        minimumCredits: 1,
        maximumCreditsPerQuestion: 5,
        missingUsageFallbackCredits: 2,
        [
            new ModelTokenPrice("TestProvider", "standard-model", 2.00m, 0.50m, 8.00m),
            new ModelTokenPrice("TestProvider", "premium-model", 10.00m, 1.00m, 40.00m)
        ]);

    private readonly UsageBasedCreditCalculator _calculator = new();

    [Fact]
    public void LowCostQuestion_ChargesTheMinimumOfOneCredit()
    {
        // 1,000 × 2 / 1M + 100 × 8 / 1M = 0.0028 USD = 0.28 credits.
        var charge = _calculator.Calculate(Policy, [Call("standard-model", 1_000, 100)]);

        Assert.Equal(1, charge.Credits);
        Assert.Equal(0.0028m, charge.ActualCost);
        Assert.Equal(0.28m, charge.RawCredits);
        Assert.False(charge.FallbackApplied);
    }

    [Fact]
    public void HigherCostQuestion_ChargesMultipleIntegerCredits()
    {
        // 10,000 × 2 / 1M + 1,000 × 8 / 1M = 0.028 USD = 2.8 credits.
        var charge = _calculator.Calculate(Policy, [Call("standard-model", 10_000, 1_000)]);

        Assert.Equal(3, charge.Credits);
        Assert.Equal(2.8m, charge.RawCredits);
    }

    [Fact]
    public void UsageAcrossSeveralModelCalls_IsAggregated()
    {
        // Three calls of 0.0028 USD each: 0.84 credits together, 0.28 each.
        var charge = _calculator.Calculate(Policy,
        [
            Call("standard-model", 1_000, 100),
            Call("standard-model", 1_000, 100),
            Call("standard-model", 1_000, 100)
        ]);

        Assert.Equal(3, charge.ModelCalls);
        Assert.Equal(0.0084m, charge.ActualCost);
        Assert.Equal(1, charge.Credits);

        var heavier = _calculator.Calculate(Policy,
        [
            Call("standard-model", 4_000, 500),
            Call("standard-model", 4_000, 500)
        ]);

        // 2 × (0.008 + 0.004) = 0.024 USD = 2.4 credits.
        Assert.Equal(3, heavier.Credits);
    }

    [Fact]
    public void DifferentModels_UseTheirOwnPrices()
    {
        var standard = _calculator.Calculate(Policy, [Call("standard-model", 2_000, 500)]);
        var premium = _calculator.Calculate(Policy, [Call("premium-model", 2_000, 500)]);
        var mixed = _calculator.Calculate(Policy, [Call("standard-model", 2_000, 500), Call("premium-model", 1_000, 200)]);

        Assert.Equal(0.008m, standard.ActualCost);   // 0.004 + 0.004
        Assert.Equal(0.04m, premium.ActualCost);     // 0.02 + 0.02
        Assert.Equal(0.026m, mixed.ActualCost);      // 0.008 + 0.01 + 0.008
        Assert.Equal(1, standard.Credits);
        Assert.Equal(4, premium.Credits);
        Assert.Equal(3, mixed.Credits);
    }

    [Theory]
    [InlineData(5_000, 1.00, 1)]
    [InlineData(5_050, 1.01, 2)]
    [InlineData(10_000, 2.00, 2)]
    [InlineData(10_001, 2.0002, 3)]
    public void RawCredits_AreRoundedUpToTheNextWholeCredit(int inputTokens, double expectedRaw, int expectedCredits)
    {
        var charge = _calculator.Calculate(Policy, [Call("standard-model", inputTokens, 0)]);

        Assert.Equal((decimal)expectedRaw, charge.RawCredits);
        Assert.Equal(expectedCredits, charge.Credits);
    }

    [Fact]
    public void CachedInputTokens_AreBilledAtTheCachedPriceAndNotAgainAsInput()
    {
        // 10,000 input of which 8,000 cached: 2,000 × 2 / 1M + 8,000 × 0.5 / 1M = 0.008 USD.
        var cached = _calculator.Calculate(Policy, [Call("standard-model", 10_000, 0, cachedInputTokens: 8_000)]);
        var uncached = _calculator.Calculate(Policy, [Call("standard-model", 10_000, 0)]);

        Assert.Equal(0.008m, cached.ActualCost);
        Assert.Equal(0.02m, uncached.ActualCost);
        Assert.Equal(1, cached.Credits);
        Assert.Equal(2, uncached.Credits);
    }

    [Fact]
    public void CachedTokensAboveTheReportedInput_MarkUsageIncomplete()
    {
        var charge = _calculator.Calculate(Policy, [Call("standard-model", 1_000, 0, cachedInputTokens: 5_000)]);

        Assert.False(charge.CostComplete);
        Assert.Equal(PricingConfidence.INCOMPLETE_USAGE, charge.PricingConfidence);
    }

    [Fact]
    public void Gpt56Luna_SeparatesCacheReadsAndBothWriteLifetimesWithoutDoubleCounting()
    {
        var policy = Gpt56Policy();
        var usage = new ModelCallUsage(
            "OpenAI", "gpt-5.6-luna", 12_000, 4_000, 100,
            CacheWriteInputTokens: 7_000,
            CacheWrite30mInputTokens: 3_000,
            CacheWrite12hInputTokens: 4_000);

        var charge = _calculator.Calculate(policy, [usage]);

        // Ordinary 1k × .20 + cached 4k × .02 + 30m 3k × .25 + 12h 4k × .50 + output 100 × 1.20.
        Assert.Equal(0.00315m, charge.ActualCost);
        Assert.True(charge.CostComplete);
        Assert.Equal(PricingConfidence.VERIFIED_PUBLIC_RATE, charge.PricingConfidence);
        Assert.False(charge.FallbackApplied);
    }

    [Fact]
    public void Gpt56Luna_MissingCacheWriteUsageIsIncompleteInsteadOfZeroCost()
    {
        var charge = _calculator.Calculate(Gpt56Policy(),
        [
            new ModelCallUsage("OpenAI", "gpt-5.6-luna", 10_000, 0, 100)
        ]);

        Assert.False(charge.CostComplete);
        Assert.Equal(PricingConfidence.INCOMPLETE_USAGE, charge.PricingConfidence);
        Assert.Equal(1, charge.UnmeasuredModelCalls);
        Assert.Equal(2, charge.Credits);
    }

    [Fact]
    public void Gpt56Luna_AppliesLongContextSurchargeOnlyToThatModelCall()
    {
        var longContext = _calculator.Calculate(Gpt56Policy(),
        [
            new ModelCallUsage("OpenAI", "gpt-5.6-luna", 272_001, 0, 100,
                CacheWriteInputTokens: 0)
        ]);
        var otherProvider = _calculator.Calculate(Gpt56Policy(),
        [
            new ModelCallUsage("Other", "ordinary", 272_001, 0, 100)
        ]);

        Assert.Equal(0.1089804m, longContext.ActualCost);
        Assert.Equal(PricingConfidence.VERIFIED_PUBLIC_RATE, longContext.PricingConfidence);
        Assert.Equal(0.0545202m, otherProvider.ActualCost);
        Assert.False(otherProvider.FallbackApplied);
    }

    [Fact]
    public void Gpt56Luna_RejectsInconsistentCacheWriteTotals()
    {
        var charge = _calculator.Calculate(Gpt56Policy(),
        [
            new ModelCallUsage("OpenAI", "gpt-5.6-luna", 10_000, 1_000, 100,
                CacheWriteInputTokens: 5_000,
                CacheWrite30mInputTokens: 2_000,
                CacheWrite12hInputTokens: 2_000)
        ]);

        Assert.False(charge.CostComplete);
        Assert.Equal(PricingConfidence.INCOMPLETE_USAGE, charge.PricingConfidence);

        var contradictoryZero = _calculator.Calculate(Gpt56Policy(),
        [
            new ModelCallUsage("OpenAI", "gpt-5.6-luna", 10_000, 1_000, 100,
                CacheWriteInputTokens: 0,
                CacheWrite30mInputTokens: 1)
        ]);
        Assert.False(contradictoryZero.CostComplete);
        Assert.Equal(PricingConfidence.INCOMPLETE_USAGE, contradictoryZero.PricingConfidence);
    }

    [Fact]
    public void UsageSummary_PreservesUnknownCacheWriteTotalsInsteadOfSummingThemAsZero()
    {
        var summary = AiExecutionUsageSummary.From(
        [
            Facts("turn-write", "gpt-5.6-luna", 10_000, 100) with
            {
                CacheWriteInputTokens = 2_000,
                CacheWrite30mInputTokens = 2_000,
                CacheWrite12hInputTokens = 0
            },
            Facts("turn-write", "other", 1_000, 20)
        ]);

        Assert.NotNull(summary);
        Assert.Null(summary.CacheWriteInputTokens);
        Assert.Null(summary.CacheWrite30mInputTokens);
        Assert.Null(summary.CacheWrite12hInputTokens);
    }

    [Fact]
    public void MissingTokenUsage_ChargesTheDocumentedFallbackWithoutInventingTokens()
    {
        var charge = _calculator.Calculate(Policy,
        [
            Call("standard-model", 1_000, 100),
            new ModelCallUsage("TestProvider", "standard-model", InputTokens: null, CachedInputTokens: null, OutputTokens: null)
        ]);

        Assert.True(charge.FallbackApplied);
        Assert.Equal(1, charge.UnmeasuredModelCalls);
        Assert.Equal(0.0028m, charge.ActualCost);
        Assert.Equal(2, charge.Credits);
    }

    [Fact]
    public void MeasuredUsageAboveTheFallback_IsStillCharged()
    {
        var charge = _calculator.Calculate(Policy,
        [
            Call("premium-model", 2_000, 500),
            Call("unpriced-model", 1_000, 100)
        ]);

        Assert.True(charge.FallbackApplied);
        Assert.Equal(4, charge.Credits);
    }

    [Fact]
    public void UnpricedModel_UsesAProviderReportedCostOnlyInThePolicyCurrency()
    {
        var reported = _calculator.Calculate(Policy,
            [new ModelCallUsage("Other", "unpriced", 1_000, null, 100, ProviderReportedCost: 0.025m, ProviderReportedCurrency: "usd")]);
        var foreign = _calculator.Calculate(Policy,
            [new ModelCallUsage("Other", "unpriced", 1_000, null, 100, ProviderReportedCost: 25m, ProviderReportedCurrency: "IRR")]);

        Assert.False(reported.FallbackApplied);
        Assert.Equal(3, reported.Credits);
        Assert.True(foreign.FallbackApplied);
        Assert.Equal(2, foreign.Credits);
    }

    [Fact]
    public void ChargeAboveTheMaximum_IsCappedPerQuestion()
    {
        var charge = _calculator.Calculate(Policy, [Call("premium-model", 50_000, 10_000)]);

        Assert.True(charge.CappedAtMaximum);
        Assert.Equal(5, charge.Credits);
    }

    [Fact]
    public void QuestionWithoutModelCalls_ChargesTheMinimum()
    {
        var charge = _calculator.Calculate(Policy, []);

        Assert.Equal(1, charge.Credits);
        Assert.Equal(0, charge.ModelCalls);
        Assert.False(charge.FallbackApplied);
    }

    [Fact]
    public void PricingMultiplier_ScalesTheCostBeforeRounding()
    {
        var policy = new UsageBasedCreditPolicy(
            "usage-test", "USD", 0.01m, 1.5m, 1, 5, 1, Policy.ModelPrices);

        // 0.0084 USD × 1.5 / 0.01 = 1.26 credits.
        var charge = new UsageBasedCreditCalculator().Calculate(policy,
            [Call("standard-model", 1_000, 100), Call("standard-model", 1_000, 100), Call("standard-model", 1_000, 100)]);

        Assert.Equal(1.26m, charge.RawCredits);
        Assert.Equal(2, charge.Credits);
    }

    [Theory]
    [InlineData(0, 1, 1, 5, 1)]
    [InlineData(0.01, 0, 1, 5, 1)]
    [InlineData(0.01, 1, 0, 5, 1)]
    [InlineData(0.01, 1, 3, 2, 2)]
    [InlineData(0.01, 1, 1, 5, 6)]
    public void InvalidPolicy_IsRejected(double costPerCredit, double multiplier, int minimum, int maximum, int fallback)
    {
        Assert.ThrowsAny<ArgumentException>(() => new UsageBasedCreditPolicy(
            "usage-test", "USD", (decimal)costPerCredit, (decimal)multiplier, minimum, maximum, fallback, []));
    }

    [Fact]
    public void ConfigurationOptions_ValidateOnlyWhenUsageBillingIsActive()
    {
        var legacy = new UsageBasedCreditBillingOptions { CostPerCredit = 0 };
        var enforcedWithoutOperations = new UsageBasedCreditBillingOptions { Mode = UsageBasedBillingMode.Enforced };
        var enforcedWithoutVerifiedPrice = new UsageBasedCreditBillingOptions
        {
            Mode = UsageBasedBillingMode.Enforced,
            OperationCodes = ["AiQuery.Scanner"],
            ModelPrices =
            [
                new ModelTokenPriceOptions
                {
                    ProviderKey = "test-provider",
                    ModelKey = "test-model",
                    InputPerMillionTokens = 1m,
                    CachedInputPerMillionTokens = 0.5m,
                    OutputPerMillionTokens = 2m
                }
            ]
        };
        var enforced = new UsageBasedCreditBillingOptions
        {
            Mode = UsageBasedBillingMode.Enforced,
            OperationCodes = ["AiQuery.Scanner"],
            ModelPrices =
            [
                new ModelTokenPriceOptions
                {
                    ProviderKey = "test-provider",
                    ModelKey = "test-model",
                    InputPerMillionTokens = 1m,
                    CachedInputPerMillionTokens = 0.5m,
                    OutputPerMillionTokens = 2m,
                    PriceVerified = true,
                    PricingConfidence = PricingConfidence.VERIFIED_CONTRACT
                }
            ]
        };

        Assert.True(legacy.IsValid());
        Assert.False(legacy.AppliesTo("AiQuery.Scanner"));
        Assert.False(enforcedWithoutOperations.IsValid());
        Assert.False(enforcedWithoutVerifiedPrice.IsValid());
        Assert.True(enforced.IsValid());
        Assert.True(enforced.AppliesTo("aiquery.scanner"));
        Assert.False(enforced.AppliesTo("ProfessionalScanner.Metered"));
    }

    [Fact]
    public void PublicRates_AreAllowedForShadowModelingButNotEnforcedBilling()
    {
        var rate = new ModelTokenPriceOptions
        {
            ProviderKey = "OpenAI",
            ModelKey = "gpt-5.6-luna",
            InputPerMillionTokens = 0.20m,
            CachedInputPerMillionTokens = 0.02m,
            OutputPerMillionTokens = 1.20m,
            PricingConfidence = PricingConfidence.VERIFIED_PUBLIC_RATE,
            CacheWriteUsageRequired = true,
            CacheReadUsageRequired = true,
            CacheWrite30mInputMultiplier = 1.25m,
            CacheWrite12hInputMultiplier = 2.5m,
            LongContextThresholdInputTokens = 272_000,
            LongContextInputMultiplier = 2m,
            LongContextOutputMultiplier = 1.5m
        };
        var shadow = new UsageBasedCreditBillingOptions
        {
            Mode = UsageBasedBillingMode.Shadow,
            OperationCodes = ["AiQuery.Scanner"],
            ModelPrices = [rate]
        };
        var enforced = new UsageBasedCreditBillingOptions
        {
            Mode = UsageBasedBillingMode.Enforced,
            OperationCodes = ["AiQuery.Scanner"],
            ModelPrices = [rate]
        };
        var legacyFlagOnly = new UsageBasedCreditBillingOptions
        {
            Mode = UsageBasedBillingMode.Enforced,
            OperationCodes = ["AiQuery.Scanner"],
            ModelPrices =
            [
                new ModelTokenPriceOptions
                {
                    ProviderKey = rate.ProviderKey,
                    ModelKey = rate.ModelKey,
                    InputPerMillionTokens = rate.InputPerMillionTokens,
                    CachedInputPerMillionTokens = rate.CachedInputPerMillionTokens,
                    OutputPerMillionTokens = rate.OutputPerMillionTokens,
                    PriceVerified = true
                }
            ]
        };

        Assert.True(shadow.IsValid());
        Assert.Equal(PricingConfidence.VERIFIED_PUBLIC_RATE,
            Assert.Single(shadow.ToPolicy().ModelPrices).PricingConfidence);
        Assert.False(enforced.IsValid());
        Assert.False(legacyFlagOnly.IsValid());
    }

    [Fact]
    public void RequestFingerprint_IgnoresTraceAndDerivedServerContextOnRetry()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var first = new AiQueryRequest(
            "alert context snapshot A",
            tenantId,
            actorId,
            "trace-one",
            ConversationId: conversationId,
            OriginalUserMessage: "Summarize this alert",
            BillingOperationId: Guid.NewGuid());
        var retry = first with
        {
            Message = "alert context snapshot B",
            CorrelationId = "trace-two"
        };

        Assert.Equal(AiQueryRequestFingerprint.Compute(first), AiQueryRequestFingerprint.Compute(retry));
        Assert.NotEqual(
            AiQueryRequestFingerprint.Compute(first),
            AiQueryRequestFingerprint.Compute(retry with { OriginalUserMessage = "Explain this alert" }));
    }

    [Fact]
    public void Accumulator_ClaimsEachCompletedCallOnce_AndReplaysAClaimByKey()
    {
        var accumulator = new InMemoryAiExecutionUsageAccumulator();
        accumulator.Record(Facts("turn-1", "standard-model", 100, 10));
        accumulator.Record(Facts("turn-1", "standard-model", 50, 5) with { Status = AiExecutionStatus.InvalidStructuredOutput });

        var first = accumulator.ClaimUnbilled("turn-1", "turn-1:reservation");
        accumulator.Record(Facts("turn-1", "premium-model", 200, 20));
        var replay = accumulator.ClaimUnbilled("turn-1", "turn-1:reservation");
        var second = accumulator.ClaimUnbilled("turn-1", "turn-1:feature141:reservation");
        var exhausted = accumulator.ClaimUnbilled("turn-1", "another-reservation");

        Assert.Equal(100, Assert.Single(first).InputTokens);
        Assert.Equal(first, replay);
        Assert.Equal("premium-model", Assert.Single(second).ModelKey);
        Assert.Empty(exhausted);
        Assert.Empty(accumulator.ClaimUnbilled("unknown-turn", "unknown-turn:reservation"));
    }

    [Fact]
    public void Accumulator_CanPreflightUnclaimedUsageWithoutClaimingIt()
    {
        var accumulator = new InMemoryAiExecutionUsageAccumulator();
        accumulator.Record(Facts("turn-preflight", "standard-model", 100, 10));
        accumulator.Record(Facts("turn-preflight", "standard-model", 50, 5) with
        {
            Status = AiExecutionStatus.InvalidStructuredOutput
        });

        var preflight = accumulator.GetUnbilled("turn-preflight");
        var claim = accumulator.ClaimUnbilled("turn-preflight", "turn-preflight:reservation");

        Assert.Equal(100, Assert.Single(preflight).InputTokens);
        Assert.Equal(preflight, claim);
    }

    [Fact]
    public void UsageSummary_DoesNotAddProviderCostsInDifferentCurrencies()
    {
        var summary = AiExecutionUsageSummary.From(
        [
            Facts("turn-currency", "standard-model", 100, 10) with
            {
                ProviderReportedCost = 0.02m,
                ProviderReportedCurrency = "USD"
            },
            Facts("turn-currency", "premium-model", 200, 20) with
            {
                ProviderReportedCost = 40m,
                ProviderReportedCurrency = "IRR"
            }
        ]);

        Assert.NotNull(summary);
        Assert.Null(summary.ProviderReportedCost);
        Assert.Null(summary.ProviderReportedCurrency);
        Assert.Equal(300, summary.InputTokens);
        Assert.Equal(30, summary.OutputTokens);
    }

    [Fact]
    public void Accumulator_SummaryListsEachModelOnceAndSumsTokens()
    {
        var accumulator = new InMemoryAiExecutionUsageAccumulator();
        accumulator.Record(Facts("turn-2", "standard-model", 100, 10));
        accumulator.Record(Facts("turn-2", "premium-model", 200, 20));
        accumulator.Record(Facts("turn-2", "standard-model", 300, 30));

        var summary = accumulator.GetSummary("turn-2");

        Assert.NotNull(summary);
        Assert.Equal("TestProvider", summary.ProviderKey);
        Assert.Equal("standard-model,premium-model", summary.ModelKey);
        Assert.Equal(600, summary.InputTokens);
        Assert.Equal(60, summary.OutputTokens);
        Assert.Equal(660, summary.TotalTokens);
    }

    [Fact]
    public void Accumulator_EvictsTurnsUntouchedBeyondTheirLifetime()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-10-29T10:00:00Z"));
        var accumulator = new InMemoryAiExecutionUsageAccumulator(clock);
        accumulator.Record(Facts("stale-turn", "standard-model", 100, 10));

        clock.Advance(TimeSpan.FromMinutes(31));
        accumulator.Record(Facts("fresh-turn", "standard-model", 100, 10));

        Assert.Null(accumulator.GetSummary("stale-turn"));
        Assert.NotNull(accumulator.GetSummary("fresh-turn"));
    }

    private static ModelCallUsage Call(string model, int inputTokens, int outputTokens, int? cachedInputTokens = null) =>
        new("TestProvider", model, inputTokens, cachedInputTokens, outputTokens);

    private static UsageBasedCreditPolicy Gpt56Policy() => new(
        "usage-test-gpt56",
        "USD",
        0.0125m,
        1m,
        1,
        5,
        2,
        [
            new ModelTokenPrice(
                "OpenAI", "gpt-5.6-luna", 0.20m, 0.02m, 1.20m,
                CacheWrite30mInputMultiplier: 1.25m,
                CacheWrite12hInputMultiplier: 2.5m,
                LongContextThresholdInputTokens: 272_000,
                LongContextInputMultiplier: 2m,
                LongContextOutputMultiplier: 1.5m,
                CacheWriteUsageRequired: true,
                CacheReadUsageRequired: true,
                PricingConfidence: PricingConfidence.VERIFIED_PUBLIC_RATE),
            new ModelTokenPrice("Other", "ordinary", 0.20m, 0.02m, 1.20m)
        ]);

    private static AiExecutionUsageFacts Facts(string correlationId, string model, int inputTokens, int outputTokens) =>
        new(correlationId, "TestProvider", model, AiExecutionStatus.Completed, TimeSpan.Zero, 1, inputTokens, outputTokens);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
