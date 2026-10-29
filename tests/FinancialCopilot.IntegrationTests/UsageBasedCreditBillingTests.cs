using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Billing;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

// Feature 147: usage-based AI credit billing on the production chat billing composition
// (AiFacadeBillingHook, reservation authorization, finalization, wallet and ledger) with an
// in-memory billing store. One credit costs 0.01 USD; token counts and prices are deterministic.
public sealed class UsageBasedCreditBillingEndpointTests : IClassFixture<UsageBasedCreditBillingApiFactory>
{
    private readonly UsageBasedCreditBillingApiFactory _factory;

    public UsageBasedCreditBillingEndpointTests(UsageBasedCreditBillingApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task V2ScannerChat_EnforcedMode_ChargesIntegerCreditsFromEveryMeasuredModelCall()
    {
        var balanceBefore = ReadWallet(AiFacadeApiFactory.OrganizationAccountId).Balance;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = "P/E below 6" });
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        // The existing specialist scanner tool still answers the question.
        Assert.Equal("Scanner", document.RootElement.GetProperty("intent").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("scannerTable").GetProperty("rows").GetArrayLength());

        var usage = document.RootElement.GetProperty("usage");
        var credits = usage.GetProperty("creditsCharged").GetDecimal();
        var billingOperationId = Guid.Parse(response.Headers.GetValues("Idempotency-Key").Single());
        var ledger = Assert.Single(
            _factory.ReadUsageEntries(),
            entry => entry.IdempotencyKey == $"{billingOperationId:N}:charge");
        // Every completed fake model call reports 10 input tokens at 2,500 USD per million: 2.5 credits per call.
        // Failed attempts (the fake's invalid semantic-interpretation output) are not billed.
        var expectedCredits = Math.Min(5m, decimal.Ceiling(ledger.PromptTokens!.Value * 2_500m / 1_000_000m / 0.01m));

        Assert.True(ledger.PromptTokens >= 10, $"No measured model call was billed: {body}");
        Assert.Equal(expectedCredits, credits);
        Assert.True(credits > 1m);
        Assert.Equal(decimal.Truncate(credits), credits);
        Assert.Equal("usage-test-v1", usage.GetProperty("pricingPolicyVersion").GetString());
        Assert.Equal(JsonValueKind.Null, usage.GetProperty("estimatedCost").ValueKind);
        Assert.Equal(credits, ledger.CreditsCharged);
        Assert.Equal("usage-test-v1", ledger.PricingPolicyVersion);
        var wallet = ReadWallet(AiFacadeApiFactory.OrganizationAccountId);
        Assert.Equal(balanceBefore - credits, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
        Assert.Equal(wallet.Balance, usage.GetProperty("remainingSpendingCapacity").GetDecimal());
    }

    private WalletProjectionRow ReadWallet(Guid accountId)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<BillingDbContext>()
            .WalletProjections.AsNoTracking().Single(row => row.CustomerAccountId == accountId);
    }
}

