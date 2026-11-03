# Feature 152 — Max implementation tasks

**Local status:** Max quota-mode and approved overlapping-window implementation, confirmed plan mapping/capabilities, and focused verification are complete. `[~]` means local implementation is complete but the listed external or operations gate remains open. Feature 139's shared full-window/Free-once foundation is reused; its staging/operations gate remains open.

## TahlilApp contract and identity

- [x] Exchange/status parsers and API defaults map approved `sapio_max` to Max; configured authoritative `starts_at`/`expires_at` values are persisted. Unit tests cover both parser paths. External staging response verification remains pending.
- [x] Max has explicit Unlimited mode and no numeric allowance. Max's seeded capability rows exactly match Pro, including limits, through a new migration; equality is tested. While Max is active it controls quota mode; after expiry, the latest-expiring active non-Max window determines the effective plan. Pro-over-Plus is retained only for an exact expiry tie.

## SapioAI Billing and API

- [x] Persist explicit quota mode on plans, reservations, and usage, with source-window metadata. Preserve finite wallet and ledger numbers; reuse Feature 139's full-window/one-time-Free lifecycle.
- [x] Include Max in effective-plan, cooldown, renewal, expiry, revocation, and Free-fallback handling. Max receives no numeric allowance or new Free grant. Revoked external rows are excluded from capability checks.
- [x] Admit active authoritative Max without numeric wallet sufficiency through a zero-hold, source-bound reservation. Authentication, account, capability, operational checks, and request fingerprint idempotency remain active.
- [x] Route capacity upgrades, finalization, release, retry, and refund by stored mode/source. Unlimited usage records actual usage/cost with zero numeric debit; cancellation and refunds cannot mint credits. Active subscription and top-up sources sum as finite credits when Max is inactive, and each source retains its own expiry; no Plus-to-Pro transfer is applied.
- [x] Self/admin usage and subscription projections expose quota mode and authoritative expiry while preserving numeric finite balances and reports.

## Frontend and verification

- [x] Display Unlimited as «نامحدود» with effective expiry; hide finite progress while active and preserve numeric values. Expiry/revocation refresh returns to the effective finite/Free state.
- [x] Cover configured parsing, explicit capability behavior, zero-balance Max admission with overlapping finite sources, Max expiry into latest non-Max plan, independent source expiry, reservation/settlement/refund, revocation, replay after plan change, refund rejection, API/UI display, PostgreSQL concurrency/migrations, and finite Feature 139/151 regressions.

## External TahlilApp and staging/operations

- [ ] Implement and verify the Max offer, checkout/renewal and authoritative `sapio_max` status/exchange response in the external TahlilApp repository. This repository does not include that work.
- [ ] Jointly stage expiry/revocation, active-Max Unlimited behavior, latest-expiring non-Max selection, independent source expiry, and Feature 151 top-up exclusion before enabling the offer. Verify no 90-day expiry calculation or numeric sentinel.
- [ ] Complete Feature 139 operations review and deployment approval. No staging or production gate is claimed complete.
