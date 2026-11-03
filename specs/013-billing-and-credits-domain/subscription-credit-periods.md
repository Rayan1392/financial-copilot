> CURRENT POLICY: See the final "Current confirmed full-window policy" section. Earlier 25-credit Free and 30-day grant-cycle text is retained only as implementation history; Feature 139 T-40-T-43 are pending.

﻿# SapioAI subscription credit periods

**Implementation status (2026-10-24):** Revision 6 transfer and authoritative activation handling are implemented. Focused Billing/Feature 139 tests pass; the PostgreSQL concurrency test is skipped because Docker is unavailable. The migration was not applied by this work, and the production/staging migration state and external integration gates remain unverified.

## Ownership and allowance policy

TahlilApp owns purchase, external subscription status and eligibility. SapioAI Billing owns internal capabilities, monthly credit periods, wallet balances, consumption, refunds and ledger accounting.

| Plan | Period allowance |
| --- | ---: |
| Free | 25 credits |
| Plus | 260 credits |
| Pro | 2600 credits |

An allowance period is exactly 30 days from its activation anchor. New periods retain that anchor and use successive 30-day boundaries. Unused credits expire at the boundary and do not roll over. Consumption and refunds are recorded as new immutable usage-ledger entries; historical rows are never rewritten.

Free→Plus and Free→Pro forfeit only the tracked remaining Free allowance through a negative adjustment linked to its original grant, then grant one full paid allowance. Plus→Pro starts a fresh authoritative Pro period, grants 2600 credits and transfers the remaining Plus balance using paired ledger adjustments. The transfer expires at the Pro period boundary. Pro→Plus is not applied as a paid downgrade.

Paid capability expiry and credit-period expiry are separate: TahlilApp expiry immediately makes Billing capability reads resolve to Free. The already-issued period remains valid until its own 30-day boundary, when remaining credits expire and a Free period begins. This behavior is covered by the implementation and must be accepted in the coordinated TahlilApp/Billing contract.

## Existing Billing implementation

The implementation extends, and does not replace, the existing Billing wallet projection and append-only usage ledger:

- `SubscriptionCreditPeriodRow` stores account, plan, activation anchor/revision, period number, start/expiry, granted/remaining amount, status and grant-ledger reference.
- A unique period key and unique ledger grant key make repeat activation and rollover requests idempotent. The period row and wallet projection have concurrency revisions; the hosted allocation worker processes due accounts in bounded batches.
- `UsageLedgerEntryRow.SubscriptionCreditsApplied` attributes the part of a usage charge drawn from a subscription allowance. `RelatedEntryId` links it to the period grant. A refund restores this part only while the same period is still active; later refunds do not revive expired subscription credits.
- Grant, expiry and Freeâ†’paid forfeiture are `Adjustment` ledger entries with unique keys and outbox events. Usage charges/refunds remain append-only.
- Wallets that existed before this lifecycle and have no period row are legacy. Login, sync and the worker do not seed them, reset them or rewrite their ledger. They require an approved snapshot/reconciliation plan before a new recurring allowance is activated.
- Refunds restore the subscription-attributed portion to the same period only while it remains active. After expiry, the period is not revived; the existing Billing refund flow credits the full refund to the general wallet. Product must confirm this late-refund treatment before staging because it can make a refunded subscription-funded charge spendable after the period boundary.

Forward-only migration: `20261023200709_AddSubscriptionCreditLifecycle`. Its Up path adds `billing_subscription_credit_periods` and nullable `SubscriptionCreditsApplied`; it has no balance or historical-ledger data update. Apply it only after staging backup, snapshot review and the approved reconciliation plan. Never run it automatically against production.

## Decisions required before staging allocation

1. TahlilApp must deploy contract version 2 and provide authoritative `effectiveFrom` in both exchange and status responses. SapioAI accepts legacy responses for eligibility but does not create paid grants or renewals without a verified anchor.
2. Plus→Pro carry and the fresh Pro period at TahlilApp `effectiveFrom` are approved and implemented. The transfer expires with that Pro period.
3. No legacy wallet or ledger reconciliation has been approved. Existing balances are preserved.
4. Same-process concurrency and idempotency are tested; a cross-process PostgreSQL race test remains an operational verification gate.
5. No subscription refund workflow is supported or added. Existing usage-refund accounting does not revive an expired subscription period.

## Repository verification

On 2026-10-23:

- Billing unit filter: 48 passed, 0 failed.
- Feature 139 / native-enrollment integration filter: 36 passed, 0 failed.
- Billing persistence and endpoint integration filter: 34 passed, 0 failed.
- Backend Release solution build: passed, 0 warnings, 0 errors.
- Full UnitTests: 2,312 passed, 23 failed; the failure names match the pre-credit-lifecycle TRX. See `../139-tahlilapp-session-exchange/ImplementationReport.md` for the commands and broader regression history.

## Approved transfer and activation-time policy

