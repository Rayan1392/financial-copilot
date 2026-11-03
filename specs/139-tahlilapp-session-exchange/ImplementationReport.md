# Feature 139 shared subscription-credit lifecycle

**Current status (2026-11-01): Shared lifecycle implementation and local verification are complete.** The full PostgreSQL verification set and disposable migration/backfill check passed against an isolated local PostgreSQL 18 cluster. The only remaining T-43 work is joint staging/rollout review; no production or shared database was used or changed. Features 151 and 152 were not started.

## Current behavior and implementation

- Plus and Pro create one grant for each complete authoritative TahlilApp subscription window `(slug, starts_at, expires_at)`. Catalog allowance is read from `billing_subscription_plans.IncludedCredits` and snapshotted in `AllowanceCredits` when the period is created. Retries keep the window's grant; a distinct renewal reads the then-current catalog allowance. No day-30/day-60 refill or expired-credit carryover occurs.
- Current seed catalog values are Free 250, Plus 260 and Pro 2600 in `BillingEntityConfigurations`. Runtime provisioning and usage do not substitute these values: missing or non-positive plan configuration returns an explicit error. A later catalog edit affects a new grant only.
- Free is assigned once on eligible initial local wallet provisioning. Expiry is internally assigned at 30 days from the persisted first-provisioning anchor in `CustomerAccount.SubscriptionEffectiveFrom`; repeat login/sync does not move it. `FreeCreditGrantEvidenceRow` is unique per user. Historical Free periods and grants, including 25-credit grants, count as received. Paid expiry, exhaustion, expiry, login and synchronization do not reissue Free.
- TahlilApp supplies paid plan windows and authoritative expiration dates; no paid subscription is represented as `subscription = null`. There is no TahlilApp Free plan or Free-expiry field, and none is required. The earlier internal assignment rule and its persisted anchor are reused.
- Expired sources are excluded from spendable reads before cleanup. Reservations hold specific sources; settlement/release retains source attribution. Legacy pending reservations without holds remain pending and are settled against an unambiguous source by their persisted creation time; cleanup defers closing an expired source while a reservation remains. Refunds attributed to expired sources are ledger-offset and never restore spendable wallet balance.
- Ambiguous legacy wallet balances are preserved for reviewed reconciliation. Backfills do not reset balances, rewrite ledger history, grant the old 225-credit Free difference, or retrospectively allocate paid allowances.

Code references: `SubscriptionCreditLifecycleService.cs` (initial Free eligibility/anchor, complete paid windows, allowance snapshots, source holds, settlement, expiry and refunds); `OwnedIdentityBillingProvisioner.cs` (persisted provisioning anchor); `BillingPersistenceModels.cs` and `BillingEntityConfigurations.cs` (durable grant evidence and source models/catalog); `BillingRepositories.cs` (expiry-aware spendable reads); `UsageReservationAuthorizationService.cs`, `UsageFinalizationService.cs` and `UsageRefundService.cs` (source attribution); `UsageController.cs` and `ServiceCollectionExtensions.cs` (expiry-aware wallet projection registration).

## T-43 migration/backfill verification

Migrations applied and verified only in uniquely named disposable test databases created by `PostgreSqlIntegrationFixture`:

- `20261101102658_FullWindowCreditGrantEvidence`
- `20261101103053_UsageCreditSourceHolds`
- `20261101103218_SnapshotSubscriptionCreditAllowance`

`SubscriptionCreditMigrationPostgreSqlTests.LifecycleBackfill_PreservesHistoryAndBalance_RecordsHistoricalFreeAndSnapshotsAllowances` starts at `20261029150802_AddBillingReservationRequestFingerprint`, inserts representative legacy data, applies the remaining migrations, then verifies:

- A historical 25-credit Free period creates permanent per-user evidence without an additional grant; the evidence records the historical grant and has no fabricated assigned expiry.
- The original Free seed row moves from 25/v1 to the current 250 allowance only under the migration's guarded condition.
- A paid period allowance snapshot is copied from its linked grant ledger entry.
- Wallet balance (including an unattributed 40-credit legacy remainder), reserved amount, wallet revision, usage ledger count and consumption attribution remain unchanged.
- A legacy `Reserved` reservation without a source hold is preserved; migration creates no fabricated holds or backfill ledger adjustments.