public sealed class UsageBasedCreditBillingHookTests(UsageBasedCreditBillingApiFactory factory)
    : IClassFixture<UsageBasedCreditBillingApiFactory>
{
    private readonly BillingHookHarness _billing = new(factory);

    [Fact]
    public async Task LowCostAnswer_ChargesOneCredit()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var correlationId = NewCorrelationId();
        _billing.RecordCall(correlationId, "standard-model", 1_000, 100); // 0.0028 USD

        var handle = await _billing.ReserveAsync(account, correlationId);
        var usage = await _billing.FinalizeAsync(handle, "Completed");

        Assert.Equal(1m, usage!.CreditsCharged);
        Assert.Equal("usage-test-v1", usage.PricingPolicyVersion);
        Assert.Equal(9m, usage.RemainingSpendingCapacity);
        var ledger = _billing.ReadLedger(correlationId);
        Assert.Equal(1_000, ledger.PromptTokens);
        Assert.Equal(100, ledger.CompletionTokens);
        Assert.Equal("standard-model", ledger.ModelName);
    }

    [Fact]
    public async Task SmallerFinalCharge_ReleasesTheRestOfTheReservation()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var correlationId = NewCorrelationId();

        var handle = await _billing.ReserveAsync(account, correlationId);
        handle = await _billing.EnsureReservedCapacityAsync(handle);
        var held = _billing.ReadWallet(account.AccountId);
        _billing.RecordCall(correlationId, "standard-model", 2_000, 500);  // 0.008 USD
        _billing.RecordCall(correlationId, "premium-model", 1_000, 200);   // 0.018 USD
        var usage = await _billing.FinalizeAsync(handle, "Completed");

        Assert.Equal(5m, handle.ReservedCredits);
        Assert.Equal(5m, held.ReservedAmount);
        Assert.Equal(3m, usage!.CreditsCharged); // 2.6 raw credits
        var wallet = _billing.ReadWallet(account.AccountId);
        Assert.Equal(7m, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
        var reservation = _billing.ReadReservation(correlationId);
        Assert.Equal("Committed", reservation.Status);
        Assert.Equal(5m, reservation.ReservedCredits);
        Assert.Equal(3m, reservation.CommittedCredits);
        var ledger = _billing.ReadLedger(correlationId);
        Assert.Equal(3_000, ledger.PromptTokens);
        Assert.Equal(700, ledger.CompletionTokens);
        Assert.Equal("standard-model,premium-model", ledger.ModelName);
    }

    [Fact]
    public async Task InsufficientBalance_IsRefusedBeforeAnExpensiveExecution_AndReleasesTheInitialHold()
    {
        var empty = await _billing.CreateAccountAsync(balance: 0.5m);
        var low = await _billing.CreateAccountAsync(balance: 3.7m);

        await Assert.ThrowsAsync<InsufficientCreditException>(() => _billing.ReserveAsync(empty, NewCorrelationId()));
        var lowCorrelationId = NewCorrelationId();
        var lowHandle = await _billing.ReserveAsync(low, lowCorrelationId);
        Assert.Equal(1m, lowHandle.ReservedCredits);
        await Assert.ThrowsAsync<InsufficientCreditException>(() => _billing.EnsureReservedCapacityAsync(lowHandle));

        var emptyWallet = _billing.ReadWallet(empty.AccountId);
        Assert.Equal(0.5m, emptyWallet.Balance);
        Assert.Equal(0m, emptyWallet.ReservedAmount);
        var lowWallet = _billing.ReadWallet(low.AccountId);
        Assert.Equal(3.7m, lowWallet.Balance);
        Assert.Equal(0m, lowWallet.ReservedAmount);
        Assert.Empty(_billing.ReadLedgerEntries(lowCorrelationId));
    }

    [Fact]
    public async Task UsageAboveTheInitialMinimum_IsNeverExecutedOrSubsidizedWhenUpgradeFails()
    {
        var account = await _billing.CreateAccountAsync(balance: 2m);
        var correlationId = NewCorrelationId();

        var handle = await _billing.ReserveAsync(account, correlationId);
        await Assert.ThrowsAsync<InsufficientCreditException>(() => _billing.EnsureReservedCapacityAsync(handle));
        // The fake expensive route is deliberately never invoked after the failed upgrade.
        Assert.Null(_billing.ReadSummary(correlationId));

        var wallet = _billing.ReadWallet(account.AccountId);
        Assert.Equal(2m, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
        Assert.Empty(_billing.ReadLedgerEntries(correlationId));
    }

    [Fact]
    public async Task ReservationUpgrade_ChargesMeasuredUsageAndReleasesUnusedCredits()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var correlationId = NewCorrelationId();
        var handle = await _billing.ReserveAsync(account, correlationId);
        Assert.Equal(1m, handle.ReservedCredits);

        handle = await _billing.EnsureReservedCapacityAsync(handle);
        Assert.Equal(5m, handle.ReservedCredits);
        _billing.RecordCall(correlationId, "premium-model", 2_000, 500); // 0.04 USD = 4 credits
        var usage = await _billing.FinalizeAsync(handle, "Completed");

        Assert.Equal(4m, usage!.CreditsCharged);
        var wallet = _billing.ReadWallet(account.AccountId);
        Assert.Equal(6m, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
        var reservation = _billing.ReadReservation(correlationId);
        Assert.Equal(5m, reservation.ReservedCredits);
        Assert.Equal(4m, reservation.CommittedCredits);
    }

    [Fact]
    public async Task RepeatedFinalizationAndReservation_DoNotChargeTwice()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var correlationId = NewCorrelationId();
        var handle = await _billing.ReserveAsync(account, correlationId);
        handle = await _billing.EnsureReservedCapacityAsync(handle);
        _billing.RecordCall(correlationId, "premium-model", 1_000, 200); // 0.018 USD
        var first = await _billing.FinalizeAsync(handle, "Completed");
        _billing.RecordCall(correlationId, "premium-model", 1_000, 200); // a late call is not re-billed by the replay
        var replayed = await _billing.FinalizeAsync(handle, "Completed");
        var replayedHandle = await _billing.ReserveAsync(account, correlationId);

        Assert.Equal(2m, first!.CreditsCharged);
        Assert.Equal(first.CreditsCharged, replayed!.CreditsCharged);
        Assert.Equal(first.PromptTokens, replayed.PromptTokens);
        Assert.Equal(handle.ReservationId, replayedHandle.ReservationId);
        Assert.Equal(handle.ReservedCredits, replayedHandle.ReservedCredits);
        Assert.Single(_billing.ReadLedgerEntries(correlationId));
        var wallet = _billing.ReadWallet(account.AccountId);
        Assert.Equal(8m, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
    }

    [Fact]
    public async Task MissingTokenUsage_ChargesTheConfiguredFallback()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var correlationId = NewCorrelationId();
        _billing.RecordCall(correlationId, "standard-model", inputTokens: null, outputTokens: null);

        var handle = await _billing.ReserveAsync(account, correlationId);
        var usage = await _billing.FinalizeAsync(handle, "Completed");

        Assert.Equal(2m, usage!.CreditsCharged);
        Assert.Equal(8m, _billing.ReadWallet(account.AccountId).Balance);
    }

    [Theory]
    [InlineData("ClarificationRequired")]
    [InlineData("ValidationFailed")]
    [InlineData("ProviderFailed")]
    [InlineData("CancelledBeforeExecution")]
    public async Task UnsuccessfulOrUnsupportedOutcome_IsNotCharged(string completionStatus)
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var correlationId = NewCorrelationId();
        _billing.RecordCall(correlationId, "premium-model", 2_000, 500);

        var handle = await _billing.ReserveAsync(account, correlationId);
        var usage = await _billing.FinalizeAsync(handle, completionStatus);

        Assert.Equal(0m, usage!.CreditsCharged);
        var wallet = _billing.ReadWallet(account.AccountId);
        Assert.Equal(10m, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
    }

    [Fact]
    public async Task SemanticNoDataThenFeature141Fallback_SettlesTheSameReservationOnce()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var correlationId = NewCorrelationId();
        _billing.RecordCall(correlationId, "standard-model", 2_000, 500);  // semantic intent: 0.008 USD

        var turnHandle = await _billing.ReserveAsync(account, correlationId);
        // The semantic no-data/clarification result defers settlement while Feature 141 continues.
        turnHandle = await _billing.EnsureReservedCapacityAsync(turnHandle);
        _billing.RecordCall(correlationId, "premium-model", 1_000, 200);   // engine navigation: 0.018 USD
        var finalUsage = await _billing.FinalizeAsync(turnHandle, "Completed", executionRoute: "feature141_fallback");

        Assert.Equal(3m, finalUsage!.CreditsCharged); // 0.026 USD = 2.6 raw credits
        var ledger = _billing.ReadLedger(correlationId);
        Assert.Equal(3_000, ledger.PromptTokens);
        Assert.Equal(700, ledger.CompletionTokens);
        Assert.Equal(3m, Assert.Single(_billing.ReadLedgerEntries(correlationId)).CreditsCharged);
        var wallet = _billing.ReadWallet(account.AccountId);
        Assert.Equal(7m, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
    }

    [Fact]
    public async Task RetryWithStableBillingOperationIdAndDifferentCorrelation_ReplaysOneCharge()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var operationId = Guid.NewGuid();
        var firstCorrelation = NewCorrelationId();
        var firstHandle = await _billing.ReserveAsync(account, firstCorrelation, billingOperationId: operationId);
        _billing.RecordCall(firstCorrelation, "premium-model", 1_000, 200); // 0.018 USD
        firstHandle = await _billing.EnsureReservedCapacityAsync(firstHandle);
        var first = await _billing.FinalizeAsync(firstHandle, "Completed");

        var retryCorrelation = NewCorrelationId();
        var retryHandle = await _billing.ReserveAsync(account, retryCorrelation, billingOperationId: operationId);
        var retry = await _billing.FinalizeAsync(retryHandle, "Completed");

        Assert.Equal(2m, first!.CreditsCharged);
        Assert.Equal(first.CreditsCharged, retry!.CreditsCharged);
        var key = $"{operationId:N}:charge";
        Assert.Equal(key, retryHandle.ReservationId.Replace(":reservation", ":charge", StringComparison.Ordinal));
        Assert.Single(_billing.ReadLedgerEntriesByKey(key));
        Assert.Equal(8m, _billing.ReadWallet(account.AccountId).Balance);
        Assert.Equal(0m, _billing.ReadWallet(account.AccountId).ReservedAmount);
    }

    [Fact]
    public async Task OperationOutsideTheUsagePolicy_KeepsItsOperationPrice()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var correlationId = NewCorrelationId();
        _billing.RecordCall(correlationId, "premium-model", 2_000, 500);

        var handle = await _billing.ReserveAsync(account, correlationId, operationCode: "AiQuery.StockAnalysis");
        var usage = await _billing.FinalizeAsync(handle, "Completed");

        Assert.Equal(3m, handle.ReservedCredits);
        Assert.Equal(3m, usage!.CreditsCharged);
        Assert.Equal("v1", usage.PricingPolicyVersion);
    }

    private static string NewCorrelationId() => $"f147-{Guid.NewGuid():N}";
}

