# Feature 139 — Implementation Report

## Revision 6 execution update — 2026-10-24

**Status: PARTIAL.** Product decisions for Plus→Pro carry and authoritative TahlilApp activation were applied. TahlilApp staging and operational gates remain unverified.

- **Tasks:** 24/33 complete; 6 partial; 2 deferred (`T-24`, `T-28`); 1 open Operations task (`T-06`).
- **ACs:** 82/99 fully verified. AC-97 and AC-98 have unit coverage, while their PostgreSQL concurrency evidence is pending. The 15 previously incomplete external, privacy, or operations criteria remain incomplete.
- **Focused unit tests:** 134 passed, 0 failed, 0 skipped (`SubscriptionCreditLifecycleTests`, Billing sync/service, TahlilApp policy/parsers/status refresh).
- **Focused integration tests:** 53 passed, 0 failed, 1 skipped. TahlilApp launch, native enrollment, Billing persistence/purchase, and Feature 139 suites passed. The skipped PostgreSQL transfer-race test could not start because `docker info` failed: Docker Desktop Linux engine pipe was unavailable.
- **Full unit regression:** 2,323 passed, 23 failed, 0 skipped (2,346 total). The 23 failing test names exactly match the saved pre-change TRX at `%TEMP%\\feature139-regression-20261023\\unit.trx`; these are unrelated to Billing/Feature 139.
- **Backend Release build:** `dotnet build src/backend/FinancialCopilot.sln --configuration Release --no-restore` succeeded with 0 warnings and 0 errors.
- **Migration:** `20261024004630_AddAuthoritativeSubscriptionActivationRevision` adds nullable `ExternalSubscriptionRevision` and a default-false `SubscriptionActivationIsAuthoritative` marker. It does not modify balances or ledger rows and was not applied by this work; database applied state is unverified because no database connection was used.
- **Contract:** SapioAI sends `X-SapioAI-Contract-Version: 2` for exchange and status calls. The deployed response remains parse-compatible; missing, malformed, non-UTC, future or expired activation timestamps cannot anchor paid allocations.
- **Credit lifecycle:** Free→paid forfeiture is auditable. Plus→Pro creates a fresh Pro period with 2600 credits plus only the unconsumed Plus remainder, paired transfer ledger adjustments, and expiry with that Pro period. Pro→Plus resolves to Free rather than retaining paid Pro eligibility.
- **Staging:** TahlilApp contract deployment, API credentials, real launch/status round trips, PostgreSQL concurrency execution, migration application in non-production staging, legacy-wallet reconciliation, and Operations checks remain unverified. No production migration, deployment, commit or push was performed.

This update supersedes earlier statements below that Plus→Pro transfer and the authoritative activation decision were still awaiting Product approval.

**Report date:** 2026-10-24
**Status:** `PARTIAL — Revision 6 billing semantics are implemented; PostgreSQL concurrency, TahlilApp staging contract and operational gates remain unverified.`

Design.md Revision 4 §22 remains authoritative for identity and integration reconciliation; Revision 6 §24 supersedes earlier Plus→Pro and activation-anchor questions. User-facing product name is SapioAI; existing `FinancialCopilot` namespaces, routes and deployed API contracts are preserved. No commit, push, deployment or production migration was performed.

## 1. Tasks and acceptance criteria

- **Tasks:** 24/33 fully checked; 6 partial (`T-04`, `T-18`, `T-21`, `T-30`, `T-32`, `T-33`); 2 deferred (`T-24`, `T-28`); 1 Operations-owned task open (`T-06`).
- **Acceptance criteria:** 82/99 fully verified. AC-01…AC-04, AC-06, AC-21, AC-22, AC-34, AC-67, AC-70, AC-71, AC-75, AC-79, AC-81, AC-95, AC-97 and AC-98 lack full evidence. AC-97/AC-98 unit coverage passes, but PostgreSQL concurrency execution is skipped because Docker is unavailable.
- **Phase 1 deferrals:** T-24 (admin masked profile/audited reveal) and T-28 (explicit account linking) are not launch-critical and remain deferred. The batch status endpoint is also deferred unless measured scale requires it.

## 2. New and completed implementation work

