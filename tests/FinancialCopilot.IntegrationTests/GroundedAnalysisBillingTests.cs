using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

// Change «sol-deep-analysis-web-search» T04 (FR-11, AC-1 … AC-3, AC-5, AC-6, AC-11): an answered analysis settles at
// its fixed tier amount in every billing mode; every non-answer is free; a cancelled offer is claimed at zero even
// with a zero balance; an unlimited account loses nothing but still writes the reservation key.

public sealed class GroundedAnalysisEnforcedBillingTests(GroundedAnalysisEnforcedBillingApiFactory factory)
    : IClassFixture<GroundedAnalysisEnforcedBillingApiFactory>
{
    private readonly BillingHookHarness _billing = new(factory);

    [Theory]
    [InlineData(AnalysisDepth.Simple, false, 8)]
    [InlineData(AnalysisDepth.Simple, true, 12)]
    [InlineData(AnalysisDepth.Deep, false, 16)]
    [InlineData(AnalysisDepth.Deep, true, 25)]
    public async Task AnsweredTier_ChargesTheFixedAmount_EvenInEnforcedMode(AnalysisDepth depth, bool web, int credits)
    {
        var account = await _billing.CreateAccountAsync(balance: 100m);
        var correlationId = $"ga-tier-{Guid.NewGuid():N}";
        // An expensive call that the usage-based calculator would price far above the tier (AC-1, AC-5).
        _billing.RecordCall(correlationId, "premium-model", 200_000, 50_000);

        var handle = await _billing.ReserveAsync(account, correlationId);
        handle = await EnsureAsync(handle, credits);
        var usage = await _billing.FinalizeAsync(
            handle, GroundedAnalysisBillingPolicy.AnsweredStatus(new AnalysisChoice(depth, web)), "report_discussion");

        Assert.Equal(credits, handle.ReservedCredits);
        Assert.Equal(credits, usage!.CreditsCharged);
        Assert.Equal(100m - credits, _billing.ReadWallet(account.AccountId).Balance);
        Assert.Equal(0m, _billing.ReadWallet(account.AccountId).ReservedAmount);
        Assert.Equal($"GroundedAnalysisAnswered.{depth}{(web ? "Web" : "NoWeb")}", _billing.ReadLedger(correlationId).CompletionStatus);
    }

    [Fact]
    public async Task AnsweredTier_IgnoresTokenAndSearchCounts()
    {
        var charges = new List<decimal>();
        foreach (var (input, output) in new[] { (100, 10), (9_000, 2_500), (60_000, 8_000) })
        {
            var account = await _billing.CreateAccountAsync(balance: 100m);
            var correlationId = $"ga-tokens-{Guid.NewGuid():N}";
            _billing.RecordCall(correlationId, "standard-model", input, output);
            var handle = await EnsureAsync(await _billing.ReserveAsync(account, correlationId), 25);
            var usage = await _billing.FinalizeAsync(
                handle, GroundedAnalysisBillingPolicy.AnsweredStatus(new AnalysisChoice(AnalysisDepth.Deep, true)));
            charges.Add(usage!.CreditsCharged);
        }

        Assert.All(charges, charge => Assert.Equal(25m, charge));
    }

    [Theory]
    [InlineData("ProviderFailed")]
    [InlineData("NoData")]
    [InlineData("CancelledBeforeExecution")]
    public async Task NonAnswer_ChargesZero_AndReleasesTheHold(string status)
    {
        var account = await _billing.CreateAccountAsync(balance: 30m);
        var correlationId = $"ga-zero-{Guid.NewGuid():N}";
        var handle = await EnsureAsync(await _billing.ReserveAsync(account, correlationId), 25);

        var usage = await _billing.FinalizeAsync(handle, status);

        Assert.Equal(0m, usage!.CreditsCharged);
        Assert.Equal(30m, _billing.ReadWallet(account.AccountId).Balance);
        Assert.Equal(0m, _billing.ReadWallet(account.AccountId).ReservedAmount);
    }

    [Fact]
    public async Task InsufficientBalance_FailsBeforeTheModelCall()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var handle = await _billing.ReserveAsync(account, $"ga-short-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<FinancialCopilot.Billing.InsufficientCreditException>(() => EnsureAsync(handle, 16));
        Assert.Equal(0m, _billing.ReadWallet(account.AccountId).ReservedAmount);
    }

    [Fact]
    public async Task Replay_ReturnsTheFirstSettlement()
    {
        var account = await _billing.CreateAccountAsync(balance: 100m);
        var correlationId = $"ga-replay-{Guid.NewGuid():N}";
        var handle = await EnsureAsync(await _billing.ReserveAsync(account, correlationId), 16);
        var status = GroundedAnalysisBillingPolicy.AnsweredStatus(new AnalysisChoice(AnalysisDepth.Deep, false));

        var first = await _billing.FinalizeAsync(handle, status);
        var second = await _billing.FinalizeAsync(handle, status);

        Assert.Equal(16m, first!.CreditsCharged);
        Assert.Equal(16m, second!.CreditsCharged);
        Assert.Single(_billing.ReadLedgerEntries(correlationId));
        Assert.Equal(84m, _billing.ReadWallet(account.AccountId).Balance);
    }

    [Fact]
    public async Task OfferCancel_WithZeroBalance_IsClaimedAtZero_AndTheKeyIsTaken()
    {
        var account = await _billing.CreateAccountAsync(balance: 0m);
        var correlationId = $"ga-cancel-{Guid.NewGuid():N}";
        var offerOperationId = Guid.NewGuid();

        var handle = await _billing.ReserveAsync(account, correlationId, GroundedAnalysisBillingPolicy.OfferCancelOperationCode,
            billingOperationId: offerOperationId);
        var usage = await _billing.FinalizeAsync(handle, GroundedAnalysisBillingPolicy.OfferCancelledCompletionStatus);

        Assert.Equal(0m, handle.ReservedCredits);
        Assert.Equal(0m, usage!.CreditsCharged);
        Assert.Equal("Committed", _billing.ReadReservation(correlationId).Status);
        // A later confirm with the same offer key and another operation is refused by the unique key.
        await Assert.ThrowsAnyAsync<Exception>(() => _billing.ReserveAsync(
            account, $"ga-confirm-{Guid.NewGuid():N}", GroundedAnalysisBillingPolicy.AnalysisOperationCode,
            billingOperationId: offerOperationId));
    }

    [Fact]
    public async Task UnlimitedAccount_IsChargedNothing_AndTheReservationKeyIsWritten()
    {
        var account = await _billing.CreateAccountAsync(balance: 0m);
        await MakeUnlimitedAsync(account.AccountId);
        var correlationId = $"ga-max-{Guid.NewGuid():N}";

        var handle = await _billing.ReserveAsync(account, correlationId, GroundedAnalysisBillingPolicy.AnalysisOperationCode);
        handle = await EnsureAsync(handle, 25);
        var usage = await _billing.FinalizeAsync(
            handle, GroundedAnalysisBillingPolicy.AnsweredStatus(new AnalysisChoice(AnalysisDepth.Deep, true)));

        Assert.Equal("Unlimited", handle.QuotaMode);
        Assert.Equal(0m, _billing.ReadWallet(account.AccountId).Balance);
        Assert.Equal(0m, _billing.ReadWallet(account.AccountId).ReservedAmount);
        Assert.Equal("Committed", _billing.ReadReservation(correlationId).Status);
        Assert.NotNull(usage);
    }

    private async Task<FinancialCopilot.Application.AI.Orchestration.BillingReservationHandle> EnsureAsync(
        FinancialCopilot.Application.AI.Orchestration.BillingReservationHandle handle, decimal credits)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBillingFacadeHook>()
            .EnsureReservedCapacityAsync(handle, credits, CancellationToken.None);
    }

    private async Task MakeUnlimitedAsync(Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var account = await db.CustomerAccounts.SingleAsync(row => row.Id == accountId);
        account.ExternalSubscriptionManaged = true;
        account.SubscriptionPlanCode = "Max";
        account.SubscriptionEffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        account.SubscriptionEffectiveTo = DateTimeOffset.UtcNow.AddDays(30);
        db.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = accountId, IdentityKey = $"max:{Guid.NewGuid():N}",
            PlanCode = "Max", StartsAtUtc = DateTimeOffset.UtcNow.AddDays(-1), ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();
    }
}