public sealed class UsageBasedCreditTransientPersistenceRetryTests(
    UsageBasedCreditTransientFailureApiFactory factory)
    : IClassFixture<UsageBasedCreditTransientFailureApiFactory>
{
    private readonly BillingHookHarness _billing = new(factory);

    [Fact]
    public async Task RetryAfterTransientPersistenceFailure_FinalizesTheClaimedUsageOnce()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var correlationId = $"f147-transient-{Guid.NewGuid():N}";
        _billing.RecordCall(correlationId, "premium-model", 1_000, 200); // 0.018 USD
        var handle = await _billing.ReserveAsync(account, correlationId);
        handle = await _billing.EnsureReservedCapacityAsync(handle);

        await Assert.ThrowsAsync<DbUpdateException>(() => _billing.FinalizeAsync(handle, "Completed"));
        Assert.Empty(_billing.ReadLedgerEntries(correlationId));

        var finalized = await _billing.FinalizeAsync(handle, "Completed");
        var replayed = await _billing.FinalizeAsync(handle, "Completed");

        Assert.Equal(2m, finalized!.CreditsCharged);
        Assert.Equal(2m, replayed!.CreditsCharged);
        Assert.Single(_billing.ReadLedgerEntries(correlationId));
        var wallet = _billing.ReadWallet(account.AccountId);
        Assert.Equal(8m, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
    }
}

