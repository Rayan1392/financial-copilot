using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed record TopUpWindowResponse(string Slug, DateTimeOffset StartsAtUtc, DateTimeOffset ExpiresAtUtc);
public sealed record TopUpEligibilityResponse(bool Linked, bool Eligible, string? Plan, TopUpWindowResponse? Subscription, IReadOnlyList<TopUpWindowResponse> Subscriptions, DateTimeOffset CheckedAtUtc);
public sealed record TopUpDeliveryRequest(string EventId, string PurchaseId, string PaymentId, string UserId, int CreditsPurchased, string? PackageId, string? PackageVersion, TopUpWindowResponse Subscription, DateTimeOffset PaidAtUtc);
public sealed record TopUpDeliveryResponse(string Status, string PurchaseId, Guid? GrantId, int CreditsGranted, DateTimeOffset ExpiresAtUtc, DateTimeOffset? GrantedAtUtc);
public sealed record TopUpStatusResponse(string Status, string PurchaseId, Guid? GrantId, int CreditsGranted, DateTimeOffset ExpiresAtUtc, DateTimeOffset? GrantedAtUtc);
public sealed record TopUpReconciliationPurchase(string PurchaseId, string PaymentId, string EventId, string ExternalUserId,
    Guid CustomerAccountId, int CreditsPurchased, string PackageId, string PackageVersion, string Status,
    string PlanSlug, DateTimeOffset PlanStartsAtUtc, DateTimeOffset PlanExpiresAtUtc, DateTimeOffset PaidAtUtc,
    DateTimeOffset ReceivedAtUtc, Guid? GrantId, DateTimeOffset? GrantedAtUtc);
public sealed record TopUpIdentityConflict(DateTimeOffset OccurredAtUtc, string PurchaseReceiptId, string EventId,
    string ConflictCode, string ExistingIdentity, string IncomingIdentity);
public sealed record TopUpReconciliationResponse(IReadOnlyList<TopUpReconciliationPurchase> Purchases,
    IReadOnlyList<TopUpIdentityConflict> IdentityConflicts, DateTimeOffset GeneratedAtUtc);

