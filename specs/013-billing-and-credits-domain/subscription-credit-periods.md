# SapioAI subscription credit periods

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
