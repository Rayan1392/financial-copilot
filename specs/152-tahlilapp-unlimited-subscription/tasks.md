# Feature 152 — Max implementation tasks

All tasks pending. Execution order: Feature 139 T-40–T-43 shared full-window/one-time-Free lifecycle → Feature 152 Max plan/mode/admission work. Feature 151 may proceed independently after the shared prerequisite.

## TahlilApp contract and identity

- [ ] Confirm proposed `sapio_max` slug with TahlilApp and update Feature 139 exchange/status samples, parser, configurable `PlanMap`, validator and policy; use supplied `starts_at`/`expires_at`, not a 90-day calculation.
- [ ] Define/approve overlap precedence and explicit Max capability/limit matrix; add plan and capability rows without inheriting Pro implicitly.

## SapioAI Billing and API

- [ ] Persist explicit quota mode on plan/entitlement and mode plus source-window ID on reservations/usage; preserve finite numeric wallet and ledger types. Consume Feature 139 T-40–T-43 full-window/Free-once source lifecycle rather than rebuilding it here.
- [ ] Update authoritative effective-plan, paid-expiry cooldown, renewal, revocation and Free fallback reads to include Max while skipping numeric allowance grants and **never** triggering a new Free grant; filter revoked external rows in `PlanCapabilityService`.
- [ ] At admission, bypass wallet sufficiency only for active Max; make `UsageReservation` zero-hold invariant mode-aware, create zero-credit source-bound reservation, retain auth/capability/operational checks and idempotency fingerprint.
- [ ] Route capacity upgrades, finalization, release, retry and usage refund by stored reservation mode/source; record actual usage/cost with zero credit debit and never credit wallet from Unlimited refund.
- [ ] Extend self/admin usage and subscription responses with explicit quota mode and authoritative expiry; preserve finite numeric fields and reports.

## Frontend and verification

- [ ] Display Max as «نامحدود» with its authoritative expiry, hide finite progress while active, then show effective finite/Free state after expiry/revocation; avoid numeric infinity.
- [ ] Implement AC-01–AC-07 tests across parser, capability, reservation, finalization, refunds, expiration, transitions, API and UI, including PostgreSQL races and finite regression.
- [ ] Joint staging checks of the actual Max slug/window and effective behavior before enabling offer; verify no hardcoded 90-day expiry or numeric sentinel.
