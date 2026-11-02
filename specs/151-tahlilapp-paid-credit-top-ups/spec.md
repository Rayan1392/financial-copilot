# Feature 151 — TahlilApp paid credit top-ups

**Status:** Updated implementation is under focused verification. TahlilApp coordination and joint staging remain pending. Delivery stays disabled by default. Feature 152 Max and Feature 139 identity/subscription synchronization are shared dependencies.

## Business rules

TahlilApp owns offer pricing, checkout, verified payment, and provider audit. SapioAI receives only the purchased credit quantity. Active database-controlled offers remain 100, 500, and 1000 credits. A top-up leaves plan and subscription dates unchanged, creates a source-aware grant, and expires at the original authoritative Plus/Pro source expiry. Free cannot receive grants. A payment made during Max is ineligible. A paid Plus/Pro invoice from before Max began may be delivered during a Max transition only while its own source remains active and unrevoked.

The four-field webhook has no source window. SapioAI matches the verified payment time against persisted Plus/Pro windows. A single match can be granted subject to the current authoritative plan rules. No match is reconciled as ineligible or expired. Multiple matching windows are not inferable from the contract, so the invoice becomes terminal `ambiguous-unfulfilled` with no grant and no selected source; operations must reconcile it. Existing source allocation, holds, settlement, expiry, revocation, refund, audit, outbox, and status behavior stays intact. Grant state is atomic.

## Contracts

`GET /api/v1/billing/partners/tahlilapp/credit-balance?userId=<URL-encoded-id>` is an unauthenticated, read-only partner balance/status query resolved through the existing TahlilApp identity mapping. It is IP rate limited and `no-store`. It returns effective plan, plan start/expiry, `quotaMode`, `availableCredits`, and `asOfUtc`. Max returns `quotaMode: unlimited` and `availableCredits: null`; finite balance reflects expiry-aware usable credits less reservations. Active Max and latest-expiring non-Max overlap resolution, including the Pro-over-Plus exact tie, use Feature 152's current policy. See [integration-contract.md](integration-contract.md) for details and the userId entropy assessment.

`POST /api/v1/billing/partners/tahlilapp/top-ups` accepts exactly `invoiceId`, `userId`, `creditsPurchased`, and `paidAtUtc`; the JWT remains in the Authorization header. SapioAI fingerprints exactly those four immutable values, resolves subscription sources server-side, and uses unique `(partner, invoiceId)` identity. Same-data retries return the original outcome; changed user, quantity, or timestamp returns 409. TahlilApp must map one verified payment to at most one immutable invoice ID and retain provider payment identifiers on its side.

The no-JWT balance contract carries a concrete risk if external user IDs are guessable: callers could enumerate and read spendable balance, effective plan, and dates. This repository stores but does not generate those IDs and cannot verify production entropy. The minimum compatible protection is a TahlilApp guarantee of cryptographically random IDs with at least 128 bits of entropy, plus the current IP rate limit at SapioAI and the edge. This work does not add JWT to the agreed GET endpoint.

## Authentication and delivery lifecycle

Webhook requests retain the configured partner JWT issuer, audience, `topup.deliver` scope, `kid` key selection, signature/lifetime validation, and consumed `(iss,jti)` replay protection. Every retry gets a fresh short-lived JWT and `jti`, while preserving the invoice/body. Invoice idempotency remains independent of token replay protection. `Billing:TahlilAppTopUps:Enabled` remains false until coordinated staging.

Payment reversals for new receipts are not automatic because payment-provider IDs are intentionally absent. A separate authenticated invoice-keyed reversal event is needed if SapioAI must automate refunds/chargebacks. Legacy receipt payment identifiers remain for historical reconciliation.
