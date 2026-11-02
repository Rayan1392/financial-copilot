using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Billing.Contracts;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record TopUpDeliveryRequest(string InvoiceId, string UserId, int CreditsPurchased, DateTimeOffset PaidAtUtc);
public sealed record TopUpDeliveryResponse(string Status, string InvoiceId, Guid? GrantId, int CreditsGranted, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? GrantedAtUtc);
public sealed record TopUpStatusResponse(string Status, string InvoiceId, Guid? GrantId, int CreditsGranted, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? GrantedAtUtc);
public sealed record TahlilAppCreditBalanceResponse(string Plan, DateTimeOffset? SubscriptionStartsAtUtc,
    DateTimeOffset? SubscriptionExpiresAtUtc, string QuotaMode, decimal? AvailableCredits, DateTimeOffset AsOfUtc);
public sealed record TopUpReconciliationPurchase(string InvoiceId, string? LegacyPaymentId, string ExternalUserId,
    Guid CustomerAccountId, int CreditsPurchased, string Status,
    string? PlanSlug, DateTimeOffset? PlanStartsAtUtc, DateTimeOffset? PlanExpiresAtUtc, DateTimeOffset PaidAtUtc,
    DateTimeOffset ReceivedAtUtc, Guid? GrantId, DateTimeOffset? GrantedAtUtc);
public sealed record TopUpIdentityConflict(DateTimeOffset OccurredAtUtc, string ReceiptId, string InvoiceId,
    string ConflictCode, string ExistingIdentity, string IncomingIdentity);
public sealed record TopUpReconciliationResponse(IReadOnlyList<TopUpReconciliationPurchase> Purchases,
    IReadOnlyList<TopUpIdentityConflict> IdentityConflicts, DateTimeOffset GeneratedAtUtc);