public sealed class UsageBasedCreditShadowModeTests(UsageBasedCreditShadowApiFactory factory)
    : IClassFixture<UsageBasedCreditShadowApiFactory>
{
    private readonly BillingHookHarness _billing = new(factory);

    [Fact]
    public async Task ShadowMode_ChargesTheLegacyCredit_WhileEvaluatingTheUsageBasedCharge()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var correlationId = $"f147-shadow-{Guid.NewGuid():N}";
        _billing.RecordCall(correlationId, "premium-model", 2_000, 500); // proposes 4 credits

        var handle = await _billing.ReserveAsync(account, correlationId);
        var usage = await _billing.FinalizeAsync(handle, "Completed");

        Assert.Equal(1m, handle.ReservedCredits);
        Assert.Equal(1m, usage!.CreditsCharged);
        Assert.Equal("v1", usage.PricingPolicyVersion);
        Assert.Equal(9m, _billing.ReadWallet(account.AccountId).Balance);
        Assert.Equal(2_000, _billing.ReadLedger(correlationId).PromptTokens);
    }
}

public sealed class UsageBasedCreditShadowTelemetryFailureTests
    : IClassFixture<UsageBasedCreditShadowTelemetryFailureApiFactory>
{
    private readonly UsageBasedCreditShadowTelemetryFailureApiFactory _factory;

    public UsageBasedCreditShadowTelemetryFailureTests(UsageBasedCreditShadowTelemetryFailureApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task ShadowTelemetryFailure_DoesNotInterruptChatOrChangeLegacyCharge()
    {
        var balanceBefore = ReadWallet(AiFacadeApiFactory.OrganizationAccountId).Balance;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = "P/E below 6" });
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        Assert.Equal("Scanner", document.RootElement.GetProperty("intent").GetString());
        var usage = document.RootElement.GetProperty("usage");
        Assert.Equal(1m, usage.GetProperty("creditsCharged").GetDecimal());
        Assert.Equal("v1", usage.GetProperty("pricingPolicyVersion").GetString());

        var operationId = Guid.Parse(response.Headers.GetValues("Idempotency-Key").Single());
        var ledger = Assert.Single(
            _factory.ReadUsageEntries(),
            entry => entry.IdempotencyKey == $"{operationId:N}:charge");
        Assert.Equal(1m, ledger.CreditsCharged);

        var wallet = ReadWallet(AiFacadeApiFactory.OrganizationAccountId);
        Assert.Equal(balanceBefore - 1m, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
    }

    private WalletProjectionRow ReadWallet(Guid accountId)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<BillingDbContext>()
            .WalletProjections.AsNoTracking().Single(row => row.CustomerAccountId == accountId);
    }
}

