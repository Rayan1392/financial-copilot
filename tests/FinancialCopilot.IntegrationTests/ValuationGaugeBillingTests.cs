using FinancialCopilot.Application.AI.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

// Change «valuation-gauge-per-card-credits» T3 (AC-07, AC-08): a usable gauge answer settles at one credit per card in
// every UsageBasedCredits:Mode, whatever model calls the turn recorded; a replay settles once.

public abstract class ValuationGaugeBillingModeTests<TFactory>(TFactory factory) : IClassFixture<TFactory>
    where TFactory : UsageBasedCreditBillingApiFactory
{
    private readonly BillingHookHarness _billing = new(factory);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AnsweredCards_ChargeOneCreditEach(int cards)
    {
        var account = await _billing.CreateAccountAsync(balance: 50m);
        var correlationId = $"vg-cards-{Guid.NewGuid():N}";
        // A call the usage-based calculator would price far above three credits.
        _billing.RecordCall(correlationId, "premium-model", 200_000, 50_000);

        var handle = await EnsureAsync(await _billing.ReserveAsync(account, correlationId), cards);
        var usage = await _billing.FinalizeAsync(handle, ValuationGaugeBillingPolicy.AnsweredStatus(cards), "semantic:valuation_gauges");

        Assert.Equal(cards, usage!.CreditsCharged);
        Assert.Equal(50m - cards, _billing.ReadWallet(account.AccountId).Balance);
        Assert.Equal(0m, _billing.ReadWallet(account.AccountId).ReservedAmount);
        Assert.Equal($"ValuationGaugesAnswered.{cards}", _billing.ReadLedger(correlationId).CompletionStatus);
    }

    [Fact]
    public async Task Replay_ReturnsTheFirstSettlement()
    {
        var account = await _billing.CreateAccountAsync(balance: 50m);
        var correlationId = $"vg-replay-{Guid.NewGuid():N}";
        var handle = await EnsureAsync(await _billing.ReserveAsync(account, correlationId), 3);
        var status = ValuationGaugeBillingPolicy.AnsweredStatus(3);

        var first = await _billing.FinalizeAsync(handle, status);
        var second = await _billing.FinalizeAsync(handle, status);

        Assert.Equal(3m, first!.CreditsCharged);
        Assert.Equal(3m, second!.CreditsCharged);
        Assert.Single(_billing.ReadLedgerEntries(correlationId));
        Assert.Equal(47m, _billing.ReadWallet(account.AccountId).Balance);
    }

    [Fact]
    public async Task InsufficientBalance_FailsAtTheHold_AndReleasesIt()
    {
        var account = await _billing.CreateAccountAsync(balance: 2m);
        var handle = await _billing.ReserveAsync(account, $"vg-short-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<FinancialCopilot.Billing.InsufficientCreditException>(() => EnsureAsync(handle, 3));
        Assert.Equal(2m, _billing.ReadWallet(account.AccountId).Balance);
        Assert.Equal(0m, _billing.ReadWallet(account.AccountId).ReservedAmount);
    }

    private async Task<BillingReservationHandle> EnsureAsync(BillingReservationHandle handle, decimal credits)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBillingFacadeHook>()
            .EnsureReservedCapacityAsync(handle, credits, CancellationToken.None);
    }
}

public sealed class ValuationGaugeEnforcedBillingTests(UsageBasedCreditBillingApiFactory factory)
    : ValuationGaugeBillingModeTests<UsageBasedCreditBillingApiFactory>(factory);

public sealed class ValuationGaugeShadowBillingTests(UsageBasedCreditShadowApiFactory factory)
    : ValuationGaugeBillingModeTests<UsageBasedCreditShadowApiFactory>(factory);

public sealed class ValuationGaugeLegacyBillingTests(UsageBasedCreditLegacyApiFactory factory)
    : ValuationGaugeBillingModeTests<UsageBasedCreditLegacyApiFactory>(factory);
