using System.Security.Cryptography;
using System.Text.Json;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Administration;

/// <summary>Bounded, tenant-scoped read models for the additive Feature 146 admin surfaces.</summary>
public sealed class EfCoreAdminUserQueries : IAdminUserQueries
{
    private const int MaximumPageSize = 100;
    private const string DefaultSort = "registeredAtUtc-desc,userId-asc";
    private const string ActiveStatus = "Active";
    private readonly AuthDbContext auth;
    private readonly BillingDbContext billing;
    private readonly TimeProvider timeProvider;
    private readonly IDataProtector cursorProtector;
    private readonly ILogger<EfCoreAdminUserQueries> logger;

    public EfCoreAdminUserQueries(
        AuthDbContext auth,
        BillingDbContext billing,
        TimeProvider timeProvider,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<EfCoreAdminUserQueries> logger)
    {
        this.auth = auth;
        this.billing = billing;
        this.timeProvider = timeProvider;
        this.cursorProtector = dataProtectionProvider.CreateProtector("Feature146.AdminUserQueries.Cursor.v1");
        this.logger = logger;
    }

    public async Task<AdminUserDirectoryPage> GetDirectoryAsync(
        Guid tenantId,
        AdminUserDirectoryQuery query,
        bool canReadSubscription,
        bool canReadCredits,
        bool canResolveBillingAccount,
        CancellationToken cancellationToken)
    {
        var filters = NormalizeAndValidate(query);
        var filterHash = HashFilters(filters);
        var cursor = ReadCursor(query.Cursor, "directory", tenantId, Guid.Empty, filters.PageSize, filters.Sort, filterHash);
        var asOfUtc = cursor?.AsOfUtc ?? timeProvider.GetUtcNow().ToUniversalTime();

        await using var transaction = IsPostgres(auth)
            ? await auth.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (transaction is not null)
        {
            await auth.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout = '5s'", cancellationToken);
        }

        var pageQuery = from membership in auth.UserTenants.AsNoTracking()
                        join user in auth.Users.AsNoTracking() on membership.UserId equals user.Id
                        join metadataRow in auth.UserOperationalMetadata.AsNoTracking() on user.Id equals metadataRow.UserId into metadataRows
                        from metadata in metadataRows.DefaultIfEmpty()
                        join profileRow in auth.UserProfiles.AsNoTracking() on user.Id equals profileRow.UserId into profileRows
                        from profile in profileRows.DefaultIfEmpty()
                        where membership.TenantId == tenantId
                        select new { User = user, Metadata = metadata, Profile = profile };

        if (filters.Status == "enabled")
        {
            pageQuery = pageQuery.Where(row => row.User.IsEnabled && (row.User.LockoutEnd == null || row.User.LockoutEnd <= asOfUtc));
        }
        else if (filters.Status == "disabled")
        {
            pageQuery = pageQuery.Where(row => !row.User.IsEnabled);
        }
        else if (filters.Status == "locked")
        {
            pageQuery = pageQuery.Where(row => row.User.IsEnabled && row.User.LockoutEnd > asOfUtc);
        }

        if (filters.AcquisitionSource is not null)
        {
            pageQuery = filters.AcquisitionSource == UserOperationalMetadataCodes.Unknown
                ? pageQuery.Where(row => row.Metadata == null || row.Metadata.AcquisitionSourceCode == UserOperationalMetadataCodes.Unknown)
                : pageQuery.Where(row => row.Metadata != null && row.Metadata.AcquisitionSourceCode == filters.AcquisitionSource);
        }

        if (filters.LinkedProvider is not null)
        {
            pageQuery = filters.LinkedProvider switch
            {
                "tahlilapp" => pageQuery.Where(row => auth.TahlilAppExternalIdentities.Any(link =>
                    link.TenantId == tenantId && link.LocalUserId == row.User.Id && link.Provider == ExternalUserId.Provider && link.Status != "Revoked")),
                "telegram" => pageQuery.Where(row => auth.TelegramAccountLinks.Any(link =>
                    link.TenantId == tenantId && link.ActorId == row.User.Id && link.RevokedAtUtc == null)),
                _ => pageQuery
            };
        }

        if (filters.Search is not null)
        {
            if (Guid.TryParse(filters.Search, out var userId))
            {
                pageQuery = pageQuery.Where(row => row.User.Id == userId);
            }
            else
            {
                var normalizedSearch = filters.Search.ToUpperInvariant();
                pageQuery = pageQuery.Where(row =>
                    (row.User.NormalizedEmail != null && row.User.NormalizedEmail.Contains(normalizedSearch)) ||
                    (row.Profile != null && (
                        (row.Profile.FirstName != null && row.Profile.FirstName.ToUpper().Contains(normalizedSearch)) ||
                        (row.Profile.LastName != null && row.Profile.LastName.ToUpper().Contains(normalizedSearch)) ||
                        ((row.Profile.FirstName ?? "") + " " + (row.Profile.LastName ?? "")).ToUpper().Contains(normalizedSearch) ||
                        ((row.Profile.LastName ?? "") + " " + (row.Profile.FirstName ?? "")).ToUpper().Contains(normalizedSearch))));
            }
        }

        if (filters.ExternalUserId is not null)
        {
            pageQuery = pageQuery.Where(row => auth.TahlilAppExternalIdentities.Any(link =>
                link.TenantId == tenantId &&
                link.LocalUserId == row.User.Id &&
                link.Provider == ExternalUserId.Provider &&
                link.ExternalUserId.StartsWith(filters.ExternalUserId)));
        }

        if (filters.RegisteredFromUtc is { } registeredFrom)
        {
            pageQuery = pageQuery.Where(row => row.Metadata != null && row.Metadata.RegisteredAtUtc >= registeredFrom);
        }
        if (filters.RegisteredToUtc is { } registeredTo)
        {
            pageQuery = pageQuery.Where(row => row.Metadata != null && row.Metadata.RegisteredAtUtc <= registeredTo);
        }
        if (filters.LastLoginFromUtc is { } loginFrom)
        {
            pageQuery = pageQuery.Where(row => row.Metadata != null && row.Metadata.LastSuccessfulLoginAtUtc >= loginFrom);
        }
        if (filters.LastLoginToUtc is { } loginTo)
        {
            pageQuery = pageQuery.Where(row => row.Metadata != null && row.Metadata.LastSuccessfulLoginAtUtc <= loginTo);
        }

        if (cursor is not null)
        {
            if (cursor.RegisteredAtUtc is null)
            {
                pageQuery = pageQuery.Where(row => row.Metadata == null || row.Metadata.RegisteredAtUtc == null
                    ? row.User.Id.CompareTo(cursor.LastUserId) > 0
                    : false);
            }
            else
            {
                var lastRegisteredAtUtc = cursor.RegisteredAtUtc.Value;
                pageQuery = pageQuery.Where(row => row.Metadata == null || row.Metadata.RegisteredAtUtc == null ||
                    row.Metadata.RegisteredAtUtc < lastRegisteredAtUtc ||
                    (row.Metadata.RegisteredAtUtc == lastRegisteredAtUtc && row.User.Id.CompareTo(cursor.LastUserId) > 0));
            }
        }

        var rows = await pageQuery
            .OrderBy(row => row.Metadata == null || row.Metadata.RegisteredAtUtc == null)
            .ThenByDescending(row => row.Metadata == null ? null : row.Metadata.RegisteredAtUtc)
            .ThenBy(row => row.User.Id)
            .Take(filters.PageSize + 1)
            .Select(row => new DirectoryRow(
                row.User.Id,
                row.User.Email,
                row.Profile == null ? null : row.Profile.FirstName,
                row.Profile == null ? null : row.Profile.LastName,
                row.User.IsEnabled,
                row.User.LockoutEnd,
                row.Metadata == null ? null : row.Metadata.RegisteredAtUtc,
                row.Metadata == null ? UserOperationalMetadataCodes.Unknown : row.Metadata.AcquisitionSourceCode,
                row.Metadata == null ? null : row.Metadata.LastSuccessfulLoginAtUtc,
                row.Metadata == null ? null : row.Metadata.LastLoginProviderCode))
            .ToArrayAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var hasMore = rows.Length > filters.PageSize;
        var pageRows = rows.Take(filters.PageSize).ToArray();
        var items = pageRows.Select(row => new AdminUserDirectoryItem(
            row.UserId,
            row.Email,
            JoinDisplayName(row.FirstName, row.LastName),
            row.IsEnabled,
            IsLocked(row.LockoutEnd, asOfUtc),
            GetStatus(row.IsEnabled, row.LockoutEnd, asOfUtc),
            row.AcquisitionSourceCode,
            row.RegisteredAtUtc,
            row.LastSuccessfulLoginAtUtc,
            row.LastLoginProviderCode,
            Array.Empty<string>(),
            NotAuthorized<AdminUserSubscriptionSummary>(),
            NotAuthorized<AdminUserCreditSummary>())).ToArray();

        if (items.Length > 0)
        {
            await AddProviderCodesAsync(tenantId, items, cancellationToken);
        }

        if (canResolveBillingAccount)
        {
            await AddDirectoryBillingSummariesAsync(tenantId, asOfUtc, items, canReadSubscription, canReadCredits, cancellationToken);
        }

        string? nextCursor = null;
        if (hasMore && pageRows.Length > 0)
        {
            var last = pageRows[^1];
            nextCursor = WriteCursor(new CursorPayload(
                1, "directory", tenantId, Guid.Empty, filters.PageSize, filters.Sort, filterHash,
                asOfUtc, last.RegisteredAtUtc, last.UserId, null, null));
        }

        return new AdminUserDirectoryPage(items, nextCursor, hasMore, asOfUtc);
    }

