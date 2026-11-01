# Feature 151 — Implementation tasks

All tasks are **not started**. Dependency order: Feature 139 T-40–T-43 (shared full-window/one-time-Free source lifecycle) → Feature 151 source-aware top-up fulfillment. Feature 152 Max accounting is independent; Feature 151 eligibility must recognize the future Max plan code.

## SapioAI backend

- [ ] Add coordinated versioned active-offer quantity allowlist (100/500/1000); validate positive integer `creditsPurchased` and consistency with optional package ID/version.
- [ ] Add dedicated inbound partner authentication, feature flag, validation and rate/body limits; user/session credentials cannot grant.
- [ ] Add partner eligibility read returning original active Plus/Pro window tuples and excluding Free/active Max; resolve official `user_id`, refresh status and bind purchase to its checkout-time `(slug,starts_at,expires_at)` window, not arrival-time effective plan.
- [ ] Add Billing purchase/receipt and top-up source persistence, unique event/purchase/payment and ledger keys, atomic grant or `expired-unfulfilled` outcome, exact conflict comparison, audit/outbox and reconciliation views. Check existing fulfillment **before** current eligibility so retry after expiry/plan change returns the stored outcome.
- [ ] Extend finite reservation holds, capacity upgrade, finalization, release, expiry and usage refunds with source attribution and account/wallet concurrency protection; expire only remaining original-window top-up credits and never restore expired source credits.
- [ ] Add self-scoped top-up status read by opaque purchase ID; return 404 for not-yet-received and other-user purchases, and `granted` only after commit.

## TahlilApp backend (external repository; not inspected)

- [ ] Own three offers/prices; verify SapioAI linkage and paid eligibility, and persist the chosen original subscription tuple at checkout.
- [ ] Verify gateway payment server-side and retain its monetary audit; durably store verified purchase and positive integer quantity before webhook delivery.
- [ ] Issue partner service tokens, send stable IDs with durable retries, classify terminal/retryable errors, reconcile paid-but-ungranted purchases, handle refund/chargeback cases.

## Frontend

- [ ] TahlilApp: show offers, eligibility errors, purchase confirmation and pending/reconciliation state.
- [ ] SapioAI: read own purchase status, show processing until grant, then refresh `GET /api/v1/usage/me`; use bounded polling and later refresh, never mutate credits from redirect.

## Verification and rollout

- [ ] Implement AC-01–AC-10 unit, API and PostgreSQL concurrency tests, including independent-process duplicates, source expiry/renewal, grant-versus-hold/usage/refund races, rollback, response loss, delayed delivery and payment-before-Max transition. Consume Feature 139 T-40–T-43 shared lifecycle tests; do not duplicate ownership.
- [ ] Joint staging contract/auth/key-rotation tests with TahlilApp and its stored original-window tuple; confirm Feature 139 identity, status and migration readiness.
- [ ] Deploy schema and secrets with endpoint disabled; enable delivery after tests; monitor paid-ungranted age and reconciliation before exposing offers. Keep delivery available while draining paid purchases during rollback.