public sealed class GroundedAnalysisLegacyBillingTests(GroundedAnalysisLegacyBillingApiFactory factory)
    : IClassFixture<GroundedAnalysisLegacyBillingApiFactory>
{
    private readonly BillingHookHarness _billing = new(factory);

    [Fact]
    public async Task LegacyMode_AnsweredTier_ChargesTheFixedAmount()
    {
        var account = await _billing.CreateAccountAsync(balance: 50m);
        var correlationId = $"ga-legacy-{Guid.NewGuid():N}";
        var handle = await _billing.ReserveAsync(account, correlationId, "AiQuery.FinancialStatement");
        using (var scope = factory.Services.CreateScope())
        {
            handle = await scope.ServiceProvider.GetRequiredService<IBillingFacadeHook>()
                .EnsureReservedCapacityAsync(handle, 12, CancellationToken.None);
        }

        var usage = await _billing.FinalizeAsync(
            handle, GroundedAnalysisBillingPolicy.AnsweredStatus(new AnalysisChoice(AnalysisDepth.Simple, true)));

        Assert.Equal(12m, usage!.CreditsCharged);
        Assert.Equal(38m, _billing.ReadWallet(account.AccountId).Balance);
    }
}

public sealed class GroundedAnalysisEnforcedBillingApiFactory : UsageBasedCreditBillingApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(GroundedAnalysisPricingConfiguration.Values));
    }
}

public sealed class GroundedAnalysisLegacyBillingApiFactory : UsageBasedCreditBillingApiFactory
{
    protected override string UsageBillingMode => "Legacy";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(GroundedAnalysisPricingConfiguration.Values));
    }
}

internal static class GroundedAnalysisPricingConfiguration
{
    public static Dictionary<string, string?> Values { get; } = new()
    {
        ["GroundedAnalysisPricing:Credits:SimpleNoWeb"] = "8",
        ["GroundedAnalysisPricing:Credits:SimpleWeb"] = "12",
        ["GroundedAnalysisPricing:Credits:DeepNoWeb"] = "16",
        ["GroundedAnalysisPricing:Credits:DeepWeb"] = "25"
    };
}
