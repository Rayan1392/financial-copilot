# Feature 151 — Implementation tasks

All in-repository implementation and focused verification are complete, including overlap accounting with Feature 152. TahlilApp-side integration and joint staging/operations gates remain pending. Feature 139 shared lifecycle implementation/local verification is complete; its joint staging/operations review remains pending. Feature 151 does not implement Max accounting; it applies the shared entitlement/quota result and preserves original source expiries.

## SapioAI backend

- [x] Add database-controlled versioned active-offer quantity allowlist (100/500/1000); validate positive integer `creditsPurchased` and optional package ID/version agreement.
- [x] Add dedicated inbound partner JWT authentication, feature flag, validation and rate/body limits; user/session credentials cannot grant.
- [x] Add partner eligibility read using linked TahlilApp identity, current authoritative refresh, active original Plus/Pro windows, and Free/Max exclusion.
- [x] Add Billing purchase receipts, top-up sources, unique event/purchase/payment and ledger keys, atomic grant or persisted `expired-unfulfilled` / `ineligible-unfulfilled`, conflict comparison, audit/outbox, and replay lookup before new-grant eligibility.
- [x] Add BillingAdmin-authorized, tenant-scoped purchase outcome and identity conflict reconciliation through the existing billing admin panel/report route.
- [x] Extend finite source holds, capacity upgrade, finalization, release, expiry, revocation and usage refunds with top-up source attribution; expired credits are excluded and never restored.
- [x] Add self-scoped status read by opaque purchase ID; not-yet-received and other-user purchases return 404; only committed `granted` reports credits.

## TahlilApp backend (external repository; not inspected)

- [ ] Own three offers/prices; verify SapioAI linkage and paid eligibility, and persist the chosen original subscription tuple at checkout.
- [ ] Verify gateway payment server-side and retain its monetary audit; durably store verified purchase and positive integer quantity before webhook delivery.
- [ ] Issue partner service tokens, send stable IDs with durable retries, classify terminal/retryable errors, reconcile paid-but-ungranted purchases, handle refund/chargeback cases.

## Frontend

- [ ] TahlilApp: show offers, eligibility errors, purchase confirmation and pending/reconciliation state.
- [x] SapioAI: read own purchase status, show processing until grant, then refresh `GET /api/v1/usage/me`; use bounded polling and never mutate credits from redirect.

## Verification and rollout

- [x] Add unit, API and PostgreSQL tests for configured quantities, package mismatch, partner auth/scope, duplicate replay, delayed delivery, expiry outcomes, identity uniqueness, atomic rollback, independent-connection duplicate grant, and overlapping plan/top-up source accounting.
- [x] Add PostgreSQL races for top-up grant versus reservation, usage finalization, usage refund and source expiry; settlement retry after optimistic concurrency; replay after expiry/window changes.
- [x] Investigate the two failing frontend assertions against clean `HEAD`; record exact baseline failures without skipping or weakening tests.
- [ ] Joint staging contract/auth/key-rotation tests with TahlilApp and its stored original-window tuple; confirm Feature 139 identity, status and migration readiness.
- [ ] Deploy schema and secrets with endpoint disabled; enable delivery after tests; monitor paid-ungranted age and reconciliation before exposing offers. Keep delivery available while draining paid purchases during rollback.