- Revoked TahlilApp status now deletes the encrypted profile in the same Auth database save while retaining the external-identity denial tombstone. Local-user deletion cascades to profile and external-identity rows.
- `/launch` checks for an existing SapioAI session and asks before replacing it. Confirm exchanges the one-time code; cancel returns to `/chat`.
- `/launch-failed` offers a return link only when the validated `VITE_TAHLILAPP_RETURN_URL` is configured. It remains a user-triggered navigation and does not create an automatic redirect loop.
- Integration coverage now exercises the real application log sink with unique profile PII, `amr=tahlilapp`, launch rate limiting, profile revocation/deletion, different-code same-identity concurrency, expiry guards and native direct login resolving the launch-created user.
- The native-identity test host disables database-backed production workers and Feature 141 because it uses in-memory test stores. The tests no longer need a tracked database credential to start.
- Runtime appsettings no longer contains PostgreSQL connection values. EF design-time commands require `FINANCIAL_COPILOT_CONNECTION_STRING` from the operator's environment or secret store.
- Updated the affected .NET 10 dependencies, OpenAPI, MailKit, Testcontainers and SSH.NET packages to versions that restore without the previously surfaced advisory/downgrade errors.
- Added `SubscriptionCreditLifecycleService` and a bounded allocation worker using the existing wallet and append-only usage ledger. New-wallet/activation grants, period expiry, Free-to-paid forfeiture and usage/refund attribution are auditable and keyed for idempotency.
- Added `SubscriptionCreditPeriodRow`, its unique/concurrency indexes, nullable `SubscriptionCreditsApplied`, and forward-only EF migration `20261023200709_AddSubscriptionCreditLifecycle`. The migration changes schema only; it does not backfill or reset production balances and was not applied by this work; database applied state is unverified because no database connection was used.
- Generated the idempotent migration delta with EF CLI `10.0.13` into `%TEMP%\feature139-credit-lifecycle.sql` and inspected the SQL: it adds the nullable usage attribution column, creates the period table/indexes, and records migration history; it contains no wallet balance update or ledger backfill.
- Preserved existing wallet balances and historical ledgers for accounts without period rows. Their recurring allocation is held until a reviewed legacy reconciliation is approved.
- Added lifecycle tests for initial Free/Plus/Pro allowance, exact 30-day expiry, no rollover, Free-to-Plus/Pro forfeiture, partial/full consumption, Plus-to-Pro no-balance-change hold, Pro-to-Plus rejection, expired Pro-to-Free, repeat events, worker concurrency, legacy preservation, and launch/native-login parity.

## 3. Billing and credit allocation

TahlilApp owns purchases, active external subscription status and user eligibility. SapioAI owns internal plan capabilities, wallet balances, consumption and immutable ledgers.

The approved monthly allowances are Free `25`, Plus `260`, Pro `2600`; the period is exactly 30 days, with no rollover. First activation and due rollover create one keyed grant. Free-to-Plus/Pro forfeits the tracked remaining Free allowance through a ledger adjustment and grants the full paid allowance. Usage is attributed to the subscription period and the existing wallet; consumption history remains append-only. Repeated login, exchange, status sync and worker retry do not grant twice or reset balances.

At the period boundary unused credits expire through a negative ledger adjustment. External paid expiry removes paid capabilities immediately, while the existing credit period remains valid until its own 30-day end; Free begins at that boundary. Pro-to-Plus is rejected. Plus-to-Pro synchronizes external eligibility but makes no credit balance change until Product explicitly approves the proposed remaining-Plus transfer and timing.

The former Revision 5 product questions are resolved by Revision 6: TahlilApp `effectiveFrom` is authoritative, and Plus→Pro carries only the remaining Plus balance into a fresh Pro period. TahlilApp v2 rollout and legacy wallet reconciliation remain staging gates. The generated migration is schema-only and was not applied by this work; database applied state is unverified because no database connection was used.

Refund boundary: the existing `UsageRefundService` returns refund credits to the general wallet. It restores subscription-period attribution only while the source period remains active; after expiry it does not reopen that period. Product must confirm whether a refund of subscription-funded usage remains spendable after expiry. No changed late-refund policy was invented.

## 4. Permission mapping

The current external `permissions[]` is parsed as informational metadata only. No authoritative TahlilApp permission codes have been supplied, so no external permission grants or removes SapioAI capabilities.

SapioAI registered `admin.users.pii.read` for possible later admin reveal; it is not assigned to default roles and authorizes no current endpoint. TahlilApp determines external eligibility; SapioAI Billing maps the approved subscription plan to internal capabilities. Before permission-dependent authorization is enabled in staging, TahlilApp and SapioAI Product/Security must approve a versioned list of exact external codes and their mapping.

## 5. Focused verification

