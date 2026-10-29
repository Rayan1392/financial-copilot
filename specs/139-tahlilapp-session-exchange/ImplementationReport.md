# Feature 139 — Implementation Report

## Revision 9 — Official `name` persisted as `DisplayName` (2026-10-28)

**Status: IMPLEMENTED; migration NOT applied.** The official exchange `name` was parsed but discarded, so every real launch created an all-null profile and the profile page could render `null null`. It is now stored in `auth_user_profiles.DisplayName`.

- **Schema.** Migration `20261028144627_Feature139UserProfileDisplayName` adds one nullable column, `auth_user_profiles.DisplayName character varying(100)`, with no default and no index (`Down` drops only that column). The model snapshot changes only by that property. `auth_users` and `auth_external_identities` are unchanged; `user_id` remains in `auth_external_identities.ExternalUserId`.
- **Parsing.** `TahlilAppExchangeSnapshot.Name` (`string?`) became `DisplayName` (`IncomingProfileField?`; null = Absent). `TahlilAppExchangeParser` validates with `PersonName`. A non-string `name` previously rejected the whole exchange; it is now `Invalid` and the launch proceeds (Design §10.2 / Revision 9).
- **Synchronisation.** `TahlilAppAccountProvisioner` applies `ProfileFieldMergePolicy` to `DisplayName` exactly as for the other profile fields. Changed fields log `displayName` by name; an invalid value logs `TahlilAppProfileFieldRejected` with field name and reason only.
- **Read surfaces.** `OwnProfileView`/`OwnProfileResponse` add nullable `DisplayName`. The admin directory projects and searches `DisplayName` and displays it before the first/last join (`ResolveDisplayName`). Frontend: `OwnProfile` fields are nullable; `profileDisplayName` gives DisplayName → first + last → «—»; masked identifiers render «—» when null.
- **Test-host logging fix.** `Program.cs` uses `UseSerilog(...)` without `writeToProviders`, so the `TahlilAppLogCaptureProvider` used by T-25 never received application log calls; `LaunchLogsNeverContainProfilePiiValues` passed without observing provisioner logs. `TahlilAppLaunchApiFactory` now routes the test host's `ILoggerFactory` to the capture provider. With real capture, the existing T-25 test and the new DisplayName redaction test both pass and positively observe the `displayName` change and rejection log lines. Production logging is unchanged.

Tests added or updated:

| Area | Tests |
| --- | --- |
| Parser (unit) | Official JSON and Persian «کامران عبدی» sample, trim, Absent, Null, 8 invalid shapes (number, boolean, object, array, empty, whitespace, control characters), 100/101-character boundary, `ToString` redaction |
| Admin precedence (unit) | `ResolveDisplayName`: DisplayName wins, trimmed; blank falls back to first/last; all-blank gives null |
| Launch (integration) | Through the production `TahlilAppIdentityGateway` and parser over a stubbed HTTP 200 body: first launch, changed name, identical name (`UpdatedAtUtc` unchanged), missing/null/invalid keeps stored value, first launch without a usable name, same local user and one identity link reused, `auth_users` unchanged |
| Profile API (integration) | `displayName` returned; missing names are JSON `null` |
| Admin directory (integration, EF InMemory) | Display and search by DisplayName; first/last fallback and search for older records; unnamed user shows null |
| PII logs (integration) | Valid and invalid name values never appear in captured logs; field-name lines do |
| PostgreSQL (integration, skippable) | Upgrade from the previous migration keeps existing rows; column type, length, nullability, no default, no index; rollback; directory SQL search and fallback |
| Frontend | `profileDisplayName` precedence and «—»; rendered `TahlilAppProfileCard` shows DisplayName, fallback, and never `null null` |

Verification for Revision 9:

- Backend Release solution build (non-incremental): 0 warnings, 0 errors.
- Focused unit tests (TahlilApp, admin display name, merge policy): 126 passed, 0 failed.
- Full unit suite: 2,320 passed, 20 failed (2,340). The 20 failures are in the same unrelated areas recorded below (Feature 128/141, semantic dialogue, financial source planning, schema navigation, Telegram/Nadpco, structured query); none in Feature 139, profile or admin.
- Focused Feature 139/profile/auth integration tests: 86 passed, 1 failed, 2 skipped (89). The failure, `TahlilAppLaunchDisabledIntegrationTests.WhenFeatureIsDisabled_LaunchReturnsNotFound` (expected 404, actual 401), reproduces identically on a clean `HEAD` worktree and predates this revision. The 2 skips are the new PostgreSQL tests (Docker unavailable).
- Full integration suite: 549 passed, 58 failed, 57 skipped (664). A clean `HEAD` worktree run gives 533 passed, 58 failed, 55 skipped (646), and the two failure lists are identical test-for-test: no new failure.
- Frontend: focused profile/auth tests 45 passed (8 files). Full vitest run: 169 passed, 2 failed; the 2 failures (`markdown-message`, `monthly-product-trend-chart` Persian digit formatting) do not import any changed module, and the 3 browser-only `scripts/*.browser.test.mjs` files are not vitest targets. No type or lint error is on a changed line; `_app.profile.tsx` has pre-existing prettier findings.

