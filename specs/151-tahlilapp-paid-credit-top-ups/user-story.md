# Feature 151 — User stories and acceptance criteria

**Status:** Local contracts are updated; focused implementation verification and external TahlilApp coordination remain in progress.

**US-151-01:** As a TahlilApp customer, I can check my current SapioAI credit balance and subscription state.

- **AC-01:** `GET /api/v1/billing/partners/tahlilapp/credit-balance?userId=...` URL-decodes the stable user ID and resolves the existing TahlilApp external identity mapping.
- **AC-02:** The endpoint is read-only, anonymous by agreement, IP rate limited, and returns `Cache-Control: no-store`; it performs no checkout eligibility check or entitlement refresh.
- **AC-03:** It returns effective plan, authoritative subscription start/expiry, `quotaMode`, `availableCredits`, and `asOfUtc`. Max returns Unlimited and `availableCredits: null`; finite balance excludes expired sources and reservations.
- **AC-04:** Active Max takes precedence through its expiry. Otherwise latest-expiring active non-Max wins with established Pro-over-Plus exact-tie behavior. Unknown user IDs do not reveal personal or payment details.
- **AC-05:** Since this ID is not authenticated, TahlilApp must guarantee non-enumerable IDs with at least 128 bits of cryptographic entropy. The repository cannot verify how production IDs are generated; guessing risk and compatible mitigation are documented.

**US-151-02:** As a verified payer, I receive the purchased credits without changing my subscription.

- **AC-06:** The webhook accepts exactly `invoiceId`, `userId`, `creditsPurchased`, and `paidAtUtc`. It rejects unknown properties. JWT remains only in the Authorization header.
- **AC-07:** SapioAI resolves active TahlilApp identity, persisted authoritative Plus/Pro source windows, current plan, and offer quantity server-side. It accepts only active database-controlled quantities 100, 500, or 1000; Free is ineligible. Payment during Max is rejected; a prior Plus/Pro payment can complete after Max starts only while its original finite source remains active and unrevoked.
- **AC-08:** A uniquely matched Plus/Pro source gets an atomic grant expiring at that source's authoritative expiry. Expired, revoked, or otherwise ineligible cases grant zero and remain visible for reconciliation.
- **AC-09:** If multiple Plus/Pro source windows include `paidAtUtc`, SapioAI persists `ambiguous-unfulfilled` without a grant or guessed source. This outcome is terminal for that invoice and needs operational reconciliation.
- **AC-10:** A grant preserves wallet, source allocation, ledger, audit, outbox, reservation, expiry, revocation, refund, and rollback invariants.

**US-151-03:** As a payer/operator, I can retry and reconcile one immutable invoice safely.

- **AC-11:** `invoiceId` is immutable, bounded, unique, and fingerprints exactly invoice, user, credits, and paid time. Same-data retry returns the stored outcome; changed user, quantity, or paid timestamp returns 409 without changing the original grant.
- **AC-12:** TahlilApp guarantees at most one immutable invoice per verified payment and keeps provider IDs internally. The separate provider reversal protocol remains external work.
- **AC-13:** JWT retains required partner scope/signature/lifetime and unique one-time `jti`; every retry uses a fresh JWT while body and invoice remain unchanged. Balance GET does not use this JWT.

**Focused verification:** balance finite/Unlimited/expiry/overlap, decoded/unknown IDs, no mutation and no JWT; exact four-field webhook and rejection of subscription/legacy fields; offer/plan/source validation; invoice conflict, replay, duplicate/concurrency, rollback and expiry; partner JWT requirements. No second checkout eligibility check is added.
