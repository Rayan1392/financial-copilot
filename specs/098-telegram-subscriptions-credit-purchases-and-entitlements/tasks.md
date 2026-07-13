# Tasks — Telegram Subscriptions, Credit Purchases, and Entitlements

## 1. Billing Ownership and Product Decisions

- [ ] Reuse Feature 013 `SubscriptionPlan`, plan capabilities, subscription, wallet projection, usage ledger, financial transactions, reservation/finalization, and Billing outbox; do not create Telegram-owned balances or ledgers.
- [ ] Reuse Feature 035 admin authorization/audit and Feature 087 canonical actor link; Telegram only presents catalog, creates payment/receipt intents, and displays Billing status.
- [ ] Govern plan entitlements/limits and credit-package catalog in Billing with stable product code/version, amount/currency, validity, active period, and channel visibility.
- [ ] Select manual receipt review as the MVP flow unless a gateway is separately approved; keep a provider-neutral payment-attempt contract for future gateway callbacks.

## 2. Lifecycle and Persistence

- [ ] Define `PaymentRequest/CheckoutIntent` lifecycle `Pending`, `AwaitingPayment`, `ReceiptSubmitted`, `UnderReview`, `Approved`, `Rejected`, `Expired`, `Cancelled`, `Failed`, `RefundPending`, `Refunded`, `Fulfilled` with valid transitions.
- [ ] Persist actor/product/price snapshot/currency, unique payment reference, provider/reference hashes, receipt attachment metadata, expiry, reviewer/reason, fulfillment transaction id, and audit timestamps.
- [ ] Protect receipt/payment data: store only required metadata/secure object reference, never card data; define malware/content validation, access control, encryption, and retention/redaction.
- [ ] Enforce unique internal idempotency key, provider transaction/reference, and one fulfillment per request; index actor/status/created/expiry/reconciliation queries.
- [ ] Fulfill approval/callback transactionally through existing Billing accounting/outbox so purchased credits or subscription activation occur exactly once.

## 3. Subscription and Credit Semantics

- [ ] Define purchased-credit wallet behavior, expiry if any, and interaction with Feature 088 free allowance and subscription allowance using the shared Billing allocation order.
- [ ] Define subscription activation start, expiry, renewal, upgrade/downgrade overlap, cancellation/non-renewal, grace period, and entitlement cache invalidation.
- [ ] Define duplicate/late payment, amount/currency mismatch, expired checkout, rejected receipt resubmission, and payment received after cancellation.
- [ ] Define refund policy as explicit financial transaction/reversal and entitlement adjustment rules; never delete original ledger/payment records.
- [ ] Define reconciliation states and operator report matching approved/received/fulfilled/refunded totals.

## 4. Application and API Contracts

- [ ] Implement list Billing catalog, create/get/cancel checkout, submit receipt, admin review approve/reject, provider callback placeholder/adapter, reconcile, and current entitlement projection use cases.
- [ ] Validate actor ownership, active product/version, quoted amount/currency, request expiry, transition/version, reviewer permission, and fulfillment idempotency.
- [ ] Specify Billing endpoints with idempotency header, correlation id, payment reference, expiry, available next actions, and sanitized status; callback uses provider authentication/signature/replay validation.
- [ ] Require rejection/reconciliation/refund reasons and immutable audit; concurrent reviewers must produce one terminal decision.

## 5. Telegram UX

- [ ] Provide `/plans`, `/credits`, purchase type/catalog/detail/confirm, receipt upload/status/cancel, and entitlement refresh flows using versioned actor-owned callbacks.
- [ ] Show exact product, amount/currency, allowance/limits, duration/expiry, payment reference, secure receipt instructions, review status, and support path in Persian.
- [ ] Do not collect card/PIN data; gateway flow opens an approved secure link and manual flow accepts only configured receipt document/image types.
- [ ] Publish approval/rejection/expiry/refund status through Feature 097; repeated callbacks/status checks must not re-fulfill.

## 6. Security, Observability, and Tests

- [ ] Protect admin/payment endpoints with existing policies, service authentication, signature/timestamp/nonce verification, upload limits/scanning, rate limits, and actor/tenant isolation.
- [ ] Store provider secrets outside source control and redact receipts, payment identifiers, actor/Telegram ids, and signatures from logs.
- [ ] Measure checkout funnel, review age, approval/rejection, callback validation, duplicate prevention, fulfillment latency/failure, reconciliation mismatch, renewal/expiry, and refund.
- [ ] Unit-test lifecycle, entitlement dates, consumption ordering, duplicates, refunds, mismatches, late payments, and renewal.
- [ ] Integration/concurrency-test actor isolation, two reviewers/callback replays, one Billing fulfillment, ledger/wallet/subscription projection, outbox, expiry, and reconciliation.
- [ ] Given a valid receipt is approved, when fulfillment runs/retries, then one immutable financial transaction activates exactly one entitlement/credit grant.
- [ ] Given duplicate/invalid/expired payment evidence, when processed, then no duplicate fulfillment occurs and an auditable localized state is returned.

## Completion Gate

- [ ] Keep tasks unchecked until Billing owners approve lifecycle/refund decisions and security, concurrency, reconciliation, expiry, callback, and Telegram UX tests pass.