Remaining gates: PostgreSQL schema/rollback and directory tests must run where Docker is available; migration application in staging; staging verification with a live TahlilApp `name`; existing TahlilApp users receive `DisplayName` only on their next launch (no backfill is possible because the name was never stored). No production API call, deployment, migration or commit was made.

## Revision 8 — Temporary Closed Registration policy

Implemented: `/auth` now provides local email/password login and password recovery without a public sign-up control; the frontend public registration helper was removed. The sole public registration endpoint, `POST /api/auth/v1/register`, rejects requests before account creation with HTTP 403 and `REGISTRATION_DISABLED`. The option defaults to false and is explicit in API settings. The restriction is not applied in `OwnedIdentityService` or the provider provisioner. Feature 139 launch tests run with public registration disabled, preserving first-time and repeated TahlilApp provisioning; Feature 145 credential enrollment, direct login and refresh remain separate supported flows. No database, billing policy, deployment, or external API changes were made for this increment.

Verification for Revision 8: focused frontend auth tests pass (2 files, 3 tests); focused authentication/Feature 139 integration tests pass (53 tests, including closed-endpoint side-effect checks, first/repeat external launch, enrollment, direct login and refresh); backend Release solution build succeeds with 0 warnings and 0 errors. Full regression suites were not rerun. Previously recorded unrelated failures remain documented in the earlier regression sections above. Staging remains blocked on the existing contract, migration, wallet reconciliation, key-ring, SMTP, proxy and external end-to-end evidence gates; registration-disabled staging verification is also pending. No production API, deployment, or migration was executed.

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
## Revision 7 — Official TahlilApp contract and approved independent subscriptions

This revision supersedes conflicting assumptions in Revisions 1–6. Those sections remain as implementation history only.

The confirmed API uses HTTP Basic Auth with client ID `financial-copilot` and an environment supplied secret. The base is `https://api.tahlilapp.com/api`; exchange is `POST /partners/exchange` with `{ "code": "..." }`; status is `GET /partners/users/{user_id}/status`. Responses identify the account by root `user_id`, allow nullable `name`, and contain `subscription.plans[]` entries with `slug`, `starts_at`, and `expires_at`. Supported slugs are `sapio_plus` and `sapio_pro`. The contract does not supply `effectiveFrom`, subscription IDs, or revisions. Prior `effectiveFrom` and revision requirements are superseded. The new `starts_at` field is pending external contract delivery; no historical activation time may be inferred.

Each paid plan is an independent subscription. Plus and Pro can be active together; the effective plan is Pro whenever Pro is active, otherwise Plus when Plus is active, otherwise Free. Both valid paid purchases contribute grants. A Pro activation transfers only remaining eligible Plus credits, paired in the append-only ledger; consumed Plus credits are never granted again. Each plan-period grant is idempotent by account plus `slug + starts_at + expires_at` identity and 30-day period number. Consumption is attributed to source periods and uses earliest-expiry-first ordering. The wallet remains an aggregate projection; source period rows, grant references, charge allocations, refunds, expiry, and transfer ledger rows preserve accounting provenance. No migration rewrites historical wallet or ledger entries.

Paid access is computed from the active per-plan windows, independently from credit accounting. The last paid subscription expires at the greatest authoritative `expires_at` among the user's paid subscriptions when no paid window remains active. At that timestamp effective plan becomes Free and the 30-day Free-credit cooldown begins, even if SapioAI observes expiry later. The durable cooldown row is separate from authentication and plan state; repeat login/status sync cannot extend it. Reservation authorization denies AI usage during cooldown even if a balance remains. Authentication stays available. Reactivation permits paid reservations immediately; a later last-paid expiry uses the later authoritative expiry. Tracked Free credits are forfeited on paid activation; unexpired paid balances remain held during cooldown and expire by their own source period. No Free grant occurs during cooldown. Free allocation resumes after it, with a durable period identity to prevent duplicate historical grants.

Implementation adds per-plan subscription state, credit-period identity, immutable usage-source allocations, and cooldown persistence. The EF migration is `20261028104343_Feature139OfficialPlanSubscriptionsAndCooldown`; it has not been applied. A legacy period-key backfill preserves existing period uniqueness without altering wallet totals or ledger history. Existing legacy wallets still require a separately approved reconciliation before new subscription grants can be attributed.

Verification: focused Feature 139/Billing tests pass (121 unit tests; 20 integration tests pass and one PostgreSQL concurrency test is skipped because its database infrastructure is unavailable). The backend Release solution build passes with zero warnings and errors. The full unit suite has 20 unrelated failures: 18 match failures in the saved baseline TRX and two are in Nadpco/Telegram tests; details are in `ImplementationReport.md`. No production API call, deployment, or database migration was performed. TahlilApp delivery of `starts_at`, joint staging exchange/status verification, migration application, and operational readiness remain unverified.