| Suite | Command/filter | Result |
| --- | --- | --- |
| Billing and credit lifecycle unit | `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~BillingServiceTests|FullyQualifiedName~SubscriptionCreditLifecycleTests|FullyQualifiedName~TahlilAppBillingSyncTests|FullyQualifiedName~TahlilAppEntitlementRefreshTests"` | 48 passed, 0 failed |
| Feature 139 / native enrollment integration | `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~TahlilApp|FullyQualifiedName~NativeEnrollmentIntegrationTests"` | 36 passed, 0 failed |
| Billing persistence / endpoints integration | `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~BillingPersistenceTests|FullyQualifiedName~BillingEndpointTests"` | 34 passed, 0 failed |
| Narrow subscription lifecycle / Tahlil sync | `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~SubscriptionCreditLifecycleTests|FullyQualifiedName~TahlilAppBillingSyncTests|FullyQualifiedName~TahlilAppEntitlementRefreshTests"` | 37 passed, 0 failed |
| Native identity/profile/forwarded headers | focused filter for `NativeEnrollmentIntegrationTests`, `OwnProfileIntegrationTests`, `ForwardedHeadersSecurityTests` | 29 passed, 0 failed |
| Frontend launch and return URL | `npm run test -- --pool=threads --run src/integrations/financial-copilot/__tests__/launch.test.ts src/integrations/financial-copilot/__tests__/launch-return-url.test.ts` | 16 passed, 0 failed |
| Backend Release build | `dotnet build src/backend/FinancialCopilot.sln --configuration Release --no-restore` | Passed, 0 warnings, 0 errors |
| Frontend production build | `npm run build` | Passed |

The TahlilApp gateway is replaced with an in-test double in integration tests. These results do not verify a live TahlilApp service.

## 6. Full regression results

Full backend test command, with optional Feature 141 disabled for this test process because the local test environment has no application database:

```powershell
$env:Feature141__Enabled = 'false'
dotnet test src/backend/FinancialCopilot.sln --configuration Release
```

| Suite | Result | Observed failure detail |
| --- | --- | --- |
| Unit | 2,312 passed, 23 failed, 0 skipped (2,335 total) | All 23 failed test names match the earlier pre-credit-lifecycle TRX at `%TEMP%\feature139-regression-20261023\unit.trx`; the current run added 10 passing lifecycle tests. Failures span unrelated source-planner, semantic routing/catalog, Feature 141, NADPCO, Telegram and quarterly-report suites. |
| Integration, initial baseline | 191 passed, 396 failed, 48 skipped (635 total) | First captured sequential run. The first isolated V2 failure reproduced a database-backed `FundPortfolioImportProcessingWorker` exception from an unset PostgreSQL connection string; Feature 143's reconciliation worker also logged the same missing connection. `TestServer` shutdown and disposed-service errors were cascading symptoms. |
| Integration, after test-host isolation | 518 passed, 69 failed, 48 skipped (635 total) | Removing production `IHostedService` registrations in the common `AuthenticationApiFactory` eliminated the cascading host shutdowns. Six PostgreSQL cases reported `DockerUnavailableException` (Docker unavailable); 63 other failures remain and are listed in the TRX. A later focused correction supplied the missing test-only JWT key; Foundation and Fund Portfolio admin now pass 5/5. The full integration suite has not yet been rerun after that correction. |
| Architecture | 11 passed, 1 failed (12 total) | This increment reran the suite and reproduced `MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction`; the same failure name is in the earlier pre-credit-lifecycle architecture TRX. It is a source assertion in the semantic executor area, outside Billing. |
| Frontend full tests | 156 passed, 2 failed (158 tests; 22 files) | After `npm ci`, the standalone quarterly PNG browser check passes. Remaining failures are quarterly-table numeral expectations in `markdown-message.test.tsx` and a failed-product-message assertion in `monthly-product-trend-chart.test.tsx`. `disclosures.test.ts` was fixed to label receipt time as Tehran and passes 2/2 in a focused rerun. |

Sequential baseline logs and TRX files are preserved under `%TEMP%\feature139-regression-20261023`. The rerun after isolation is under `%TEMP%\feature139-regression-after-base-isolation`; exact failing test names and stack traces are in `integration.trx` and `integration.log`. Current architecture/frontend logs are under `%TEMP%\feature139-regression-final-20261023`; focused verification logs are under `%TEMP%\feature139-final-focused-after-isolation`. The scoped integration runs in this increment used `$env:Feature141__Enabled = 'false'` because the local test environment has no application database. Current focused totals: lifecycle/Billing unit 48/48; Feature 139/native integration 36/36; Billing persistence/endpoints 34/34; backend Release build 0 warnings/errors. Full integration was not rerun in this increment. Earlier targeted V2 isolation reproduction passed 7/7; Foundation/Fund Portfolio auth configuration passed 5/5 after adding test-only signing-key configuration.