Rows with ambiguous historical provenance are intentionally left in place for reviewed reconciliation. Migration SQL does not guess a source or destroy their value. The existing service behavior for legacy pending reservations is covered by `SubscriptionCreditLifecycleTests.LegacyPendingReservationWithoutSourceHoldSettlesByPersistedWindowBeforeExpiryCleanup`.

## Verification results

All counts below are the final runs after correcting two PostgreSQL test setups. The first Plus→Pro concurrency test sent a legacy plan-only decision; it now sends an authoritative plan window. The refund/expiry test had `Reason` and `IdempotencyKey` reversed; its command arguments now match the contract. No assertions were removed or weakened, and no implementation defect was exposed by these two initial failures.

| Check | Result |
| --- | --- |
| Subscription lifecycle + usage PostgreSQL independent-connection tests, including duplicate grants, concurrent reservations, usage/expiry, refund/expiry and retry-after-commit | **11 passed, 0 failed, 0 skipped** |
| Disposable migration/backfill PostgreSQL test | **1 passed, 0 failed, 0 skipped** |
| Combined PostgreSQL filter (both rows above) | **12 passed, 0 failed, 0 skipped** |
| Focused Billing/authentication unit regression filter (`TahlilApp | OwnedIdentity | Billing | SubscriptionCreditLifecycle`) | **238 passed, 0 failed, 0 skipped** |
| Launch/BillingPersistence/OwnedIdentity integration regression filter | **52 passed, 0 failed, 0 skipped** |
| Backend Release solution build | **Passed, 0 warnings, 0 errors** |

Docker Desktop was present but its Linux engine endpoint returned an API error. The two installed PostgreSQL services use application ports 5432/5433 and were not used. Instead, PostgreSQL 18 `initdb` created a temporary cluster under `%TEMP%` on loopback port 55439; the fixture accepted that explicitly configured disposable admin connection and created/dropped each test database. The cluster was stopped after verification. Its stopped data directory remains at `%TEMP%\feature139-pg-d3bb3b914e3543df8e7cda8ef6adf924`; shell safety policy rejected recursive removal. No production, shared, or staging database was connected to or changed.

Exact focused commands:

```powershell
$env:FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Pooling=false'
dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --no-restore --configuration Release --filter 'FullyQualifiedName~SubscriptionCreditLifecyclePostgreSqlTests|FullyQualifiedName~UsageBasedCreditBillingPostgreSqlTests|FullyQualifiedName~SubscriptionCreditMigrationPostgreSqlTests' --logger 'console;verbosity=minimal'
dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --no-restore --configuration Release --filter 'FullyQualifiedName~TahlilApp|FullyQualifiedName~OwnedIdentity|FullyQualifiedName~Billing|FullyQualifiedName~SubscriptionCreditLifecycle'
dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --no-restore --configuration Release --filter 'FullyQualifiedName~TahlilAppLaunchIntegrationTests|FullyQualifiedName~BillingPersistenceTests|FullyQualifiedName~OwnedIdentityEndpointTests'
dotnet build src/backend/FinancialCopilot.sln --no-restore --configuration Release --verbosity quiet
```

The disposable PostgreSQL cluster used for these runs has been stopped. Re-run the first command only after configuring the fixture with a disposable, loopback PostgreSQL admin endpoint on a non-application port and database `postgres`; the fixture creates and drops uniquely named databases. Do not point it at production or a shared database.

## Task status and remaining blockers

- **T-40:** Complete. Authoritative paid windows and the existing internal Free expiry assignment rule are confirmed and implemented; no TahlilApp Free-expiry contract blocker remains.
- **T-41:** Complete. Database-configured full-window/Free-once grants, durable evidence and conservative history handling are implemented and tested.
- **T-42:** Complete. Source-aware admission, settlement, expiry and refund behavior is implemented and tested.
- **T-43:** **Locally verified; joint staging/rollout review remains open.** All 12 PostgreSQL checks now execute without skips, and migration/backfill behavior passes in a disposable database. Do not apply migrations to production from this report alone. Staging contract confirmation, operational rollout approval and any production backfill review remain separate release activities.

There is no external Free-expiry contract blocker. No local database check remains skipped. The only remaining blocker for rollout is the intentionally unperformed joint staging/operations review; no production data or deployment was touched.

## Readiness for Features 151 and 152

The shared source and lifecycle foundation is ready for separate follow-on implementation: Feature 151 can add period-bound top-up sources, and Feature 152 can add explicit Unlimited-mode usage. Neither feature was started here. This readiness is code/test readiness; staging and production rollout approval remain separate.