public sealed class TahlilAppTopUpService(BillingDbContext billing, AuthDbContext auth, TimeProvider clock,
    ITahlilAppEntitlementRefresher? entitlementRefresher = null)
{
    public async Task<TopUpEligibilityResponse?> GetEligibilityAsync(string externalUserId, CancellationToken ct)
    {
        var link = await auth.TahlilAppExternalIdentities.AsNoTracking().SingleOrDefaultAsync(row => row.Provider == "TahlilApp" && row.ExternalUserId == externalUserId, ct);
        var now = clock.GetUtcNow();
        if (link is null) return null;
        await RefreshAuthoritativeStatusAsync(link.TenantId, link.LocalUserId, ct);
        var user = await auth.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == link.LocalUserId, ct);
        var account = await billing.CustomerAccounts.AsNoTracking().SingleOrDefaultAsync(row => row.TenantId == link.TenantId && row.UserId == link.LocalUserId && row.AccountType == "Individual", ct);
        if (user is null || account is null || !user.IsEnabled || link.Status != "Active") return new(true, false, null, null, [], now);
        var rows = await billing.ExternalPlanSubscriptions.AsNoTracking()
            .Where(row => row.CustomerAccountId == account.Id && !row.IsRevoked &&
                          (row.PlanCode == "Plus" || row.PlanCode == "Pro" || row.PlanCode == "Max") &&
                          row.StartsAtUtc <= now && row.ExpiresAtUtc > now)
            .OrderBy(row => row.ExpiresAtUtc).ThenBy(row => row.StartsAtUtc).ToListAsync(ct);
        var windows = rows.Where(row => row.PlanCode is "Plus" or "Pro")
            .Select(row => new TopUpWindowResponse(row.IdentityKey[..row.IdentityKey.IndexOf(':')], row.StartsAtUtc, row.ExpiresAtUtc)).ToArray();
        var effective = TahlilAppSubscriptionPolicy.ResolveActivePlanSet(
            rows.Select(row => new TahlilAppPlanSubscription(
                row.IdentityKey, row.PlanCode, row.StartsAtUtc, row.ExpiresAtUtc, row.IdentityKey)).ToArray(), now);
        var eligible = windows.Length > 0 && (effective.PlanCode is "Plus" or "Pro");
        return new(true, eligible, eligible ? effective.PlanCode : null, windows.Length == 1 ? windows[0] : null, windows, now);
    }

    public async Task<TopUpDeliveryResponse> DeliverAsync(TopUpDeliveryRequest request, CancellationToken ct)
    {
        Validate(request);
        var existing = await FindExistingAsync(request, ct);
        if (existing is not null) return await ReplayOrRecordConflictAsync(existing, request, ct);
        var now = clock.GetUtcNow();
        if (await ResolveOfferAsync(request, ct) is null) throw new TopUpContractException(422, "topup-quantity-invalid");
        var link = await auth.TahlilAppExternalIdentities.SingleOrDefaultAsync(row => row.Provider == "TahlilApp" && row.ExternalUserId == request.UserId, ct);
        if (link is null) throw new TopUpContractException(404, "topup-user-unresolved");
        await RefreshAuthoritativeStatusAsync(link.TenantId, link.LocalUserId, ct);
        await auth.Entry(link).ReloadAsync(ct);
        var user = await auth.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == link.LocalUserId, ct);
        var account = await billing.CustomerAccounts.SingleOrDefaultAsync(row => row.TenantId == link.TenantId && row.UserId == link.LocalUserId, ct);
        if (account is null) throw new TopUpContractException(404, "topup-user-unresolved");
        if (user is null || !user.IsEnabled || link.Status != "Active" || account.AccountType != "Individual")
            return await PersistUnfulfilledAsync(request, link, account, null, "ineligible-unfulfilled", now, ct);
        var key = $"{request.Subscription.Slug}:{request.Subscription.StartsAtUtc.ToUnixTimeMilliseconds()}:{request.Subscription.ExpiresAtUtc.ToUnixTimeMilliseconds()}";
        var window = await billing.ExternalPlanSubscriptions.SingleOrDefaultAsync(row => row.CustomerAccountId == account.Id && row.IdentityKey == key, ct);
        if (request.PaidAtUtc > now) throw new TopUpContractException(400, "invalid-topup-event");
        if (window is null || window.PlanCode is not ("Plus" or "Pro") || request.PaidAtUtc < window.StartsAtUtc || request.PaidAtUtc >= window.ExpiresAtUtc)
            return await PersistUnfulfilledAsync(request, link, account, null, "ineligible-unfulfilled", now, ct);
        var status = window.IsRevoked || window.ExpiresAtUtc <= now ? "expired-unfulfilled" :
            !account.SubscriptionActivationIsAuthoritative || account.SubscriptionPlanCode is "Free" ||
            account.SubscriptionPlanCode is not ("Plus" or "Pro" or "Max") ? "ineligible-unfulfilled" : "granted";
        if (status == "ineligible-unfulfilled")
            return await PersistUnfulfilledAsync(request, link, account, window.Id, status, now, ct);
        var receipt = new TahlilAppTopUpPurchaseRow { Id = Guid.NewGuid(), EventId = request.EventId, PurchaseId = request.PurchaseId, PaymentId = request.PaymentId, CustomerAccountId = account.Id, ExternalUserId = request.UserId, ExternalPlanSubscriptionId = window.Id, PlanSlug = request.Subscription.Slug, PlanStartsAtUtc = window.StartsAtUtc, PlanExpiresAtUtc = window.ExpiresAtUtc, CreditsPurchased = request.CreditsPurchased, PackageId = request.PackageId, PackageVersion = request.PackageVersion, PaidAtUtc = request.PaidAtUtc, Status = status, ReceivedAtUtc = now, GrantId = status == "granted" ? Guid.NewGuid() : null };
        billing.TahlilAppTopUpPurchases.Add(receipt);
        if (status == "granted")
        {
            var wallet = await billing.WalletProjections.SingleOrDefaultAsync(row => row.CustomerAccountId == account.Id, ct) ?? throw new InvalidOperationException("Wallet projection is not configured.");
            var ledgerId = Guid.NewGuid();
            receipt.GrantLedgerEntryId = ledgerId; receipt.GrantedAtUtc = now;
            billing.UsageLedgerEntries.Add(new UsageLedgerEntryRow { Id = ledgerId, CustomerAccountId = account.Id, ActorId = link.LocalUserId, TenantId = link.TenantId, EntryType = "Adjustment", OperationCode = "Billing.TahlilAppCreditTopUpGrant", CreditsCharged = request.CreditsPurchased, PricingPolicyVersion = "tahlilapp-topup-v1", IdempotencyKey = $"tahlilapp-topup:{request.PurchaseId}", OccurredAt = now, ExternalUserId = request.UserId, AuditDescription = $"Verified TahlilApp top-up; purchase {request.PurchaseId}.", RelatedEntryId = receipt.Id, AllocationSource = "tahlilapp-topup" });
            billing.TopUpCreditSources.Add(new TopUpCreditSourceRow { Id = receipt.GrantId!.Value, CustomerAccountId = account.Id, ExternalPlanSubscriptionId = window.Id, PurchaseId = receipt.Id, GrantLedgerEntryId = ledgerId, GrantedCredits = request.CreditsPurchased, RemainingCredits = request.CreditsPurchased, ExpiresAtUtc = window.ExpiresAtUtc, CreatedAtUtc = now, Revision = 1 });
            wallet.Balance += request.CreditsPurchased; wallet.UpdatedAt = now; wallet.Revision++;
            BillingOutboxWriter.Add(billing, "TahlilAppTopUpPurchase", receipt.Id,
                "Billing.TahlilAppCreditTopUpGranted", $"tahlilapp-topup:{request.PurchaseId}:granted",
                new { receipt.PurchaseId, receipt.CustomerAccountId, request.CreditsPurchased, GrantId = receipt.GrantId, ExpiresAtUtc = window.ExpiresAtUtc }, now);
        }
        billing.AdminAudits.Add(new BillingAdminAuditRow { Id = Guid.NewGuid(), OccurredAt = now, TenantId = link.TenantId, ActorId = link.LocalUserId, ActionCode = status == "granted" ? "billing.tahlilapp-topup.granted" : "billing.tahlilapp-topup.unfulfilled", TargetType = "TahlilAppTopUpPurchase", TargetId = receipt.Id.ToString(), Reason = status, CorrelationId = request.EventId, IdempotencyKey = $"tahlilapp-topup:{request.PurchaseId}", After = $"{request.CreditsPurchased}|{window.ExpiresAtUtc:O}" });
        if (status != "granted")
            BillingOutboxWriter.Add(billing, "TahlilAppTopUpPurchase", receipt.Id,
                "Billing.TahlilAppCreditTopUpUnfulfilled", $"tahlilapp-topup:{request.PurchaseId}:unfulfilled",
                new { receipt.PurchaseId, receipt.CustomerAccountId, receipt.Status, receipt.PlanExpiresAtUtc }, now);
        try { await billing.SaveChangesAsync(ct); }
        catch (DbUpdateException) { billing.ChangeTracker.Clear(); existing = await FindExistingAsync(request, ct); if (existing is not null) return await ReplayOrRecordConflictAsync(existing, request, ct); throw; }
        if (status == "ineligible-unfulfilled") throw new TopUpContractException(409, "topup-ineligible");
        return Map(receipt);
    }

    public async Task<TopUpStatusResponse?> GetStatusAsync(Guid userId, Guid tenantId, string purchaseId, CancellationToken ct)
    {
        var accountId = await billing.CustomerAccounts.AsNoTracking().Where(row => row.UserId == userId && row.TenantId == tenantId).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(ct);
        if (accountId is null) return null;
        var row = await billing.TahlilAppTopUpPurchases.AsNoTracking().SingleOrDefaultAsync(item => item.CustomerAccountId == accountId && item.PurchaseId == purchaseId, ct);
        return row is null ? null : new(row.Status, row.PurchaseId, row.GrantId, row.Status == "granted" ? row.CreditsPurchased : 0, row.PlanExpiresAtUtc, row.GrantedAtUtc);
    }

    public async Task<TopUpReconciliationResponse> GetReconciliationAsync(Guid tenantId, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var purchases = await (from receipt in billing.TahlilAppTopUpPurchases.AsNoTracking()
                               join account in billing.CustomerAccounts.AsNoTracking() on receipt.CustomerAccountId equals account.Id
                               where account.TenantId == tenantId
                               orderby receipt.ReceivedAtUtc descending, receipt.Id
                               select new TopUpReconciliationPurchase(receipt.PurchaseId, receipt.PaymentId, receipt.EventId,
                                   receipt.ExternalUserId, receipt.CustomerAccountId, receipt.CreditsPurchased,
                                   receipt.PackageId ?? string.Empty, receipt.PackageVersion ?? string.Empty, receipt.Status,
                                   receipt.PlanSlug, receipt.PlanStartsAtUtc, receipt.PlanExpiresAtUtc, receipt.PaidAtUtc,
                                   receipt.ReceivedAtUtc, receipt.GrantId, receipt.GrantedAtUtc))
            .Take(limit).ToListAsync(ct);
        var conflicts = await billing.AdminAudits.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.ActionCode == "billing.tahlilapp-topup.identity-conflict")
            .OrderByDescending(row => row.OccurredAt).ThenBy(row => row.Id).Take(limit)
            .Select(row => new TopUpIdentityConflict(row.OccurredAt, row.TargetId, row.CorrelationId,
                row.Reason, row.Before ?? string.Empty, row.After ?? string.Empty))
            .ToListAsync(ct);
        return new(purchases, conflicts, clock.GetUtcNow());
    }

    private async Task<TopUpDeliveryResponse> ReplayOrRecordConflictAsync(TahlilAppTopUpPurchaseRow existing,
        TopUpDeliveryRequest request, CancellationToken ct)
    {
        try { return Replay(existing, request); }
        catch (TopUpContractException exception) when (exception.Code == "topup-identity-conflict")
        {
            var account = await billing.CustomerAccounts.AsNoTracking()
                .Where(row => row.Id == existing.CustomerAccountId)
                .Select(row => new { row.TenantId, row.UserId })
                .SingleAsync(ct);
            static string Identity(string eventId, string purchaseId, string paymentId) =>
                $"event={eventId};purchase={purchaseId};payment={paymentId}";
            billing.AdminAudits.Add(new BillingAdminAuditRow
            {
                Id = Guid.NewGuid(), OccurredAt = clock.GetUtcNow(), TenantId = account.TenantId,
                ActorId = account.UserId ?? Guid.Empty, ActionCode = "billing.tahlilapp-topup.identity-conflict",
                TargetType = "TahlilAppTopUpPurchase", TargetId = existing.Id.ToString("D"),
                Reason = exception.Code, CorrelationId = request.EventId,
                Before = Identity(existing.EventId, existing.PurchaseId, existing.PaymentId),
                After = Identity(request.EventId, request.PurchaseId, request.PaymentId)
            });
            await billing.SaveChangesAsync(ct);
            throw;
        }
    }
    private async Task<TopUpDeliveryResponse> PersistUnfulfilledAsync(TopUpDeliveryRequest request,
        TahlilAppExternalIdentityRow link, CustomerAccountRow account, Guid? windowId, string status,
        DateTimeOffset now, CancellationToken ct)
    {
        var row = new TahlilAppTopUpPurchaseRow
        {
            Id = Guid.NewGuid(), EventId = request.EventId, PurchaseId = request.PurchaseId, PaymentId = request.PaymentId,
            CustomerAccountId = account.Id, ExternalUserId = request.UserId, ExternalPlanSubscriptionId = windowId,
            PlanSlug = request.Subscription.Slug, PlanStartsAtUtc = request.Subscription.StartsAtUtc,
            PlanExpiresAtUtc = request.Subscription.ExpiresAtUtc, CreditsPurchased = request.CreditsPurchased,
            PackageId = request.PackageId, PackageVersion = request.PackageVersion, PaidAtUtc = request.PaidAtUtc,
            Status = status, ReceivedAtUtc = now
        };
        billing.TahlilAppTopUpPurchases.Add(row);
        billing.AdminAudits.Add(new BillingAdminAuditRow
        {
            Id = Guid.NewGuid(), OccurredAt = now, TenantId = link.TenantId, ActorId = link.LocalUserId,
            ActionCode = "billing.tahlilapp-topup.unfulfilled", TargetType = "TahlilAppTopUpPurchase",
            TargetId = row.Id.ToString(), Reason = status, CorrelationId = request.EventId
        });
        BillingOutboxWriter.Add(billing, "TahlilAppTopUpPurchase", row.Id,
            "Billing.TahlilAppCreditTopUpUnfulfilled", $"tahlilapp-topup:{request.PurchaseId}:unfulfilled",
            new { row.PurchaseId, row.CustomerAccountId, row.Status, row.PlanExpiresAtUtc }, now);
        try { await billing.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            billing.ChangeTracker.Clear();
            var existing = await FindExistingAsync(request, ct);
            if (existing is not null) return await ReplayOrRecordConflictAsync(existing, request, ct);
            throw;
        }
        if (status == "ineligible-unfulfilled") throw new TopUpContractException(409, "topup-ineligible");
        return Map(row);
    }
    private async Task<TopUpOfferRow?> ResolveOfferAsync(TopUpDeliveryRequest r, CancellationToken ct)
    {
        var active = await billing.TopUpOffers.Where(x => x.IsActive && x.Credits == r.CreditsPurchased).ToListAsync(ct);
        if (r.PackageId is null && r.PackageVersion is null) return active.OrderBy(x => x.Version).FirstOrDefault();
        if (r.PackageId is null || r.PackageVersion is null) return null;
        return active.SingleOrDefault(x => x.OfferId == r.PackageId && x.Version == r.PackageVersion);
    }
    private async Task RefreshAuthoritativeStatusAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (entitlementRefresher is null) throw new TopUpContractException(503, "topup-temporary-failure");
        try { await entitlementRefresher.RefreshIfDueAsync(tenantId, userId, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { throw new TopUpContractException(503, "topup-temporary-failure"); }
        var link = await auth.TahlilAppExternalIdentities.AsNoTracking().SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.LocalUserId == userId && row.Provider == "TahlilApp", ct);
        if (link?.LastSyncFailureCategory is not null) throw new TopUpContractException(503, "topup-temporary-failure");
    }
    private Task<TahlilAppTopUpPurchaseRow?> FindExistingAsync(TopUpDeliveryRequest r, CancellationToken ct) =>
        billing.TahlilAppTopUpPurchases.SingleOrDefaultAsync(x => x.Partner == "TahlilApp" && (x.EventId == r.EventId || x.PurchaseId == r.PurchaseId || x.PaymentId == r.PaymentId), ct);
    private static TopUpDeliveryResponse Replay(TahlilAppTopUpPurchaseRow x, TopUpDeliveryRequest r)
    {
        if (x.PurchaseId != r.PurchaseId || x.PaymentId != r.PaymentId || x.ExternalUserId != r.UserId ||
            x.PlanSlug != r.Subscription.Slug || !SameContractInstant(x.PlanStartsAtUtc, r.Subscription.StartsAtUtc) ||
            !SameContractInstant(x.PlanExpiresAtUtc, r.Subscription.ExpiresAtUtc) ||
            x.CreditsPurchased != r.CreditsPurchased || x.PackageId != r.PackageId ||
            x.PackageVersion != r.PackageVersion || !SameContractInstant(x.PaidAtUtc, r.PaidAtUtc))
            throw new TopUpContractException(409, "topup-identity-conflict");
        return Map(x);
    }
    private static bool SameContractInstant(DateTimeOffset left, DateTimeOffset right) =>
        left.ToUnixTimeMilliseconds() == right.ToUnixTimeMilliseconds();
    private static TopUpDeliveryResponse Map(TahlilAppTopUpPurchaseRow x) => new(x.Status, x.PurchaseId, x.GrantId, x.Status == "granted" ? x.CreditsPurchased : 0, x.PlanExpiresAtUtc, x.GrantedAtUtc);
    private static void Validate(TopUpDeliveryRequest r)
    {
        static bool Id(string? x) => !string.IsNullOrWhiteSpace(x) && x.Length <= 160 && x.Trim() == x;
        if (!Id(r.EventId) || !Id(r.PurchaseId) || !Id(r.PaymentId) || !Id(r.UserId) || r.Subscription is null || string.IsNullOrWhiteSpace(r.Subscription.Slug) || r.Subscription.Slug.Length > 100 || r.Subscription.ExpiresAtUtc <= r.Subscription.StartsAtUtc || r.PaidAtUtc < r.Subscription.StartsAtUtc || r.PackageId?.Length > 100 || r.PackageVersion?.Length > 40) throw new TopUpContractException(400, "invalid-topup-event");
    }
}

public sealed class TopUpContractException(int status, string code) : Exception(code) { public int Status { get; } = status; public string Code { get; } = code; }
