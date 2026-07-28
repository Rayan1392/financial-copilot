# Telegram Billing Purchases

Feature 098 lets linked Telegram users create Billing-owned checkout intents for credit packs and subscription products. Telegram is only a presentation channel: Billing remains the source of truth for products, checkout state, financial transactions, wallet credit grants, subscription activation, audits, and outbox events.

## MVP Flow

The active MVP flow is manual receipt review.

1. User lists products with `/plans` or `GET /api/v1/billing/catalog`.
2. User creates a checkout with `/buy PRODUCT_CODE` or `POST /api/v1/billing/checkouts`.
3. Billing returns a unique payment reference, quoted amount/currency, expiry, status, and version.
4. User pays through the approved external path and submits receipt metadata with `/receipt ...` or `POST /api/v1/billing/checkouts/{id}/receipt`.
5. Billing admin approves or rejects through `POST /api/v1/admin/billing/receipt-reviews/{id}`.
6. Approval records one `Payment` financial transaction and exactly one fulfillment effect:
   - credit pack: one immutable `Billing.PurchasedCredits` usage ledger adjustment and wallet projection update;
   - subscription: existing customer-account subscription fields are updated with plan and validity dates.
7. Fulfilled, rejected, and cancelled status events are handed to Feature 097 notification intents for Telegram delivery.

Provider callback support is deliberately a placeholder until a gateway is approved. `POST /api/v1/billing/payment-callback/{provider}` validates the callback envelope and returns `NotConfigured`; it does not fulfill payments without an authenticated provider adapter.

## Persistence

Migration `20260715142409_ImplementTelegramBillingPurchases` adds:

- `billing_purchase_products`: Billing-owned product catalog with product type, product version, price snapshot, currency, credit amount, plan code, duration, channel visibility, and active flag.
- `billing_checkout_intents`: actor-isolated checkout lifecycle with payment reference, status/version, idempotency keys, receipt metadata, provider reference hash, reviewer fields, fulfillment transaction references, audit timestamps, expiry, and concurrency token.

The model enforces unique checkout idempotency keys, payment references, provider reference hashes, and fulfillment transaction references.

## Commands

- `/plans`: show Telegram-visible Billing products.
- `/buy PRODUCT_CODE`: create a checkout.
- `/receipt CHECKOUT_ID VERSION Image|Document RECEIPT_REFERENCE`: submit sanitized receipt metadata.
- `/checkout CHECKOUT_ID`: show checkout status.
- `/cancel_checkout CHECKOUT_ID VERSION reason`: cancel a checkout before terminal fulfillment.

Users must not send card numbers, PINs, CVV, bank credentials, or raw payment secrets to Telegram. Receipt storage should point to a secure object reference with malware/content validation and access control outside Telegram.