public sealed class TahlilAppTopUpService(BillingDbContext billing, AuthDbContext auth, TimeProvider clock,
    ITahlilAppEntitlementRefresher? entitlementRefresher = null,
    IExpiryAwareWalletReadService? spendableWallets = null)
{
    public async Task<TahlilAppCreditBalanceResponse?> GetCreditBalanceAsync(string externalUserId, CancellationToken ct)
    {
        var link = await auth.TahlilAppExternalIdentities.AsNoTracking().SingleOrDefaultAsync(
            row => row.Provider == "TahlilApp" && row.ExternalUserId == externalUserId, ct);
        if (link is null || link.Status != "Active") return null;
        var user = await auth.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == link.LocalUserId, ct);
        var account = await billing.CustomerAccounts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.TenantId == link.TenantId && row.UserId == link.LocalUserId && row.AccountType == "Individual", ct);
        if (user is null || !user.IsEnabled || account is null) return null;

        var now = clock.GetUtcNow();
        var windows = await billing.ExternalPlanSubscriptions.AsNoTracking()
            .Where(row => row.CustomerAccountId == account.Id && !row.IsRevoked && row.StartsAtUtc <= now && row.ExpiresAtUtc > now)
            .Select(row => new { row.IdentityKey, row.PlanCode, row.StartsAtUtc, row.ExpiresAtUtc })
            .ToListAsync(ct);
        var decision = TahlilAppSubscriptionPolicy.ResolveActivePlanSet(windows.Select(row =>
            new TahlilAppPlanSubscription(row.IdentityKey, row.PlanCode, row.StartsAtUtc, row.ExpiresAtUtc, row.IdentityKey)).ToArray(), now);
        var plan = decision.PlanCode;
        var startsAt = decision.EffectiveFrom;
        var expiresAt = decision.EffectiveTo;
        if (plan == "Free")
        {
            startsAt = account.SubscriptionEffectiveFrom;
            expiresAt = account.SubscriptionEffectiveTo;
        }
        else if (startsAt is null)
        {
            var selected = windows.Where(row => row.PlanCode == plan && row.ExpiresAtUtc == expiresAt)
                .Select(row => (DateTimeOffset?)row.StartsAtUtc).Min();
            startsAt = selected;
        }

        if (plan == "Max")
            return new(plan, startsAt, expiresAt, "unlimited", null, now);
        if (spendableWallets is null) throw new TopUpContractException(503, "topup-temporary-failure");
        var wallet = await spendableWallets.GetSpendableSnapshotAsync(account.Id, ct);
        return new(plan, startsAt, expiresAt, "finite", Math.Max(0m, wallet.Balance - wallet.ReservedAmount), now);
    }

    public async Task<bool> TryConsumeTokenJtiAsync(string issuer, string jti, DateTimeOffset expiresAtUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(jti) || jti.Length > 200 || expiresAtUtc <= clock.GetUtcNow()) return false;
        if (await billing.ConsumedPartnerTokens.AsNoTracking().AnyAsync(row => row.Issuer == issuer && row.Jti == jti, ct)) return false;
        billing.ConsumedPartnerTokens.Add(new ConsumedPartnerTokenRow { Issuer = issuer, Jti = jti, ExpiresAtUtc = expiresAtUtc, ConsumedAtUtc = clock.GetUtcNow() });
        try { await billing.SaveChangesAsync(ct); return true; }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            billing.ChangeTracker.Clear();
            if (await billing.ConsumedPartnerTokens.AsNoTracking().AnyAsync(row => row.Issuer == issuer && row.Jti == jti, ct)) return false;
            throw;
        }
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
        if (request.PaidAtUtc > now) throw new TopUpContractException(400, "invalid-topup-event");
        var historicalWindows = await billing.ExternalPlanSubscriptions.AsNoTracking()
            .Where(row => row.CustomerAccountId == account.Id && row.StartsAtUtc <= request.PaidAtUtc && row.ExpiresAtUtc > request.PaidAtUtc)
            .Select(row => new { row.Id, row.IdentityKey, row.PlanCode, row.StartsAtUtc, row.ExpiresAtUtc, row.IsRevoked, row.RevokedAtUtc })
            .ToListAsync(ct);
        var activeWindowsAtPayment = historicalWindows
            .Where(row => !row.IsRevoked || row.RevokedAtUtc is null || row.RevokedAtUtc > request.PaidAtUtc).ToArray();
        var planAtPayment = TahlilAppSubscriptionPolicy.ResolveActivePlanSet(activeWindowsAtPayment
            .Select(row => new TahlilAppPlanSubscription(row.IdentityKey, row.PlanCode,
                row.StartsAtUtc, row.ExpiresAtUtc, row.IdentityKey)).ToArray(), request.PaidAtUtc);
        if (planAtPayment.PlanCode is not ("Plus" or "Pro"))
            return await PersistUnfulfilledAsync(request, link, account, null, "ineligible-unfulfilled", now, ct);
        var sourcesAtPayment = activeWindowsAtPayment.Where(row => row.PlanCode is "Plus" or "Pro")
            .OrderBy(row => row.ExpiresAtUtc).ThenBy(row => row.StartsAtUtc).ToArray();
        if (sourcesAtPayment.Length > 1)
            return await PersistUnfulfilledAsync(request, link, account, null, "ambiguous-unfulfilled", now, ct);
        var resolvedSource = sourcesAtPayment.SingleOrDefault();
        var window = resolvedSource is null ? null : await billing.ExternalPlanSubscriptions.SingleOrDefaultAsync(row => row.Id == resolvedSource.Id, ct);
        if (window is null)
            return await PersistUnfulfilledAsync(request, link, account, null, "ineligible-unfulfilled", now, ct);
        var status = window.IsRevoked || window.ExpiresAtUtc <= now ? "expired-unfulfilled" :
            !account.SubscriptionActivationIsAuthoritative || account.SubscriptionPlanCode is "Free" ||
            account.SubscriptionPlanCode is not ("Plus" or "Pro" or "Max") ? "ineligible-unfulfilled" : "granted";
        if (status == "ineligible-unfulfilled")
            return await PersistUnfulfilledAsync(request, link, account, window.Id, status, now, ct);
        var receipt = new TahlilAppTopUpPurchaseRow { Id = Guid.NewGuid(), InvoiceId = request.InvoiceId, RequestFingerprint = Fingerprint(request), CustomerAccountId = account.Id, ExternalUserId = request.UserId, ExternalPlanSubscriptionId = window.Id, PlanSlug = window.IdentityKey[..window.IdentityKey.IndexOf(':')], PlanStartsAtUtc = window.StartsAtUtc, PlanExpiresAtUtc = window.ExpiresAtUtc, CreditsPurchased = request.CreditsPurchased, PaidAtUtc = request.PaidAtUtc, Status = status, ReceivedAtUtc = now, GrantId = status == "granted" ? Guid.NewGuid() : null };
        billing.TahlilAppTopUpPurchases.Add(receipt);
        if (status == "granted")
        {
            var wallet = await billing.WalletProjections.SingleOrDefaultAsync(row => row.CustomerAccountId == account.Id, ct) ?? throw new InvalidOperationException("Wallet projection is not configured.");
            var ledgerId = Guid.NewGuid();
            receipt.GrantLedgerEntryId = ledgerId; receipt.GrantedAtUtc = now;
            billing.UsageLedgerEntries.Add(new UsageLedgerEntryRow { Id = ledgerId, CustomerAccountId = account.Id, ActorId = link.LocalUserId, TenantId = link.TenantId, EntryType = "Adjustment", OperationCode = "Billing.TahlilAppCreditTopUpGrant", CreditsCharged = request.CreditsPurchased, PricingPolicyVersion = "tahlilapp-topup-v1", IdempotencyKey = $"tahlilapp-topup:{request.InvoiceId}", OccurredAt = now, ExternalUserId = request.UserId, AuditDescription = $"Verified TahlilApp top-up invoice {request.InvoiceId}.", RelatedEntryId = receipt.Id, AllocationSource = "tahlilapp-topup" });
            billing.TopUpCreditSources.Add(new TopUpCreditSourceRow { Id = receipt.GrantId!.Value, CustomerAccountId = account.Id, ExternalPlanSubscriptionId = window.Id, PurchaseId = receipt.Id, GrantLedgerEntryId = ledgerId, GrantedCredits = request.CreditsPurchased, RemainingCredits = request.CreditsPurchased, ExpiresAtUtc = window.ExpiresAtUtc, CreatedAtUtc = now, Revision = 1 });
            wallet.Balance += request.CreditsPurchased; wallet.UpdatedAt = now; wallet.Revision++;
            BillingOutboxWriter.Add(billing, "TahlilAppTopUpPurchase", receipt.Id,
                "Billing.TahlilAppCreditTopUpGranted", $"tahlilapp-topup:{request.InvoiceId}:granted",
                new { receipt.InvoiceId, receipt.CustomerAccountId, request.CreditsPurchased, GrantId = receipt.GrantId, ExpiresAtUtc = window.ExpiresAtUtc }, now);
        }
        billing.AdminAudits.Add(new BillingAdminAuditRow { Id = Guid.NewGuid(), OccurredAt = now, TenantId = link.TenantId, ActorId = link.LocalUserId, ActionCode = status == "granted" ? "billing.tahlilapp-topup.granted" : "billing.tahlilapp-topup.unfulfilled", TargetType = "TahlilAppTopUpPurchase", TargetId = receipt.Id.ToString(), Reason = status, CorrelationId = request.InvoiceId, IdempotencyKey = $"tahlilapp-topup:{request.InvoiceId}", After = $"{request.CreditsPurchased}|{window.ExpiresAtUtc:O}" });
        if (status != "granted")
            BillingOutboxWriter.Add(billing, "TahlilAppTopUpPurchase", receipt.Id,
                "Billing.TahlilAppCreditTopUpUnfulfilled", $"tahlilapp-topup:{request.InvoiceId}:unfulfilled",
                new { receipt.InvoiceId, receipt.CustomerAccountId, receipt.Status, receipt.PlanExpiresAtUtc }, now);
        try { await billing.SaveChangesAsync(ct); }
        catch (DbUpdateException) { billing.ChangeTracker.Clear(); existing = await FindExistingAsync(request, ct); if (existing is not null) return await ReplayOrRecordConflictAsync(existing, request, ct); throw; }
        if (status == "ineligible-unfulfilled") throw new TopUpContractException(409, "topup-ineligible");
        return Map(receipt);
    }

    public async Task<TopUpStatusResponse?> GetStatusAsync(Guid userId, Guid tenantId, string invoiceId, CancellationToken ct)
    {
        var accountId = await billing.CustomerAccounts.AsNoTracking().Where(row => row.UserId == userId && row.TenantId == tenantId).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(ct);
        if (accountId is null) return null;
        var row = await billing.TahlilAppTopUpPurchases.AsNoTracking().SingleOrDefaultAsync(item => item.CustomerAccountId == accountId && item.InvoiceId == invoiceId, ct);
        return row is null ? null : new(row.Status, row.InvoiceId, row.GrantId, row.Status == "granted" ? row.CreditsPurchased : 0, row.PlanExpiresAtUtc, row.GrantedAtUtc);
    }

    public async Task<TopUpReconciliationResponse> GetReconciliationAsync(Guid tenantId, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var purchases = await (from receipt in billing.TahlilAppTopUpPurchases.AsNoTracking()
                               join account in billing.CustomerAccounts.AsNoTracking() on receipt.CustomerAccountId equals account.Id
                               where account.TenantId == tenantId
                               orderby receipt.ReceivedAtUtc descending, receipt.Id
                               select new TopUpReconciliationPurchase(receipt.InvoiceId, receipt.PaymentId, receipt.ExternalUserId,
                                   receipt.CustomerAccountId, receipt.CreditsPurchased, receipt.Status,
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
            static string Identity(string invoiceId, string fingerprint) => $"invoice={invoiceId};fingerprint={fingerprint}";
            billing.AdminAudits.Add(new BillingAdminAuditRow
            {
                Id = Guid.NewGuid(), OccurredAt = clock.GetUtcNow(), TenantId = account.TenantId,
                ActorId = account.UserId ?? Guid.Empty, ActionCode = "billing.tahlilapp-topup.identity-conflict",
                TargetType = "TahlilAppTopUpPurchase", TargetId = existing.Id.ToString("D"),
                Reason = exception.Code, CorrelationId = request.InvoiceId,
                Before = Identity(existing.InvoiceId, existing.RequestFingerprint),
                After = Identity(request.InvoiceId, Fingerprint(request))
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
            Id = Guid.NewGuid(), InvoiceId = request.InvoiceId, RequestFingerprint = Fingerprint(request),
            CustomerAccountId = account.Id, ExternalUserId = request.UserId, ExternalPlanSubscriptionId = windowId,
            PlanSlug = null, PlanStartsAtUtc = null, PlanExpiresAtUtc = null, CreditsPurchased = request.CreditsPurchased,
            PaidAtUtc = request.PaidAtUtc,
            Status = status, ReceivedAtUtc = now
        };
        billing.TahlilAppTopUpPurchases.Add(row);
        billing.AdminAudits.Add(new BillingAdminAuditRow
        {
            Id = Guid.NewGuid(), OccurredAt = now, TenantId = link.TenantId, ActorId = link.LocalUserId,
            ActionCode = "billing.tahlilapp-topup.unfulfilled", TargetType = "TahlilAppTopUpPurchase",
            TargetId = row.Id.ToString(), Reason = status, CorrelationId = request.InvoiceId
        });
        BillingOutboxWriter.Add(billing, "TahlilAppTopUpPurchase", row.Id,
            "Billing.TahlilAppCreditTopUpUnfulfilled", $"tahlilapp-topup:{request.InvoiceId}:unfulfilled",
            new { row.InvoiceId, row.CustomerAccountId, row.Status, row.PlanExpiresAtUtc }, now);
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
        return await billing.TopUpOffers.Where(x => x.IsActive && x.Credits == r.CreditsPurchased).OrderBy(x => x.Version).FirstOrDefaultAsync(ct);
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
        billing.TahlilAppTopUpPurchases.SingleOrDefaultAsync(x => x.Partner == "TahlilApp" && x.InvoiceId == r.InvoiceId, ct);
    private static TopUpDeliveryResponse Replay(TahlilAppTopUpPurchaseRow x, TopUpDeliveryRequest r)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(x.RequestFingerprint), Encoding.ASCII.GetBytes(Fingerprint(r))))
            throw new TopUpContractException(409, "topup-identity-conflict");
        return Map(x);
    }
    private static bool SameContractInstant(DateTimeOffset left, DateTimeOffset right) =>
        left.ToUnixTimeMilliseconds() == right.ToUnixTimeMilliseconds();
    private static TopUpDeliveryResponse Map(TahlilAppTopUpPurchaseRow x) => new(x.Status, x.InvoiceId, x.GrantId, x.Status == "granted" ? x.CreditsPurchased : 0, x.PlanExpiresAtUtc, x.GrantedAtUtc);
    private static string Fingerprint(TopUpDeliveryRequest r)
    {
        var canonical = string.Join("\n", r.InvoiceId, r.UserId, r.CreditsPurchased.ToString(System.Globalization.CultureInfo.InvariantCulture),
            r.PaidAtUtc.ToUniversalTime().ToString("O"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    private static void Validate(TopUpDeliveryRequest r)
    {
        static bool Id(string? x, int max) => !string.IsNullOrWhiteSpace(x) && x.Length <= max && x.Trim() == x;
        if (!Id(r.InvoiceId, 160) || !Id(r.UserId, 128) || r.CreditsPurchased <= 0 || r.PaidAtUtc == default) throw new TopUpContractException(400, "invalid-topup-event");
    }
}

public sealed class TopUpContractException(int status, string code) : Exception(code) { public int Status { get; } = status; public string Code { get; } = code; }