A period is anchored to TahlilApp `subscription.effectiveFrom`. A late first launch allocates only the currently active period in the 30-day sequence. Plusâ†’Pro transfers the remaining Plus balance and adds 2600 Pro credits in the new period. Transfer out/in ledger operations are paired and the transfer cannot roll into the next period. Missing or invalid activation timestamps hold paid grants; old contract payloads remain accepted for eligibility only. External subscription revisions prevent stale updates. No refunds or production balance reconciliation are introduced by this change.

The follow-up migration `20261024004630_AddAuthoritativeSubscriptionActivationRevision` adds external revision and authoritative-anchor metadata only. It does not backfill or modify wallets, period rows or ledger history. It is pending rollout and has not been applied to production or staging.

## Current confirmed full-window policy — pending Feature 139 T-40–T-43

This section supersedes the earlier 25-credit Free and 30-day grant-cycle policy above as the target requirement. The above sections are retained as implementation history, not as current acceptance. Free is 250 credits **once per local user**, valid until its explicitly assigned expiration. Plus is 260 and Pro 2600 **total per entire authoritative paid subscription window**. Neither plan grants again at day 30 or day 60. Max is an explicit Unlimited quota mode with no numeric allowance. Commercial paid duration is currently 90 days, but TahlilApp `starts_at`/`expires_at` control validity; SapioAI does not compute 90 days. Free has no assumed 90-day duration. The current official Free status has no guaranteed expiry; TahlilApp must confirm an assigned-expiry contract field or Product must define an explicit persisted assignment source. Until then, no invented expiry can authorize a new Free grant.

`SubscriptionCreditLifecycleService.PeriodDuration = 30 days`, `GetCycleIndex`, `AddPeriodAsync`, its due-period worker, and the Free fallback paths currently implement periodic grants. `BillingEntityConfigurations` seeds Free 25; `OwnedIdentityBillingProvisioner.EnsureProvisionedAsync` initializes a new wallet through that lifecycle. Those are verified implementation mismatches. Migrate to one source row/grant key per paid `(account, slug, starts_at, expires_at)` and one durable unique Free-grant marker per local user, independent of plan. Concurrent provisioning and retries must converge on one Free 250 grant. Paid renewal with a distinct authoritative tuple gets one grant; earlier unused source credits expire and cannot carry. Paid expiry/cooldown never creates a new Free grant. A historical Free grant of 25 proves the user has already received Free; backfill marker from ledger/period evidence and do not add a retrospective 225 without separate authorization. Preserve legacy wallet/ledger values for reviewed reconciliation rather than reset or reinterpretation.

Source attribution and holds must make spendable capacity exclude expired sources immediately, before worker cleanup. Reservation, finalization, cancellation, source expiry and refunds use the original source window. Only unused source credits expire; unrelated wallet balances remain. A late usage refund records its historical correction but cannot make expired-source credits spendable; offset the expired part in immutable accounting so wallet projection remains reconstructible. Feature 139 T-42 owns this shared mechanism. Feature 151 adds period-bound purchased-credit sources. Feature 152 adds Max mode-bound zero-credit usage. No migration, backfill or production code is performed by this documentation revision.

## Current correction (2026-11-02): one-time Free and authoritative paid windows

This section supersedes prior statements in this specification that describe recurring 30-day credit grants or require a TahlilApp Free expiry field. TahlilApp exposes paid Plus, Pro, and Max subscription windows and their authoritative expiration dates. When the user has no paid subscription, the subscription value is null. TahlilApp provides neither a Free subscription nor a Free expiration date.

Billing assigns the first Free allowance internally at initial local wallet provisioning. The persisted `CustomerAccount.SubscriptionEffectiveFrom` provisioning timestamp is the anchor; `SubscriptionCreditPeriod.ExpiresAtUtc` is assigned once to anchor plus 30 days. A unique durable per-user evidence row records the grant. Login, sync, exhaustion, expiry, paid expiry, or cooldown completion cannot create or extend it. A user with any historical Free evidence or ambiguous grant/account history does not qualify for a new Free grant; historical 25-credit grants count as already received and no retrospective 225 is awarded.

Plus and Pro each receive the configured catalog allowance once for the complete authoritative `(slug, starts_at, expires_at)` window. The current catalog seed values are Free 250, Plus 260, and Pro 2600; runtime provisioning reads `SubscriptionPlan.IncludedCredits` and snapshots it into each source. There are no day-30/day-60 grants. A distinct renewal window gets one new allowance and prior unused credits expire without carryover. No Free grant follows paid expiry.

Source holds bind admitted work to its credit period. Spendable reads exclude expired unheld source credits before cleanup. A pre-migration pending reservation without a hold may settle only against an unambiguous period containing its persisted reservation creation time; expiry cleanup defers while wallet reservations remain. If source history is ambiguous, preserve wallet and ledger balances, do not create source attribution or a new grant automatically, and require reviewed reconciliation. The new migrations preserve prior ledger rows and wallet balances, backfill historical Free evidence and allowance snapshots from linked grant entries, and create the source-hold table without synthesizing holds for legacy reservations.
