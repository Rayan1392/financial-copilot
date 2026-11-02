# Feature 151 — User stories and acceptance criteria

**Status:** implementation complete within this repository; see [ImplementationReport.md](ImplementationReport.md). External TahlilApp work and joint staging verification remain pending.

**US-151-01:** As an active Plus or Pro user, I can buy credits without renewing my subscription, before or after my balance reaches zero.

- **AC-01:** Both active Plus and Pro users can buy 100, 500 or 1000 credits. The authenticated positive integer `creditsPurchased` equals the grant; an optional package ID/version must agree with it. Invalid, noninteger, nonpositive or inconsistent quantities cannot grant.
- **AC-02:** Free, active Max, expired, revoked and suspended users cannot start a new top-up. Unavailable status prevents checkout. A paid Plus/Pro purchase delivered after transition to Max retains its original source: grant only if that source remains active/unrevoked; otherwise record `expired-unfulfilled` or `ineligible-unfulfilled` without a grant.
- **AC-03:** Plus receives 260 and Pro 2600 **once per entire authoritative subscription window**, not per month. Two distinct verified top-up payments each grant once. Each top-up adds to existing finite balance without changing plan, subscription expiry, unrelated source balances, pending reservations or usage history. Day 30 and day 60 generate no subscription grant.
- **AC-03a:** Each purchase records its original `(slug, starts_at, expires_at)` window and source; verified payment time falls within that window. Expiry removes only its unused credits, without carryover on renewal or reassociation on upgrade/downgrade. Delivery after that window expires records `expired-unfulfilled` and adds zero spendable credits.
- **AC-03b:** Charges and holds attribute credits to exact sources in deterministic expiry order. Grant, reservation, finalization, cancellation, abandoned-hold cleanup, expiry and revocation races never overdraw, double-expire or delete unrelated credits. A pre-expiry reservation may settle against its recorded hold after natural expiry; a released expired hold does not reappear.

**US-151-02:** As a payer, I see an honest pending state when I return before the credits arrive.

- **AC-04:** Redirect and client status never grant credits. Early return shows processing from a self-scoped purchase status read; committed delivery refreshes the balance through `GET /api/v1/usage/me`. Delivery succeeds even if the browser never returns.
- **AC-05:** An unresolved external user remains paid-but-ungranted and is retried/reconciled without matching on mutable personal data.

**US-151-03:** As an operator, I can reconcile every verified payment to at most one credit grant.

- **AC-06:** Sequential and concurrent duplicate deliveries return one persisted result; retry after commit/response timeout or after subsequent expiry/plan change returns that same result. A duplicate payment under another event ID never regrants. Existing fulfillment is checked before new-grant eligibility.
- **AC-07:** Reusing an event, purchase or payment ID with a different user, original subscription window, credit quantity, package or payment identity returns conflict and does not mutate balance.
- **AC-08:** Invalid authentication, unknown quantity, inactive package and package/quantity mismatch cannot grant. A failed grant transaction rolls back purchase, source, ledger and wallet together, allowing a safe retry.
- **AC-09:** Delayed/out-of-order delivery, unresolved user, ineligible user, refund and chargeback are distinguishable in reconciliation; receipt alone is never reported as grant.
- **AC-10:** Existing reservations, concurrent usage finalization, subscription grants/expiry and usage refunds preserve their invariants while a top-up arrives. A usage refund restores only still-valid original source credits and never resurrects an expired top-up or credits in a renewal window.

**Tests required, no test code in this task:** unit tests for quantity/package agreement, eligibility (including Free/Max), source ordering, expiry and refund caps; API tests for auth, all quantities and invalid/inconsistent quantities; PostgreSQL independent-connection tests for duplicate races, transaction rollback and grant-versus-reservation/finalization/expiry/refund races; timeout/replay after expiry, renewal, Max transition and delayed delivery tests with a TahlilApp stub; joint staging verification of signed delivery and original-window identity. Shared full-window and Free-once tests are owned by Feature 139 T-40–T-43, not duplicated here.
