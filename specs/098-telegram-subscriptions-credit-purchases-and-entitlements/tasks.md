# Tasks — Telegram Subscriptions, Credit Purchases, and Entitlements

## 1. Billing Ownership and Product Decisions

- [x] Reuse Feature 013 `SubscriptionPlan`, plan capabilities, subscription, wallet projection, usage ledger, financial transactions, reservation/finalization, and Billing outbox; do not create Telegram-owned balances or ledgers.
- [x] Reuse Feature 035 admin authorization/audit and Feature 087 canonical actor link; Telegram only presents catalog, creates payment/receipt intents, and displays Billing status.
- [x] Govern plan entitlements/limits and credit-package catalog in Billing with stable product code/version, amount/currency, validity, active period, and channel visibility.
- [x] Select manual receipt review as the MVP flow unless a gateway is separately approved; keep a provider-neutral payment-attempt contract for future gateway callbacks.

## 2. Lifecycle and Persistence

- [x] Define `PaymentRequest/CheckoutIntent` lifecycle `Pending`, `AwaitingPayment`, `ReceiptSubmitted`, `UnderReview`, `Approved`, `Rejected`, `Expired`, `Cancelled`, `Failed`, `RefundPending`, `Refunded`, `Fulfilled` with valid transitions.
- [x] Persist actor/product/price snapshot/currency, unique payment reference, provider/reference hashes, receipt attachment metadata, expiry, reviewer/reason, fulfillment transaction id, and audit timestamps.
- [x] Protect receipt/payment data: store only required metadata/secure object reference, never card data; define malware/content validation, access control, encryption, and retention/redaction.
- [x] Enforce unique internal idempotency key, provider transaction/reference, and one fulfillment per request; index actor/status/created/expiry/reconciliation queries.
- [x] Fulfill approval/callback transactionally through existing Billing accounting/outbox so purchased credits or subscription activation occur exactly once.

## 3. Subscription and Credit Semantics

- [x] Define purchased-credit wallet behavior, expiry if any, and interaction with Feature 088 free allowance and subscription allowance using the shared Billing allocation order.
- [x] Define subscription activation start, expiry, renewal, upgrade/downgrade overlap, cancellation/non-renewal, grace period, and entitlement cache invalidation.
- [x] Define duplicate/late payment, amount/currency mismatch, expired checkout, rejected receipt resubmission, and payment received after cancellation.
- [x] Define refund policy as explicit financial transaction/reversal and entitlement adjustment rules; never delete original ledger/payment records.
- [x] Define reconciliation states and operator report matching approved/received/fulfilled/refunded totals.

## 4. Application and API Contracts

- [x] Implement list Billing catalog, create/get/cancel checkout, submit receipt, admin review approve/reject, provider callback placeholder/adapter, reconcile, and current entitlement projection use cases.
- [x] Validate actor ownership, active product/version, quoted amount/currency, request expiry, transition/version, reviewer permission, and fulfillment idempotency.
- [x] Specify Billing endpoints with idempotency header, correlation id, payment reference, expiry, available next actions, and sanitized status; callback uses provider authentication/signature/replay validation.
- [x] Require rejection/reconciliation/refund reasons and immutable audit; concurrent reviewers must produce one terminal decision.

## 5. Telegram UX

- [x] Provide `/plans`, `/credits`, purchase type/catalog/detail/confirm, receipt upload/status/cancel, and entitlement refresh flows using versioned actor-owned callbacks.
- [x] Show exact product, amount/currency, allowance/limits, duration/expiry, payment reference, secure receipt instructions, review status, and support path in Persian.
- [x] Do not collect card/PIN data; gateway flow opens an approved secure link and manual flow accepts only configured receipt document/image types.
- [x] Publish approval/rejection/expiry/refund status through Feature 097; repeated callbacks/status checks must not re-fulfill.

## 6. Security, Observability, and Tests

- [x] Protect admin/payment endpoints with existing policies, service authentication, signature/timestamp/nonce verification, upload limits/scanning, rate limits, and actor/tenant isolation.
- [x] Store provider secrets outside source control and redact receipts, payment identifiers, actor/Telegram ids, and signatures from logs.
- [x] Measure checkout funnel, review age, approval/rejection, callback validation, duplicate prevention, fulfillment latency/failure, reconciliation mismatch, renewal/expiry, and refund.
- [x] Unit-test lifecycle, entitlement dates, consumption ordering, duplicates, refunds, mismatches, late payments, and renewal.
- [x] Integration/concurrency-test actor isolation, two reviewers/callback replays, one Billing fulfillment, ledger/wallet/subscription projection, outbox, expiry, and reconciliation.
- [x] Given a valid receipt is approved, when fulfillment runs/retries, then one immutable financial transaction activates exactly one entitlement/credit grant.
- [x] Given duplicate/invalid/expired payment evidence, when processed, then no duplicate fulfillment occurs and an auditable localized state is returned.

## Completion Gate

- [x] Keep tasks unchecked until Billing owners approve lifecycle/refund decisions and security, concurrency, reconciliation, expiry, callback, and Telegram UX tests pass.