public sealed class UsageBasedCreditLegacyModeTests(UsageBasedCreditLegacyApiFactory factory)
    : IClassFixture<UsageBasedCreditLegacyApiFactory>
{
    private readonly BillingHookHarness _billing = new(factory);

    [Fact]
    public async Task LegacyMode_ChargesOnlyTheOperationPriceAndDoesNotClaimUsage()
    {
        var account = await _billing.CreateAccountAsync(balance: 10m);
        var correlationId = $"f147-legacy-{Guid.NewGuid():N}";
        _billing.RecordCall(correlationId, "premium-model", 2_000, 500);

        var handle = await _billing.ReserveAsync(account, correlationId);
        var usage = await _billing.FinalizeAsync(handle, "Completed");

        Assert.Equal(1m, handle.ReservedCredits);
        Assert.Equal(1m, usage!.CreditsCharged);
        Assert.Equal("v1", usage.PricingPolicyVersion);
        Assert.Single(_billing.ReadUnbilled(correlationId));
        Assert.Equal(9m, _billing.ReadWallet(account.AccountId).Balance);
    }
}

public class UsageBasedCreditBillingApiFactory : ScannerExecutionApiFactory
{
    protected override bool ForceV1Orchestration => false;

    protected virtual string UsageBillingMode => "Enforced";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2",
            ["Billing:UsageBasedCredits:Mode"] = UsageBillingMode,
            ["Billing:UsageBasedCredits:PolicyVersion"] = "usage-test-v1",
            ["Billing:UsageBasedCredits:CostPerCredit"] = "0.01",
            ["Billing:UsageBasedCredits:PricingMultiplier"] = "1",
            ["Billing:UsageBasedCredits:MinimumCredits"] = "1",
            ["Billing:UsageBasedCredits:MaximumCreditsPerQuestion"] = "5",
            ["Billing:UsageBasedCredits:MissingUsageFallbackCredits"] = "2",
            // The V2 scanner fake reports 10 input tokens per call: 0.025 USD, 2.5 credits per call.
            ["Billing:UsageBasedCredits:ModelPrices:0:ProviderKey"] = "V2ScannerFake",
            ["Billing:UsageBasedCredits:ModelPrices:0:ModelKey"] = "fake-v2",
            ["Billing:UsageBasedCredits:ModelPrices:0:InputPerMillionTokens"] = "2500",
            ["Billing:UsageBasedCredits:ModelPrices:0:CachedInputPerMillionTokens"] = "0",
            ["Billing:UsageBasedCredits:ModelPrices:0:OutputPerMillionTokens"] = "0",
            ["Billing:UsageBasedCredits:ModelPrices:0:PricingConfidence"] = "VERIFIED_CONTRACT",
            ["Billing:UsageBasedCredits:ModelPrices:1:ProviderKey"] = BillingHookHarness.Provider,
            ["Billing:UsageBasedCredits:ModelPrices:1:ModelKey"] = "standard-model",
            ["Billing:UsageBasedCredits:ModelPrices:1:InputPerMillionTokens"] = "2",
            ["Billing:UsageBasedCredits:ModelPrices:1:CachedInputPerMillionTokens"] = "0.5",
            ["Billing:UsageBasedCredits:ModelPrices:1:OutputPerMillionTokens"] = "8",
            ["Billing:UsageBasedCredits:ModelPrices:1:PricingConfidence"] = "VERIFIED_CONTRACT",
            ["Billing:UsageBasedCredits:ModelPrices:2:ProviderKey"] = BillingHookHarness.Provider,
            ["Billing:UsageBasedCredits:ModelPrices:2:ModelKey"] = "premium-model",
            ["Billing:UsageBasedCredits:ModelPrices:2:InputPerMillionTokens"] = "10",
            ["Billing:UsageBasedCredits:ModelPrices:2:CachedInputPerMillionTokens"] = "1",
            ["Billing:UsageBasedCredits:ModelPrices:2:OutputPerMillionTokens"] = "40",
            ["Billing:UsageBasedCredits:ModelPrices:2:PricingConfidence"] = "VERIFIED_CONTRACT"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => new V2ScannerFakeAiModelClient());
        });
    }
}

