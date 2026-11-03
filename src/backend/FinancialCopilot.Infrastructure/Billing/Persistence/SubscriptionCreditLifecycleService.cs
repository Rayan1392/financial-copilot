using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

/// <summary>
/// Applies period-aware subscription grants to the existing wallet and immutable usage ledger.
/// Legacy wallets without period attribution are intentionally left untouched until reconciled.
/// </summary>
public sealed class SubscriptionCreditLifecycleService(BillingDbContext dbContext)
{
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
    private const string PolicyVersion = "subscription-credit-full-window-v1";
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
        if (planCode == "Free")
        {
            await EnsureInitialFreeGrantAsync(account, wallet, plan, anchor, now, cancellationToken);
            return;
        }
        await AddPeriodAsync(
            account,
            wallet,
            plan,
            anchor,
            account.SubscriptionRevision,
            0,
            now,
            cancellationToken,
            expiresAtUtc: account.SubscriptionEffectiveTo);
    }

    private async Task EnsureInitialFreeGrantAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        SubscriptionPlanRow freePlan,
        DateTimeOffset assignedAnchor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (account.UserId is not { } userId || account.SubscriptionPlanCode != "Free") return;

        var evidence = await dbContext.FreeCreditGrantEvidence
            .SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (evidence is not null) return;

        var historicalFree = await dbContext.SubscriptionCreditPeriods
            .Where(row => row.CustomerAccountId == account.Id && row.PlanCode == "Free")
            .OrderBy(row => row.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (historicalFree is not null)
        {
            dbContext.FreeCreditGrantEvidence.Add(new FreeCreditGrantEvidenceRow
            {
                UserId = userId,
                CustomerAccountId = account.Id,
                GrantLedgerEntryId = historicalFree.GrantLedgerEntryId,
                GrantedAtUtc = historicalFree.CreatedAtUtc,
                AssignedExpiresAtUtc = null,
                EvidenceSource = "historical-period"
            });
            return;
        }

        // A null paid-subscription snapshot does not itself authorize a new Free grant.
        // Paid history or an unclassified historical grant means this account needs review.
        var hasPaidHistory = await dbContext.ExternalPlanSubscriptions.AnyAsync(
            row => row.CustomerAccountId == account.Id, cancellationToken) ||
            await dbContext.PaidExpiryCooldowns.AnyAsync(row => row.CustomerAccountId == account.Id, cancellationToken);
        var hasUnclassifiedGrant = await dbContext.UsageLedgerEntries.AnyAsync(row =>
            row.CustomerAccountId == account.Id && row.OperationCode == GrantOperationCode, cancellationToken);
        if (hasPaidHistory || hasUnclassifiedGrant) return;

        var expiresAt = assignedAnchor.AddDays(30);
        await AddPeriodAsync(account, wallet, freePlan, assignedAnchor, 0, 0, now, cancellationToken,
            subscriptionIdentity: $"free-once:user:{userId:N}", expiresAtUtc: expiresAt);
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
        DateTimeOffset? effectiveFrom = null,
        bool authoritativeWindow = false) =>
        // Cross-process correctness comes from the account revision concurrency token and the
        // unique period/ledger idempotency keys, not an in-process semaphore.
            ApplyPlanChangeCoreAsync(account, wallet, previousPlanCode, newPlanCode, now, cancellationToken,
                effectiveFrom, authoritativeWindow);

    private async Task<string?> ApplyPlanChangeCoreAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        string previousPlanCode,
        string newPlanCode,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        DateTimeOffset? effectiveFrom,
        bool authoritativeWindow)
    {
        if (newPlanCode == "Max")
        {
            // Unlimited is an entitlement window only. It never creates a numeric allowance source.
            return null;
        }

        if (newPlanCode is "Plus" or "Pro" && !authoritativeWindow)
        {
            var current = await FindLatestPeriodAsync(account.Id, cancellationToken);
            if (previousPlanCode == "Free" && current is { PlanCode: "Free", Status: ActiveStatus })
                ClosePeriod(account, wallet, current, now, ForfeitedStatus, ForfeitureOperationCode);
            return "Paid credit allocation is held until a complete authoritative TahlilApp plan window is available.";
        }

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
                0,
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
                        0, now, cancellationToken);
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
                0, now, cancellationToken);
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
                0,
                now,
                cancellationToken);
            return null;
        }

        // External expiry or an explicit transition to Free revokes paid capabilities immediately.
        // Existing credits remain valid only until their authoritative source expiration.
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
        var incomingKeys = subscriptions.Select(item => item.IdentityKey).ToHashSet(StringComparer.Ordinal);
        var storedSubscriptions = await dbContext.ExternalPlanSubscriptions
            .Where(item => item.CustomerAccountId == account.Id)
            .ToListAsync(cancellationToken);
        var newlyRevoked = new List<ExternalPlanSubscriptionRow>();
        foreach (var stored in storedSubscriptions)
        {
            if (!stored.IsRevoked && !incomingKeys.Contains(stored.IdentityKey) && stored.ExpiresAtUtc > now)
            {
                stored.IsRevoked = true;
                stored.RevokedAtUtc = now;
                newlyRevoked.Add(stored);
            }
        }

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

        if (newlyRevoked.Count > 0)
        {
            // A reservation has no source allocation until finalization. Release every in-flight
            // hold before forfeiting revoked sources so none can later charge those credits.
            await ReleasePendingReservationsForRevocationAsync(account, wallet, now, cancellationToken);
            var revokedKeys = newlyRevoked.Select(item => item.IdentityKey).ToHashSet(StringComparer.Ordinal);
            var periods = await dbContext.SubscriptionCreditPeriods
                .Where(row => row.CustomerAccountId == account.Id && row.Status == ActiveStatus)
                .ToListAsync(cancellationToken);
            foreach (var period in periods.Where(row => revokedKeys.Contains(row.SubscriptionIdentity)))
            {
                ClosePeriod(account, wallet, period, now, ForfeitedStatus, ForfeitureOperationCode);
            }
            var revokedIds = newlyRevoked.Select(row => row.Id).ToArray();
            var topUps = await dbContext.TopUpCreditSources.Where(row => row.CustomerAccountId == account.Id &&
                revokedIds.Contains(row.ExternalPlanSubscriptionId) && row.Status == ActiveStatus).ToListAsync(cancellationToken);
            foreach (var source in topUps)
            {
                var holds = await dbContext.TopUpUsageCreditHolds.Where(row => row.TopUpCreditSourceId == source.Id).ToListAsync(cancellationToken);
                dbContext.TopUpUsageCreditHolds.RemoveRange(holds);
                var forfeited = source.RemainingCredits;
                if (forfeited > 0)
                {
                    wallet.Balance = Math.Max(0m, wallet.Balance - forfeited);
                    wallet.UpdatedAt = now;
                    wallet.Revision++;
                    dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
                    {
                        Id = Guid.NewGuid(), CustomerAccountId = account.Id, ActorId = Guid.Empty, TenantId = account.TenantId,
                        EntryType = nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Adjustment),
                        OperationCode = "Billing.TahlilAppTopUpForfeiture", CreditsCharged = -forfeited,
                        PricingPolicyVersion = "tahlilapp-topup-v1", IdempotencyKey = $"tahlilapp-topup-forfeiture:{source.Id:N}",
                        OccurredAt = now, RelatedEntryId = source.GrantLedgerEntryId,
                        AuditDescription = "Unused TahlilApp top-up credits forfeited after authoritative subscription revocation.",
                        AllocationSource = "tahlilapp-topup"
                    });
                }
                source.RemainingCredits = 0;
                source.Status = ForfeitedStatus;
                source.Revision++;
            }
        }

        await EnsureExternalPlanPeriodsAsync(account, wallet, now, cancellationToken);
    }

    private async Task ReleasePendingReservationsForRevocationAsync(
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pending = await dbContext.UsageReservations
            .Where(row => row.CustomerAccountId == account.Id &&
                          row.Status == nameof(FinancialCopilot.Billing.Usage.UsageReservationStatus.Reserved))
            .ToListAsync(cancellationToken);
        if (pending.Count == 0) return;

        var released = pending.Sum(row => row.ReservedCredits);
        foreach (var reservation in pending)
        {
            reservation.Status = nameof(FinancialCopilot.Billing.Usage.UsageReservationStatus.Released);
            reservation.FinalizationReason = "Released because the authoritative TahlilApp subscription snapshot revoked its credit source.";
            BillingOutboxWriter.Add(dbContext, "UsageReservation", reservation.Id,
                "Billing.UsageReservationReleased", $"{reservation.IdempotencyKey}:revoked-release",
                new { reservation.CustomerAccountId, reservation.ReservedCredits, Reason = "subscription-revoked" }, now);
        }

        wallet.ReservedAmount = Math.Max(0m, wallet.ReservedAmount - released);
        wallet.UpdatedAt = now;
        wallet.Revision++;
    }

    public async Task<bool> IsPaidExpiryCooldownActiveAsync(Guid customerAccountId, DateTimeOffset now, CancellationToken cancellationToken)
        => await GetActivePaidExpiryCooldownEndAsync(customerAccountId, now, cancellationToken) is not null;

    public async Task<DateTimeOffset?> GetActivePaidExpiryCooldownEndAsync(
        Guid customerAccountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var hasPaid = await dbContext.ExternalPlanSubscriptions.AnyAsync(
            row => row.CustomerAccountId == customerAccountId && !row.IsRevoked &&
                   row.StartsAtUtc <= now && row.ExpiresAtUtc > now &&
                   (row.PlanCode == "Plus" || row.PlanCode == "Pro" || row.PlanCode == "Max"), cancellationToken);
        if (hasPaid) return null;
        var cooldown = dbContext.PaidExpiryCooldowns.Local.SingleOrDefault(
            row => row.CustomerAccountId == customerAccountId) ??
            await dbContext.PaidExpiryCooldowns.SingleOrDefaultAsync(
                row => row.CustomerAccountId == customerAccountId, cancellationToken);
        return cooldown is not null && now < cooldown.CooldownUntilUtc
            ? cooldown.CooldownUntilUtc
            : null;
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
            .Where(row => !row.IsRevoked && row.StartsAtUtc <= now && row.ExpiresAtUtc > now)
            .OrderBy(row => row.ExpiresAtUtc)
            .ThenBy(row => row.PlanCode == "Plus" ? 0 : 1)
            .ThenBy(row => row.StartsAtUtc)
            .ToArray();
        var activePaid = active.Where(row => row.PlanCode is "Plus" or "Pro" or "Max").ToArray();
        var effective = FinancialCopilot.Domain.Identity.TahlilApp.TahlilAppSubscriptionPolicy.ResolveActivePlanSet(
            activePaid.Select(row => new FinancialCopilot.Domain.Identity.TahlilApp.TahlilAppPlanSubscription(
                row.IdentityKey, row.PlanCode, row.StartsAtUtc, row.ExpiresAtUtc, row.IdentityKey)).ToArray(),
            now);
        account.SubscriptionPlanCode = effective.PlanCode;
        account.SubscriptionEffectiveFrom = effective.EffectiveFrom;
        account.SubscriptionEffectiveTo = effective.EffectiveTo;
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
                if (subscription.PlanCode == "Max")
                {
                    // Max has no numeric source or wallet grant.
                    continue;
                }

                var periodExists = await dbContext.SubscriptionCreditPeriods.AnyAsync(row =>
                    row.CustomerAccountId == account.Id && row.SubscriptionIdentity == subscription.IdentityKey &&
                    row.PeriodNumber == 0, cancellationToken);
                periodExists |= dbContext.SubscriptionCreditPeriods.Local.Any(row =>
                    row.CustomerAccountId == account.Id && row.SubscriptionIdentity == subscription.IdentityKey &&
                    row.PeriodNumber == 0);
                if (periodExists)
                {
                    continue;
                }

                var plan = await FindPlanAsync(subscription.PlanCode, cancellationToken);
                await AddPeriodAsync(account, wallet, plan, subscription.StartsAtUtc, 0, 0, now,
                    cancellationToken, subscriptionIdentity: subscription.IdentityKey,
                    expiresAtUtc: subscription.ExpiresAtUtc);
                changed = true;
            }

            return changed;
        }

        var lastPaidExpiry = subscriptions
            .Where(row => row.PlanCode is "Plus" or "Pro" or "Max")
            .Select(row => (DateTimeOffset?)(row.RevokedAtUtc ?? row.ExpiresAtUtc))
            .Max();
        PaidExpiryCooldownRow? cooldown = null;
        if (lastPaidExpiry is { } expiry)
        {
            cooldown = await dbContext.PaidExpiryCooldowns.SingleOrDefaultAsync(
                row => row.CustomerAccountId == account.Id, cancellationToken);
            var observedRevocation = subscriptions
                .Where(row => row.PlanCode is "Plus" or "Pro" or "Max" && row.IsRevoked)
                .Select(row => (DateTimeOffset?)row.RevokedAtUtc)
                .Max();
            var cooldownEnd = expiry.AddDays(30);
            if (cooldown is null)
            {
                cooldown = new PaidExpiryCooldownRow
                {
                    CustomerAccountId = account.Id,
                    LastPaidExpiresAtUtc = expiry,
                    CooldownUntilUtc = cooldownEnd,
                    RevocationObservedAtUtc = observedRevocation,
                    Revision = 0
                };
                dbContext.PaidExpiryCooldowns.Add(cooldown);
                changed = true;
            }
            else if (expiry > cooldown.LastPaidExpiresAtUtc)
            {
                cooldown.LastPaidExpiresAtUtc = expiry;
                cooldown.CooldownUntilUtc = cooldownEnd;
                cooldown.RevocationObservedAtUtc = observedRevocation;
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

            return changed;
        }

        // Free grant assignment belongs to first local wallet provisioning. An empty paid
        // snapshot or a cooldown boundary is never a reason to create another Free source.
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

        // Neither a 30-day rollover nor a paid-to-Free transition creates credit.

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
        CancellationToken cancellationToken,
        Guid? reservationId = null)
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

        if (reservationId is { } heldReservationId)
        {
            var holds = await dbContext.UsageCreditHolds
                .Where(row => row.UsageReservationId == heldReservationId)
                .ToListAsync(cancellationToken);
            if (holds.Count > 0)
            {
                var heldPeriodIds = holds.Select(row => row.SubscriptionCreditPeriodId).ToArray();
                var heldPeriods = await dbContext.SubscriptionCreditPeriods
                    .Where(row => heldPeriodIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
                var heldApplications = new List<SubscriptionCreditApplication>();
                var amountLeft = chargeAfterOtherAllowance;
                foreach (var hold in holds.OrderBy(row => heldPeriods[row.SubscriptionCreditPeriodId].ExpiresAtUtc)
                             .ThenBy(row => heldPeriods[row.SubscriptionCreditPeriodId].StartsAtUtc)
                             .ThenBy(row => row.SubscriptionCreditPeriodId))
                {
                    var period = heldPeriods[hold.SubscriptionCreditPeriodId];
                    var applied = Math.Min(hold.CreditsHeld, amountLeft);
                    if (applied > 0)
                    {
                        period.RemainingCredits -= applied;
                        period.Revision++;
                        heldApplications.Add(new SubscriptionCreditApplication(period.Id, period.GrantLedgerEntryId, applied));
                        amountLeft -= applied;
                    }
                    dbContext.UsageCreditHolds.Remove(hold);
                }
                return heldApplications;
            }
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

    public async Task<decimal> GetExpiredUnheldCreditsAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expired = await dbContext.SubscriptionCreditPeriods
            .Where(row => row.CustomerAccountId == accountId && row.Status == ActiveStatus && row.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        var expiredTopUps = await dbContext.TopUpCreditSources
            .Where(row => row.CustomerAccountId == accountId && row.Status == ActiveStatus && row.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        var topUpIds = expiredTopUps.Select(row => row.Id).ToArray();
        var topUpHolds = topUpIds.Length == 0 ? [] : await dbContext.TopUpUsageCreditHolds
            .Where(row => topUpIds.Contains(row.TopUpCreditSourceId)).ToListAsync(cancellationToken);
        var expiredTopUpUnheld = expiredTopUps.Sum(row => Math.Max(0m, row.RemainingCredits -
            topUpHolds.Where(hold => hold.TopUpCreditSourceId == row.Id).Sum(hold => hold.CreditsHeld)));
        if (expired.Count == 0) return expiredTopUpUnheld;
        var ids = expired.Select(row => row.Id).ToArray();
        var holds = await dbContext.UsageCreditHolds.Where(row => ids.Contains(row.SubscriptionCreditPeriodId))
            .ToListAsync(cancellationToken);
        return expiredTopUpUnheld + expired.Sum(row => Math.Max(0m, row.RemainingCredits -
            holds.Where(hold => hold.SubscriptionCreditPeriodId == row.Id).Sum(hold => hold.CreditsHeld)));
    }

    public async Task HoldCreditsAsync(Guid accountId, Guid reservationId, decimal amount,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var periods = await dbContext.SubscriptionCreditPeriods
            .Where(row => row.CustomerAccountId == accountId && row.Status == ActiveStatus &&
                          row.StartsAtUtc <= now && row.ExpiresAtUtc > now)
            .OrderBy(row => row.ExpiresAtUtc).ThenBy(row => row.StartsAtUtc).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var ids = periods.Select(row => row.Id).ToArray();
        var existing = await dbContext.UsageCreditHolds.Where(row => ids.Contains(row.SubscriptionCreditPeriodId))
            .ToListAsync(cancellationToken);
        var left = amount;
        foreach (var period in periods)
        {
            if (left <= 0) break;
            var alreadyHeld = existing.Where(row => row.SubscriptionCreditPeriodId == period.Id).Sum(row => row.CreditsHeld)
                + dbContext.UsageCreditHolds.Local.Where(row => row.SubscriptionCreditPeriodId == period.Id &&
                    dbContext.Entry(row).State == EntityState.Added).Sum(row => row.CreditsHeld);
            var held = Math.Min(left, Math.Max(0m, period.RemainingCredits - alreadyHeld));
            if (held <= 0) continue;
            var prior = existing.FirstOrDefault(row => row.UsageReservationId == reservationId && row.SubscriptionCreditPeriodId == period.Id)
                ?? dbContext.UsageCreditHolds.Local.FirstOrDefault(row => row.UsageReservationId == reservationId && row.SubscriptionCreditPeriodId == period.Id);
            if (prior is null)
                dbContext.UsageCreditHolds.Add(new UsageCreditHoldRow { Id = Guid.NewGuid(), UsageReservationId = reservationId,
                    SubscriptionCreditPeriodId = period.Id, CreditsHeld = held });
            else prior.CreditsHeld += held;
            left -= held;
        }
        var topUps = await dbContext.TopUpCreditSources.Where(row => row.CustomerAccountId == accountId &&
            row.Status == ActiveStatus && row.CreatedAtUtc <= now && row.ExpiresAtUtc > now)
            .OrderBy(row => row.ExpiresAtUtc).ThenBy(row => row.CreatedAtUtc).ThenBy(row => row.Id).ToListAsync(cancellationToken);
        var topUpIds = topUps.Select(row => row.Id).ToArray();
        var topUpExisting = topUpIds.Length == 0 ? [] : await dbContext.TopUpUsageCreditHolds
            .Where(row => topUpIds.Contains(row.TopUpCreditSourceId)).ToListAsync(cancellationToken);
        foreach (var source in topUps)
        {
            if (left <= 0) break;
            var heldAlready = topUpExisting.Where(row => row.TopUpCreditSourceId == source.Id).Sum(row => row.CreditsHeld) +
                dbContext.TopUpUsageCreditHolds.Local.Where(row => row.TopUpCreditSourceId == source.Id &&
                    dbContext.Entry(row).State == EntityState.Added).Sum(row => row.CreditsHeld);
            var held = Math.Min(left, Math.Max(0m, source.RemainingCredits - heldAlready));
            if (held <= 0) continue;
            var prior = topUpExisting.FirstOrDefault(row => row.UsageReservationId == reservationId && row.TopUpCreditSourceId == source.Id) ??
                dbContext.TopUpUsageCreditHolds.Local.FirstOrDefault(row => row.UsageReservationId == reservationId && row.TopUpCreditSourceId == source.Id);
            if (prior is null) dbContext.TopUpUsageCreditHolds.Add(new TopUpUsageCreditHoldRow
                { Id = Guid.NewGuid(), UsageReservationId = reservationId, TopUpCreditSourceId = source.Id, CreditsHeld = held });
            else prior.CreditsHeld += held;
            left -= held;
        }
    }

    public async Task<decimal> ReleaseHoldsAsync(Guid reservationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var holds = await dbContext.UsageCreditHolds.Where(row => row.UsageReservationId == reservationId)
            .ToListAsync(cancellationToken);
        dbContext.UsageCreditHolds.RemoveRange(holds);
        var topUpHolds = await dbContext.TopUpUsageCreditHolds.Where(row => row.UsageReservationId == reservationId)
            .ToListAsync(cancellationToken);
        decimal expiredCredits = 0m;
        foreach (var hold in topUpHolds)
        {
            var source = await dbContext.TopUpCreditSources.SingleAsync(row => row.Id == hold.TopUpCreditSourceId, cancellationToken);
            if (source.Status == ExpiredStatus || source.ExpiresAtUtc <= now)
            {
                var heldByOtherReservations = await dbContext.TopUpUsageCreditHolds
                    .Where(row => row.TopUpCreditSourceId == source.Id && row.UsageReservationId != reservationId)
                    .SumAsync(row => (decimal?)row.CreditsHeld, cancellationToken) ?? 0m;
                var expiredRemainder = Math.Max(0m, source.RemainingCredits - heldByOtherReservations);
                source.RemainingCredits = heldByOtherReservations;
                source.Status = ExpiredStatus;
                source.Revision++;
                expiredCredits += expiredRemainder;
                if (expiredRemainder > 0)
                {
                    var account = await dbContext.CustomerAccounts.SingleAsync(row => row.Id == source.CustomerAccountId, cancellationToken);
                    dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
                    {
                        Id = Guid.NewGuid(), CustomerAccountId = source.CustomerAccountId, ActorId = Guid.Empty,
                        TenantId = account.TenantId, EntryType = nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Adjustment),
                        OperationCode = "Billing.TahlilAppTopUpExpiredHoldRelease", CreditsCharged = -expiredRemainder,
                        PricingPolicyVersion = "tahlilapp-topup-v1",
                        IdempotencyKey = $"tahlilapp-topup-expired-hold:{hold.Id:N}", OccurredAt = now,
                        RelatedEntryId = source.GrantLedgerEntryId,
                        AuditDescription = "Unused top-up credits, including the released hold, expired with the original subscription window.",
                        AllocationSource = "tahlilapp-topup"
                    });
                }
            }
        }
        dbContext.TopUpUsageCreditHolds.RemoveRange(topUpHolds);
        return expiredCredits;
    }

    public async Task<TopUpUsageChargeResult> ApplyTopUpUsageChargeAsync(
        Guid customerAccountId, DateTimeOffset reservationCreatedAt, decimal chargedCredits, DateTimeOffset now,
        Guid? reservationId, CancellationToken cancellationToken)
    {
        if (chargedCredits <= 0) return new TopUpUsageChargeResult(Array.Empty<TopUpCreditApplication>(), 0m);
        var sources = new List<TopUpCreditSourceRow>();
        var held = reservationId is { } id
            ? await dbContext.TopUpUsageCreditHolds.Where(row => row.UsageReservationId == id).ToListAsync(cancellationToken)
            : [];
        if (held.Count > 0)
        {
            var ids = held.Select(row => row.TopUpCreditSourceId).ToArray();
            sources = await dbContext.TopUpCreditSources.Where(row => ids.Contains(row.Id)).ToListAsync(cancellationToken);
        }
        else
        {
            sources = await dbContext.TopUpCreditSources.Where(row => row.CustomerAccountId == customerAccountId &&
                row.Status == ActiveStatus && row.CreatedAtUtc <= reservationCreatedAt && row.RemainingCredits > 0)
                .OrderBy(row => row.ExpiresAtUtc).ThenBy(row => row.CreatedAtUtc).ThenBy(row => row.Id).ToListAsync(cancellationToken);
        }
        var amountLeft = chargedCredits;
        var applications = new List<TopUpCreditApplication>();
        var expiredRemainder = 0m;
        foreach (var source in sources.OrderBy(row => row.ExpiresAtUtc).ThenBy(row => row.CreatedAtUtc).ThenBy(row => row.Id))
        {
            if (amountLeft <= 0) break;
            var available = held.Count > 0 ? held.Single(row => row.TopUpCreditSourceId == source.Id).CreditsHeld : source.RemainingCredits;
            var applied = Math.Min(available, amountLeft);
            if (applied <= 0) continue;
            source.RemainingCredits -= applied;
            source.Revision++;
            applications.Add(new(source.Id, source.GrantLedgerEntryId, applied));
            amountLeft -= applied;
            if (source.Status == ExpiredStatus || source.ExpiresAtUtc <= now)
            {
                var heldByOtherReservationsQuery = dbContext.TopUpUsageCreditHolds
                    .Where(row => row.TopUpCreditSourceId == source.Id);
                if (reservationId is { } currentReservationId)
                    heldByOtherReservationsQuery = heldByOtherReservationsQuery
                        .Where(row => row.UsageReservationId != currentReservationId);
                var heldByOtherReservations = await heldByOtherReservationsQuery
                    .SumAsync(row => (decimal?)row.CreditsHeld, cancellationToken) ?? 0m;
                var forfeited = Math.Max(0m, source.RemainingCredits - heldByOtherReservations);
                if (forfeited > 0)
                {
                    source.RemainingCredits = heldByOtherReservations;
                    expiredRemainder += forfeited;
                    var account = await dbContext.CustomerAccounts.SingleAsync(row => row.Id == source.CustomerAccountId, cancellationToken);
                    dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
                    {
                        Id = Guid.NewGuid(), CustomerAccountId = source.CustomerAccountId, ActorId = Guid.Empty,
                        TenantId = account.TenantId, EntryType = nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Adjustment),
                        OperationCode = "Billing.TahlilAppTopUpExpiredHoldRemainder", CreditsCharged = -forfeited,
                        PricingPolicyVersion = "tahlilapp-topup-v1", IdempotencyKey = $"tahlilapp-topup-held-remainder:{reservationId}:{source.Id:N}",
                        OccurredAt = now, RelatedEntryId = source.GrantLedgerEntryId,
                        AuditDescription = "Unconsumed reservation hold expired with the original top-up window.",
                        AllocationSource = "tahlilapp-topup"
                    });
                }
                source.Status = ExpiredStatus;
            }
        }
        if (held.Count > 0) dbContext.TopUpUsageCreditHolds.RemoveRange(held);
        return new TopUpUsageChargeResult(applications, expiredRemainder);
    }

    public async Task<SubscriptionCreditRefundResult> ApplyTopUpRefundAsync(
        UsageLedgerEntryRow originalCharge, decimal refundCredits, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var allocations = await dbContext.TopUpUsageCreditAllocations.Where(row => row.UsageLedgerEntryId == originalCharge.Id).ToListAsync(cancellationToken);
        if (allocations.Count == 0 || refundCredits <= 0)
            return new SubscriptionCreditRefundResult([], []);
        var refunded = await (from allocation in dbContext.TopUpUsageCreditAllocations
            join refund in dbContext.UsageLedgerEntries on allocation.UsageLedgerEntryId equals refund.Id
            where refund.RelatedEntryId == originalCharge.Id && refund.EntryType == nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Refund)
            select allocation).ToListAsync(cancellationToken);
        var restored = new List<SubscriptionCreditApplication>();
        var expired = new List<SubscriptionCreditApplication>();
        var left = refundCredits;
        foreach (var allocation in allocations)
        {
            if (left <= 0) break;
            var prior = refunded.Where(row => row.TopUpCreditSourceId == allocation.TopUpCreditSourceId).Sum(row => row.CreditsApplied);
            var available = Math.Max(0m, allocation.CreditsApplied - prior);
            var source = await dbContext.TopUpCreditSources.SingleAsync(row => row.Id == allocation.TopUpCreditSourceId, cancellationToken);
            var amount = Math.Min(Math.Min(available, left), source.GrantedCredits - source.RemainingCredits);
            if (amount <= 0) continue;
            left -= amount;
            var item = new SubscriptionCreditApplication(source.Id, source.GrantLedgerEntryId, amount);
            if (source.Status == ActiveStatus && now < source.ExpiresAtUtc)
            {
                source.RemainingCredits += amount;
                source.Revision++;
                restored.Add(item);
            }
            else expired.Add(item);
        }
        return new SubscriptionCreditRefundResult(restored, expired);
    }

    public async Task<SubscriptionCreditRefundResult> ApplyRefundAsync(
        UsageLedgerEntryRow originalCharge,
        decimal refundCredits,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var availableToRestore = originalCharge.SubscriptionCreditsApplied ?? 0m;
        if (availableToRestore <= 0 || originalCharge.RelatedEntryId is not { } grantLedgerEntryId)
        {
            return new SubscriptionCreditRefundResult(Array.Empty<SubscriptionCreditApplication>(), Array.Empty<SubscriptionCreditApplication>());
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
        var expired = new List<SubscriptionCreditApplication>();
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
            if (period is null) continue;
            var amount = Math.Min(Math.Min(available, remainingRefund), period.GrantedCredits - period.RemainingCredits);
            if (amount <= 0) continue;
            remainingRefund -= amount;
            var application = new SubscriptionCreditApplication(period.Id, period.GrantLedgerEntryId, amount);
            if (period.Status == ActiveStatus && now < period.ExpiresAtUtc)
            {
                period.RemainingCredits += amount;
                period.Revision++;
                restored.Add(application);
            }
            else expired.Add(application);
        }
        return new SubscriptionCreditRefundResult(restored, expired);
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
            .OrderBy(accountId => accountId)
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

    public async Task<int> ProcessDueTopUpsAsync(int maximumCount, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (maximumCount <= 0) return 0;
        var sourceIds = await dbContext.TopUpCreditSources.AsNoTracking()
            .Where(row => row.Status == ActiveStatus && row.ExpiresAtUtc <= now)
            .OrderBy(row => row.ExpiresAtUtc).ThenBy(row => row.Id)
            .Select(row => row.Id).Take(maximumCount).ToListAsync(cancellationToken);
        var processed = 0;
        foreach (var sourceId in sourceIds)
        {
            const int maximumConcurrencyAttempts = 3;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var source = await dbContext.TopUpCreditSources.SingleOrDefaultAsync(row => row.Id == sourceId, cancellationToken);
                    if (source is null || source.Status != ActiveStatus || source.ExpiresAtUtc > now) break;
                    var holds = await dbContext.TopUpUsageCreditHolds.Where(row => row.TopUpCreditSourceId == source.Id).ToListAsync(cancellationToken);
                    var held = holds.Sum(row => row.CreditsHeld);
                    var expired = Math.Max(0m, source.RemainingCredits - held);
                    var wallet = await dbContext.WalletProjections.SingleAsync(row => row.CustomerAccountId == source.CustomerAccountId, cancellationToken);
                    var account = await dbContext.CustomerAccounts.SingleAsync(row => row.Id == source.CustomerAccountId, cancellationToken);
                    if (expired > 0)
                    {
                        wallet.Balance -= expired; wallet.UpdatedAt = now; wallet.Revision++;
                        dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
                        {
                            Id = Guid.NewGuid(), CustomerAccountId = source.CustomerAccountId, ActorId = Guid.Empty,
                            TenantId = account.TenantId, EntryType = nameof(FinancialCopilot.Billing.Usage.UsageLedgerEntryType.Adjustment),
                            OperationCode = "Billing.TahlilAppTopUpExpiry", CreditsCharged = -expired,
                            PricingPolicyVersion = "tahlilapp-topup-v1", IdempotencyKey = $"tahlilapp-topup-expiry:{source.Id:N}",
                            OccurredAt = now, RelatedEntryId = source.GrantLedgerEntryId,
                            AuditDescription = "Unused TahlilApp top-up credits expired with the original subscription window.",
                            AllocationSource = "tahlilapp-topup"
                        });
                    }
                    source.RemainingCredits = held;
                    source.Status = ExpiredStatus;
                    source.Revision++;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    processed++;
                    break;
                }
                catch (DbUpdateConcurrencyException) when (attempt < maximumConcurrencyAttempts)
                {
                    // A concurrent settlement may consume part of the persisted hold while
                    // expiry is being applied. SaveChanges rolled back; reload the source,
                    // holds and wallet before calculating the unused remainder again.
                    dbContext.ChangeTracker.Clear();
                }
            }
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
        string? subscriptionIdentity = null,
        DateTimeOffset? expiresAtUtc = null)
    {
        if (periodNumber != 0)
            throw new InvalidOperationException("New subscription grants cannot use recurring period numbers.");
        var startsAt = anchorAt;
        var expiresAt = expiresAtUtc ?? (plan.Code is "Plus" or "Pro" && account.SubscriptionActivationIsAuthoritative
            ? account.SubscriptionEffectiveTo
            : null) ?? throw new InvalidOperationException(
            "An authoritative credit-source expiration is required for a new grant.");
        if (expiresAt <= startsAt || expiresAt <= now)
            throw new InvalidOperationException("The credit-source expiration is invalid or has passed.");
        subscriptionIdentity ??= $"legacy:{plan.Code}:{activationRevision}:{anchorAt.UtcDateTime.Ticks}";
        var idempotencyKey = BuildPeriodKey(account.Id, plan.Code, subscriptionIdentity, anchorAt, periodNumber);
        var existing = await dbContext.SubscriptionCreditPeriods
            .SingleOrDefaultAsync(row => row.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            ValidateExistingPeriod(existing, plan.Code, anchorAt, periodNumber, startsAt, expiresAt);
            return;
        }

        if (plan.IncludedCredits <= 0)
        {
            throw new InvalidOperationException($"Subscription plan '{plan.Code}' has no positive credit allowance.");
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
            AllowanceCredits = plan.IncludedCredits,
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
        if (plan.Code == "Free" && account.UserId is { } userId)
        {
            dbContext.FreeCreditGrantEvidence.Add(new FreeCreditGrantEvidenceRow
            {
                UserId = userId,
                CustomerAccountId = account.Id,
                GrantLedgerEntryId = grantLedgerId,
                GrantedAtUtc = now,
                AssignedExpiresAtUtc = expiresAt,
                EvidenceSource = "new-grant"
            });
        }
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
                AuditDescription = $"Unused {period.PlanCode} subscription credits {closedStatus.ToLowerInvariant()} at the source expiration.",
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

    private static string BuildPeriodKey(
        Guid accountId,
        string planCode,
        string subscriptionIdentity,
        DateTimeOffset anchorAt,
        long periodNumber) =>
        $"subscription-credit:{accountId:N}:{planCode}:{subscriptionIdentity}:{periodNumber}";

    private static void ValidateExistingPeriod(
        SubscriptionCreditPeriodRow existing,
        string planCode,
        DateTimeOffset anchorAt,
        long periodNumber,
        DateTimeOffset startsAt,
        DateTimeOffset expiresAt)
    {
        if (existing.PlanCode != planCode ||
            existing.AnchorAtUtc != anchorAt ||
            existing.PeriodNumber != periodNumber ||
            existing.StartsAtUtc != startsAt ||
            existing.ExpiresAtUtc != expiresAt)
        {
            throw new InvalidOperationException("A subscription credit period idempotency key was reused for different terms.");
        }
    }
}

public sealed record TopUpCreditApplication(Guid SourceId, Guid GrantLedgerEntryId, decimal CreditsApplied);
public sealed record TopUpUsageChargeResult(IReadOnlyList<TopUpCreditApplication> Applications, decimal ExpiredRemainderCredits);

public sealed record SubscriptionCreditApplication(
    Guid PeriodId,
    Guid GrantLedgerEntryId,
    decimal CreditsApplied);

public sealed record SubscriptionCreditRefundResult(
    IReadOnlyList<SubscriptionCreditApplication> Restored,
    IReadOnlyList<SubscriptionCreditApplication> Expired);
