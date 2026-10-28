using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

/// <summary>
/// Applies period-aware subscription grants to the existing wallet and immutable usage ledger.
/// Legacy wallets without period attribution are intentionally left untouched until reconciled.
/// </summary>
public sealed class SubscriptionCreditLifecycleService(BillingDbContext dbContext)
{
    public static readonly TimeSpan PeriodDuration = TimeSpan.FromDays(30);

    public const string ActiveStatus = "Active";
    public const string ExpiredStatus = "Expired";
    public const string ForfeitedStatus = "Forfeited";
    public const string TransferredStatus = "Transferred";
    public const string SubscriptionAllocationSource = "subscription-credit-period";

    private const string GrantOperationCode = "Billing.SubscriptionCreditGrant";
    private const string ExpiryOperationCode = "Billing.SubscriptionCreditExpiry";
    private const string ForfeitureOperationCode = "Billing.SubscriptionCreditForfeiture";
    private const string TransferOutOperationCode = "Billing.SubscriptionCreditTransferOut";
    private const string TransferInOperationCode = "Billing.SubscriptionCreditTransferIn";
    private const string PolicyVersion = "subscription-credit-30d-v1";
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> AllocationLocks = new();

    public Task InitializeNewWalletAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        WithAccountLockAsync(
            account.Id,
            () => InitializeNewWalletCoreAsync(account, wallet, now, cancellationToken),
            cancellationToken);

    private async Task InitializeNewWalletCoreAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (account.ExternalSubscriptionManaged)
        {
            await EnsureExternalPlanPeriodsAsync(account, wallet, now, cancellationToken);
            return;
        }
        var planCode = FinancialCopilot.Billing.Accounts.SubscriptionEffectiveness.ResolveEffectivePlanCode(
            account.SubscriptionPlanCode,
            account.SubscriptionEffectiveTo,
            now) ?? "Free";
        if (planCode is "Plus" or "Pro" &&
            (account.SubscriptionEffectiveFrom is null || !account.SubscriptionActivationIsAuthoritative))
        {
            planCode = "Free";
        }
        var plan = await FindPlanAsync(planCode, cancellationToken);
        var anchor = account.SubscriptionEffectiveFrom ?? now;
        await AddPeriodAsync(
            account,
            wallet,
            plan,
            anchor,
            account.SubscriptionRevision,
            GetCycleIndex(anchor, now),
            now,
            cancellationToken);
    }

    /// <summary>
    /// Applies policy-approved transitions using the authoritative TahlilApp activation timestamp.
    /// </summary>
    public Task<string?> ApplyPlanChangeAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        string previousPlanCode,
        string newPlanCode,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        DateTimeOffset? effectiveFrom = null) =>
        // Cross-process correctness comes from the account revision concurrency token and the
        // unique period/ledger idempotency keys, not an in-process semaphore.
        ApplyPlanChangeCoreAsync(account, wallet, previousPlanCode, newPlanCode, now, cancellationToken, effectiveFrom);

    private async Task<string?> ApplyPlanChangeCoreAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        string previousPlanCode,
        string newPlanCode,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        DateTimeOffset? effectiveFrom)
    {
        if (previousPlanCode == "Pro" && newPlanCode == "Plus")
        {
            throw new InvalidOperationException("A Pro-to-Plus subscription downgrade is not allowed.");
        }

        if (previousPlanCode == "Plus" && newPlanCode == "Pro")
        {
            if (effectiveFrom is null)
            {
                return "Plus-to-Pro credits are held until TahlilApp supplies the authoritative effectiveFrom timestamp.";
            }

            var source = await FindLatestPeriodAsync(account.Id, cancellationToken);
            if (source is null || source.PlanCode != "Plus" || source.Status != ActiveStatus)
            {
                return "Plus-to-Pro credit transfer is held for legacy wallet reconciliation.";
            }

            if (now >= source.ExpiresAtUtc)
            {
                if (wallet.ReservedAmount > 0)
                {
                    return "Plus-to-Pro credit transfer is deferred while wallet credits remain reserved.";
                }

                ClosePeriod(account, wallet, source, source.ExpiresAtUtc, ExpiredStatus, ExpiryOperationCode);
                source = null;
            }

            var transferable = source?.RemainingCredits ?? 0m;
            var proPlan = await FindPlanAsync("Pro", cancellationToken);
            await AddPeriodAsync(
                account,
                wallet,
                proPlan,
                effectiveFrom.Value.ToUniversalTime(),
                account.SubscriptionRevision + 1,
                GetCycleIndex(effectiveFrom.Value, now),
                now,
                cancellationToken,
                transferable,
                source);
            return null;
        }

        var latest = await FindLatestPeriodAsync(account.Id, cancellationToken);
        if (latest is null)
        {
            return "Credit allocation is held for legacy wallet reconciliation; the existing balance was preserved.";
        }

        if (previousPlanCode == newPlanCode)
        {
            if (effectiveFrom is null)
            {
                return "Subscription credits are held until TahlilApp supplies the authoritative effectiveFrom timestamp.";
            }

            if (latest is null)
            {
                return "Subscription credits are held for legacy wallet reconciliation.";
            }

            if (latest.PlanCode == newPlanCode && latest.AnchorAtUtc == effectiveFrom.Value.ToUniversalTime())
            {
                return null;
            }

            if (latest.PlanCode is "Plus" or "Pro")
            {
                if (latest.Status == ExpiredStatus && effectiveFrom.Value.ToUniversalTime() >= latest.ExpiresAtUtc)
                {
                    var renewedPlan = await FindPlanAsync(newPlanCode, cancellationToken);
                    await AddPeriodAsync(
                        account, wallet, renewedPlan, effectiveFrom.Value.ToUniversalTime(), account.SubscriptionRevision + 1,
                        GetCycleIndex(effectiveFrom.Value, now), now, cancellationToken);
                    return null;
                }

                return "Paid credit activation is held until the existing subscription period is reconciled against TahlilApp effectiveFrom.";
            }

            if (latest.Status == ActiveStatus && latest.PlanCode == "Free" && newPlanCode is "Plus" or "Pro")
            {
                ClosePeriod(account, wallet, latest, now, ForfeitedStatus, ForfeitureOperationCode);
            }

            var plan = await FindPlanAsync(newPlanCode, cancellationToken);
            await AddPeriodAsync(
                account, wallet, plan, effectiveFrom.Value.ToUniversalTime(), account.SubscriptionRevision + 1,
                GetCycleIndex(effectiveFrom.Value, now), now, cancellationToken);
            return null;
        }

        if (previousPlanCode == "Free" && newPlanCode is "Plus" or "Pro")
        {
            if (effectiveFrom is null)
            {
                return "Paid credit allocation is held until TahlilApp supplies the authoritative effectiveFrom timestamp.";
            }

            var activeFreePeriod = latest.Status == ActiveStatus && latest.PlanCode == "Free"
                ? latest
                : null;
            if (activeFreePeriod is not null)
            {
                ClosePeriod(account, wallet, activeFreePeriod, now, ForfeitedStatus, ForfeitureOperationCode);
            }

            var paidPlan = await FindPlanAsync(newPlanCode, cancellationToken);
            await AddPeriodAsync(
                account,
                wallet,
                paidPlan,
                effectiveFrom.Value.ToUniversalTime(),
                account.SubscriptionRevision + 1,
                GetCycleIndex(effectiveFrom.Value, now),
                now,
                cancellationToken);
            return null;
        }

        // External expiry or an explicit transition to Free revokes paid capabilities immediately.
        // Credits already granted remain valid only until their own 30-day period boundary.
        if (newPlanCode == "Free")
        {
            return null;
        }

        if (previousPlanCode is "Plus" or "Pro" && newPlanCode is "Plus" or "Pro")
        {
            return "Credit allocation is held for Product approval of paid-plan transition semantics.";
        }

        return null;
    }

    /// <summary>
    /// Ensures a tracked account/wallet is current before a new usage reservation is created.
    /// The caller saves this with the reservation so grant, expiry and reservation are atomic.
    /// </summary>
    public Task<bool> EnsureCurrentPeriodTrackedAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        account.ExternalSubscriptionManaged
            ? WithAccountLockAsync(account.Id, () => EnsureExternalPlanPeriodsAsync(account, wallet, now, cancellationToken), cancellationToken)
            : WithAccountLockAsync(
            account.Id,
            () => EnsureCurrentPeriodTrackedCoreAsync(account, wallet, now, cancellationToken),
            cancellationToken);

    public async Task ApplyExternalPlanSnapshotAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        IReadOnlyList<FinancialCopilot.Domain.Identity.TahlilApp.TahlilAppPlanSubscription> subscriptions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        account.ExternalSubscriptionManaged = true;
        foreach (var subscription in subscriptions)
        {
            var row = await dbContext.ExternalPlanSubscriptions.SingleOrDefaultAsync(
                item => item.CustomerAccountId == account.Id && item.IdentityKey == subscription.IdentityKey,
                cancellationToken);
            if (row is null)
            {
                dbContext.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow
                {
                    Id = Guid.NewGuid(),
                    CustomerAccountId = account.Id,
                    IdentityKey = subscription.IdentityKey,
                    PlanCode = subscription.PlanCode,
                    StartsAtUtc = subscription.StartsAt.ToUniversalTime(),
                    ExpiresAtUtc = subscription.ExpiresAt.ToUniversalTime()
                });
            }
        }

        await EnsureExternalPlanPeriodsAsync(account, wallet, now, cancellationToken);
    }

    public async Task<bool> IsPaidExpiryCooldownActiveAsync(Guid customerAccountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hasPaid = await dbContext.ExternalPlanSubscriptions.AnyAsync(
            row => row.CustomerAccountId == customerAccountId &&
                   row.StartsAtUtc <= now && row.ExpiresAtUtc > now &&
                   (row.PlanCode == "Plus" || row.PlanCode == "Pro"), cancellationToken);
        if (hasPaid) return false;
        var cooldown = dbContext.PaidExpiryCooldowns.Local.SingleOrDefault(
            row => row.CustomerAccountId == customerAccountId) ??
            await dbContext.PaidExpiryCooldowns.SingleOrDefaultAsync(
                row => row.CustomerAccountId == customerAccountId, cancellationToken);
        return cooldown is not null && now < cooldown.CooldownUntilUtc;
    }

    private async Task<bool> EnsureExternalPlanPeriodsAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var subscriptions = await dbContext.ExternalPlanSubscriptions
            .Where(row => row.CustomerAccountId == account.Id)
            .ToListAsync(cancellationToken);
        foreach (var added in dbContext.ExternalPlanSubscriptions.Local.Where(row =>
                     row.CustomerAccountId == account.Id && subscriptions.All(existing => existing.Id != row.Id)))
        {
            subscriptions.Add(added);
        }
        var active = subscriptions
            .Where(row => row.StartsAtUtc <= now && row.ExpiresAtUtc > now)
            .OrderBy(row => row.ExpiresAtUtc)
            .ThenBy(row => row.PlanCode == "Plus" ? 0 : 1)
            .ThenBy(row => row.StartsAtUtc)
            .ToArray();
        var activePaid = active.Where(row => row.PlanCode is "Plus" or "Pro").ToArray();

        account.SubscriptionPlanCode = activePaid.Any(row => row.PlanCode == "Pro") ? "Pro" :
            activePaid.Length > 0 ? "Plus" : "Free";
        account.SubscriptionEffectiveFrom = activePaid.Length == 1 ? activePaid[0].StartsAtUtc : null;
        account.SubscriptionEffectiveTo = activePaid.Length == 0 ? null : activePaid.Max(row => row.ExpiresAtUtc);
        account.SubscriptionActivationIsAuthoritative = true;

        var changed = false;
        var expired = await dbContext.SubscriptionCreditPeriods
            .Where(row => row.CustomerAccountId == account.Id && row.Status == ActiveStatus && row.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        if (wallet.ReservedAmount <= 0)
        {
            foreach (var period in expired)
            {
                ClosePeriod(account, wallet, period, period.ExpiresAtUtc, ExpiredStatus, ExpiryOperationCode);
                changed = true;
            }
        }

        if (activePaid.Length > 0)
        {
            var freePeriods = await dbContext.SubscriptionCreditPeriods
                .Where(row => row.CustomerAccountId == account.Id && row.PlanCode == "Free" && row.Status == ActiveStatus)
                .ToListAsync(cancellationToken);
            foreach (var period in freePeriods)
            {
                ClosePeriod(account, wallet, period, now, ForfeitedStatus, ForfeitureOperationCode);
                changed = true;
            }

            foreach (var subscription in activePaid)
            {
                var cycle = GetCycleIndex(subscription.StartsAtUtc, now);
                var cycleStarts = subscription.StartsAtUtc.AddTicks(PeriodDuration.Ticks * cycle);
                if (cycleStarts >= subscription.ExpiresAtUtc) continue;

                var periodExists = await dbContext.SubscriptionCreditPeriods.AnyAsync(row =>
                    row.CustomerAccountId == account.Id && row.SubscriptionIdentity == subscription.IdentityKey &&
                    row.PeriodNumber == cycle, cancellationToken);
                periodExists |= dbContext.SubscriptionCreditPeriods.Local.Any(row =>
                    row.CustomerAccountId == account.Id && row.SubscriptionIdentity == subscription.IdentityKey &&
                    row.PeriodNumber == cycle);
                if (periodExists)
                {
                    continue;
                }

                var plan = await FindPlanAsync(subscription.PlanCode, cancellationToken);
                SubscriptionCreditPeriodRow? source = null;
                if (subscription.PlanCode == "Pro" && !subscription.PlusTransferApplied)
                {
                    var plusPeriods = await dbContext.SubscriptionCreditPeriods
                        .Where(row => row.CustomerAccountId == account.Id && row.PlanCode == "Plus" &&
                                      row.Status == ActiveStatus && row.StartsAtUtc <= now && row.ExpiresAtUtc > now)
                        .ToListAsync(cancellationToken);
                    plusPeriods.AddRange(dbContext.SubscriptionCreditPeriods.Local.Where(row =>
                        row.CustomerAccountId == account.Id && row.PlanCode == "Plus" &&
                        row.Status == ActiveStatus && row.StartsAtUtc <= now && row.ExpiresAtUtc > now &&
                        plusPeriods.All(existing => existing.Id != row.Id)));
                    source = plusPeriods.OrderBy(row => row.ExpiresAtUtc).FirstOrDefault();
                }
                var transferred = source?.RemainingCredits ?? 0m;
                if (transferred > 0 && wallet.ReservedAmount > 0)
                {
                    continue;
                }
                await AddPeriodAsync(account, wallet, plan, subscription.StartsAtUtc, 0, cycle, now,
                    cancellationToken, transferred, transferred > 0 ? source : null, subscription.IdentityKey);
                if (subscription.PlanCode == "Pro" && !subscription.PlusTransferApplied)
                {
                    subscription.PlusTransferApplied = true;
                }
                changed = true;
            }

            return changed;
        }

        var lastPaidExpiry = subscriptions
            .Where(row => row.PlanCode is "Plus" or "Pro")
            .Select(row => (DateTimeOffset?)row.ExpiresAtUtc)
            .Max();
        PaidExpiryCooldownRow? cooldown = null;
        if (lastPaidExpiry is { } expiry)
        {
            cooldown = await dbContext.PaidExpiryCooldowns.SingleOrDefaultAsync(
                row => row.CustomerAccountId == account.Id, cancellationToken);
            var cooldownEnd = expiry.AddDays(30);
            if (cooldown is null)
            {
                cooldown = new PaidExpiryCooldownRow
                {
                    CustomerAccountId = account.Id,
                    LastPaidExpiresAtUtc = expiry,
                    CooldownUntilUtc = cooldownEnd,
                    Revision = 0
                };
                dbContext.PaidExpiryCooldowns.Add(cooldown);
                changed = true;
            }
            else if (expiry > cooldown.LastPaidExpiresAtUtc)
            {
                cooldown.LastPaidExpiresAtUtc = expiry;
                cooldown.CooldownUntilUtc = cooldownEnd;
                cooldown.Revision++;
                changed = true;
            }

            if (now < cooldown.CooldownUntilUtc)
            {
                var freePeriods = await dbContext.SubscriptionCreditPeriods
                    .Where(row => row.CustomerAccountId == account.Id && row.PlanCode == "Free" && row.Status == ActiveStatus)
                    .ToListAsync(cancellationToken);
                foreach (var period in freePeriods)
                {
                    ClosePeriod(account, wallet, period, expiry, ForfeitedStatus, ForfeitureOperationCode);
                    changed = true;
                }

                return changed;
            }

            var freePlan = await FindPlanAsync("Free", cancellationToken);
            var freeCycle = GetCycleIndex(cooldown.CooldownUntilUtc, now);
            await AddPeriodAsync(account, wallet, freePlan, cooldown.CooldownUntilUtc, 0, freeCycle, now,
                cancellationToken, subscriptionIdentity: $"free-after-paid:{cooldown.CooldownUntilUtc.UtcDateTime.Ticks}");
            return true;
        }

        // Accounts that have never held a TahlilApp paid plan retain the ordinary Free cycle.
        var latestFree = await dbContext.SubscriptionCreditPeriods
            .Where(row => row.CustomerAccountId == account.Id && row.PlanCode == "Free")
            .OrderByDescending(row => row.StartsAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (latestFree is null)
        {
            var plan = await FindPlanAsync("Free", cancellationToken);
            await AddPeriodAsync(account, wallet, plan, now, 0, 0, now, cancellationToken,
                subscriptionIdentity: $"initial-free:{now.UtcDateTime.Ticks}");
            return true;
        }
        if (latestFree.Status == ActiveStatus && latestFree.ExpiresAtUtc <= now && wallet.ReservedAmount <= 0)
        {
            var plan = await FindPlanAsync("Free", cancellationToken);
            var next = Math.Max(latestFree.PeriodNumber + 1, GetCycleIndex(latestFree.AnchorAtUtc, now));
            await AddPeriodAsync(account, wallet, plan, latestFree.AnchorAtUtc, 0, next, now,
                cancellationToken, subscriptionIdentity: latestFree.SubscriptionIdentity);
            return true;
        }
        return changed;
    }

    private async Task<bool> EnsureCurrentPeriodTrackedCoreAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var latest = await FindLatestPeriodAsync(account.Id, cancellationToken);
        if (latest is null)
        {
            return false;
        }

        if (latest.Status == ActiveStatus && now < latest.ExpiresAtUtc)
        {
            return false;
        }

        var changed = false;
        if (latest.Status == ActiveStatus)
        {
            // A reservation made inside the period may still finalize. Leave the whole period
            // intact until it commits/releases; the next worker tick closes it at the boundary.
            if (wallet.ReservedAmount > 0)
            {
                return false;
            }

            ClosePeriod(account, wallet, latest, now, ExpiredStatus, ExpiryOperationCode);
            changed = true;
        }

        var effectivePlan = FinancialCopilot.Billing.Accounts.SubscriptionEffectiveness.ResolveEffectivePlanCode(
            account.SubscriptionPlanCode,
            account.SubscriptionEffectiveTo,
            now) ?? "Free";

        if (effectivePlan == latest.PlanCode)
        {
            if (effectivePlan is "Plus" or "Pro" && !account.SubscriptionActivationIsAuthoritative)
            {
                return changed;
            }

            var nextIndex = Math.Max(latest.PeriodNumber + 1, GetCycleIndex(latest.AnchorAtUtc, now));
            if (nextIndex > latest.PeriodNumber)
            {
                var plan = await FindPlanAsync(effectivePlan, cancellationToken);
                await AddPeriodAsync(
                    account,
                    wallet,
                    plan,
                    latest.AnchorAtUtc,
                    latest.ActivationRevision,
                    nextIndex,
                    now,
                    cancellationToken);
                return true;
            }

            return changed;
        }

        if (effectivePlan == "Free" && latest.PlanCode is "Plus" or "Pro")
        {
            // Do not terminate a paid grant early when upstream eligibility expires. The first
            // Free period begins at the prior 30-day boundary, and only the current cycle is issued.
            var freeAnchor = latest.ExpiresAtUtc;
            var freeIndex = GetCycleIndex(freeAnchor, now);
            var freePlan = await FindPlanAsync("Free", cancellationToken);
            await AddPeriodAsync(
                account,
                wallet,
                freePlan,
                freeAnchor,
                account.SubscriptionRevision,
                freeIndex,
                now,
                cancellationToken);
            return true;
        }

        // A paid-to-paid change is intentionally not allocated here. In particular, this prevents
        // an unapproved Plus-to-Pro transition from receiving a later automatic Pro grant.
        return changed;
    }

    public async Task<IReadOnlyList<SubscriptionCreditApplication>> ApplyUsageChargeAsync(
        Guid customerAccountId,
        DateTimeOffset reservationCreatedAt,
        decimal chargedCredits,
        string? allocationSource,
        string? allowanceDateKey,
        CancellationToken cancellationToken)
    {
        if (chargedCredits <= 0)
        {
            return Array.Empty<SubscriptionCreditApplication>();
        }

        var otherAllowanceConsumed = 0m;
        if (string.Equals(allocationSource, "TelegramDailyFreeAllowance", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(allowanceDateKey))
        {
            var dailyGrant = await dbContext.DailyFreeAllowanceGrants.SingleOrDefaultAsync(
                row => row.CustomerAccountId == customerAccountId && row.AllowanceDateKey == allowanceDateKey,
                cancellationToken);
            if (dailyGrant is not null)
            {
                otherAllowanceConsumed = await dbContext.UsageLedgerEntries
                    .Where(row => row.CustomerAccountId == customerAccountId &&
                                  row.AllocationSource == "TelegramDailyFreeAllowance" &&
                                  row.AllowanceDateKey == allowanceDateKey &&
                                  (row.EntryType == nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Charge) ||
                                   row.EntryType == nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Refund)))
                    .SumAsync(
                        row => row.EntryType == nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Charge)
                            ? row.CreditsCharged
                            : -row.CreditsCharged,
                        cancellationToken);
                var dailyRemaining = Math.Max(0, dailyGrant.Amount - dailyGrant.ExpiredCredits - otherAllowanceConsumed);
                otherAllowanceConsumed = Math.Min(chargedCredits, dailyRemaining);
            }
        }

        var chargeAfterOtherAllowance = chargedCredits - otherAllowanceConsumed;
        if (chargeAfterOtherAllowance <= 0)
        {
            return Array.Empty<SubscriptionCreditApplication>();
        }

        var periods = await dbContext.SubscriptionCreditPeriods
            .Where(row => row.CustomerAccountId == customerAccountId && row.Status == ActiveStatus)
            .ToListAsync(cancellationToken);
        var eligible = periods
            .Where(row => reservationCreatedAt >= row.StartsAtUtc && reservationCreatedAt < row.ExpiresAtUtc)
            .Where(row => row.RemainingCredits > 0)
            .OrderBy(row => row.ExpiresAtUtc)
            .ThenBy(row => row.StartsAtUtc)
            .ToArray();
        var applications = new List<SubscriptionCreditApplication>();
        var remaining = chargeAfterOtherAllowance;
        foreach (var period in eligible)
        {
            if (remaining <= 0) break;
            var applied = Math.Min(period.RemainingCredits, remaining);
            period.RemainingCredits -= applied;
            period.Revision++;
            remaining -= applied;
            applications.Add(new SubscriptionCreditApplication(period.Id, period.GrantLedgerEntryId, applied));
        }
        return applications;
    }

    public async Task<IReadOnlyList<SubscriptionCreditApplication>> ApplyRefundAsync(
        UsageLedgerEntryRow originalCharge,
        decimal refundCredits,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var availableToRestore = originalCharge.SubscriptionCreditsApplied ?? 0m;
        if (availableToRestore <= 0 || originalCharge.RelatedEntryId is not { } grantLedgerEntryId)
        {
            return Array.Empty<SubscriptionCreditApplication>();
        }

        var originalAllocations = await dbContext.UsageCreditAllocations
            .Where(row => row.UsageLedgerEntryId == originalCharge.Id)
            .ToListAsync(cancellationToken);
        var legacyAttribution = originalAllocations.Count == 0;
        if (originalAllocations.Count == 0)
        {
            originalAllocations.Add(new UsageCreditAllocationRow
            {
                UsageLedgerEntryId = originalCharge.Id,
                SubscriptionCreditPeriodId = Guid.Empty,
                GrantLedgerEntryId = grantLedgerEntryId,
                CreditsApplied = availableToRestore
            });
        }

        var refundedRows = await (from allocation in dbContext.UsageCreditAllocations
                                  join refund in dbContext.UsageLedgerEntries on allocation.UsageLedgerEntryId equals refund.Id
                                  where refund.RelatedEntryId == originalCharge.Id &&
                                        refund.EntryType == nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Refund)
                                  select allocation).ToListAsync(cancellationToken);
        var legacyRefunded = legacyAttribution
            ? await dbContext.UsageLedgerEntries
                .Where(row => row.RelatedEntryId == originalCharge.Id &&
                              row.EntryType == nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Refund))
                .SumAsync(row => row.SubscriptionCreditsApplied ?? 0m, cancellationToken)
            : 0m;
        var remainingRefund = refundCredits;
        var restored = new List<SubscriptionCreditApplication>();
        foreach (var allocation in originalAllocations)
        {
            if (remainingRefund <= 0) break;
            var prior = legacyAttribution ? legacyRefunded :
                refundedRows.Where(row => row.SubscriptionCreditPeriodId == allocation.SubscriptionCreditPeriodId)
                    .Sum(row => row.CreditsApplied);
            var available = Math.Max(0, allocation.CreditsApplied - prior);
            if (available <= 0) continue;
            var period = allocation.SubscriptionCreditPeriodId == Guid.Empty
                ? await dbContext.SubscriptionCreditPeriods.SingleOrDefaultAsync(row => row.GrantLedgerEntryId == grantLedgerEntryId, cancellationToken)
                : await dbContext.SubscriptionCreditPeriods.SingleOrDefaultAsync(row => row.Id == allocation.SubscriptionCreditPeriodId, cancellationToken);
            if (period is null || period.Status != ActiveStatus || now >= period.ExpiresAtUtc) continue;
            var amount = Math.Min(Math.Min(available, remainingRefund), period.GrantedCredits - period.RemainingCredits);
            if (amount <= 0) continue;
            period.RemainingCredits += amount;
            period.Revision++;
            remainingRefund -= amount;
            restored.Add(new SubscriptionCreditApplication(period.Id, period.GrantLedgerEntryId, amount));
        }
        return restored;
    }

    public async Task<int> ProcessDuePeriodsAsync(
        int maximumCount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (maximumCount is <= 0 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var accountIds = await dbContext.SubscriptionCreditPeriods
            .Where(row => row.Status == ActiveStatus && row.ExpiresAtUtc <= now)
            .Select(row => row.CustomerAccountId)
            .Distinct()
            .Take(maximumCount)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var accountId in accountIds)
        {
            var account = await dbContext.CustomerAccounts.SingleOrDefaultAsync(
                row => row.Id == accountId,
                cancellationToken);
            var wallet = await dbContext.WalletProjections.SingleOrDefaultAsync(
                row => row.CustomerAccountId == accountId,
                cancellationToken);
            if (account is null || wallet is null)
            {
                continue;
            }

            var changed = await EnsureCurrentPeriodTrackedAsync(account, wallet, now, cancellationToken);
            if (!changed)
            {
                continue;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            processed++;
        }

        return processed;
    }

    private async Task AddPeriodAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        SubscriptionPlanRow plan,
        DateTimeOffset anchorAt,
        long activationRevision,
        long periodNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        decimal transferredCredits = 0m,
        SubscriptionCreditPeriodRow? transferSource = null,
        string? subscriptionIdentity = null)
    {
        if (plan.IncludedCredits <= 0)
        {
            throw new InvalidOperationException($"Subscription plan '{plan.Code}' has no positive credit allowance.");
        }

        var startsAt = anchorAt.AddTicks(PeriodDuration.Ticks * periodNumber);
        var expiresAt = startsAt.Add(PeriodDuration);
        subscriptionIdentity ??= $"legacy:{plan.Code}:{activationRevision}:{anchorAt.UtcDateTime.Ticks}";
        var idempotencyKey = BuildPeriodKey(account.Id, plan.Code, subscriptionIdentity, anchorAt, periodNumber);
        var existing = await dbContext.SubscriptionCreditPeriods
            .SingleOrDefaultAsync(row => row.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            ValidateExistingPeriod(existing, plan, activationRevision, anchorAt, periodNumber, startsAt, expiresAt, transferredCredits);
            return;
        }

        var periodId = Guid.NewGuid();
        var grantLedgerId = Guid.NewGuid();
        var grantKey = $"{idempotencyKey}:grant";
        var row = new SubscriptionCreditPeriodRow
        {
            Id = periodId,
            CustomerAccountId = account.Id,
            PlanCode = plan.Code,
            SubscriptionIdentity = subscriptionIdentity,
            IdempotencyKey = idempotencyKey,
            AnchorAtUtc = anchorAt,
            ActivationRevision = activationRevision,
            PeriodNumber = periodNumber,
            StartsAtUtc = startsAt,
            ExpiresAtUtc = expiresAt,
            GrantedCredits = plan.IncludedCredits + transferredCredits,
            RemainingCredits = plan.IncludedCredits + transferredCredits,
            GrantLedgerEntryId = grantLedgerId,
            Status = ActiveStatus,
            CreatedAtUtc = now,
            Revision = 0
        };
        dbContext.SubscriptionCreditPeriods.Add(row);
        dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
        {
            Id = grantLedgerId,
            CustomerAccountId = account.Id,
            ActorId = Guid.Empty,
            TenantId = account.TenantId,
            EntryType = nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Adjustment),
            OperationCode = GrantOperationCode,
            CreditsCharged = plan.IncludedCredits,
            PricingPolicyVersion = plan.PricingPolicyVersion,
            IdempotencyKey = grantKey,
            OccurredAt = now,
            AuditDescription = $"Subscription allowance granted for {plan.Code}; period {startsAt:O} through {expiresAt:O}.",
            AllocationSource = SubscriptionAllocationSource,
            AllowanceDateKey = periodId.ToString("N")
        });
        if (transferSource is not null && transferredCredits > 0)
        {
            var transferKey = $"{idempotencyKey}:plus-transfer:{transferSource.Id:N}";
            dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
            {
                Id = Guid.NewGuid(), CustomerAccountId = account.Id, ActorId = Guid.Empty, TenantId = account.TenantId,
                EntryType = nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Adjustment),
                OperationCode = TransferOutOperationCode, CreditsCharged = -transferredCredits,
                PricingPolicyVersion = PolicyVersion, IdempotencyKey = $"{transferKey}:out", OccurredAt = now,
                AuditDescription = $"Unused Plus credits transferred into Pro period {startsAt:O}.",
                RelatedEntryId = transferSource.GrantLedgerEntryId, AllocationSource = SubscriptionAllocationSource,
                AllowanceDateKey = transferSource.Id.ToString("N")
            });
            dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
            {
                Id = Guid.NewGuid(), CustomerAccountId = account.Id, ActorId = Guid.Empty, TenantId = account.TenantId,
                EntryType = nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Adjustment),
                OperationCode = TransferInOperationCode, CreditsCharged = transferredCredits,
                PricingPolicyVersion = PolicyVersion, IdempotencyKey = $"{transferKey}:in", OccurredAt = now,
                AuditDescription = $"Transferred unused Plus credits into Pro period {startsAt:O}; expires with that period.",
                RelatedEntryId = grantLedgerId, AllocationSource = SubscriptionAllocationSource,
                AllowanceDateKey = periodId.ToString("N")
            });
            transferSource.RemainingCredits = 0;
            transferSource.Status = TransferredStatus;
            transferSource.ClosedAtUtc = now;
            transferSource.Revision++;
            BillingOutboxWriter.Add(dbContext, "SubscriptionCreditPeriod", periodId, "Billing.SubscriptionCreditsTransferred",
                $"{transferKey}:outbox", new { account.Id, Credits = transferredCredits, FromPlan = "Plus", ToPlan = "Pro", startsAt, expiresAt }, now);
        }
        wallet.Balance += plan.IncludedCredits;
        wallet.UpdatedAt = now;
        wallet.Revision++;
        BillingOutboxWriter.Add(
            dbContext,
            "SubscriptionCreditPeriod",
            periodId,
            "Billing.SubscriptionCreditsGranted",
            $"{idempotencyKey}:granted",
            new { account.Id, account.TenantId, plan.Code, Credits = plan.IncludedCredits, TransferredCredits = transferredCredits, TotalCredits = row.GrantedCredits, startsAt, expiresAt },
            now);
    }

    private void ClosePeriod(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        SubscriptionCreditPeriodRow period,
        DateTimeOffset now,
        string closedStatus,
        string operationCode)
    {
        var unused = period.RemainingCredits;
        if (unused > 0)
        {
            if (wallet.Balance - unused < wallet.ReservedAmount)
            {
                throw new InvalidOperationException("Subscription credits cannot be closed while reserved wallet credits remain.");
            }

            var key = $"{period.IdempotencyKey}:{closedStatus.ToLowerInvariant()}";
            dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
            {
                Id = Guid.NewGuid(),
                CustomerAccountId = account.Id,
                ActorId = Guid.Empty,
                TenantId = account.TenantId,
                EntryType = nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Adjustment),
                OperationCode = operationCode,
                CreditsCharged = -unused,
                PricingPolicyVersion = PolicyVersion,
                IdempotencyKey = key,
                OccurredAt = now,
                AuditDescription = $"Unused {period.PlanCode} subscription credits {closedStatus.ToLowerInvariant()} at the 30-day period boundary.",
                RelatedEntryId = period.GrantLedgerEntryId,
                AllocationSource = SubscriptionAllocationSource,
                AllowanceDateKey = period.Id.ToString("N")
            });
            wallet.Balance -= unused;
            wallet.UpdatedAt = now;
            wallet.Revision++;
            BillingOutboxWriter.Add(
                dbContext,
                "SubscriptionCreditPeriod",
                period.Id,
                closedStatus == ExpiredStatus ? "Billing.SubscriptionCreditsExpired" : "Billing.SubscriptionCreditsForfeited",
                $"{period.IdempotencyKey}:{closedStatus.ToLowerInvariant()}:outbox",
                new
                {
                    AccountId = account.Id,
                    period.PlanCode,
                    Credits = unused,
                    PeriodId = period.Id,
                    period.ExpiresAtUtc
                },
                now);
        }

        period.RemainingCredits = 0;
        period.Status = closedStatus;
        period.ClosedAtUtc = now;
        period.Revision++;
    }

    private async Task<SubscriptionCreditPeriodRow?> FindLatestPeriodAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken)
    {
        var periods = await dbContext.SubscriptionCreditPeriods
            .Where(row => row.CustomerAccountId == customerAccountId)
            .ToListAsync(cancellationToken);
        return periods
            .OrderByDescending(row => row.AnchorAtUtc)
            .ThenByDescending(row => row.ActivationRevision)
            .ThenByDescending(row => row.PeriodNumber)
            .ThenByDescending(row => row.CreatedAtUtc)
            .FirstOrDefault();
    }

    private async Task<SubscriptionPlanRow> FindPlanAsync(string planCode, CancellationToken cancellationToken) =>
        await dbContext.SubscriptionPlans.SingleOrDefaultAsync(row => row.Code == planCode, cancellationToken) ??
        throw new InvalidOperationException($"Subscription plan '{planCode}' is not configured.");

    private static async Task<T> WithAccountLockAsync<T>(
        Guid accountId,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var gate = AllocationLocks.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task WithAccountLockAsync(
        Guid accountId,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        var gate = AllocationLocks.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            gate.Release();
        }
    }

    private static long GetCycleIndex(DateTimeOffset anchorAt, DateTimeOffset now) =>
        now <= anchorAt ? 0 : Math.Max(0, (now - anchorAt).Ticks / PeriodDuration.Ticks);

    private static string BuildPeriodKey(
        Guid accountId,
        string planCode,
        string subscriptionIdentity,
        DateTimeOffset anchorAt,
        long periodNumber) =>
        $"subscription-credit:{accountId:N}:{planCode}:{subscriptionIdentity}:{periodNumber}";

    private static void ValidateExistingPeriod(
        SubscriptionCreditPeriodRow existing,
        SubscriptionPlanRow plan,
        long activationRevision,
        DateTimeOffset anchorAt,
        long periodNumber,
        DateTimeOffset startsAt,
        DateTimeOffset expiresAt,
        decimal transferredCredits)
    {
        if (existing.PlanCode != plan.Code ||
            existing.ActivationRevision != activationRevision ||
            existing.AnchorAtUtc != anchorAt ||
            existing.PeriodNumber != periodNumber ||
            existing.StartsAtUtc != startsAt ||
            existing.ExpiresAtUtc != expiresAt ||
            existing.GrantedCredits != plan.IncludedCredits + transferredCredits)
        {
            throw new InvalidOperationException("A subscription credit period idempotency key was reused for different terms.");
        }
    }
}

public sealed record SubscriptionCreditApplication(
    Guid PeriodId,
    Guid GrantLedgerEntryId,
    decimal CreditsApplied);