Revision 7 acceptance criteria AC-100 through AC-106 are defined in tasks.md and apply to this document. Revision 7 controls over conflicting earlier assumptions.

### Latest full unit regression result

`dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --no-build --no-restore` completed with 2,298 passed and 20 failed (2,318 total). The 20 failures are outside Feature 139/Billing, in existing Feature 128/141, semantic dialogue, financial source planning, schema navigation, Telegram/Nadpco and structured query tests. A prior full run had 18 failures overlapping the saved pre-change baseline TRX; the remaining failures are unrelated to this change and vary across repeated runs. No Feature 139/Billing test failed in the focused run.

## Revision 8 � Implementation status

Implemented authoritative empty-plan revocation in the existing per-plan Billing synchronizer. It marks omitted still-future plan rows revoked without changing their original expiry, forfeits remaining period credits through an idempotent append-only adjustment, releases outstanding reservations before forfeiture, and persists a stable first-observed revocation timestamp for the 30-day cooldown. Independent remaining paid plans continue to determine the effective plan. Status refresh is checked on login and before new usage reservations when the 15-minute verification TTL is stale; it uses the persisted TahlilApp external id, shared single-flight refresh and a 1.1-second per-process partner request pace. Unavailable responses do not alter authentication and fail closed after the configured stale grace.

Migration `Feature139SubscriptionRevocation` is generated and remains unapplied. Focused regression and Release build results for this revision are recorded after execution below. No production endpoint, deployment, or migration was used.

### Revision 8 verification

- Focused unit command: `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~TahlilApp|FullyQualifiedName~SubscriptionCreditLifecycle|FullyQualifiedName~Billing"` � **186 passed, 0 failed**. Includes authoritative inactive/empty-plan revocation, stable cooldown, consumed-credit preservation, pending reservation release, per-user concurrent refresh, official status path/auth, and status failure mapping.
- Focused integration command covered TahlilApp launch, Billing persistence/endpoints, V1/V2 AI facade endpoints, and Feature 139 PostgreSQL tests � **65 passed, 2 PostgreSQL tests skipped, 1 failed**. The unrelated `TahlilAppLaunchDisabledIntegrationTests.WhenFeatureIsDisabled_LaunchReturnsNotFound` returned HTTP 401 instead of the expected 404; it does not exercise the revocation path and has not been confirmed against a pre-change run.
- Backend Release solution build: **passed, 0 warnings, 0 errors**.
- The PostgreSQL tests were skipped because local PostgreSQL/Docker infrastructure was unavailable. Migration `Feature139SubscriptionRevocation` was generated only; it was not applied. No production partner endpoint was called, and no deployment, commit, or push was performed.
- A shared distributed rate limiter is still an operational requirement for multiple API instances. Current pacing is process-wide (one request per 1.1 seconds per process), so aggregate traffic across replicas must be verified or routed through one shared limiter before staging enablement.

## Revision 9 — Initial inactive Free activation verification

The first successful TahlilApp launch with `active=false` and `plans=[]` already provisions the local identity's Free wallet and period through the existing wallet lifecycle initializer. The period is anchored to the UTC `TimeProvider` timestamp when the Billing wallet is first provisioned, receives the catalog's 25-credit Free allowance, and expires 30 days later. The subsequent inactive-plan reconciliation finds that period and preserves it; repeated launch/status reconciliation does not grant another period. Since a new account has no paid history, it has no paid-expiry cooldown and can reserve and consume Free credits immediately. No production behavior change was needed.

This first-launch behavior is distinct from paid-plan revocation: an account with paid history keeps the existing cooldown and does not receive Free credits until that cooldown ends. The regression does not mutate historical plan allowance or grant ledger rows.

### Revision 9 verification

- `SubscriptionCreditLifecycleTests` Release focused run: **19 passed, 0 failed**. Includes initial inactive/empty-plan sync, stable 25-credit Free period, no cooldown, repeat reconciliation without another grant, and a successful 1-credit reservation/commit.
- `SecondLaunch_ResolvesTheSameLocalUser` Release integration test: **1 passed** using the already-built integration assembly; it verifies the actual launch API provisions a single 25-credit Free period on the first launch and preserves it on the next launch.
- `ConcurrentFirstInactiveLaunchesAcrossIndependentConnections_GrantFreeCreditsOnce`: **skipped** by the PostgreSQL integration fixture because local PostgreSQL infrastructure was unavailable. The test is present for environments where the fixture can start PostgreSQL.
- Integration project rebuild could not complete because concurrent Feature 141 edits in `Feature141EngineVerticalSliceTests.cs` currently contain unrelated compile errors. A pre-existing in-memory concurrent launch test also fails because its EF InMemory store permits duplicate Billing accounts and concurrent auth permission seeding collides; this does not provide PostgreSQL race evidence.
- No production API calls, database migrations, deployments, commits or pushes were performed.
