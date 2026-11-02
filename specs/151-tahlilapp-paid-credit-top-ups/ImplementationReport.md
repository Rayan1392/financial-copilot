# Feature 151 implementation report

**Local implementation readiness:** Complete. All in-repository Feature 151 implementation and focused verification are complete.

**Production enablement:** Not ready and intentionally disabled. Joint TahlilApp staging and operations gates remain open. No production migration or data change was applied.

## Changes and acceptance evidence

- Added database-controlled top-up quantities and versioned package validation, partner JWT authentication, bounded request handling, eligibility and source-window validation, purchase/event/payment receipts, replay and conflict handling, and atomic grant or persisted unfulfilled outcomes. Evidence: `TahlilAppTopUpService`, `TahlilAppTopUpsController`, Billing persistence/migration, and `TahlilAppTopUpEndpointTests` / `TahlilAppTopUpPostgreSqlTests`.
- Grants are tied to the persisted original authoritative Plus/Pro subscription tuple and expire at that window's expiry. Existing purchase outcomes replay before eligibility checks, including after expiry or plan changes. Duplicate delivery cannot grant twice; expired refunds do not restore spendable credits. Evidence: PostgreSQL tests for replay after expiry, original-window expiry, payment identity uniqueness, source refund and delivery races.
- Added tenant-scoped operator reconciliation at `GET /api/v1/admin/billing/top-ups/reconciliation`, following existing Billing audit read permission and admin panel patterns. It reports bounded purchase outcomes and identity conflicts; conflicting reuse is audited without a second grant. Evidence: reconciliation endpoint API coverage and frontend panel under the existing billing audit access gate.
- Added targeted concurrency retries in usage finalization, refunds and top-up expiry so production transaction paths can recover from source revision races. PostgreSQL race coverage executes the actual fulfillment, reservation, finalization, refund and lifecycle services against PostgreSQL with independent contexts/connections.
- Added SapioAI self-scoped purchase status UI. It waits for a committed grant and then refreshes the existing usage endpoint; browser return does not create credits.
- Updated shared overlap behavior: active Max controls Unlimited mode; after its expiry the latest-expiring active non-Max window determines the effective plan. Finite grants and eligible top-ups remain additive, each with its own source expiry. No cross-window transfer or extension occurs. New top-up eligibility remains excluded during active Max, and delivery remains bound to the original Plus/Pro source window.
- Feature 151 does not implement Max accounting; its eligibility, source accounting and projections consume the shared effective-plan/quota result. Existing Kesaveh investigation changes were preserved.

## Verification

| Check | Result |
|---|---:|
| `TahlilAppTopUpPostgreSqlTests` + `TahlilAppTopUpEndpointTests` (Release, disposable local PostgreSQL) | 10 passed, 0 failed, 0 skipped |
| Billing/auth/lifecycle unit filter (`TahlilApp`, `OwnedIdentity`, `Billing`, `SubscriptionCreditLifecycle`) | 243 passed, 0 failed, 0 skipped |
| Expanded billing/auth/lifecycle integration regression filter | 90 passed, 0 failed, 0 skipped |
| Backend solution Release build | Passed, 0 warnings, 0 errors |
| Frontend `npm run build` | Passed (existing route-like test-file and chunk-size warnings) |
| Focused frontend tests: `markdown-message.test.tsx` and `monthly-product-trend-chart.test.tsx` | 27 passed, 2 failed; both failures reproduce on clean baseline `HEAD 5f50f847` with the same rendered text |
| `git diff --check` | Passed; Git reported line-ending normalization warnings only |

The approved overlap behavior was verified in the PostgreSQL Feature 152 overlap test through the production lifecycle, plan projection, reservation, settlement, expiry, and refund paths. The focused Feature 151/152 PostgreSQL and endpoint test run reported **17 passed, 0 failed, 0 skipped** across `TahlilAppMaxUsagePostgreSqlTests`, `TahlilAppTopUpPostgreSqlTests`, and `TahlilAppTopUpEndpointTests` on the disposable local database.

The expanded billing/Feature 146/Feature 151/152 integration regression run reported **58 passed, 1 failed, 0 skipped**. The remaining test, `Feature146PostgreSqlIntegrationTests.Auth_migration_backfills_unknown_without_inferred_source_and_adds_query_indexes` (line 43, `Sequence contains no elements`), also failed at the same line in a detached clean baseline checkout `5f50f847` against the same disposable PostgreSQL server. The affected Customer360 regression passed after the additional reporting query was consolidated; its dedicated rerun was **1 passed, 0 failed**.

The two frontend failures are pre-existing test/render expectation mismatches, not Feature 151 regressions. Exact failing tests:

- `MarkdownMessage > renders a quarterly financial table with 16 rows inside an RTL message` expects ASCII digits and punctuation in `سال مالی 1405، فصل 2`; the UI renders Persian digits and omits that punctuation in text content.
- `MonthlyProductTrendChart > renders a failed product trend message once in chat` expects a trailing period in `بیش از یک محصول با این مشخصات یافت شد.`; the rendered message has no trailing period.

Both assertions and their components are outside the Feature 151 change set. The same two failures and 27 passing tests were independently reproduced in a clean detached worktree at `5f50f847`. Tests were not skipped or weakened. No unrelated frontend behavior was changed.

## Configuration and migration

- Migration: `20261101131932_Feature151TahlilAppPaidCreditTopUps` adds the receipt, top-up source and allocation persistence required by the contract. It was exercised by disposable local PostgreSQL tests; it has not been applied to production.
- `Billing:TahlilAppTopUps:Enabled` remains `false` in `appsettings.json`. Before staging/enablement, configure the dedicated partner token issuer, audience and signing keys through deployment secrets/configuration. The current audience default is `sapio-tahlilapp-topup`; issuer and signing keys are empty in checked-in defaults.
- Keep the database-backed active offer/package configuration aligned with TahlilApp's offer IDs and versions. Do not enable delivery until staging authentication, delivery, reconciliation and key-rotation checks pass.

## Remaining work

### Local repository

No Feature 151 local implementation or test tasks remain. The two baseline frontend assertion failures above remain visible and documented; they were not modified because clean-baseline evidence confirms they predate this feature.

### External TahlilApp work

- Implement the offer and checkout UI, eligibility check, and persist the original `(slug, starts_at, expires_at)` subscription tuple with each purchase.
- Verify payment server-side, retain the monetary audit, durably persist the verified purchase and positive integer `creditsPurchased`, then deliver with the dedicated partner token and stable event, purchase, and payment IDs.
- Retry delivery durably, classify retryable versus terminal responses, reconcile paid-but-ungranted purchases, and coordinate payment refunds/chargebacks without treating a payment refund as permission to restore expired credits.
- Do not permit new purchases for Free or active Max. Use the current delayed-delivery contract for a previously paid Plus/Pro purchase while its original window remains active.

### Staging and operations gates

- Jointly verify contract fields/statuses, partner authentication and key rotation, original-window snapshots, duplicate/retry behavior, expired and ineligible outcomes, and operator reconciliation against TahlilApp staging.
- Confirm Feature 139 identity, status and migration readiness in joint staging; its local PostgreSQL verification is complete but its staging/operations review remains pending.
- Review monitoring for paid-but-ungranted age, expired-unfulfilled outcomes and identity conflicts. Deploy the migration and secrets with delivery disabled; enable only after the joint gates pass. Keep delivery available while draining paid purchases if rollback is needed.

No external checkout/delivery integration or staging/operations review is claimed complete.