    public async Task<AdminCustomer360View> GetCustomer360Async(
        Guid tenantId,
        Guid userId,
        AdminCustomer360Permissions permissions,
        CancellationToken cancellationToken)
    {
        var asOfUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var identity = await (from membership in auth.UserTenants.AsNoTracking()
                              join user in auth.Users.AsNoTracking() on membership.UserId equals user.Id
                              join metadataRow in auth.UserOperationalMetadata.AsNoTracking() on user.Id equals metadataRow.UserId into metadataRows
                              from metadata in metadataRows.DefaultIfEmpty()
                              where membership.TenantId == tenantId && user.Id == userId
                              select new IdentityDetails(
                                  user.Id,
                                  user.IsEnabled,
                                  user.LockoutEnd,
                                  user.PasswordHash,
                                  user.EmailConfirmed,
                                  metadata == null ? UserOperationalMetadataCodes.Unknown : metadata.AcquisitionSourceCode,
                                  metadata == null ? null : metadata.RegisteredAtUtc,
                                  metadata == null ? null : metadata.LastSuccessfulLoginAtUtc,
                                  metadata == null ? null : metadata.LastLoginProviderCode))
            .SingleOrDefaultAsync(cancellationToken);

        if (identity is null)
        {
            throw MissingUser();
        }

        var tahlilLinks = await auth.TahlilAppExternalIdentities.AsNoTracking()
            .Where(link => link.TenantId == tenantId && link.LocalUserId == userId)
            .Select(link => new AdminProviderLinkView(
                "tahlilapp",
                link.Status,
                MaskExternalId(link.ExternalUserId),
                null,
                link.LinkedAtUtc,
                null,
                link.LastLaunchAtUtc,
                null))
            .ToArrayAsync(cancellationToken);
        var profileSyncedAtUtc = await auth.UserProfiles.AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .Select(profile => (DateTimeOffset?)profile.ProfileSyncedAtUtc)
            .SingleOrDefaultAsync(cancellationToken);
        var telegramLinks = await auth.TelegramAccountLinks.AsNoTracking()
            .Where(link => link.TenantId == tenantId && link.ActorId == userId)
            .Select(link => new AdminProviderLinkView(
                "telegram",
                link.RevokedAtUtc == null ? "Active" : "Revoked",
                MaskExternalId(link.TelegramUserId.ToString()),
                link.Username,
                link.LinkedAtUtc,
                link.RevokedAtUtc,
                link.LastVerifiedAtUtc,
                null))
            .ToArrayAsync(cancellationToken);
        if (profileSyncedAtUtc is not null)
        {
            tahlilLinks = tahlilLinks.Select(link => link with { ProfileSyncedAtUtc = profileSyncedAtUtc }).ToArray();
        }

        var activeRefreshSessions = await auth.RefreshTokens.AsNoTracking()
            .Where(token => token.UserId == userId && token.TenantId == tenantId && token.RevokedAt == null && token.ExpiresAt > asOfUtc)
            .Select(token => token.Id)
            .Distinct()
            .CountAsync(cancellationToken);

        var canReadAnyBilling = permissions.CanReadSubscription || permissions.CanReadCredits || permissions.CanReadPurchases;
        CustomerAccountRow? account = null;
        if (canReadAnyBilling)
        {
            try
            {
                account = await billing.CustomerAccounts.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.TenantId == tenantId && row.UserId == userId, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Customer 360 Billing account read failed. ExceptionType: {ExceptionType}", exception.GetType().Name);
                return BuildCustomer360Base(
                    identity, tenantId, asOfUtc, activeRefreshSessions, tahlilLinks.Concat(telegramLinks).ToArray(),
                    permissions.CanReadSubscription ? Unavailable<AdminCustomerSubscriptionView>("billing_read_failed") : NotAuthorized<AdminCustomerSubscriptionView>(),
                    permissions.CanReadCredits ? Unavailable<AdminCustomerCreditView>("billing_read_failed") : NotAuthorized<AdminCustomerCreditView>(),
                    permissions.CanReadPurchases ? Unavailable<AdminCustomerPurchaseSummary>("billing_read_failed") : NotAuthorized<AdminCustomerPurchaseSummary>());
            }
        }

        var subscription = permissions.CanReadSubscription
            ? await ReadSubscriptionSectionAsync(tenantId, account, asOfUtc, cancellationToken)
            : NotAuthorized<AdminCustomerSubscriptionView>();
        var credits = permissions.CanReadCredits
            ? await ReadCreditSectionAsync(account, asOfUtc, cancellationToken)
            : NotAuthorized<AdminCustomerCreditView>();
        var purchases = permissions.CanReadPurchases
            ? await ReadPurchaseSummaryAsync(tenantId, account, asOfUtc, cancellationToken)
            : NotAuthorized<AdminCustomerPurchaseSummary>();

        return BuildCustomer360Base(
            identity, tenantId, asOfUtc, activeRefreshSessions, tahlilLinks.Concat(telegramLinks).ToArray(),
            subscription, credits, purchases);
    }

