using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics.Metrics;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Purchases;
using FinancialCopilot.Billing.Usage;
using FinancialCopilot.Domain.Financial.Insights;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class BillingPurchaseUseCases(
    BillingDbContext dbContext,
    IBillableAccountResolver accountResolver,
    INotificationIntentPublisher notifications,
    TimeProvider timeProvider) : IBillingPurchaseUseCases
{
    private const string PurchasedCreditsOperation = "Billing.PurchasedCredits";
    private const string PurchasedCreditsPolicyVersion = "purchase-v1";
    private static readonly Meter Meter = new("FinancialCopilot.Billing.Purchases", "1.0.0");
    private static readonly Counter<long> CheckoutCreatedCounter = Meter.CreateCounter<long>("billing.checkout.created");
    private static readonly Counter<long> ReceiptSubmittedCounter = Meter.CreateCounter<long>("billing.checkout.receipt_submitted");
    private static readonly Counter<long> ReviewDecisionCounter = Meter.CreateCounter<long>("billing.checkout.review_decision");
    private static readonly Counter<long> FulfillmentCounter = Meter.CreateCounter<long>("billing.checkout.fulfilled");
    private static readonly Counter<long> CallbackValidationCounter = Meter.CreateCounter<long>("billing.payment_callback.validated");

    public async Task<BillingCatalogView> GetCatalogAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        var normalizedChannel = NormalizeChannel(channel);
        var products = await dbContext.PurchaseProducts
            .AsNoTracking()
            .Where(row => row.Channel == normalizedChannel && row.IsActive)
            .OrderBy(row => row.SortOrder)
            .Select(row => new BillingCatalogProductView(
                row.Code,
                Enum.Parse<BillingPurchaseProductType>(row.ProductType),
                row.Version,
                row.DisplayName,
                row.Amount,
                row.Currency,
                row.Credits,
                row.PlanCode,
                row.DurationDays,
                row.Channel))
            .ToArrayAsync(cancellationToken);

        return new BillingCatalogView(products, timeProvider.GetUtcNow());
    }

    public async Task<BillingCheckoutView> CreateCheckoutAsync(
        CreateBillingCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(command.IdempotencyKey);
        var account = await accountResolver.ResolveAsync(command.Actor, cancellationToken);
        var product = await GetActiveProductAsync(
            command.ProductCode,
            NormalizeChannel(command.Channel),
            cancellationToken);

        var idempotencyKey = command.IdempotencyKey.Trim();
        var existing = await dbContext.CheckoutIntents
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            EnsureSameCreate(existing, command.Actor, account.Id, product);
            return Map(existing);
        }

        var now = timeProvider.GetUtcNow();
        var row = new BillingCheckoutIntentRow
        {
            Id = Guid.NewGuid(),
            TenantId = command.Actor.TenantId,
            ActorId = command.Actor.ActorId,
            CustomerAccountId = account.Id,
            ProductType = product.ProductType.ToString(),
            ProductCode = product.Code,
            ProductVersion = product.Version,
            ProductDisplayName = product.DisplayName,
            Amount = product.Amount,
            Currency = product.Currency,
            Credits = product.Credits,
            PlanCode = product.PlanCode,
            DurationDays = product.DurationDays,
            Channel = product.Channel,
            PaymentReference = BuildPaymentReference(now),
            Status = BillingCheckoutStatus.AwaitingPayment.ToString(),
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(48),
            ConcurrencyToken = Guid.NewGuid(),
            Version = 1
        };
        dbContext.CheckoutIntents.Add(row);
        AddAudit(
            command.Actor.TenantId,
            command.Actor.ActorId,
            "billing.checkout.created",
            row.Id,
            "Checkout created from Telegram catalog.",
            command.CorrelationId,
            row.IdempotencyKey,
            null,
            new { row.ProductCode, row.Amount, row.Currency, row.PaymentReference });
        BillingOutboxWriter.Add(
            dbContext,
            "BillingCheckoutIntent",
            row.Id,
            "Billing.CheckoutCreated",
            $"{row.IdempotencyKey}:created",
            new { row.TenantId, row.ActorId, row.ProductCode, row.PaymentReference },
            now);

        await dbContext.SaveChangesAsync(cancellationToken);
        CheckoutCreatedCounter.Add(1, KeyValuePair.Create<string, object?>("product_type", row.ProductType));
        return Map(row);
    }

    public async Task<BillingCheckoutView?> GetCheckoutAsync(
        BillableActorContext actor,
        Guid checkoutId,
        CancellationToken cancellationToken)
    {
        var account = await accountResolver.ResolveAsync(actor, cancellationToken);
        var row = await dbContext.CheckoutIntents.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == checkoutId &&
                candidate.TenantId == actor.TenantId &&
                candidate.ActorId == actor.ActorId &&
                candidate.CustomerAccountId == account.Id,
            cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<BillingCheckoutPage> GetMyCheckoutsAsync(
        BillableActorContext actor,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        pageSize = Math.Clamp(pageSize, 1, 50);
        var account = await accountResolver.ResolveAsync(actor, cancellationToken);
        var rows = await dbContext.CheckoutIntents.AsNoTracking()
            .Where(row => row.TenantId == actor.TenantId &&
                row.ActorId == actor.ActorId &&
                row.CustomerAccountId == account.Id)
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .Skip(offset)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);
        return new BillingCheckoutPage(
            rows.Take(pageSize).Select(Map).ToArray(),
            offset,
            pageSize,
            rows.Length > pageSize);
    }

    public async Task<BillingCheckoutView> SubmitReceiptAsync(
        SubmitBillingReceiptCommand command,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(command.IdempotencyKey);
        if (!IsAllowedAttachmentKind(command.AttachmentKind))
            throw new InvalidOperationException("Receipt attachment kind is not allowed.");
        if (string.IsNullOrWhiteSpace(command.AttachmentReference) || command.AttachmentReference.Length > 500)
            throw new InvalidOperationException("Receipt attachment reference is invalid.");

        var account = await accountResolver.ResolveAsync(command.Actor, cancellationToken);
        var row = await RequireActorCheckoutAsync(command.Actor, account.Id, command.CheckoutId, cancellationToken);
        if (row.ReceiptIdempotencyKey == command.IdempotencyKey)
        {
            return Map(row) with { AlreadyApplied = true };
        }

        EnsureExpectedVersion(row, command.ExpectedVersion);
        var now = timeProvider.GetUtcNow();
        var intent = MapIntent(row);
        intent.EnsureNotExpired(now);
        intent.TransitionTo(BillingCheckoutStatus.ReceiptSubmitted);
        intent.TransitionTo(BillingCheckoutStatus.UnderReview);
        ApplyStatus(row, intent, now);
        row.ReceiptIdempotencyKey = command.IdempotencyKey.Trim();
        row.ReceiptAttachmentKind = command.AttachmentKind.Trim();
        row.ReceiptAttachmentReference = command.AttachmentReference.Trim();
        row.ReceiptContentHash = Sha256(command.AttachmentReference.Trim());
        row.ProviderName = "ManualReceipt";
        row.ProviderReferenceHash = string.IsNullOrWhiteSpace(command.ProviderReference)
            ? null
            : Sha256(command.ProviderReference.Trim());
        row.ReceiptSubmittedAtUtc = now;
        AddAudit(
            command.Actor.TenantId,
            command.Actor.ActorId,
            "billing.receipt.submitted",
            row.Id,
            "Receipt submitted for manual review.",
            command.CorrelationId,
            row.ReceiptIdempotencyKey,
            null,
            new { row.ReceiptAttachmentKind, row.Status });

        await dbContext.SaveChangesAsync(cancellationToken);
        ReceiptSubmittedCounter.Add(1, KeyValuePair.Create<string, object?>("product_type", row.ProductType));
        return Map(row);
    }

    public async Task<BillingCheckoutView> CancelCheckoutAsync(
        CancelBillingCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new InvalidOperationException("Cancellation reason is required.");
        var account = await accountResolver.ResolveAsync(command.Actor, cancellationToken);
        var row = await RequireActorCheckoutAsync(command.Actor, account.Id, command.CheckoutId, cancellationToken);
        EnsureExpectedVersion(row, command.ExpectedVersion);
        var now = timeProvider.GetUtcNow();
        var intent = MapIntent(row);
        intent.EnsureNotExpired(now);
        intent.TransitionTo(BillingCheckoutStatus.Cancelled);
        ApplyStatus(row, intent, now);
        row.ReviewReason = command.Reason.Trim();
        AddAudit(
            command.Actor.TenantId,
            command.Actor.ActorId,
            "billing.checkout.cancelled",
            row.Id,
            row.ReviewReason,
            command.CorrelationId,
            null,
            null,
            new { row.Status });
        await dbContext.SaveChangesAsync(cancellationToken);
        await PublishCheckoutStatusAsync(row, "BillingCheckoutCancelled", InsightSeverity.Notice, cancellationToken);
        return Map(row);
    }

    public async Task<BillingCheckoutView> ReviewReceiptAsync(
        ReviewBillingReceiptCommand command,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(command.IdempotencyKey);
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new InvalidOperationException("Review reason is required.");

        var row = await dbContext.CheckoutIntents.SingleOrDefaultAsync(
            candidate => candidate.Id == command.CheckoutId && candidate.TenantId == command.TenantId,
            cancellationToken) ?? throw new KeyNotFoundException("Checkout was not found.");
        if (row.ReviewIdempotencyKey == command.IdempotencyKey)
        {
            return Map(row) with { AlreadyApplied = true };
        }

        EnsureExpectedVersion(row, command.ExpectedVersion);
        var now = timeProvider.GetUtcNow();
        var intent = MapIntent(row);
        intent.EnsureNotExpired(now);
        if (intent.Status != BillingCheckoutStatus.UnderReview)
            throw new InvalidOperationException("Only checkouts under review can receive a reviewer decision.");

        if (!command.Approved)
        {
            intent.TransitionTo(BillingCheckoutStatus.Rejected);
            ApplyStatus(row, intent, now);
            row.ReviewerActorId = command.ReviewerActorId;
            row.ReviewReason = command.Reason.Trim();
            row.ReviewIdempotencyKey = command.IdempotencyKey.Trim();
            row.ReviewedAtUtc = now;
            AddAudit(command.TenantId, command.ReviewerActorId, "billing.receipt.rejected", row.Id,
                row.ReviewReason, command.CorrelationId, row.ReviewIdempotencyKey, null, new { row.Status });
            await dbContext.SaveChangesAsync(cancellationToken);
            ReviewDecisionCounter.Add(1, KeyValuePair.Create<string, object?>("decision", "rejected"));
            await PublishCheckoutStatusAsync(row, "BillingCheckoutRejected", InsightSeverity.Important, cancellationToken);
            return Map(row);
        }

        intent.TransitionTo(BillingCheckoutStatus.Approved);
        ApplyStatus(row, intent, now);
        row.ReviewerActorId = command.ReviewerActorId;
        row.ReviewReason = command.Reason.Trim();
        row.ReviewIdempotencyKey = command.IdempotencyKey.Trim();
        row.ReviewedAtUtc = now;
        FulfillApproved(row, command.ReviewerActorId, command.CorrelationId, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        ReviewDecisionCounter.Add(1, KeyValuePair.Create<string, object?>("decision", "approved"));
        FulfillmentCounter.Add(1, KeyValuePair.Create<string, object?>("product_type", row.ProductType));
        await PublishCheckoutStatusAsync(row, "BillingCheckoutFulfilled", InsightSeverity.Important, cancellationToken);
        return Map(row);
    }

    public Task<PaymentCallbackResult> ProcessPaymentCallbackAsync(
        PaymentCallbackCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Provider) ||
            string.IsNullOrWhiteSpace(command.PaymentReference) ||
            string.IsNullOrWhiteSpace(command.ProviderReference) ||
            string.IsNullOrWhiteSpace(command.Signature) ||
            string.IsNullOrWhiteSpace(command.Nonce))
        {
            CallbackValidationCounter.Add(1, KeyValuePair.Create<string, object?>("status", "rejected"));
            return Task.FromResult(new PaymentCallbackResult(
                "Rejected",
                null,
                Fulfilled: false,
                "Provider callback payload is incomplete."));
        }

        CallbackValidationCounter.Add(1, KeyValuePair.Create<string, object?>("status", "not_configured"));
        return Task.FromResult(new PaymentCallbackResult(
            "NotConfigured",
            null,
            Fulfilled: false,
            "Manual receipt review is the active MVP flow; provider callbacks require a configured authenticated adapter."));
    }

    public async Task<BillingReconciliationSummary> ReconcileAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.CheckoutIntents.AsNoTracking()
            .Where(row => row.TenantId == tenantId)
            .ToArrayAsync(cancellationToken);
        var statuses = rows.GroupBy(row => row.Status).ToDictionary(group => group.Key, group => group.Count());
        var fulfilled = rows.Where(row => row.Status == BillingCheckoutStatus.Fulfilled.ToString()).ToArray();
        var refunded = rows.Where(row => row.Status == BillingCheckoutStatus.Refunded.ToString()).ToArray();
        return new BillingReconciliationSummary(
            Count(statuses, BillingCheckoutStatus.AwaitingPayment),
            Count(statuses, BillingCheckoutStatus.UnderReview),
            Count(statuses, BillingCheckoutStatus.Fulfilled),
            Count(statuses, BillingCheckoutStatus.Rejected),
            Count(statuses, BillingCheckoutStatus.Expired),
            fulfilled.Sum(row => row.Amount),
            refunded.Sum(row => row.Amount),
            timeProvider.GetUtcNow());
    }

    private void FulfillApproved(
        BillingCheckoutIntentRow row,
        Guid reviewerActorId,
        string correlationId,
        DateTimeOffset now)
    {
        if (row.FulfilledAtUtc is not null)
        {
            return;
        }

        var payment = new FinancialTransactionRow
        {
            Id = Guid.NewGuid(),
            CustomerAccountId = row.CustomerAccountId,
            Type = FinancialTransactionType.Payment.ToString(),
            Amount = row.Amount,
            Currency = row.Currency,
            IdempotencyKey = $"{row.IdempotencyKey}:payment",
            OccurredAt = now
        };
        dbContext.FinancialTransactions.Add(payment);
        row.FulfillmentFinancialTransactionId = payment.Id;

        if (row.ProductType == BillingPurchaseProductType.CreditPack.ToString())
        {
            var wallet = dbContext.WalletProjections.SingleOrDefault(walletRow =>
                walletRow.CustomerAccountId == row.CustomerAccountId) ??
                throw new KeyNotFoundException("Wallet projection is not configured.");
            var ledger = new UsageLedgerEntryRow
            {
                Id = Guid.NewGuid(),
                CustomerAccountId = row.CustomerAccountId,
                ActorId = row.ActorId,
                TenantId = row.TenantId,
                EntryType = UsageLedgerEntryType.Adjustment.ToString(),
                OperationCode = PurchasedCreditsOperation,
                CreditsCharged = row.Credits,
                PricingPolicyVersion = PurchasedCreditsPolicyVersion,
                IdempotencyKey = $"{row.IdempotencyKey}:credits",
                OccurredAt = now,
                AuditDescription = $"Purchased credit pack {row.ProductCode} via {row.Channel}."
            };
            dbContext.UsageLedgerEntries.Add(ledger);
            wallet.Balance += row.Credits;
            wallet.UpdatedAt = now;
            wallet.Revision++;
            row.FulfillmentLedgerEntryId = ledger.Id;
        }
        else
        {
            var account = dbContext.CustomerAccounts.SingleOrDefault(accountRow =>
                accountRow.Id == row.CustomerAccountId && accountRow.TenantId == row.TenantId) ??
                throw new KeyNotFoundException("Billing account is not configured.");
            var effectiveFrom = now;
            if (account.SubscriptionEffectiveTo is not null && account.SubscriptionEffectiveTo > now)
            {
                effectiveFrom = account.SubscriptionEffectiveTo.Value;
            }

            account.SubscriptionPlanCode = row.PlanCode;
            account.SubscriptionEffectiveFrom = effectiveFrom;
            account.SubscriptionEffectiveTo = effectiveFrom.AddDays(row.DurationDays ?? 30);
            account.SubscriptionRevision++;
        }

        var intent = MapIntent(row);
        intent.TransitionTo(BillingCheckoutStatus.Fulfilled);
        ApplyStatus(row, intent, now);
        row.FulfilledAtUtc = now;
        AddAudit(row.TenantId, reviewerActorId, "billing.checkout.fulfilled", row.Id,
            row.ReviewReason ?? "Approved receipt fulfilled.", correlationId, row.ReviewIdempotencyKey,
            null, new { row.ProductCode, row.Status, row.FulfillmentFinancialTransactionId, row.FulfillmentLedgerEntryId });
        BillingOutboxWriter.Add(
            dbContext,
            "BillingCheckoutIntent",
            row.Id,
            "Billing.CheckoutFulfilled",
            $"{row.IdempotencyKey}:fulfilled",
            new { row.TenantId, row.ActorId, row.ProductCode, row.ProductType, row.Amount, row.Currency },
            now);
    }

    private async Task PublishCheckoutStatusAsync(
        BillingCheckoutIntentRow row,
        string eventType,
        InsightSeverity severity,
        CancellationToken cancellationToken)
    {
        await notifications.EnqueueAsync(
            new NotificationIntentRequest(
                new NotificationActor(row.TenantId, row.ActorId, "User"),
                NotificationChannel.Telegram,
                eventType,
                row.Id.ToString("D"),
                $"{eventType}:{row.Id:N}:{row.Status}",
                severity,
                JsonSerializer.Serialize(new
                {
                    row.Id,
                    row.ProductCode,
                    row.ProductDisplayName,
                    row.Amount,
                    row.Currency,
                    row.PaymentReference,
                    row.Status,
                    row.ReviewReason
                }),
                timeProvider.GetUtcNow(),
                timeProvider.GetUtcNow().AddDays(7),
                row.IdempotencyKey,
                EvidenceReference: $"BillingDbContext:billing_checkout_intents/{row.Id:D}",
                Category: "Billing",
                CooldownKey: $"BillingCheckout:{row.Id:N}:{row.Status}"),
            cancellationToken);
    }

    private async Task<BillingCheckoutIntentRow> RequireActorCheckoutAsync(
        BillableActorContext actor,
        Guid accountId,
        Guid checkoutId,
        CancellationToken cancellationToken) =>
        await dbContext.CheckoutIntents.SingleOrDefaultAsync(row =>
            row.Id == checkoutId &&
            row.TenantId == actor.TenantId &&
            row.ActorId == actor.ActorId &&
            row.CustomerAccountId == accountId,
            cancellationToken) ?? throw new KeyNotFoundException("Checkout was not found.");

    private async Task<BillingPurchaseProduct> GetActiveProductAsync(
        string productCode,
        string channel,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.PurchaseProducts.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Code == productCode.Trim() &&
                candidate.Channel == channel &&
                candidate.IsActive,
            cancellationToken) ?? throw new KeyNotFoundException("Purchase product was not found.");
        return new BillingPurchaseProduct(
            row.Code,
            Enum.Parse<BillingPurchaseProductType>(row.ProductType),
            row.Version,
            row.DisplayName,
            row.Amount,
            row.Currency,
            row.Credits,
            row.PlanCode,
            row.DurationDays,
            row.Channel,
            row.IsActive);
    }

    private void AddAudit(
        Guid tenantId,
        Guid actorId,
        string actionCode,
        Guid checkoutId,
        string reason,
        string correlationId,
        string? idempotencyKey,
        object? before,
        object? after) =>
        dbContext.AdminAudits.Add(new BillingAdminAuditRow
        {
            Id = Guid.NewGuid(),
            OccurredAt = timeProvider.GetUtcNow(),
            TenantId = tenantId,
            ActorId = actorId,
            ActionCode = actionCode,
            TargetType = "BillingCheckoutIntent",
            TargetId = checkoutId.ToString("D"),
            Reason = reason,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey,
            Before = before is null ? null : System.Text.Json.JsonSerializer.Serialize(before),
            After = after is null ? null : System.Text.Json.JsonSerializer.Serialize(after)
        });

    private static void ApplyStatus(
        BillingCheckoutIntentRow row,
        BillingCheckoutIntent intent,
        DateTimeOffset now)
    {
        row.Status = intent.Status.ToString();
        row.Version = intent.Version;
        row.UpdatedAtUtc = now;
        row.ConcurrencyToken = Guid.NewGuid();
    }

    private static BillingCheckoutIntent MapIntent(BillingCheckoutIntentRow row) =>
        new(
            row.Id,
            row.TenantId,
            row.ActorId,
            row.CustomerAccountId,
            Enum.Parse<BillingPurchaseProductType>(row.ProductType),
            row.ProductCode,
            row.ProductVersion,
            row.Amount,
            row.Currency,
            row.PaymentReference,
            Enum.Parse<BillingCheckoutStatus>(row.Status),
            row.CreatedAtUtc,
            row.ExpiresAtUtc,
            row.Version);

    private static BillingCheckoutView Map(BillingCheckoutIntentRow row) =>
        new(
            row.Id,
            row.CustomerAccountId,
            row.ActorId,
            row.TenantId,
            Enum.Parse<BillingPurchaseProductType>(row.ProductType),
            row.ProductCode,
            row.ProductVersion,
            row.ProductDisplayName,
            row.Amount,
            row.Currency,
            row.PaymentReference,
            Enum.Parse<BillingCheckoutStatus>(row.Status),
            row.CreatedAtUtc,
            row.ExpiresAtUtc,
            row.ReceiptSubmittedAtUtc,
            row.ReviewedAtUtc,
            row.FulfilledAtUtc,
            row.ReceiptAttachmentKind,
            row.ReceiptAttachmentReference,
            row.ReviewReason,
            row.Version);

    private static string BuildPaymentReference(DateTimeOffset now) =>
        $"TG{now:yyyyMMddHHmmss}{RandomNumberGenerator.GetInt32(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static void EnsureSameCreate(
        BillingCheckoutIntentRow row,
        BillableActorContext actor,
        Guid accountId,
        BillingPurchaseProduct product)
    {
        if (row.TenantId != actor.TenantId ||
            row.ActorId != actor.ActorId ||
            row.CustomerAccountId != accountId ||
            !row.ProductCode.Equals(product.Code, StringComparison.Ordinal) ||
            !row.ProductVersion.Equals(product.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Checkout idempotency key was reused for a different request.");
        }
    }

    private static void EnsureExpectedVersion(BillingCheckoutIntentRow row, int expectedVersion)
    {
        if (row.Version != expectedVersion)
        {
            throw new InvalidOperationException("Checkout version does not match the current value.");
        }
    }

    private static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 160)
        {
            throw new InvalidOperationException("A bounded idempotency key is required.");
        }
    }

    private static string NormalizeChannel(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Telegram" : value.Trim();

    private static bool IsAllowedAttachmentKind(string value) =>
        value.Equals("Image", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Document", StringComparison.OrdinalIgnoreCase);

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static int Count(IReadOnlyDictionary<string, int> statuses, BillingCheckoutStatus status) =>
        statuses.TryGetValue(status.ToString(), out var value) ? value : 0;
}