public sealed class UsageBasedCreditShadowApiFactory : UsageBasedCreditBillingApiFactory
{
    protected override string UsageBillingMode => "Shadow";
}

public sealed class UsageBasedCreditShadowTelemetryFailureApiFactory : UsageBasedCreditBillingApiFactory
{
    protected override string UsageBillingMode => "Shadow";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiExecutionUsageAccumulator>();
            services.AddSingleton<IAiExecutionUsageAccumulator, ThrowingAiExecutionUsageAccumulator>();
        });
    }
}

internal sealed class ThrowingAiExecutionUsageAccumulator : IAiExecutionUsageAccumulator
{
    public void Record(AiExecutionUsageFacts facts) =>
        throw new InvalidOperationException("Simulated Shadow usage observation failure.");

    public AiExecutionUsageSummary? GetSummary(string correlationId) =>
        throw new InvalidOperationException("Simulated Shadow usage summary failure.");

    public IReadOnlyList<AiExecutionUsageFacts> ClaimUnbilled(string correlationId, string claimKey) =>
        throw new InvalidOperationException("Simulated Shadow usage claim failure.");
}

public sealed class UsageBasedCreditLegacyApiFactory : UsageBasedCreditBillingApiFactory
{
    protected override string UsageBillingMode => "Legacy";
}

public sealed class UsageBasedCreditTransientFailureApiFactory : UsageBasedCreditBillingApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<FinancialCopilot.Billing.Contracts.IUsageFinalizationService>();
            services.AddScoped<UsageFinalizationService>();
            services.AddSingleton<TransientCommitFailureSwitch>();
            services.AddScoped<FinancialCopilot.Billing.Contracts.IUsageFinalizationService>(provider =>
                new FailOnceUsageFinalizationService(
                    provider.GetRequiredService<UsageFinalizationService>(),
                    provider.GetRequiredService<TransientCommitFailureSwitch>()));
        });
    }
}

internal sealed class TransientCommitFailureSwitch
{
    private int _remainingFailures = 1;

    public bool ShouldFailOnce() => Interlocked.Exchange(ref _remainingFailures, 0) == 1;
}

internal sealed class FailOnceUsageFinalizationService(
    IUsageFinalizationService inner,
    TransientCommitFailureSwitch failureSwitch) : IUsageFinalizationService
{
    public Task<UsageFinalizationResult> CommitAsync(
        UsageCommitCommand command,
        CancellationToken cancellationToken)
    {
        if (failureSwitch.ShouldFailOnce())
            throw new DbUpdateException("Simulated transient billing persistence failure.");

        return inner.CommitAsync(command, cancellationToken);
    }

    public Task<UsageFinalizationResult> ReleaseAsync(
        UsageReleaseCommand command,
        CancellationToken cancellationToken) => inner.ReleaseAsync(command, cancellationToken);
}

// Drives the registered IBillingFacadeHook the way the chat workflow does, for isolated organization
// accounts (an API-client actor resolves its tenant's organization account).
internal sealed class BillingHookHarness(AiFacadeApiFactory factory)
{
    public const string Provider = "BillingTestProvider";
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Guid> _billingOperationIds =
        new(StringComparer.Ordinal);