    public async Task<AdminCreditHistoryPage> GetCreditHistoryAsync(
        Guid tenantId,
        Guid userId,
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ValidatePageSize(pageSize);
        var account = await RequireTenantAccountAsync(tenantId, userId, cancellationToken);
        var filterHash = HashFilters(new { account.Id, tenantId, userId });
        var pageCursor = ReadCursor(cursor, "credits", tenantId, userId, pageSize, "occurredAtUtc-desc,id-desc,recordType-asc", filterHash);
        var asOfUtc = pageCursor?.AsOfUtc ?? timeProvider.GetUtcNow().ToUniversalTime();

        if (account.Id == Guid.Empty)
        {
            return new AdminCreditHistoryPage(Array.Empty<AdminCreditHistoryItem>(), null, false, asOfUtc);
        }

        var ledger = billing.UsageLedgerEntries.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.CustomerAccountId == account.Id)
            .Select(row => new
            {
                row.Id,
                OccurredAtUtc = row.OccurredAt,
                RecordType = "usage_ledger",
                EntryType = (string?)row.EntryType,
                OperationCode = (string?)row.OperationCode,
                Credits = row.CreditsCharged,
                row.AllocationSource,
                ExpiresAtUtc = (DateTimeOffset?)null,
                row.RelatedEntryId,
                Status = row.CompletionStatus ?? "unknown",
                row.CustomerAccountId,
                RemainingCredits = (decimal?)null,
                GrantLedgerEntryId = (Guid?)null,
                row.AuditDescription
            });
        var periods = billing.SubscriptionCreditPeriods.AsNoTracking()
            .Where(row => row.CustomerAccountId == account.Id)
            .Select(row => new
            {
                row.Id,
                OccurredAtUtc = row.CreatedAtUtc,
                RecordType = "subscription_credit_period",
                EntryType = (string?)"credit_grant",
                OperationCode = (string?)"subscription_period",
                Credits = row.GrantedCredits,
                AllocationSource = (string?)"subscription",
                ExpiresAtUtc = (DateTimeOffset?)row.ExpiresAtUtc,
                RelatedEntryId = (Guid?)row.GrantLedgerEntryId,
                row.Status,
                row.CustomerAccountId,
                RemainingCredits = (decimal?)row.RemainingCredits,
                GrantLedgerEntryId = (Guid?)row.GrantLedgerEntryId,
                AuditDescription = (string?)null
            });
        var combined = ledger.Concat(periods);
        if (pageCursor is not null)
        {
            var lastOccurred = pageCursor.OccurredAtUtc!.Value;
            var lastId = pageCursor.LastItemId!.Value;
            var lastType = pageCursor.RecordType!;
            combined = combined.Where(row => row.OccurredAtUtc < lastOccurred ||
            (row.OccurredAtUtc == lastOccurred && (row.Id.CompareTo(lastId) < 0 ||
                    (row.Id == lastId && row.RecordType.CompareTo(lastType) > 0))));
        }

