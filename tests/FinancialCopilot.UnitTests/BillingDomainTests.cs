using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Billing.Usage;

namespace FinancialCopilot.UnitTests;

public sealed class BillingDomainTests
{
    [Fact]
    public void OrganizationHybridAccount_UsesOnlyApprovedCreditLineForCapacity()
    {
        var accountId = Guid.NewGuid();
        var account = new CustomerAccount(
            accountId,
            Guid.NewGuid(),
            CustomerAccountType.Organization,
            BillingMode.Hybrid,
            new CreditLine(approvedLimit: 10, warningThreshold: 2));
        var wallet = new WalletSnapshot(accountId, Balance: 100, ReservedAmount: 4, DateTimeOffset.UtcNow);

        Assert.Equal(106, account.GetAvailableSpendingCapacity(wallet));
        Assert.True(account.CanReserve(wallet, 106));
        Assert.False(account.CanReserve(wallet, 106.01m));
    }

    [Fact]
    public void IndividualAccount_CannotHaveOverdraftOrCreditLine()
    {
        Assert.Throws<ArgumentException>(() => new CustomerAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CustomerAccountType.Individual,
            BillingMode.Hybrid,
            new CreditLine(approvedLimit: 10, warningThreshold: 2)));
    }

    [Fact]
    public void IndividualAccount_RejectsExecutionBeyondWalletCapacity()
    {
        var accountId = Guid.NewGuid();
        var account = new CustomerAccount(
            accountId,
            Guid.NewGuid(),
            CustomerAccountType.Individual,
            BillingMode.Prepaid);
        var wallet = new WalletSnapshot(accountId, Balance: 1, ReservedAmount: 0.2m, DateTimeOffset.UtcNow);

        Assert.False(account.CanReserve(wallet, 0.81m));
        Assert.True(account.CanReserve(wallet, 0.8m));
    }

    [Fact]
    public void Reservation_CanBeCommittedOnceWithinReservedAmount()
    {
        var reservation = new UsageReservation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "request-1",
            "AiQuery.Scanner",
            reservedCredits: 3,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(5));

        reservation.Commit(2.4m);

        Assert.Equal(UsageReservationStatus.Committed, reservation.Status);
        Assert.Equal(2.4m, reservation.CommittedCredits);
        Assert.Throws<InvalidOperationException>(() => reservation.Release());
    }

    [Fact]
    public void PartnerAttribution_DoesNotCreateAUserWalletIdentity()
    {
        var attribution = new PartnerUsageAttribution(" partner-user-123 ");

        Assert.Equal("partner-user-123", attribution.NormalizedExternalUserId);
    }

    [Fact]
    public void SubscriptionPlan_RejectsNegativeIncludedCredits()
    {
        var plan = new SubscriptionPlan("DIRECT_STARTER", "Starter", -1, "v1");

        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Validate());
    }

    [Fact]
    public void UsageChargeCalculator_UsesOperationBasedAndCachedPricing()
    {
        var policy = new PricingPolicy(
            "v1",
            new Dictionary<string, decimal> { ["AiQuery.Scanner"] = 2.4m },
            CachedMultiplier: 0.25m,
            ZeroChargeStatuses: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ValidationFailed" });
        var calculator = new OperationUsageChargeCalculator(
            new ConfiguredPricingPolicyProvider([policy]));

        var result = calculator.Calculate(new UsageChargeRequest(
            "AiQuery.Scanner",
            "v1",
            Cached: true,
            CompletionStatus: "Completed",
            UsageUnits: [],
            ProviderCosts: []));

        Assert.Equal(0.6m, result.CreditsCharged);
        Assert.True(result.Cached);
    }

    [Fact]
    public void UsageChargeCalculator_DoesNotChargeConfiguredFailedValidation()
    {
        var policy = new PricingPolicy(
            "v1",
            new Dictionary<string, decimal> { ["AiQuery.Scanner"] = 2.4m },
            CachedMultiplier: 0.25m,
            ZeroChargeStatuses: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ValidationFailed" });
        var calculator = new OperationUsageChargeCalculator(
            new ConfiguredPricingPolicyProvider([policy]));

        var result = calculator.Calculate(new UsageChargeRequest(
            "AiQuery.Scanner",
            "v1",
            Cached: false,
            CompletionStatus: "ValidationFailed",
            UsageUnits: [],
            ProviderCosts: []));

        Assert.Equal(0, result.CreditsCharged);
    }
}