    public sealed record Account(Guid TenantId, Guid AccountId, Guid ActorId, Guid ApiClientId);

    public async Task<Account> CreateAccountAsync(decimal balance)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await db.Database.EnsureCreatedAsync();
        var account = new Account(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = account.AccountId,
            TenantId = account.TenantId,
            AccountType = "Organization",
            BillingMode = "Prepaid"
        });
        db.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = account.AccountId,
            Balance = balance,
            ReservedAmount = 0m,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return account;
    }

    public void RecordCall(string correlationId, string model, int? inputTokens, int? outputTokens) =>
        factory.Services.GetRequiredService<IAiExecutionUsageAccumulator>().Record(new AiExecutionUsageFacts(
            correlationId, Provider, model, AiExecutionStatus.Completed, TimeSpan.Zero, 1, inputTokens, outputTokens));

    public async Task<BillingReservationHandle> ReserveAsync(
        Account account,
        string correlationId,
        string operationCode = "AiQuery.Scanner",
        string? usageCorrelationId = null,
        Guid? billingOperationId = null)
    {
        var operationId = billingOperationId ?? _billingOperationIds.GetOrAdd(correlationId, static _ => Guid.NewGuid());
        _billingOperationIds[correlationId] = operationId;
        using var scope = factory.Services.CreateScope();
        var handle = await scope.ServiceProvider.GetRequiredService<IBillingFacadeHook>().TryReserveAsync(
            new BillingReservationRequest(
                correlationId, account.TenantId, account.ActorId, operationCode,
                UserId: null, ApiClientId: account.ApiClientId, UsageCorrelationId: usageCorrelationId,
                BillingOperationId: operationId),
            CancellationToken.None);
        return Assert.IsType<BillingReservationHandle>(handle);
    }

    public async Task<BillingReservationHandle> EnsureReservedCapacityAsync(BillingReservationHandle handle)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBillingFacadeHook>()
            .EnsureReservedCapacityAsync(handle, CancellationToken.None);
    }

    public async Task<UsageAccountingResult?> FinalizeAsync(
        BillingReservationHandle handle,
        string completionStatus,
        string? executionRoute = null)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBillingFacadeHook>().FinalizeAsync(
            handle, new BillingFinalizationRequest(completionStatus, ExecutionRoute: executionRoute), CancellationToken.None);
    }

    public WalletProjectionRow ReadWallet(Guid accountId) =>
        Read(db => db.WalletProjections.AsNoTracking().Single(row => row.CustomerAccountId == accountId));

    public UsageReservationRow ReadReservation(string correlationId) =>
        Read(db => db.UsageReservations.AsNoTracking().Single(
            row => row.IdempotencyKey == $"{GetBillingOperationId(correlationId):N}:reservation"));

    public UsageLedgerEntryRow ReadLedger(string correlationId) => Assert.Single(ReadLedgerEntries(correlationId));

    public IReadOnlyList<UsageLedgerEntryRow> ReadLedgerEntries(string correlationId) =>
        Read(db => db.UsageLedgerEntries.AsNoTracking().Where(
            row => row.IdempotencyKey == $"{GetBillingOperationId(correlationId):N}:charge").ToList());

    public IReadOnlyList<UsageLedgerEntryRow> ReadLedgerEntriesByKey(string idempotencyKey) =>
        Read(db => db.UsageLedgerEntries.AsNoTracking().Where(row => row.IdempotencyKey == idempotencyKey).ToList());

    public AiExecutionUsageSummary? ReadSummary(string correlationId) =>
        factory.Services.GetRequiredService<IAiExecutionUsageAccumulator>().GetSummary(correlationId);

    public IReadOnlyList<AiExecutionUsageFacts> ReadUnbilled(string correlationId) =>
        factory.Services.GetRequiredService<IAiExecutionUsageAccumulator>().GetUnbilled(correlationId);

    private Guid GetBillingOperationId(string correlationId) =>
        _billingOperationIds.TryGetValue(correlationId, out var operationId)
            ? operationId
            : throw new InvalidOperationException($"No billing operation id is recorded for {correlationId}.");

    private T Read<T>(Func<BillingDbContext, T> query)
    {
        using var scope = factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<BillingDbContext>());
    }
}
