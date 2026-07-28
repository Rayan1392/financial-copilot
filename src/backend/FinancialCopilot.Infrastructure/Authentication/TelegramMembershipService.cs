using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;
using FinancialCopilot.Domain.Identity;
using FinancialCopilot.Domain.Identity.Telegram;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using System.Diagnostics.Metrics;
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
    private static readonly Meter Meter = new("FinancialCopilot.TelegramMembership");
    private static readonly Counter<long> VerificationCounter = Meter.CreateCounter<long>("telegram.membership.verifications");
    private static readonly Counter<long> GrantCounter = Meter.CreateCounter<long>("telegram.membership.daily_grants");
    private static readonly Counter<long> DuplicateGrantPreventionCounter = Meter.CreateCounter<long>("telegram.membership.duplicate_grant_prevented");
    private static readonly Counter<long> CacheHitCounter = Meter.CreateCounter<long>("telegram.membership.cache_hits");
    private static readonly Counter<long> CacheMissCounter = Meter.CreateCounter<long>("telegram.membership.cache_misses");
    private static readonly Counter<long> ProviderFailureCounter = Meter.CreateCounter<long>("telegram.membership.provider_failures");
    private static readonly Histogram<double> ProviderLatencyMs = Meter.CreateHistogram<double>("telegram.membership.provider_latency_ms");

    public async Task<TelegramMembershipVerificationResult> VerifyRequiredChannelMembershipAsync(
        CurrentActor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireWebUser(actor);
        var link = await linkReader.GetCurrentAsync(actor, cancellationToken) ??
            throw new InvalidOperationException("A linked Telegram account is required before membership verification.");
        return await VerifyAndPersistAsync(
            actor.ActorId,
            actor.TenantId,
            link.TelegramUserId,
            correlationId,
            updateSchedule: true,
            cancellationToken);
    }

    public async Task<TelegramMembershipVerificationResult> RevalidateLatestMembershipAsync(
        Guid actorId,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var link = await GetActiveLinkAsync(actorId, tenantId, cancellationToken) ??
            throw new InvalidOperationException("A linked Telegram account is required before membership verification.");
        return await VerifyAndPersistAsync(
            actorId,
            tenantId,
            link.TelegramUserId,
            correlationId,
            updateSchedule: false,
            cancellationToken);
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
            BuildActions(link, membership),
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
            DuplicateGrantPreventionCounter.Add(1);
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
            DuplicateGrantPreventionCounter.Add(1);
            return new DailyFreeAllowanceGrantResult(false, amount, dateKey, options.Value.PolicyVersion, expiresAt, bucket.RemainingCredits);
        }

        GrantCounter.Add(1, new KeyValuePair<string, object?>("policyVersion", options.Value.PolicyVersion));
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

        var usedEntries = await billingDbContext.UsageLedgerEntries.AsNoTracking()
            .Where(row =>
                row.CustomerAccountId == account.Id &&
                row.ActorId == actor.ActorId &&
                row.EntryType == nameof(UsageLedgerEntryType.Charge))
            .ToListAsync(cancellationToken);
        var used = usedEntries
            .Where(row => row.OccurredAt >= grant.GrantedAtUtc && row.OccurredAt < grant.ExpiresAtUtc)
            .Sum(row => row.CreditsCharged);
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
                row.ExpiredAtUtc == null)
            .ToListAsync(cancellationToken);
        expired = expired.Where(row => row.ExpiresAtUtc <= now).ToList();
        if (expired.Count == 0)
        {
            return;
        }

        var walletRow = await billingDbContext.WalletProjections.SingleAsync(row => row.CustomerAccountId == customerAccountId, cancellationToken);
        var balance = walletRow.Balance;
        foreach (var grant in expired)
        {
            var usedEntries = await billingDbContext.UsageLedgerEntries
                .Where(row =>
                    row.CustomerAccountId == customerAccountId &&
                    row.ActorId == actorId &&
                    row.EntryType == nameof(UsageLedgerEntryType.Charge))
                .ToListAsync(cancellationToken);
            var used = usedEntries
                .Where(row => row.OccurredAt >= grant.GrantedAtUtc && row.OccurredAt < grant.ExpiresAtUtc)
                .Sum(row => row.CreditsCharged);
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
        if (row is null)
        {
            CacheMissCounter.Add(1, new KeyValuePair<string, object?>("channelId", channelId));
            return null;
        }

        CacheHitCounter.Add(
            1,
            new KeyValuePair<string, object?>("channelId", channelId),
            new KeyValuePair<string, object?>("eligible", row.IsEligible),
            new KeyValuePair<string, object?>("fresh", row.ExpiresAtUtc > timeProvider.GetUtcNow()));

        return new TelegramMembershipVerificationResult(
            Enum.Parse<TelegramChannelMembershipStatus>(row.Status),
            row.IsEligible,
            row.VerifiedAtUtc,
            row.ExpiresAtUtc,
            row.ChannelId,
            row.CorrelationId,
            Enum.Parse<TelegramMembershipFailureCategory>(row.FailureCategory),
            BuildActions(Enum.Parse<TelegramChannelMembershipStatus>(row.Status), row.ChannelId));
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

    private async Task<TelegramMembershipVerificationResult> VerifyAndPersistAsync(
        Guid actorId,
        Guid tenantId,
        long telegramUserId,
        string correlationId,
        bool updateSchedule,
        CancellationToken cancellationToken)
    {
        var channelId = RequireChannelId();
        var started = timeProvider.GetTimestamp();
        var observation = await membershipProvider.GetMembershipAsync(
            telegramUserId,
            channelId,
            correlationId,
            cancellationToken);
        ProviderLatencyMs.Record(timeProvider.GetElapsedTime(started).TotalMilliseconds);
        var now = timeProvider.GetUtcNow();
        var expires = observation.Status == TelegramChannelMembershipStatus.UnknownProviderFailure
            ? now.AddMinutes(Math.Max(1, options.Value.ProviderFailureCacheMinutes))
            : now.AddMinutes(Math.Max(1, options.Value.VerificationCacheMinutes));

        await StoreVerificationAsync(
            actorId,
            tenantId,
            telegramUserId,
            channelId,
            observation.Status,
            observation.Status.IsEligible(),
            observation.ObservedAtUtc,
            now,
            expires,
            observation.FailureCategory,
            correlationId,
            cancellationToken);

        if (updateSchedule)
        {
            await UpsertRevalidationScheduleAsync(
                actorId,
                tenantId,
                telegramUserId,
                channelId,
                expires,
                correlationId,
                cancellationToken);
        }

        VerificationCounter.Add(
            1,
            new KeyValuePair<string, object?>("status", observation.Status.ToString()),
            new KeyValuePair<string, object?>("eligible", observation.Status.IsEligible()),
            new KeyValuePair<string, object?>("failureCategory", observation.FailureCategory.ToString()));
        if (observation.FailureCategory != TelegramMembershipFailureCategory.None)
        {
            ProviderFailureCounter.Add(
                1,
                new KeyValuePair<string, object?>("status", observation.Status.ToString()),
                new KeyValuePair<string, object?>("failureCategory", observation.FailureCategory.ToString()));
        }

        logger.LogInformation(
            "Telegram membership verified for actor {ActorId} with status {Status} and correlation {CorrelationId}.",
            actorId,
            observation.Status,
            correlationId);

        return new TelegramMembershipVerificationResult(
            observation.Status,
            observation.Status.IsEligible(),
            now,
            expires,
            channelId,
            correlationId,
            observation.FailureCategory,
            BuildActions(observation.Status, channelId));
    }

    private async Task<TelegramAccountLinkRow?> GetActiveLinkAsync(
        Guid actorId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await authDbContext.Set<TelegramAccountLinkRow>().AsNoTracking()
            .SingleOrDefaultAsync(row =>
                row.ActorId == actorId &&
                row.TenantId == tenantId &&
                row.RevokedAtUtc == null,
                cancellationToken);

    private async Task UpsertRevalidationScheduleAsync(
        Guid actorId,
        Guid tenantId,
        long telegramUserId,
        string channelId,
        DateTimeOffset nextDueAtUtc,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var row = await authDbContext.TelegramMembershipRevalidations.SingleOrDefaultAsync(
            candidate => candidate.ActorId == actorId &&
                candidate.TenantId == tenantId &&
                candidate.ChannelId == channelId,
            cancellationToken);
        if (row is null)
        {
            authDbContext.TelegramMembershipRevalidations.Add(new TelegramMembershipRevalidationRow
            {
                Id = Guid.NewGuid(),
                ActorId = actorId,
                TenantId = tenantId,
                TelegramUserId = telegramUserId,
                ChannelId = channelId,
                NextDueAtUtc = nextDueAtUtc,
                CorrelationId = correlationId
            });
        }
        else
        {
            row.TelegramUserId = telegramUserId;
            row.NextDueAtUtc = nextDueAtUtc;
            row.LeaseExpiresAtUtc = null;
            row.LeaseOwner = null;
            row.AttemptCount = 0;
            row.LastAttemptedAtUtc = null;
            row.LastFailureCategory = null;
            row.LastError = null;
            row.DeadLetteredAtUtc = null;
            row.CorrelationId = correlationId;
        }

        await authDbContext.SaveChangesAsync(cancellationToken);
    }

    private IReadOnlyList<TelegramInlineAction> BuildActions(
        TelegramLinkView? link,
        TelegramMembershipVerificationResult? membership)
    {
        if (link is null)
        {
            return [];
        }

        if (membership is null || membership.ValidUntilUtc <= timeProvider.GetUtcNow())
        {
            return
            [
                new TelegramInlineAction("recheck", "بررسی دوباره عضویت", CallbackData: "tgm.recheck.v1", IsPrimary: true),
                new TelegramInlineAction("join", "ورود به کانال", Url: BuildJoinUrl(RequireChannelId()), CallbackData: "tgm.join.v1")
            ];
        }

        return membership.Actions ?? BuildActions(membership.Status, membership.ChannelId);
    }

    private IReadOnlyList<TelegramInlineAction> BuildActions(
        TelegramChannelMembershipStatus status,
        string channelId)
    {
        var recheck = new TelegramInlineAction("recheck", "بررسی دوباره عضویت", CallbackData: "tgm.recheck.v1", IsPrimary: true);
        var join = new TelegramInlineAction("join", "ورود به کانال", Url: BuildJoinUrl(channelId), CallbackData: "tgm.join.v1");
        return status.IsEligible() ? [recheck] : [join, recheck];
    }

    private static string? BuildJoinUrl(string channelId)
    {
        var value = channelId.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith("@", StringComparison.Ordinal))
        {
            return $"https://t.me/{value[1..]}";
        }

        return value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? value
                : $"https://t.me/{value.TrimStart('+')}";
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