        var rows = await combined
            .OrderByDescending(row => row.OccurredAtUtc)
            .ThenByDescending(row => row.Id)
            .ThenBy(row => row.RecordType)
            .Take(pageSize + 1)
            .Select(row => new AdminCreditHistoryItem(
                row.Id,
                row.OccurredAtUtc,
                row.RecordType,
                row.EntryType,
                row.OperationCode,
                row.Credits,
                row.AllocationSource,
                row.ExpiresAtUtc,
                row.RelatedEntryId,
                row.Status,
                row.CustomerAccountId,
                row.RemainingCredits,
                row.GrantLedgerEntryId,
                row.AuditDescription))
            .ToArrayAsync(cancellationToken);
        return BuildCreditHistoryPage(rows, tenantId, userId, account.Id, pageSize, filterHash, asOfUtc);
    }

    public async Task<AdminPurchaseHistoryPage> GetPurchaseHistoryAsync(
        Guid tenantId,
        Guid userId,
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ValidatePageSize(pageSize);
        var account = await RequireTenantAccountAsync(tenantId, userId, cancellationToken);
        var filterHash = HashFilters(new { account.Id, tenantId, userId });
        var pageCursor = ReadCursor(cursor, "purchases", tenantId, userId, pageSize, "createdAtUtc-desc,id-desc", filterHash);
        var asOfUtc = pageCursor?.AsOfUtc ?? timeProvider.GetUtcNow().ToUniversalTime();
        if (account.Id == Guid.Empty)
        {
            return new AdminPurchaseHistoryPage(Array.Empty<AdminPurchaseHistoryItem>(), null, false, asOfUtc);
        }

        var query = billing.CheckoutIntents.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.CustomerAccountId == account.Id);
        if (pageCursor is not null)
        {
            var lastCreatedAt = pageCursor.OccurredAtUtc!.Value;
            var lastId = pageCursor.LastItemId!.Value;
            query = query.Where(row => row.CreatedAtUtc < lastCreatedAt ||
                (row.CreatedAtUtc == lastCreatedAt && row.Id.CompareTo(lastId) < 0));
        }

        var pageRows = await query.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = pageRows.Length > pageSize;
        var checkoutRows = pageRows.Take(pageSize).ToArray();
        var transactionIds = checkoutRows.Where(row => row.FulfillmentFinancialTransactionId.HasValue)
            .Select(row => row.FulfillmentFinancialTransactionId!.Value)
            .Distinct()
            .ToArray();
        var transactions = transactionIds.Length == 0
            ? new Dictionary<Guid, FinancialTransactionRow>()
            : await billing.FinancialTransactions.AsNoTracking().Where(row => transactionIds.Contains(row.Id) && row.CustomerAccountId == account.Id)
                .ToDictionaryAsync(row => row.Id, cancellationToken);
        var items = checkoutRows.Select(row => MapPurchase(row, transactions.GetValueOrDefault(row.FulfillmentFinancialTransactionId ?? Guid.Empty))).ToArray();

        string? nextCursor = null;
        if (hasMore && checkoutRows.Length > 0)
        {
            var last = checkoutRows[^1];
            nextCursor = WriteCursor(new CursorPayload(
                1, "purchases", tenantId, userId, pageSize, "createdAtUtc-desc,id-desc", filterHash,
                asOfUtc, null, last.Id, last.CreatedAtUtc, null));
        }

        return new AdminPurchaseHistoryPage(items, nextCursor, hasMore, asOfUtc);
    }

    private async Task AddProviderCodesAsync(Guid tenantId, AdminUserDirectoryItem[] items, CancellationToken cancellationToken)
    {
        var userIds = items.Select(item => item.UserId).ToArray();
        var tahlil = await auth.TahlilAppExternalIdentities.AsNoTracking()
            .Where(link => link.TenantId == tenantId && userIds.Contains(link.LocalUserId) && link.Status != "Revoked")
            .Select(link => new { UserId = link.LocalUserId, Provider = "tahlilapp" })
            .ToArrayAsync(cancellationToken);
        var telegram = await auth.TelegramAccountLinks.AsNoTracking()
            .Where(link => link.TenantId == tenantId && userIds.Contains(link.ActorId) && link.RevokedAtUtc == null)
            .Select(link => new { UserId = link.ActorId, Provider = "telegram" })
            .ToArrayAsync(cancellationToken);
        var providerMap = tahlil.Concat(telegram).GroupBy(row => row.UserId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<string>)group.Select(row => row.Provider).Distinct().OrderBy(provider => provider).ToArray());
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = items[i] with { LinkedProviderCodes = providerMap.GetValueOrDefault(items[i].UserId, Array.Empty<string>()) };
        }
    }

    private async Task AddDirectoryBillingSummariesAsync(
        Guid tenantId,
        DateTimeOffset asOfUtc,
        AdminUserDirectoryItem[] items,
        bool canReadSubscription,
        bool canReadCredits,
        CancellationToken cancellationToken)
    {
        var userIds = items.Select(item => item.UserId).ToArray();
        Dictionary<Guid, CustomerAccountRow> accounts;
        try
        {
            accounts = await billing.CustomerAccounts.AsNoTracking()
                .Where(row => row.TenantId == tenantId && row.UserId.HasValue && userIds.Contains(row.UserId.Value))
                .ToDictionaryAsync(row => row.UserId!.Value, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Customer directory Billing account read failed. ExceptionType: {ExceptionType}", exception.GetType().Name);
            for (var i = 0; i < items.Length; i++)
            {
                items[i] = items[i] with
                {
                    Subscription = canReadSubscription
                        ? Unavailable<AdminUserSubscriptionSummary>("billing_read_failed")
                        : NotAuthorized<AdminUserSubscriptionSummary>(),
                    Credits = canReadCredits
                        ? Unavailable<AdminUserCreditSummary>("billing_read_failed")
                        : NotAuthorized<AdminUserCreditSummary>()
                };
            }
            return;
        }

        var accountIds = accounts.Values.Select(account => account.Id).ToArray();
        var capabilityMap = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
        var subscriptionReadFailed = false;
        if (canReadSubscription)
        {
            try
            {
                var planCodes = accounts.Values.Where(account => account.SubscriptionPlanCode != null)
                    .Select(account => account.SubscriptionPlanCode!).Distinct().ToArray();
                var capabilities = planCodes.Length == 0
                    ? Array.Empty<PlanCapabilityRow>()
                    : await billing.PlanCapabilities.AsNoTracking().Where(row => planCodes.Contains(row.PlanCode) && row.IsEnabled)
                        .ToArrayAsync(cancellationToken);
                capabilityMap = capabilities.GroupBy(row => row.PlanCode)
                    .ToDictionary(group => group.Key, group => (IReadOnlyCollection<string>)group.Select(row => row.CapabilityCode).Distinct().OrderBy(code => code).ToArray(), StringComparer.Ordinal);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                subscriptionReadFailed = true;
                logger.LogWarning("Customer directory subscription summary read failed. ExceptionType: {ExceptionType}", exception.GetType().Name);
            }
        }

        var wallets = new Dictionary<Guid, WalletProjectionRow>();
        var periodStats = new Dictionary<Guid, PeriodSummary>();
        var creditReadFailed = false;
        if (canReadCredits && accountIds.Length > 0)
        {
            try
            {
                wallets = await billing.WalletProjections.AsNoTracking()
                    .Where(wallet => accountIds.Contains(wallet.CustomerAccountId))
                    .ToDictionaryAsync(wallet => wallet.CustomerAccountId, cancellationToken);
                periodStats = await billing.SubscriptionCreditPeriods.AsNoTracking()
                    .Where(period => accountIds.Contains(period.CustomerAccountId) && period.Status == "active" && period.ExpiresAtUtc > asOfUtc && period.RemainingCredits > 0)
                    .GroupBy(period => period.CustomerAccountId)
                    .Select(group => new PeriodSummary(group.Key, group.Sum(period => period.RemainingCredits), group.Min(period => period.ExpiresAtUtc)))
                    .ToDictionaryAsync(row => row.AccountId, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                creditReadFailed = true;
                logger.LogWarning("Customer directory credit summary read failed. ExceptionType: {ExceptionType}", exception.GetType().Name);
            }
        }

        for (var i = 0; i < items.Length; i++)
        {
            accounts.TryGetValue(items[i].UserId, out var account);
            var subscription = !canReadSubscription
                ? NotAuthorized<AdminUserSubscriptionSummary>()
                : subscriptionReadFailed
                    ? Unavailable<AdminUserSubscriptionSummary>("billing_read_failed")
                : account is null
                    ? Unavailable<AdminUserSubscriptionSummary>("billing_account_unavailable")
                    : Available(new AdminUserSubscriptionSummary(
                        account.Id,
                        account.AccountType,
                        account.SubscriptionPlanCode,
                        account.SubscriptionEffectiveFrom,
                        account.SubscriptionEffectiveTo,
                        account.SubscriptionRevision,
                        IsSubscriptionActive(account, asOfUtc),
                        account.SubscriptionPlanCode is { } planCode && capabilityMap.TryGetValue(planCode, out var values) ? values : Array.Empty<string>(),
                        "Billing",
                        asOfUtc));

            AdminOptionalSection<AdminUserCreditSummary> credits;
            if (!canReadCredits)
            {
                credits = NotAuthorized<AdminUserCreditSummary>();
            }
            else if (creditReadFailed)
            {
                credits = Unavailable<AdminUserCreditSummary>("billing_read_failed");
            }
            else if (account is null || !wallets.TryGetValue(account.Id, out var wallet))
            {
                credits = Unavailable<AdminUserCreditSummary>("wallet_projection_unavailable");
            }
            else
            {
                decimal activeCredits = 0;
                DateTimeOffset? nextExpiry = null;
                if (periodStats.TryGetValue(account.Id, out var stats))
                {
                    activeCredits = stats.Credits;
                    nextExpiry = stats.NextExpiry;
                }
                var spendable = wallet.Balance + (account.CreditLineApprovedLimit ?? 0m) - wallet.ReservedAmount;
                credits = Available(new AdminUserCreditSummary(spendable, wallet.ReservedAmount, account.CreditLineApprovedLimit, activeCredits, nextExpiry, "Billing wallet projection and subscription credit periods", asOfUtc));
            }

            items[i] = items[i] with { CustomerAccountId = account?.Id, Subscription = subscription, Credits = credits };
        }
    }

    private async Task<AdminOptionalSection<AdminCustomerSubscriptionView>> ReadSubscriptionSectionAsync(
        Guid tenantId,
        CustomerAccountRow? account,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        if (account is null)
        {
            return Unavailable<AdminCustomerSubscriptionView>("billing_account_unavailable");
        }

        try
        {
            var plan = account.SubscriptionPlanCode is null
                ? null
                : await billing.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(row => row.Code == account.SubscriptionPlanCode, cancellationToken);
            var capabilities = account.SubscriptionPlanCode is null
                ? Array.Empty<AdminCustomerPlanCapabilityView>()
                : await billing.PlanCapabilities.AsNoTracking()
                    .Where(row => row.PlanCode == account.SubscriptionPlanCode && row.IsEnabled)
                    .OrderBy(row => row.CapabilityCode)
                    .Select(row => new AdminCustomerPlanCapabilityView(row.CapabilityCode, row.IsEnabled, row.Limit, row.PolicyVersion))
                    .ToArrayAsync(cancellationToken);
            var external = await auth.TahlilAppExternalIdentities.AsNoTracking()
                .Where(link => link.TenantId == tenantId && link.LocalUserId == account.UserId && link.Provider == ExternalUserId.Provider)
                .Select(link => new { link.Status, link.LastEntitlementSyncAtUtc, link.NextEntitlementSyncDueAtUtc, link.LastSyncFailureCategory })
                .SingleOrDefaultAsync(cancellationToken);
            string? syncStatus = external is null
                ? null
                : external.LastSyncFailureCategory is not null ? "stale"
                : external.NextEntitlementSyncDueAtUtc > asOfUtc ? "current"
                : external.LastEntitlementSyncAtUtc is null ? "unknown"
                : "due";

            return Available(new AdminCustomerSubscriptionView(
                account.Id,
                account.AccountType,
                account.SubscriptionPlanCode,
                plan?.Name,
                account.SubscriptionEffectiveFrom,
                account.SubscriptionEffectiveTo,
                account.SubscriptionRevision,
                IsSubscriptionActive(account, asOfUtc),
                capabilities,
                syncStatus,
                external?.LastEntitlementSyncAtUtc,
                "Billing account and plan; TahlilApp sync state from Identity",
                asOfUtc));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Customer 360 subscription section read failed. ExceptionType: {ExceptionType}", exception.GetType().Name);
            return Unavailable<AdminCustomerSubscriptionView>("billing_read_failed");
        }
    }

    private async Task<AdminOptionalSection<AdminCustomerCreditView>> ReadCreditSectionAsync(
        CustomerAccountRow? account,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        if (account is null)
        {
            return Unavailable<AdminCustomerCreditView>("billing_account_unavailable");
        }

        try
        {
            var wallet = await billing.WalletProjections.AsNoTracking()
                .SingleOrDefaultAsync(row => row.CustomerAccountId == account.Id, cancellationToken);
            if (wallet is null)
            {
                return Unavailable<AdminCustomerCreditView>("wallet_projection_unavailable");
            }

            var activePeriods = await billing.SubscriptionCreditPeriods.AsNoTracking()
                .Where(period => period.CustomerAccountId == account.Id && period.Status == "active" && period.ExpiresAtUtc > asOfUtc && period.RemainingCredits > 0)
                .OrderBy(period => period.ExpiresAtUtc)
                .ThenBy(period => period.Id)
                .Take(100)
                .Select(period => new AdminCreditPeriodView(
                    period.Id, period.PlanCode, period.GrantedCredits, period.RemainingCredits,
                    period.StartsAtUtc, period.ExpiresAtUtc, period.Status))
                .ToArrayAsync(cancellationToken);
            var available = wallet.Balance + (account.CreditLineApprovedLimit ?? 0m) - wallet.ReservedAmount;
            return Available(new AdminCustomerCreditView(
                available,
                wallet.ReservedAmount,
                account.CreditLineApprovedLimit,
                activePeriods.Sum(period => period.RemainingCredits),
                activePeriods.FirstOrDefault()?.ExpiresAtUtc,
                activePeriods,
                "Billing wallet projection and subscription credit periods",
                asOfUtc));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Customer 360 credits section read failed. ExceptionType: {ExceptionType}", exception.GetType().Name);
            return Unavailable<AdminCustomerCreditView>("billing_read_failed");
        }
    }

    private async Task<AdminOptionalSection<AdminCustomerPurchaseSummary>> ReadPurchaseSummaryAsync(
        Guid tenantId,
        CustomerAccountRow? account,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        if (account is null)
        {
            return Unavailable<AdminCustomerPurchaseSummary>("billing_account_unavailable");
        }

        try
        {
            var recentRows = await billing.CheckoutIntents.AsNoTracking()
                .Where(row => row.TenantId == tenantId && row.CustomerAccountId == account.Id)
                .OrderByDescending(row => row.CreatedAtUtc)
                .ThenByDescending(row => row.Id)
                .Take(5)
                .ToArrayAsync(cancellationToken);
            var transactionIds = recentRows.Where(row => row.FulfillmentFinancialTransactionId.HasValue)
                .Select(row => row.FulfillmentFinancialTransactionId!.Value).Distinct().ToArray();
            var transactions = transactionIds.Length == 0
                ? new Dictionary<Guid, FinancialTransactionRow>()
                : await billing.FinancialTransactions.AsNoTracking().Where(row => transactionIds.Contains(row.Id) && row.CustomerAccountId == account.Id)
                    .ToDictionaryAsync(row => row.Id, cancellationToken);
            var completedCount = await billing.CheckoutIntents.AsNoTracking()
                .Where(row => row.TenantId == tenantId && row.CustomerAccountId == account.Id &&
                    row.Status == "fulfilled" && row.FulfilledAtUtc != null && row.FulfillmentFinancialTransactionId != null &&
                    billing.FinancialTransactions.Any(transaction => transaction.Id == row.FulfillmentFinancialTransactionId && transaction.CustomerAccountId == account.Id))
                .CountAsync(cancellationToken);

            return Available(new AdminCustomerPurchaseSummary(
                completedCount,
                recentRows.Select(row => MapPurchase(row, transactions.GetValueOrDefault(row.FulfillmentFinancialTransactionId ?? Guid.Empty))).ToArray(),
                "unavailable",
                "Billing checkout intents and financial transactions; external entitlements are not local purchases",
                asOfUtc));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Customer 360 purchases section read failed. ExceptionType: {ExceptionType}", exception.GetType().Name);
            return Unavailable<AdminCustomerPurchaseSummary>("billing_read_failed");
        }
    }

    private async Task<CustomerAccountRow> RequireTenantAccountAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var isMember = await auth.UserTenants.AsNoTracking().AnyAsync(row => row.TenantId == tenantId && row.UserId == userId, cancellationToken);
        if (!isMember)
        {
            throw MissingUser();
        }

        return await billing.CustomerAccounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.TenantId == tenantId && row.UserId == userId, cancellationToken)
            ?? new CustomerAccountRow { Id = Guid.Empty, TenantId = tenantId, UserId = userId };
    }

    private AdminCreditHistoryPage BuildCreditHistoryPage(
        AdminCreditHistoryItem[] rows,
        Guid tenantId,
        Guid userId,
        Guid accountId,
        int pageSize,
        string filterHash,
        DateTimeOffset asOfUtc)
    {
        var hasMore = rows.Length > pageSize;
        var items = rows.Take(pageSize).ToArray();
        string? nextCursor = null;
        if (hasMore && items.Length > 0)
        {
            var last = items[^1];
            nextCursor = WriteCursor(new CursorPayload(
                1, "credits", tenantId, userId, pageSize, "occurredAtUtc-desc,id-desc,recordType-asc", filterHash,
                asOfUtc, null, last.Id, last.OccurredAtUtc, last.RecordType));
        }

        return new AdminCreditHistoryPage(items, nextCursor, hasMore, asOfUtc);
    }

    private AdminCustomer360View BuildCustomer360Base(
        IdentityDetails identity,
        Guid tenantId,
        DateTimeOffset asOfUtc,
        int activeRefreshSessions,
        IReadOnlyCollection<AdminProviderLinkView> links,
        AdminOptionalSection<AdminCustomerSubscriptionView> subscription,
        AdminOptionalSection<AdminCustomerCreditView> credits,
        AdminOptionalSection<AdminCustomerPurchaseSummary> purchases) =>
        new(
            identity.Id,
            tenantId,
            identity.IsEnabled,
            IsLocked(identity.LockoutEnd, asOfUtc),
            identity.AcquisitionSourceCode,
            identity.RegisteredAtUtc,
            identity.LastSuccessfulLoginAtUtc,
            identity.LastLoginProviderCode,
            !string.IsNullOrWhiteSpace(identity.PasswordHash),
            identity.EmailConfirmed,
            activeRefreshSessions,
            new[] { tenantId },
            links,
            subscription,
            credits,
            purchases,
            asOfUtc);

    private string WriteCursor(CursorPayload payload) => cursorProtector.Protect(JsonSerializer.Serialize(payload));

    private CursorPayload? ReadCursor(
        string? raw,
        string kind,
        Guid tenantId,
        Guid userId,
        int pageSize,
        string sort,
        string filterHash)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CursorPayload>(cursorProtector.Unprotect(raw));
            if (payload is null || payload.Version != 1 || payload.Kind != kind || payload.TenantId != tenantId ||
                payload.UserId != userId || payload.PageSize != pageSize || payload.Sort != sort || payload.FilterHash != filterHash)
            {
                throw Invalid("The continuation cursor does not match this tenant, sort, page size, or filter set.");
            }

            return payload;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw Invalid("The continuation cursor is invalid or has expired.");
        }
        catch (JsonException)
        {
            throw Invalid("The continuation cursor is invalid.");
        }
    }

    private static string HashFilters<T>(T filters) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(filters)));

    private static AdminPurchaseHistoryItem MapPurchase(BillingCheckoutIntentRow row, FinancialTransactionRow? transaction) => new(
        row.Id,
        row.CreatedAtUtc,
        row.ProductType,
        row.ProductCode,
        row.ProductDisplayName,
        row.Status,
        row.Amount,
        row.Currency,
        row.Channel,
        row.ProviderName,
        MaskPaymentReference(row.PaymentReference),
        row.ReceiptSubmittedAtUtc,
        row.ReviewedAtUtc,
        row.FulfilledAtUtc,
        transaction?.Id,
        transaction?.Type,
        transaction?.OccurredAt,
        null);

    private static DirectoryFilters NormalizeAndValidate(AdminUserDirectoryQuery query)
    {
        ValidatePageSize(query.PageSize);
        var status = (query.Status ?? "any").Trim().ToLowerInvariant();
        if (status is not ("any" or "enabled" or "disabled" or "locked"))
        {
            throw Invalid("Status must be any, enabled, disabled, or locked.");
        }

        var source = string.IsNullOrWhiteSpace(query.AcquisitionSource) ? null : query.AcquisitionSource.Trim().ToLowerInvariant();
        if (source is not null && source is not (UserOperationalMetadataCodes.Native or UserOperationalMetadataCodes.TahlilApp or UserOperationalMetadataCodes.Unknown))
        {
            throw Invalid("Acquisition source must be native, tahlilapp, or unknown.");
        }

        var linkedProvider = string.IsNullOrWhiteSpace(query.LinkedProvider) ? null : query.LinkedProvider.Trim().ToLowerInvariant();
        if (linkedProvider is not null && linkedProvider is not ("tahlilapp" or "telegram"))
        {
            throw Invalid("Linked provider must be tahlilapp or telegram.");
        }

        var externalProvider = string.IsNullOrWhiteSpace(query.ExternalProvider) ? null : query.ExternalProvider.Trim().ToLowerInvariant();
        var externalId = string.IsNullOrWhiteSpace(query.ExternalUserId) ? null : query.ExternalUserId.Trim();
        if ((externalProvider is null) != (externalId is null))
        {
            throw Invalid("External provider and external user ID must be supplied together.");
        }
        if (externalProvider is not null && externalProvider != "tahlilapp")
        {
            throw Invalid("External ID search is available only for the indexed tahlilapp provider namespace.");
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (query.RegisteredFromUtc > query.RegisteredToUtc || query.LastLoginFromUtc > query.LastLoginToUtc)
        {
            throw Invalid("Date range start must not be later than its end.");
        }
        var sort = string.IsNullOrWhiteSpace(query.Sort) ? DefaultSort : query.Sort.Trim();
        if (sort != DefaultSort)
        {
            throw Invalid($"Sort must be {DefaultSort}.");
        }

        return new DirectoryFilters(
            query.PageSize,
            search,
            status,
            source,
            linkedProvider,
            externalId,
            NormalizeDate(query.RegisteredFromUtc),
            NormalizeDate(query.RegisteredToUtc),
            NormalizeDate(query.LastLoginFromUtc),
            NormalizeDate(query.LastLoginToUtc),
            sort);
    }

    private static DateTimeOffset? NormalizeDate(DateTimeOffset? value) => value?.ToUniversalTime();

    private static void ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw Invalid("Page size must be between 1 and 100.");
        }
    }

    private static bool IsPostgres(DbContext context) =>
        string.Equals(context.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);

    private static bool IsLocked(DateTimeOffset? lockoutEnd, DateTimeOffset asOfUtc) => lockoutEnd > asOfUtc;

    private static bool IsSubscriptionActive(CustomerAccountRow account, DateTimeOffset asOfUtc) =>
        account.SubscriptionPlanCode is not null &&
        (account.SubscriptionEffectiveFrom is null || account.SubscriptionEffectiveFrom <= asOfUtc) &&
        (account.SubscriptionEffectiveTo is null || account.SubscriptionEffectiveTo > asOfUtc);

    private static string GetStatus(bool isEnabled, DateTimeOffset? lockoutEnd, DateTimeOffset asOfUtc) =>
        !isEnabled ? "disabled" : IsLocked(lockoutEnd, asOfUtc) ? "locked" : "enabled";

    private static string? JoinDisplayName(string? firstName, string? lastName)
    {
        var parts = new[] { firstName?.Trim(), lastName?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var displayName = string.Join(" ", parts);
        return displayName.Length == 0 ? null : displayName;
    }

    private static string MaskExternalId(string? externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return "***";
        var visibleSuffix = externalId.Length <= 4 ? externalId : externalId[^4..];
        return $"***{visibleSuffix}";
    }

    private static string MaskPaymentReference(string? paymentReference)
    {
        if (string.IsNullOrWhiteSpace(paymentReference)) return "***";
        return $"***{(paymentReference.Length <= 4 ? paymentReference : paymentReference[^4..])}";
    }

    private static AdminOptionalSection<T> Available<T>(T data) => new("available", data);
    private static AdminOptionalSection<T> NotAuthorized<T>() => new("not_authorized", default);
    private static AdminOptionalSection<T> Unavailable<T>(string reason) => new("unavailable", default, reason);
    private static AdminManagementException MissingUser() => new("resource-not-found", "User was not found.", 404);
    private static AdminManagementException Invalid(string message) => new("validation-failed", message, 400);

    private sealed record DirectoryRow(
        Guid UserId,
        string? Email,
        string? FirstName,
        string? LastName,
        bool IsEnabled,
        DateTimeOffset? LockoutEnd,
        DateTimeOffset? RegisteredAtUtc,
        string AcquisitionSourceCode,
        DateTimeOffset? LastSuccessfulLoginAtUtc,
        string? LastLoginProviderCode);

    private sealed record IdentityDetails(
        Guid Id,
        bool IsEnabled,
        DateTimeOffset? LockoutEnd,
        string? PasswordHash,
        bool EmailConfirmed,
        string AcquisitionSourceCode,
        DateTimeOffset? RegisteredAtUtc,
        DateTimeOffset? LastSuccessfulLoginAtUtc,
        string? LastLoginProviderCode);

    private sealed record PeriodSummary(Guid AccountId, decimal Credits, DateTimeOffset NextExpiry);

    private sealed record DirectoryFilters(
        int PageSize,
        string? Search,
        string Status,
        string? AcquisitionSource,
        string? LinkedProvider,
        string? ExternalUserId,
        DateTimeOffset? RegisteredFromUtc,
        DateTimeOffset? RegisteredToUtc,
        DateTimeOffset? LastLoginFromUtc,
        DateTimeOffset? LastLoginToUtc,
        string Sort);

    private sealed record CursorPayload(
        int Version,
        string Kind,
        Guid TenantId,
        Guid UserId,
        int PageSize,
        string Sort,
        string FilterHash,
        DateTimeOffset AsOfUtc,
        DateTimeOffset? RegisteredAtUtc,
        Guid? LastItemId,
        DateTimeOffset? OccurredAtUtc,
        string? RecordType)
    {
        public Guid LastUserId => LastItemId ?? Guid.Empty;
    }
}
