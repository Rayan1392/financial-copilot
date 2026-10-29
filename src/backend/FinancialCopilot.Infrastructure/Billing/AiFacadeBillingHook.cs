using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Billing.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;

namespace FinancialCopilot.Infrastructure.Billing;

public sealed class AiFacadeBillingHook(
    IBillableAccountResolver accountResolver,
    ICustomerAccountRepository accounts,
    IWalletService wallets,
    ICreditReservationService reservationService,
    IUsageChargeCalculator chargeCalculator,
    IUsageFinalizationService finalizationService,
    IDailyFreeAllowanceService dailyFreeAllowanceService,
    IAiExecutionUsageAccumulator usageAccumulator,
    IUsageReservationRepository reservations,
    IUsageReservationUpgradeService reservationUpgrader,
    IUsageLedgerRepository usageLedger,
    IOptions<UsageBasedCreditBillingOptions> usageBasedCreditOptions,
    UsageBasedCreditCalculator usageBasedCreditCalculator,
    ILogger<AiFacadeBillingHook> logger) : IBillingFacadeHook
{
    private const string PricingPolicyVersion = "v1";
    private static readonly Meter Meter = new("FinancialCopilot.TelegramMembership");
    private static readonly Counter<long> BucketConsumptionCounter = Meter.CreateCounter<long>("telegram.membership.bucket_consumptions");
    private static readonly Counter<long> DeniedReservationCounter = Meter.CreateCounter<long>("telegram.membership.denied_reservations");
    private static readonly Meter BillingMeter = new("FinancialCopilot.Billing");
    private static readonly Histogram<int> UsageBasedCreditsHistogram = BillingMeter.CreateHistogram<int>("billing.usage_based.proposed_credits");
    private readonly UsageBasedCreditBillingOptions creditOptions = usageBasedCreditOptions.Value;

    public async Task<BillingReservationHandle?> TryReserveAsync(
        BillingReservationRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(
                request.ActorId,
                request.TenantId,
                request.UserId,
                request.ApiClientId,
                request.ExternalUserId),
            cancellationToken);
        var wallet = await wallets.GetSnapshotAsync(account.Id, cancellationToken);
        var actorContext = new BillableActorContext(
            request.ActorId,
            request.TenantId,
            request.UserId,
            request.ApiClientId,
            request.ExternalUserId);
        var reservationKey = GetReservationIdempotencyKey(request);
        var operationKey = request.BillingOperationId?.ToString("N") ?? reservationKey;
        var allowance = await dailyFreeAllowanceService.EnsureAsync(
            actorContext,
            account,
            operationKey,
            cancellationToken);
        if (allowance.Granted)
        {
            wallet = await wallets.GetSnapshotAsync(account.Id, cancellationToken);
        }
        var maximumCharge = chargeCalculator.Calculate(
            CreateChargeRequest(request.OperationCode, "Completed", cached: false));
        FinancialCopilot.Billing.Usage.UsageReservation reservation;
        try
        {
            reservation = await ReserveWithFreshWalletRetryAsync(
                account,
                wallet,
                request.OperationCode,
                maximumCharge.CreditsCharged,
                reservationKey,
                request.RequestFingerprint,
                cancellationToken);
        }
        catch (FinancialCopilot.Billing.InsufficientCreditException)
        {
            DeniedReservationCounter.Add(
                1,
                new KeyValuePair<string, object?>("operationCode", request.OperationCode),
                new KeyValuePair<string, object?>("hadTelegramAllowance", allowance.RemainingCredits > 0));
            throw;
        }

        if (allowance.RemainingCredits > 0)
        {
            BucketConsumptionCounter.Add(
                1,
                new KeyValuePair<string, object?>("operationCode", request.OperationCode),
                new KeyValuePair<string, object?>("allowanceDateKey", allowance.AllowanceDateKey));
        }

        return new BillingReservationHandle(
            reservation.IdempotencyKey,
            request.CorrelationId,
            account.Id,
            request.TenantId,
            request.ActorId,
            request.ApiClientId,
            request.ExternalUserId,
            request.OperationCode,
            allowance.RemainingCredits > 0 ? "TelegramDailyFreeAllowance" : null,
            string.IsNullOrWhiteSpace(allowance.AllowanceDateKey) ? null : allowance.AllowanceDateKey,
            reservation.ReservedCredits,
            request.UsageCorrelationId,
            request.BillingOperationId);
    }

    private async Task<FinancialCopilot.Billing.Usage.UsageReservation> ReserveWithFreshWalletRetryAsync(
        CustomerAccount account,
        WalletSnapshot wallet,
        string operationCode,
        decimal operationCredits,
        string reservationKey,
        string? requestFingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            var credits = await GetReservationCreditsAsync(
                account, wallet, operationCode, operationCredits, reservationKey, cancellationToken);
            return await reservationService.ReserveAsync(
                account, wallet, operationCode, credits, reservationKey, cancellationToken, requestFingerprint);
        }
        catch (InvalidOperationException exception)
            when (string.Equals(exception.Message, "Reservation request used a stale wallet snapshot.", StringComparison.Ordinal))
        {
            // Another request may have advanced the wallet after the initial snapshot.
            // The reservation service retains the fencing check; refresh and retry once
            // so normal concurrent requests do not surface an internal billing race.
            var refreshedWallet = await wallets.GetSnapshotAsync(account.Id, cancellationToken);
            var credits = await GetReservationCreditsAsync(
                account, refreshedWallet, operationCode, operationCredits, reservationKey, cancellationToken);
            return await reservationService.ReserveAsync(
                account, refreshedWallet, operationCode, credits, reservationKey, cancellationToken, requestFingerprint);
        }
    }

    // Keep the initial hold at the minimum so deterministic, inexpensive routes remain usable
    // with one credit. The hold is increased atomically before a more expensive path starts.
    private async Task<decimal> GetReservationCreditsAsync(
        CustomerAccount account,
        WalletSnapshot wallet,
        string operationCode,
        decimal operationCredits,
        string reservationKey,
        CancellationToken cancellationToken)
    {
        if (!EnforcesUsageBasedCredits(operationCode))
        {
            return operationCredits;
        }

        // A replayed turn reuses its original hold: the reservation service rejects a different
        // amount for the same idempotency key, and the balance has moved since the first attempt.
        if (await reservations.FindByIdempotencyKeyAsync(reservationKey, cancellationToken) is { } existing)
        {
            return existing.ReservedCredits;
        }

        var minimum = Math.Max(decimal.Ceiling(operationCredits), creditOptions.MinimumCredits);
        var availableBalance = Math.Max(0m, wallet.Balance - wallet.ReservedAmount);
        if (availableBalance < minimum)
            throw new FinancialCopilot.Billing.InsufficientCreditException();
        return minimum;
    }

    public async Task<BillingReservationHandle> EnsureReservedCapacityAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken)
    {
        if (!EnforcesUsageBasedCredits(handle.OperationCode))
            return handle;

        return await EnsureReservedCapacityAsync(
            handle,
            creditOptions.MaximumCreditsPerQuestion,
            cancellationToken);
    }

    public async Task<BillingReservationHandle> EnsureCurrentUsageCapacityAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken)
    {
        if (!EnforcesUsageBasedCredits(handle.OperationCode))
            return handle;

        var usageCorrelationId = handle.UsageCorrelationId ?? handle.CorrelationId;
        var calls = usageAccumulator.GetUnbilled(usageCorrelationId);
        if (calls.Count == 0)
            return handle;

        var proposed = usageBasedCreditCalculator.Calculate(
            creditOptions.ToPolicy(),
            calls.Select(ToModelCallUsage).ToArray());
        if (proposed.Credits <= handle.ReservedCredits)
            return handle;

        return await EnsureReservedCapacityAsync(handle, proposed.Credits, cancellationToken);
    }

    private async Task<BillingReservationHandle> EnsureReservedCapacityAsync(
        BillingReservationHandle handle,
        decimal requiredCredits,
        CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(handle.CustomerAccountId, cancellationToken) ??
            throw new KeyNotFoundException("Billing account is not configured.");
        try
        {
            var reservation = await reservationUpgrader.EnsureReservedCapacityAsync(
                account,
                handle.ReservationId,
                requiredCredits,
                cancellationToken);
            return handle with { ReservedCredits = reservation.ReservedCredits };
        }
        catch (FinancialCopilot.Billing.InsufficientCreditException)
        {
            var current = await reservations.FindByIdempotencyKeyAsync(handle.ReservationId, CancellationToken.None);
            if (current?.Status == UsageReservationStatus.Reserved)
            {
                await finalizationService.ReleaseAsync(
                    new UsageReleaseCommand(
                        handle.CustomerAccountId,
                        handle.TenantId,
                        handle.ReservationId,
                        "Insufficient balance for the selected AI execution path."),
                    CancellationToken.None);
            }
            throw;
        }
    }

    public async Task<UsageAccountingResult?> FinalizeAsync(
        BillingReservationHandle handle,
        BillingFinalizationRequest request,
        CancellationToken cancellationToken)
    {
        var ledgerIdempotencyKey = GetLedgerIdempotencyKey(handle);
        var existingEntry = await usageLedger.FindByIdempotencyKeyAsync(ledgerIdempotencyKey, cancellationToken);
        if (existingEntry is not null)
            return await ReplayFinalizationAsync(handle, existingEntry, cancellationToken);

        var usageCorrelationId = handle.UsageCorrelationId ?? handle.CorrelationId;
        var shadowMode = creditOptions.Mode == UsageBasedBillingMode.Shadow;
        AiExecutionUsageSummary? usage;
        try
        {
            usage = usageAccumulator.GetSummary(usageCorrelationId);
        }
        catch (Exception exception) when (shadowMode && exception is not OperationCanceledException)
        {
            usage = null;
            LogShadowTelemetryFailure(handle, exception);
        }
        var charge = chargeCalculator.Calculate(
            CreateChargeRequest(handle.OperationCode, request.CompletionStatus, request.Cached));

        if (EvaluatesUsageBasedCredits(handle.OperationCode))
        {
            try
            {
                // A final settlement claims all completed calls once for audit and shadow comparison.
                // A semantic outcome that can continue through Feature 141 does not settle here, so
                // its earlier calls remain available to the final answer's single charge.
                var calls = usageAccumulator.ClaimUnbilled(usageCorrelationId, handle.ReservationId);
                usage = AiExecutionUsageSummary.From(calls);
                charge = ApplyUsageBasedCredits(handle, request.CompletionStatus, charge, calls, request.ExecutionRoute);
                if (creditOptions.Mode == UsageBasedBillingMode.Enforced && charge.CreditsCharged > handle.ReservedCredits)
                {
                    handle = await EnsureReservedCapacityAsync(handle, charge.CreditsCharged, cancellationToken);
                }
            }
            catch (Exception exception) when (shadowMode && exception is not OperationCanceledException)
            {
                // Shadow data is observational; retain the operation-based charge if its
                // usage collection, calculation, metric, or log path fails.
                LogShadowTelemetryFailure(handle, exception);
            }
        }

        UsageFinalizationResult finalized;
        try
        {
            finalized = await finalizationService.CommitAsync(
                new UsageCommitCommand(
                handle.CustomerAccountId,
                handle.ActorId,
                handle.TenantId,
                handle.ApiClientId,
                handle.ExternalUserId,
                handle.ReservationId,
                ledgerIdempotencyKey,
                charge,
                request.CompletionStatus,
                request.ProviderName ?? usage?.ProviderKey,
                request.ModelName ?? usage?.ModelKey,
                request.PromptTokens ?? usage?.InputTokens,
                request.CompletionTokens ?? usage?.OutputTokens,
                request.TotalTokens ?? usage?.TotalTokens,
                request.EstimatedCost ?? usage?.ProviderReportedCost,
                handle.AllocationSource,
                handle.AllowanceDateKey),
                cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
        {
            existingEntry = await usageLedger.FindByIdempotencyKeyAsync(ledgerIdempotencyKey, cancellationToken);
            if (existingEntry is null)
                throw;
            return await ReplayFinalizationAsync(handle, existingEntry, cancellationToken);
        }

        if (EvaluatesUsageBasedCredits(handle.OperationCode))
        {
            try
            {
                logger.LogInformation(
                    "Usage-based billing settlement completed. Mode={Mode} OperationId={OperationId} CorrelationId={CorrelationId} " +
                    "ExecutionRoute={ExecutionRoute} SettlementOutcome={SettlementOutcome} ActualCredits={ActualCredits} " +
                    "ReservedCredits={ReservedCredits} RemainingCredits={RemainingCredits} LedgerId={LedgerId}",
                    creditOptions.Mode,
                    GetBillingOperationId(handle),
                    handle.CorrelationId,
                    request.ExecutionRoute,
                    request.CompletionStatus,
                    charge.CreditsCharged,
                    finalized.Reservation.ReservedCredits,
                    finalized.Wallet.Balance - finalized.Wallet.ReservedAmount,
                    finalized.LedgerEntry?.Id);
            }
            catch (Exception exception) when (shadowMode && exception is not OperationCanceledException)
            {
                // Settlement has succeeded. Do not turn a log-sink error into a failed chat.
                LogShadowTelemetryFailure(handle, exception);
            }
        }
        var account = await accounts.FindAsync(handle.CustomerAccountId, cancellationToken) ??
            throw new KeyNotFoundException("Billing account is not configured.");

        return new UsageAccountingResult(
            handle.OperationCode,
            request.CompletionStatus,
            charge.CreditsCharged,
            account.GetAvailableSpendingCapacity(finalized.Wallet),
            charge.PricingPolicyVersion,
            charge.Cached,
            usage?.ProviderKey,
            usage?.ModelKey,
            usage?.InputTokens,
            usage?.OutputTokens,
            usage?.TotalTokens,
            EstimatedCost: null,
            request.ExecutionRoute);
    }

    public async Task ReleaseAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken)
    {
        await finalizationService.ReleaseAsync(
            new UsageReleaseCommand(
                handle.CustomerAccountId,
                handle.TenantId,
                handle.ReservationId,
                "Workflow released without a chargeable outcome."),
            cancellationToken);
    }

    private bool EnforcesUsageBasedCredits(string operationCode) =>
        creditOptions.Mode == UsageBasedBillingMode.Enforced && creditOptions.AppliesTo(operationCode);

    private bool EvaluatesUsageBasedCredits(string operationCode) =>
        (creditOptions.Mode is UsageBasedBillingMode.Shadow or UsageBasedBillingMode.Enforced) &&
        creditOptions.AppliesTo(operationCode);

    private UsageChargeResult ApplyUsageBasedCredits(
        BillingReservationHandle handle,
        string completionStatus,
        UsageChargeResult operationCharge,
        IReadOnlyList<AiExecutionUsageFacts> calls,
        string? executionRoute)
    {
        var policy = creditOptions.ToPolicy();
        var proposed = usageBasedCreditCalculator.Calculate(
            policy,
            calls.Select(call => new ModelCallUsage(
                    call.ProviderKey,
                    call.ModelKey,
                    call.InputTokens,
                    call.CachedInputTokens,
                    call.OutputTokens,
                    call.ProviderReportedCost,
                    call.ProviderReportedCurrency,
                    call.CacheWriteInputTokens,
                    call.CacheWrite30mInputTokens,
                    call.CacheWrite12hInputTokens))
                .ToArray());
        var enforced = creditOptions.Mode == UsageBasedBillingMode.Enforced;
        var charge = enforced && operationCharge.CreditsCharged > 0
            ? new UsageChargeResult(proposed.Credits, policy.Version, operationCharge.Cached)
            : operationCharge;
        var recoveredValue = charge.CreditsCharged * policy.CostPerCredit;

        UsageBasedCreditsHistogram.Record(
            proposed.Credits,
            new KeyValuePair<string, object?>("mode", creditOptions.Mode.ToString()),
            new KeyValuePair<string, object?>("operationCode", handle.OperationCode),
            new KeyValuePair<string, object?>("fallbackApplied", proposed.FallbackApplied),
            new KeyValuePair<string, object?>("cappedAtMaximum", proposed.CappedAtMaximum));
        logger.LogInformation(
            "Usage-based credit charge evaluated. Mode={Mode} PolicyVersion={PolicyVersion} OperationId={OperationId} CorrelationId={CorrelationId} " +
            "OperationCode={OperationCode} CompletionStatus={CompletionStatus} OperationCredits={OperationCredits} " +
            "ProposedCredits={ProposedCredits} ChargedCredits={ChargedCredits} ReservedCredits={ReservedCredits} " +
            "ActualCost={ActualCost} RecoveredValue={RecoveredValue} RecoveryDelta={RecoveryDelta} " +
            "Currency={Currency} CostPerCredit={CostPerCredit} PricingMultiplier={PricingMultiplier} " +
            "MinimumCredits={MinimumCredits} MaximumCreditsPerQuestion={MaximumCreditsPerQuestion} " +
            "MissingUsageFallbackCredits={MissingUsageFallbackCredits} RawCredits={RawCredits} ModelCalls={ModelCalls} " +
            "UnmeasuredModelCalls={UnmeasuredModelCalls} CostComplete={CostComplete} PricingConfidence={PricingConfidence} " +
            "InputTokens={InputTokens} CachedInputTokens={CachedInputTokens} CacheWriteInputTokens={CacheWriteInputTokens} " +
            "CacheWrite30mInputTokens={CacheWrite30mInputTokens} CacheWrite12hInputTokens={CacheWrite12hInputTokens} " +
            "OutputTokens={OutputTokens} FallbackApplied={FallbackApplied} CappedAtMaximum={CappedAtMaximum}",
            creditOptions.Mode,
            policy.Version,
            GetBillingOperationId(handle),
            handle.CorrelationId,
            handle.OperationCode,
            completionStatus,
            operationCharge.CreditsCharged,
            proposed.Credits,
            charge.CreditsCharged,
            handle.ReservedCredits,
            proposed.ActualCost,
            recoveredValue,
            recoveredValue - proposed.ActualCost,
            policy.Currency,
            policy.CostPerCredit,
            policy.PricingMultiplier,
            policy.MinimumCredits,
            policy.MaximumCreditsPerQuestion,
            policy.MissingUsageFallbackCredits,
            proposed.RawCredits,
            proposed.ModelCalls,
            proposed.UnmeasuredModelCalls,
            proposed.CostComplete,
            proposed.PricingConfidence,
            FormatKnownTotal(calls.Select(call => call.InputTokens)),
            FormatKnownTotal(calls.Select(call => call.CachedInputTokens)),
            FormatKnownTotal(calls.Select(call => call.CacheWriteInputTokens)),
            FormatKnownTotal(calls.Select(call => call.CacheWrite30mInputTokens)),
            FormatKnownTotal(calls.Select(call => call.CacheWrite12hInputTokens)),
            FormatKnownTotal(calls.Select(call => call.OutputTokens)),
            proposed.FallbackApplied,
            proposed.CappedAtMaximum);

        logger.LogInformation(
            "Usage-based model-call accounting. OperationId={OperationId} CorrelationId={CorrelationId} " +
            "ExecutionRoute={ExecutionRoute} Calls={ModelCalls}",
            GetBillingOperationId(handle),
            handle.CorrelationId,
            executionRoute,
            string.Join(";", calls.Select(call =>
                $"{call.ProviderKey}/{call.ModelKey}:calls=1,input={call.InputTokens?.ToString() ?? "?"}," +
                $"cached={call.CachedInputTokens?.ToString() ?? "?"}," +
                $"write={call.CacheWriteInputTokens?.ToString() ?? "?"}," +
                $"write30m={call.CacheWrite30mInputTokens?.ToString() ?? "?"}," +
                $"write12h={call.CacheWrite12hInputTokens?.ToString() ?? "?"}," +
                $"output={call.OutputTokens?.ToString() ?? "?"}," +
                $"workload={call.Workload?.ToString() ?? "?"},attempt={call.AttemptNumber}")));

        return charge;
    }

    private void LogShadowTelemetryFailure(BillingReservationHandle handle, Exception exception)
    {
        try
        {
            logger.LogWarning(
                "Shadow usage telemetry failed; the Legacy charge will continue. " +
                "OperationId={OperationId} CorrelationId={CorrelationId} FailureType={FailureType}",
                GetBillingOperationId(handle),
                handle.CorrelationId,
                exception.GetType().Name);
        }
        catch
        {
            // Logging is part of the optional Shadow observation path as well.
        }
    }

    private async Task<UsageAccountingResult> ReplayFinalizationAsync(
        BillingReservationHandle handle,
        UsageLedgerEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.CustomerAccountId != handle.CustomerAccountId ||
            entry.ActorId != handle.ActorId ||
            entry.TenantId != handle.TenantId ||
            entry.ApiClientId != handle.ApiClientId ||
            !string.Equals(entry.OperationCode, handle.OperationCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Billing idempotency key was already used for a different account or operation.");
        }

        var wallet = await wallets.GetSnapshotAsync(handle.CustomerAccountId, cancellationToken);
        var account = await accounts.FindAsync(handle.CustomerAccountId, cancellationToken) ??
            throw new KeyNotFoundException("Billing account is not configured.");
        return new UsageAccountingResult(
            entry.OperationCode,
            entry.CompletionStatus ?? "Completed",
            entry.CreditsCharged,
            account.GetAvailableSpendingCapacity(wallet),
            entry.PricingPolicyVersion,
            Cached: false,
            entry.ProviderName,
            entry.ModelName,
            entry.PromptTokens,
            entry.CompletionTokens,
            entry.TotalTokens,
            EstimatedCost: null);
    }

    private static string GetReservationIdempotencyKey(BillingReservationRequest request) =>
        $"{request.BillingOperationId?.ToString("N") ?? Guid.NewGuid().ToString("N")}:reservation";

    private static string GetLedgerIdempotencyKey(BillingReservationHandle handle)
    {
        const string reservationSuffix = ":reservation";
        return handle.ReservationId.EndsWith(reservationSuffix, StringComparison.Ordinal)
            ? $"{handle.ReservationId[..(handle.ReservationId.Length - reservationSuffix.Length)]}:charge"
            : $"{handle.BillingOperationId?.ToString("N") ?? handle.ReservationId}:charge";
    }

    private static string GetBillingOperationId(BillingReservationHandle handle)
    {
        if (handle.BillingOperationId is { } billingOperationId)
            return billingOperationId.ToString("N");

        const string reservationSuffix = ":reservation";
        return handle.ReservationId.EndsWith(reservationSuffix, StringComparison.Ordinal)
            ? handle.ReservationId[..(handle.ReservationId.Length - reservationSuffix.Length)]
            : handle.ReservationId;
    }

    private static ModelCallUsage ToModelCallUsage(AiExecutionUsageFacts call) =>
        new(
            call.ProviderKey,
            call.ModelKey,
            call.InputTokens,
            call.CachedInputTokens,
            call.OutputTokens,
            call.ProviderReportedCost,
            call.ProviderReportedCurrency,
            call.CacheWriteInputTokens,
            call.CacheWrite30mInputTokens,
            call.CacheWrite12hInputTokens);

    private static string FormatKnownTotal(IEnumerable<int?> values)
    {
        var materialized = values.ToArray();
        return materialized.All(value => value.HasValue)
            ? materialized.Sum(value => value!.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "?";
    }

    private static UsageChargeRequest CreateChargeRequest(
        string operationCode,
        string completionStatus,
        bool cached) =>
        new(
            operationCode,
            PricingPolicyVersion,
            cached,
            completionStatus,
            UsageUnits: [],
            ProviderCosts: []);
}
