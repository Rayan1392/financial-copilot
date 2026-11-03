# Feature 151 — Implementation tasks

## SapioAI

- [x] Add anonymous, read-only `GET /api/v1/billing/partners/tahlilapp/credit-balance` using existing identity mapping and expiry-aware wallet reads.
- [x] Return finite/Unlimited quota mode, effective plan, dates, spendable credits (null for Unlimited), and as-of time; keep Max/latest-expiry/tie policy aligned with Feature 152.
- [x] Apply a dedicated per-IP rate limit (`RateLimiting:TahlilAppCreditBalance`, separate from login) and no-store headers; document the unverified userId entropy and enumeration risk.
- [x] Remove the old top-up eligibility endpoint; do not call eligibility from TahlilApp checkout.
- [x] Accept exactly four webhook fields and preserve partner JWT Authorization validation and one-time `jti` protection.
- [x] Resolve current identity, eligible plan, and credit source from server-side subscription records at `paidAtUtc`: Free/no window and active Max are ineligible; Plus, Pro, and overlapping Plus/Pro are eligible, with the grant window chosen by the shared overlap rule (latest expiry, Pro on tie).
- [x] Fingerprint only invoice, user, credit quantity, and paid timestamp; preserve invoice replay/conflict semantics and atomic grant/reconciliation.
- [x] Keep historical provider-ID columns and legacy uniqueness for old receipts; allow optional source evidence on new ineligible receipts that have no source window.
- [x] Add tests for balance finite/Unlimited/expired/overlap/tie/Free cases, URL decoding, unknown and malformed IDs, read-only footprint, ignored JWT, dedicated rate limit, removed body fields, invoice conflicts on user/quantity/time, and unknown users.
- [x] Add eligibility tests for Free, Plus, Pro, overlapping Plus/Pro (later Pro, later Plus, exact tie), Max overlapping Plus/Pro, payment outside every window, and overlap delivered after the selected window expired.
- [x] Run focused tests/builds and verify migration model consistency, including PostgreSQL concurrency/rollback/expiry tests on a disposable database; no shared or production migration was applied.
- [x] Update API/OpenAPI and integration documentation and provide the Persian HTML integration guide.

## External TahlilApp

- [ ] Send exactly the four webhook fields and retain provider payment IDs internally.
- [ ] Guarantee one immutable invoice per verified payment. Issue a new short-lived JWT with unique random `jti` per attempt and retry the same invoice/body.
- [ ] Guarantee external user IDs are cryptographically random and non-enumerable with at least 128 bits of entropy before using the anonymous balance endpoint. Production ID generation is outside this repository.
- [ ] Call the balance endpoint for informational status only; do not add a second checkout eligibility gate.
- [ ] Define a separate authenticated invoice-keyed reversal event if automated chargebacks/refunds are needed.

## Rollout

- [ ] Coordinate joint staging for balance privacy/limits, four-field validation, auth claims/key rotation, idempotent retry, source overlap, expiry, reconciliation, and reversals.
- [ ] Keep `Billing:TahlilAppTopUps:Enabled=false`; do not deploy, apply a production migration, or change production data/settings as part of this work.
