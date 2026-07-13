using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;
using FinancialCopilot.Domain.Identity;
using FinancialCopilot.Domain.Identity.Telegram;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramMembershipService(
    AuthDbContext authDbContext,
    BillingDbContext billingDbContext,
    ITelegramIdentityLinkReader linkReader,
    ITelegramChannelMembershipProvider membershipProvider,
    IBillableAccountResolver accountResolver,
    IWalletService wallets,
    IOptions<TelegramMembershipOptions> options,
    TimeProvider timeProvider,
    ILogger<TelegramMembershipService> logger) : ITelegramMembershipService, IDailyFreeAllowanceService
{
    private const string GrantOperationCode = "Telegram.DailyFreeAllowance";
    private const string ExpiryOperationCode = "Telegram.DailyFreeAllowanceExpiry";
    private const string AllocationSource = "TelegramDailyFreeAllowance";

    public async Task<TelegramMembershipVerificationResult> VerifyRequiredChannelMembershipAsync(
        CurrentActor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireWebUser(actor);
        var link = await linkReader.GetCurrentAsync(actor, cancellationToken) ??
            throw new InvalidOperationException("A linked Telegram account is required before membership verification.");
        var channelId = RequireChannelId();
        var observation = await membershipProvider.GetMembershipAsync(
            link.TelegramUserId,
            channelId,
            correlationId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var expires = observation.Status == TelegramChannelMembershipStatus.UnknownProviderFailure
            ? now.AddMinutes(Math.Max(1, options.Value.ProviderFailureCacheMinutes))
            : now.AddMinutes(Math.Max(1, options.Value.VerificationCacheMinutes));

        await StoreVerificationAsync(
            actor.ActorId,
            actor.TenantId,
            link.TelegramUserId,
            channelId,
            observation.Status,
            observation.Status.IsEligible(),
            observation.ObservedAtUtc,
            now,
            expires,
            observation.FailureCategory,
            correlationId,
            cancellationToken);

        logger.LogInformation(
            "Telegram membership verified for actor {ActorId} with status {Status} and correlation {CorrelationId}.",
            actor.ActorId,
            observation.Status,
            correlationId);

        return new TelegramMembershipVerificationResult(
            observation.Status,
            observation.Status.IsEligible(),
            now,
            expires,
            channelId,
            correlationId,
            observation.FailureCategory);
    }

    public async Task<TelegramEntitlementView> GetMyTelegramEntitlementAsync(
        CurrentActor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireWebUser(actor);
        var link = await linkReader.GetCurrentAsync(actor, cancellationToken);
        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(actor.ActorId, actor.TenantId, actor.UserId, null, null),
            cancellationToken);
        var wallet = await wallets.GetSnapshotAsync(account.Id, cancellationToken);
        var membership = await GetLatestVerificationAsync(actor.ActorId, actor.TenantId, cancellationToken);
        var bucket = await GetBucketAsync(
            new BillableActorContext(actor.ActorId, actor.TenantId, actor.UserId, null, null),
            account,
            cancellationToken);

        return new TelegramEntitlementView(
            link,
            membership,
            new TelegramDailyFreeAllowanceView(
                bucket.AllowanceDateKey,
                bucket.PolicyVersion,
                bucket.TotalCredits,
                bucket.UsedCredits,
                bucket.RemainingCredits,
                bucket.ExpiresAtUtc),
            account.GetAvailableSpendingCapacity(wallet),
            "Free daily allowance, then active subscription allowance, then purchased credits.",
            DetermineNextAction(link, membership, bucket),
            timeProvider.GetUtcNow());
    }

    public async Task<DailyFreeAllowanceGrantResult> EnsureAsync(
        BillableActorContext actor,
        CustomerAccount account,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (actor.UserId is null || actor.ActorId == Guid.Empty)
        {
            return EmptyGrant();
        }

        var verification = await GetLatestVerificationAsync(actor.ActorId, actor.TenantId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (verification is null ||
            !verification.IsEligible ||
            verification.ValidUntilUtc <= now)
        {
            return EmptyGrant();
        }

        await ExpireOldAllowancesAsync(account.Id, actor.ActorId, actor.TenantId, now, cancellationToken);

        var (dateKey, expiresAt) = GetTehranDay(now);
        var existing = await billingDbContext.DailyFreeAllowanceGrants
            .AsNoTracking()
            .SingleOrDefaultAsync(row =>
                row.CustomerAccountId == account.Id &&
                row.ActorId == actor.ActorId &&
                row.AllowanceDateKey == dateKey &&
                row.PolicyVersion == options.Value.PolicyVersion,
                cancellationToken);
        if (existing is not null)
        {
            var bucket = await GetBucketAsync(actor, account, cancellationToken);
            return new DailyFreeAllowanceGrantResult(false, existing.Amount, dateKey, existing.PolicyVersion, existing.ExpiresAtUtc, bucket.RemainingCredits);
        }

        var walletRow = await billingDbContext.WalletProjections.SingleAsync(
            row => row.CustomerAccountId == account.Id,
            cancellationToken);
        var amount = options.Value.DailyFreeCredits;
        var entry = new UsageLedgerEntry(
            Guid.NewGuid(),
            account.Id,
            actor.ActorId,
            actor.TenantId,
            ApiClientId: null,
            UsageLedgerEntryType.Adjustment,
            GrantOperationCode,
            amount,
            options.Value.PolicyVersion,
            $"telegram-free:{actor.ActorId:N}:{dateKey}:{options.Value.PolicyVersion}",
            now,
            AuditDescription: "Daily Telegram channel membership free allowance.",
            AllocationSource: AllocationSource,
            AllowanceDateKey: dateKey);
        var currentWallet = new WalletSnapshot(walletRow.CustomerAccountId, walletRow.Balance, walletRow.ReservedAmount, walletRow.UpdatedAt, walletRow.Revision);
        var updatedWallet = currentWallet.AddCredits(amount, now);

        billingDbContext.UsageLedgerEntries.Add(MapLedgerRow(entry));
        billingDbContext.DailyFreeAllowanceGrants.Add(new DailyFreeAllowanceGrantRow
        {
            Id = Guid.NewGuid(),
            CustomerAccountId = account.Id,
            ActorId = actor.ActorId,
            TenantId = actor.TenantId,
            AllowanceDateKey = dateKey,
            PolicyVersion = options.Value.PolicyVersion,
            Amount = amount,
            GrantedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            LedgerEntryId = entry.Id,
            CorrelationId = correlationId
        });
        ApplyWallet(walletRow, updatedWallet);
        BillingOutboxWriter.Add(
            billingDbContext,
            "UsageLedgerEntry",
            entry.Id,
            "Billing.TelegramDailyFreeAllowanceGranted",
            $"{entry.IdempotencyKey}:granted",
            new { entry.CustomerAccountId, entry.ActorId, entry.CreditsCharged, entry.AllowanceDateKey },
            now);

        try
        {
            await billingDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var bucket = await GetBucketAsync(actor, account, cancellationToken);
            return new DailyFreeAllowanceGrantResult(false, amount, dateKey, options.Value.PolicyVersion, expiresAt, bucket.RemainingCredits);
        }

        return new DailyFreeAllowanceGrantResult(true, amount, dateKey, options.Value.PolicyVersion, expiresAt, amount);
    }

    public async Task<DailyFreeAllowanceBucket> GetBucketAsync(
        BillableActorContext actor,
        CustomerAccount account,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var (dateKey, expiresAt) = GetTehranDay(now);
        var policyVersion = options.Value.PolicyVersion;
        var grant = actor.UserId is null
            ? null
            : await billingDbContext.DailyFreeAllowanceGrants.AsNoTracking().SingleOrDefaultAsync(row =>
                row.CustomerAccountId == account.Id &&
                row.ActorId == actor.ActorId &&
                row.AllowanceDateKey == dateKey &&
                row.PolicyVersion == policyVersion,
                cancellationToken);
        if (grant is null)
        {
            return new DailyFreeAllowanceBucket(dateKey, policyVersion, options.Value.DailyFreeCredits, 0, 0, expiresAt);
        }

        var used = await billingDbContext.UsageLedgerEntries.AsNoTracking()
            .Where(row =>
                row.CustomerAccountId == account.Id &&
                row.ActorId == actor.ActorId &&
                row.EntryType == nameof(UsageLedgerEntryType.Charge) &&
                row.OccurredAt >= grant.GrantedAtUtc &&
                row.OccurredAt < grant.ExpiresAtUtc)
            .SumAsync(row => (decimal?)row.CreditsCharged, cancellationToken) ?? 0m;
        used = Math.Min(grant.Amount, used);
        return new DailyFreeAllowanceBucket(dateKey, policyVersion, grant.Amount, used, Math.Max(0, grant.Amount - used), grant.ExpiresAtUtc);
    }

    private async Task ExpireOldAllowancesAsync(
        Guid customerAccountId,
        Guid actorId,
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expired = await billingDbContext.DailyFreeAllowanceGrants
            .Where(row =>
                row.CustomerAccountId == customerAccountId &&
                row.ActorId == actorId &&
                row.ExpiredAtUtc == null &&
                row.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        var walletRow = await billingDbContext.WalletProjections.SingleAsync(row => row.CustomerAccountId == customerAccountId, cancellationToken);
        var balance = walletRow.Balance;
        foreach (var grant in expired)
        {
            var used = await billingDbContext.UsageLedgerEntries
                .Where(row =>
                    row.CustomerAccountId == customerAccountId &&
                    row.ActorId == actorId &&
                    row.EntryType == nameof(UsageLedgerEntryType.Charge) &&
                    row.OccurredAt >= grant.GrantedAtUtc &&
                    row.OccurredAt < grant.ExpiresAtUtc)
                .SumAsync(row => (decimal?)row.CreditsCharged, cancellationToken) ?? 0m;
            var unused = Math.Max(0, grant.Amount - Math.Min(grant.Amount, used));
            var expirable = Math.Min(balance, unused);
            grant.ExpiredAtUtc = now;
            grant.ExpiredCredits = expirable;
            if (expirable <= 0)
            {
                continue;
            }

            var entry = new UsageLedgerEntry(
                Guid.NewGuid(),
                customerAccountId,
                actorId,
                tenantId,
                ApiClientId: null,
                UsageLedgerEntryType.Charge,
                ExpiryOperationCode,
                expirable,
                grant.PolicyVersion,
                $"telegram-free-expiry:{actorId:N}:{grant.AllowanceDateKey}:{grant.PolicyVersion}",
                now,
                AuditDescription: "Expired unused Telegram daily free allowance.",
                AllocationSource: AllocationSource,
                AllowanceDateKey: grant.AllowanceDateKey);
            billingDbContext.UsageLedgerEntries.Add(MapLedgerRow(entry));
            balance -= expirable;
        }

        walletRow.Balance = balance;
        walletRow.UpdatedAt = now;
        walletRow.Revision += 1;
        await billingDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task StoreVerificationAsync(
        Guid actorId,
        Guid tenantId,
        long telegramUserId,
        string channelId,
        TelegramChannelMembershipStatus status,
        bool eligible,
        DateTimeOffset providerObservedAtUtc,
        DateTimeOffset verifiedAtUtc,
        DateTimeOffset expiresAtUtc,
        TelegramMembershipFailureCategory failureCategory,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var previous = await authDbContext.TelegramChannelMembershipVerifications
            .Where(row => row.ActorId == actorId && row.TenantId == tenantId && row.ChannelId == channelId && row.IsLatest)
            .ToListAsync(cancellationToken);
        foreach (var row in previous)
        {
            row.IsLatest = false;
        }

        authDbContext.TelegramChannelMembershipVerifications.Add(new TelegramChannelMembershipVerificationRow
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            TenantId = tenantId,
            TelegramUserId = telegramUserId,
            ChannelId = channelId,
            Status = status.ToString(),
            IsEligible = eligible,
            ProviderObservedAtUtc = providerObservedAtUtc,
            VerifiedAtUtc = verifiedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            FailureCategory = failureCategory.ToString(),
            CorrelationId = correlationId,
            IsLatest = true
        });
        await authDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<TelegramMembershipVerificationResult?> GetLatestVerificationAsync(
        Guid actorId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var channelId = RequireChannelId();
        var row = await authDbContext.TelegramChannelMembershipVerifications.AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.ActorId == actorId &&
                candidate.TenantId == tenantId &&
                candidate.ChannelId == channelId &&
                candidate.IsLatest,
                cancellationToken);
        return row is null
            ? null
            : new TelegramMembershipVerificationResult(
                Enum.Parse<TelegramChannelMembershipStatus>(row.Status),
                row.IsEligible,
                row.VerifiedAtUtc,
                row.ExpiresAtUtc,
                row.ChannelId,
                row.CorrelationId,
                Enum.Parse<TelegramMembershipFailureCategory>(row.FailureCategory));
    }

    private string RequireChannelId() =>
        string.IsNullOrWhiteSpace(options.Value.RequiredChannelId)
            ? throw new InvalidOperationException("Telegram required channel is not configured.")
            : options.Value.RequiredChannelId.Trim();

    private static string DetermineNextAction(
        TelegramLinkView? link,
        TelegramMembershipVerificationResult? membership,
        DailyFreeAllowanceBucket bucket)
    {
        if (link is null) return "LinkTelegramAccount";
        if (membership is null || membership.ValidUntilUtc <= DateTimeOffset.UtcNow) return "VerifyMembership";
        if (!membership.IsEligible) return "JoinRequiredChannel";
        if (bucket.RemainingCredits <= 0) return "UsePaidEntitlement";
        return "Ready";
    }

    private (string DateKey, DateTimeOffset ExpiresAtUtc) GetTehranDay(DateTimeOffset utcNow)
    {
        var zone = GetTehranTimeZone();
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var localDate = DateOnly.FromDateTime(local.DateTime);
        var nextLocalMidnight = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var expiresAt = TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, zone);
        return (localDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), expiresAt);
    }

    private static TimeZoneInfo GetTehranTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        }
    }

    private static DailyFreeAllowanceGrantResult EmptyGrant() =>
        new(false, 0, string.Empty, string.Empty, DateTimeOffset.MinValue, 0);

    private static void RequireWebUser(CurrentActor actor)
    {
        if (actor.ActorType != ActorType.User || actor.AuthenticationMode != AuthenticationMode.WebAppUser)
        {
            throw new UnauthorizedAccessException("A canonical web user is required.");
        }
    }

    private static void ApplyWallet(WalletProjectionRow row, WalletSnapshot wallet)
    {
        row.Balance = wallet.Balance;
        row.ReservedAmount = wallet.ReservedAmount;
        row.UpdatedAt = wallet.UpdatedAt;
        row.Revision = wallet.Revision;
    }

    private static UsageLedgerEntryRow MapLedgerRow(UsageLedgerEntry entry) =>
        new()
        {
            Id = entry.Id,
            CustomerAccountId = entry.CustomerAccountId,
            ActorId = entry.ActorId,
            TenantId = entry.TenantId,
            ApiClientId = entry.ApiClientId,
            EntryType = entry.EntryType.ToString(),
            OperationCode = entry.OperationCode,
            CreditsCharged = entry.CreditsCharged,
            PricingPolicyVersion = entry.PricingPolicyVersion,
            IdempotencyKey = entry.IdempotencyKey,
            OccurredAt = entry.OccurredAt,
            ExternalUserId = entry.ExternalUserId,
            AuditDescription = entry.AuditDescription,
            RelatedEntryId = entry.RelatedEntryId,
            CompletionStatus = entry.CompletionStatus,
            ProviderName = entry.ProviderName,
            ModelName = entry.ModelName,
            PromptTokens = entry.PromptTokens,
            CompletionTokens = entry.CompletionTokens,
            TotalTokens = entry.TotalTokens,
            EstimatedCost = entry.EstimatedCost,
            AllocationSource = entry.AllocationSource,
            AllowanceDateKey = entry.AllowanceDateKey
        };
}