## 7. Staging runbook and operational evidence

`StagingReadinessChecklist.md` now contains exact commands and evidence gates for:

- Restore, focused/full tests and Release builds.
- Auth and Billing migration listing, idempotent SQL generation/review and staging-only application. Design-time connection uses `FINANCIAL_COPILOT_CONNECTION_STRING`.
- Lifecycle migration `20261023200709_AddSubscriptionCreditLifecycle`, EF CLI/runtime match `10.0.13`, legacy-wallet balance/reservation/ledger snapshot, and no-apply-before-approval guard.
- Secret configuration, secret-store usage, credential rotation, and TahlilApp API-key issuance/rotation coordination.
- Durable Data Protection volume, backup/archive verification and a restore drill.
- Trusted reverse proxies, forwarded-header/rate-limit checks and same-site frontend/API topology.
- SMTP configuration and test-message verification.
- Contract approval, disposable staging launch/exchange/status cases, authentication parity, Billing/credit checks and the final feature-flag gate.

All external and operational checklist boxes remain unchecked because no staging environment, live TahlilApp service, secret manager, SMTP provider, proxy topology, migration window or key-ring volume was available here. No migration was applied and no deployment was attempted.

## 8. Remaining SapioAI work

- Resolve the 23 unchanged unit failures, rerun the full integration suite to get a current count after the five focused test-only JWT fixes, resolve the remaining integration/architecture/frontend failures, and rerun all suites. Six PostgreSQL integration cases were blocked by unavailable Docker in the last full integration run.
- Add/verify dedicated hosted-worker integration coverage with explicit test stores; the integration project contains no tests that directly host `FundPortfolioImportProcessingWorker` or `TelegramFinancialStatementReconciliationWorker`. The test-only factory override leaves production registrations unchanged.
- Close the three frontend assertion failures and rerun frontend regression.
- TahlilApp must deploy the approved version 2 contract and return authoritative UTC `effectiveFrom`; no SapioAI observation timestamp is substituted.
- Plus→Pro transfer and 30-day Pro timing are approved and implemented; cross-process PostgreSQL evidence and live contract integration remain pending.
- Approve a legacy-wallet reconciliation plan before assigning period grants to existing wallets; their current balances and historical ledger rows are preserved.
- Confirm the existing general-wallet handling of refunds for subscription-funded usage after the source period expires.
- Add a cross-process PostgreSQL concurrency/race test for competing allocation workers when PostgreSQL integration infrastructure is available.
- Execute and attach evidence for staging Auth/Billing migrations, persistent key-ring backup/restore, proxy/rate-limit, SMTP and E2E checks after environments are supplied.
- Get the existing Product/Billing decision on account-wide behavior for expired admin-set subscription windows.
- Keep permission-dependent authorization disabled until the coordinated permission contract is approved.

## 9. Remaining TahlilApp dependencies

- Approve and provide the exact launch, exchange and per-user status endpoints, DTOs, error semantics, SLA and service-key policy.
- Issue/restrict the staging service API key and confirm launch-code single-use/atomic consumption and real concurrency behavior.
- Approve exact external permission codes only if permission-dependent authorization is required.
- Provide a disposable staging account and run the contract and end-to-end round trip with SapioAI QA.

## 10. Staging blockers and final status

Staging is blocked on the unresolved external API contract/key, real launch/exchange/status tests, approval of the activation anchor, Plus-to-Pro handling and late-refund behavior, legacy-wallet reconciliation, auth and Billing migration evidence, credential rotation, persistent key-ring restore, trusted proxy and same-site verification, SMTP verification, and the non-green full regression. T-06 and all checklist operations gates remain open. T-24 and T-28 remain explicit Phase 1 deferrals.

**Final status: `PARTIAL`.** SapioAI-owned Plus→Pro transfer, authoritative activation validation, ledger operations, revision ordering and tests are implemented. Focused suites pass and the backend Release build is clean. The full unit suite has 23 failures, each matching the saved pre-change TRX. PostgreSQL concurrency could not run because Docker Desktop is unavailable. TahlilApp contract deployment/real status and exchange tests, legacy-wallet reconciliation, migration execution in staging and Operations gates remain unverified; Feature 139 is not `COMPLETE`.
