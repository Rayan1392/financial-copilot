# Feature 139 — Implementation Report

Written for: engineers and operators who will review, test, and decide on rollout of the TahlilApp session exchange.

## 1. Final implementation status

**`PARTIAL`**

The Sapino-side launch path and native email/password journey are implemented and tested. The TahlilApp side is external and unverified, and launch-critical operational prerequisites remain open. Status `COMPLETE` is not claimed because production credential rotation, proxy topology, persistent key-ring operations, migration rollout, and the external round trip remain operator/integration work.

The integration handoff is documented in [`TahlilAppIntegrationContract.md`](TahlilAppIntegrationContract.md) and [`StagingReadinessChecklist.md`](StagingReadinessChecklist.md). The contract deliberately separates fields consumed today from permission/status extensions that require coordinated changes. The checklist is evidence-driven and remains open until staging owners execute it.

## 2. Completed and incomplete tasks

Full list with evidence is in `tasks.md`.

| Status | Tasks |
| --- | --- |
| Implemented and verified by tests | T-01, T-02, T-03, T-04, T-05 (with a restart-simulation gap), T-07, T-08, T-09, T-10, T-11, T-12, T-13, T-15, T-16, T-18, T-19, T-20, T-22, T-23, T-26, T-27, T-29, T-30 |
| Implemented, verification gap stated | T-25 (log-sink test not written), T-15 (JWT `amr` value not asserted) |
| Not implemented in this increment | T-14 (profile deletion on revoke) |
| Deferred with reason | T-17, T-24, T-28 |
| Operations (not done here) | T-06 (backup, restore drill, runbook, health self-check) |

## 3. Verified and unverified acceptance criteria

Counts are against the 87 ACs in `Design.md` §19 and §22.8.

**Verified by an automated test in this increment (42):** AC-05, AC-06, AC-08 (header sent; TahlilApp side external), AC-10, AC-11, AC-12, AC-13, AC-16, AC-17, AC-18, AC-20, AC-25, AC-26, AC-27, AC-28, AC-29, AC-30, AC-31, AC-35, AC-36, AC-37, AC-38, AC-39, AC-42, AC-56, AC-57, AC-58, AC-59, AC-60, AC-61, AC-62, AC-63, AC-64 (warning not asserted), AC-68, AC-69, AC-73, AC-78 (restart simulated), AC-80, AC-82, AC-83, AC-84, AC-86, AC-87.

**Verified by code review only (15):** AC-07, AC-09, AC-14, AC-15, AC-19, AC-23, AC-24, AC-40, AC-41, AC-43 (limiter configured; 429 path untested), AC-65, AC-66, AC-67, AC-72 (no log-sink test), AC-74, AC-85.

**Not implemented in this increment (deferred or not done):** AC-21, AC-22 (verified by inspection only; no code path shares a cookie), AC-70, AC-71 (admin PII), AC-75 (external status endpoint is not available in this repository), AC-76, AC-77 (profile deletion on revoke), AC-79, AC-81 (key-ring restore drill, operations). Native credential and parity criteria are now covered by the recovery integration suite; Sapino-side stale-grace and background revalidation code exists, but its real TahlilApp dependency remains unverified.

**External, not verifiable here:** AC-01–AC-04; the TahlilApp halves of AC-05, AC-06, AC-08. These are verified only against an in-test double. A real TahlilApp round trip has not been run.

## 4. Production files changed

Backend, modified:
- `FinancialCopilot.API/Program.cs` — registers `AddTahlilAppIdentity`.
- `FinancialCopilot.API/Controllers/AuthController.cs` — null-safe email in the response mapping only.
- `FinancialCopilot.API/Contracts/OwnedIdentityContracts.cs` — launch body, profile contracts.
- `FinancialCopilot.Infrastructure/Authentication/OwnedIdentityService.cs` — session `amr`, email-less JWT, `CreateSessionForLinkedUserAsync`, `PrepareBaselineAsync`, refresh carries the method.
- `FinancialCopilot.Infrastructure/Authentication/OwnedIdentityContracts.cs` — nullable profile email, two new interface members.
- `FinancialCopilot.Infrastructure/Authentication/Persistence/AuthDbContext.cs` — three new tables and the refresh column.
- `FinancialCopilot.Infrastructure/Authentication/Persistence/AuthPersistenceModels.cs` — `RefreshTokenRow.AuthenticationMethod`.
- `FinancialCopilot.Infrastructure/Billing/Persistence/PlanCapabilityService.cs` — expiry guard.
- `FinancialCopilot.Infrastructure/Billing/Persistence/BillingRepositories.cs` — expiry guard in `SubscriptionPlanRepository`.
- `FinancialCopilot.Infrastructure/FinancialCopilot.Infrastructure.csproj` — `Microsoft.AspNetCore.DataProtection` 10.0.4 (version already in the local package cache).

Backend, new:
- Domain: `Identity/TahlilApp/TahlilAppValueObjects.cs`, `TahlilAppSubscriptionPolicy.cs`, `ProfileMasking.cs`.
- Application: `Authentication/TahlilApp/TahlilAppLaunchPorts.cs`, `TahlilAppLaunchUseCase.cs`; `Authentication/Profile/OwnProfileUseCase.cs`.
- Billing: `Accounts/SubscriptionEffectiveness.cs`.
- Infrastructure: `Authentication/TahlilApp/` (options and validator, exchange parser, gateway, attempt store, account provisioner, profile protector, session issuer, composition root); `Authentication/Profile/OwnProfileStore.cs`; `Authentication/Persistence/TahlilAppPersistenceModels.cs`; `Billing/Persistence/ExternalSubscriptionSynchronizer.cs`.
- Migration: `Authentication/Persistence/Migrations/20261017135632_AddTahlilAppIdentityAndProfile.cs` (+ Designer), and the model snapshot.
- API: `Controllers/TahlilAppLaunchController.cs`, `Controllers/ProfileController.cs`.

Frontend:
- Modified: `src/integrations/financial-copilot/auth.ts` (one export, `completeTahlilAppLaunch`), `src/routeTree.gen.ts` (regenerated by `npm run build`).
- New: `src/routes/launch.tsx`, `src/routes/launch-failed.tsx`, `src/integrations/financial-copilot/launch.ts`, `__tests__/launch.test.ts`.

Tests, new: 5 unit files (`TahlilAppDomainTests`, `TahlilAppLaunchUseCaseTests`, `TahlilAppInfrastructureTests`, `TahlilAppBillingSyncTests`, `TahlilAppProfileProtectionTests`); 4 integration files (`TahlilAppLaunchIntegrationTests`, `TahlilAppLaunchDisabledIntegrationTests`, `OwnProfileIntegrationTests`, plus the factory in the launch file).

Spec documents: `Design.md` (Revision 4, §22), `tasks.md`, `user-story.md`, `design-review.md`, `tasks-review.md`, this report.

Not changed on purpose: the pre-existing uncommitted NADPCO normaliser and its test, and the two untracked 143 and 144 investigation notes that were present at session start.

## 5. Database migrations

`20261017135632_AddTahlilAppIdentityAndProfile` is additive:
- Creates `auth_external_identities` (unique `(Provider, ExternalUserId)` and `(Provider, LocalUserId)`).
- Creates `auth_tahlilapp_launch_attempts` (unique `CodeHash`, index on `CreatedAtUtc`).
- Creates `auth_user_profiles` (1:1 with `auth_users`; no index or unique constraint on PII columns).
- Adds a nullable `AuthenticationMethod` to `auth_refresh_tokens`.

No backfill. Verified by generation and by `EnsureCreated` in tests. **Not applied to any live database.** Apply it with the backup-first procedure in Feature 145's staging checklist.

## 6. API endpoints introduced

| Method and path | Auth | Behaviour |
| --- | --- | --- |
| `POST /api/auth/v1/tahlilapp/launch` | Anonymous, `PublicAuthentication` limit | Body `{ code }`. `200` session (sets refresh cookie); `401 launch-invalid`; `403 launch-account-disabled`; `503 launch-unavailable`; `404` when the feature is off. |
| `GET /api/auth/v1/profile` | JWT | Own profile. Names in full; national id and mobile masked; `Cache-Control: no-store`. |
| `POST /api/auth/v1/profile/reveal` | JWT, `AuthenticatedActor` limit | Body `{ field: "nationalId" \| "mobile" }`. Returns the caller's own value; `no-store`. |

The existing `/me` returns an empty `email` for email-less users. No other existing endpoint changed behaviour.

## 7. Frontend routes and components introduced

- `/launch` — reads and removes `?code=` before any request, exchanges it once, then `location.replace('/chat')` (constant target). Sends `no-referrer`. Does not redirect to `/auth`.
- `/launch-failed?reason=&correlationId=` — closed reason enum only; no automatic redirect; link to `/auth`. The "return to TahlilApp" button is not rendered because the return URL is server configuration with no frontend key.

## 8. Authentication and identity flow verification

Verified by integration tests on the real API pipeline (only the TahlilApp gateway is a double):
- First launch creates one local user with no email, no password, and an encrypted profile.
- Second launch with a new code resolves the same local user id.
- A replayed code is refused without a second exchange.
- A malformed code is refused without an exchange.
- An exchange outage returns `503` once, and the code is not retried.
- A suspended external user gets `403` and no cookie.
- A presented refresh cookie is revoked and a new one issued (`Replaced by TahlilApp launch.`).
- Five concurrent launches with one code produce one exchange and one success.
- The session token is accepted by `/me`, and an email-less session returns an empty email.

Not verified: the real TahlilApp service, the TahlilApp launch-code issuance, and the `amr` value inside the JWT.

## 9. Subscription and permissions mapping verification

- Mapping is in Domain (`TahlilAppSubscriptionPolicy`) and fails closed to Free for: unknown plan, inactive, missing expiry, expired, or a `Free` external plan. Tested with a matrix.
- An active paid plan with a future expiry is synced with that expiry as `EffectiveTo`.
- The Billing read path applies the expiry rule in `PlanCapabilityService` (both reads) and `SubscriptionPlanRepository`. A lapsed paid window reads as Free. Tested for capability loss; the repository change is not directly tested.
- External permission names never grant anything: `permissions[]` is counted, not interpreted.
- No plan, balance, or permission is written into the JWT as an authorisation source.

**Behaviour change for every account:** an admin-set subscription window that has already lapsed now reads as Free. This is the designed expiry behaviour, but it applies beyond TahlilApp users and should be communicated before release.

## 10. Credit allocation and idempotency verification

- Repeated identical syncs: no revision bump, no audit row, wallet balance unchanged (tested).
- Sync grants no credits. Included credits are seeded once when a wallet is first created, which is existing Billing behaviour and was verified by the billing review.
- Plan changes do not grant credits. A downgrade to Free clears the window and bumps the revision once (tested).
- Upgrades to a paid plan grant nothing extra. Whether paid activation should include credits is a Billing and product decision (Design Q5) and is not decided here.

**Approved catalog correction:** the canonical monthly AI-credit grants are Free `25`, Plus `260`, and Pro `2600`. The values are seeded in the billing model and migration `20261018092758_UpdateApprovedMonthlyAiCredits`. Existing wallets and usage ledgers are not reset; external subscription synchronization remains idempotent and grants no credits on sync or plan change. The migration has not been applied to any live database.

## 11. Security test results

- Replay, concurrency, malformed input, and expiry-related failures: tested (section 8).
- Gateway: no retry on any failure (tested for 5xx, 401, 403, 429, 3xx, timeout); no redirect followed; 8 KB body cap; content-type check; credential sent only as a header; no credential when unset.
- PII: stored encrypted (ciphertext is not the plaintext; last-4 stored); decryption fails closed to null; `ToString` overrides redact PII; logs carry field names and local user ids only. **The log-sink test (AC-72) is not written.**
- Key ring: startup fails when the feature is enabled and the ring is missing or not writable (tested); a fresh provider over the same ring decrypts (simulated restart, tested).
- Session fixation: presented refresh tokens are revoked on launch (tested).

**Security recovery findings and status:**
- **F-09 fixed in repository code:** forwarded headers are disabled by default, require explicit trusted proxies or non-global networks when enabled, bound the forwarding limit, and run before logging/authentication/rate limiting. Focused tests cover trusted, untrusted, forged, and multi-hop inputs. Deployment must still supply the correct proxy allow-list.
- **F-10 fixed in repository contents, operational rotation remains required:** committed JWT, database, SMTP, provider, worker, and Telegram credential values were removed from tracked runtime configuration and documentation; JWT startup validation now fails unless an issuer or a sufficiently long signing key is supplied externally. Operators must rotate any credentials that may have been used from earlier repository revisions and verify the deployed secret store.
- **F-11 fixed:** same-origin redirect validation rejects backslashes, protocol-relative paths, control/whitespace characters, and multi-layer encoded bypasses. Frontend regression tests cover the encoded cases.

## 12. Focused test results

| Suite | Filter | Result |
| --- | --- | --- |
| Backend unit | Feature 139/native/billing/security-focused filters | 161 passed, 0 failed |
| Backend integration | Feature 139/native/forwarded-header/owned-identity-focused filters | 52 passed, 0 failed |
| Frontend | auth, launch, and safe-redirect focused files with `--pool=threads` | 29 passed, 0 failed |

Frontend note: the default `forks` pool times out starting workers in this environment, and the pre-existing `auth.test.ts` fails the same way. The threads pool is used for this report.

## 13. Regression and build results

Latest full suites run sequentially before the final configuration-only secret scrub; the scrub and design-time connection-string change were followed by a clean Release build and did not alter test code paths:

| Suite | Result | Classification |
| --- | --- | --- |
| Unit (`FinancialCopilot.UnitTests`) | 2,297 passed, 19 failed, 2,316 total | Failures are in existing Feature 128/141/143 and semantic-routing/recognizer tests; none are in the recovery-focused filters. |
| Integration (`FinancialCopilot.IntegrationTests`) | 528 passed, 54 failed, 48 skipped, 630 total | Failures are concentrated in existing Feature 087/128/129/141 and data/admin scenarios; Docker/Testcontainers PostgreSQL cases were skipped because Docker is unavailable. Recovery-focused filters remained green. |
| Architecture (`FinancialCopilot.ArchitectureTests`) | 11 passed, 1 failed | `MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction`; unrelated to this recovery and unchanged by its files. |
| Frontend build (`npm run build`) | Exit 0 | Regenerated the route tree with `/launch` and `/launch-failed`. |
| Frontend focused tests | 29 passed, 0 failed | `safe-redirect`, `auth`, and `launch` tests pass with the threads pool. |
| Frontend full tests | 149 passed, 2 failed, 21 files | Two unrelated assertion failures (`disclosures`, `monthly-product-trend-chart`) plus one environment-only browser-script suite failure; no recovery-focused failure. |
| Backend Release build (`src/backend/FinancialCopilot.sln`) | Passed | 0 errors, 0 warnings |

Verification practice: two test runs in parallel collided on shared build outputs and produced truncated output. The reported runs are sequential.

These are not claims of a green suite. The pre-existing classification rests on test-name and failure-signature matches against the Feature 145 audit. I did not rerun that baseline in this session, so the classification is evidence-based but not re-established here.

## 14. External TahlilApp dependencies

- **Q1 contract confirmation:** paths, field names, the `permissions` shape, and error codes are a proposal. The gateway implements the proposal only.
- **Q2 service credential:** Sapino reads the key from an environment variable. TahlilApp must issue and restrict it.
- **Launch-code issuance (S-2) and exchange endpoint (S-3):** external and untested against the real service.
- **Batch status and webhook (Q4):** not required by the current Sapino worker, which performs bounded per-user status GETs. A batch endpoint remains an optional scale extension, not an implemented dependency.
- **Launch-code single use and atomic consumption:** TahlilApp's responsibility. Sapino's local replay guard adds a second barrier but cannot replace it.

## 15. Operational deployment prerequisites

Before enabling `Authentication:TahlilApp:Enabled` in any environment:
1. **Rotate and relocate committed credentials (F-10).**
2. **Persistent key ring (T-06):** configure `DataProtection:KeyRingPath` on a durable volume; run a backup and a restore drill; write the key-loss runbook. Keys must not be stored in the database.
3. **Forwarded headers (F-09, Q12):** the middleware is configured securely but disabled by default. Before enabling it, provide the exact trusted proxy/network allow-list and verify the reverse-proxy topology so the public limiter uses the real client IP.
4. **Same-site topology (Q9):** the refresh cookie is `SameSite=Strict`. The frontend and the API must be same-site.
5. **Legal basis and retention for PII (Q13):** a decision for Legal and Product before storing national id and mobile.
6. **Migration staging:** apply the migration to staging first, after a backup, following Feature 145's checklist.
7. **Non-production TahlilApp round trip:** exchange, error mapping, and TLS settings tested against a real staging TahlilApp.
8. **Return-URL decision:** whether `/launch-failed` should show a "return to TahlilApp" button, and the configuration key for it.

## 16. Remaining blockers and follow-ups

Blockers for enabling the feature in production: credential rotation, key-ring operations, migration staging, same-site/proxy configuration, legal review, the external launch/exchange/status contract, and the real staging round trip (sections 14–15 and the staging checklist).

Deferred Sapino work (documented in `Design.md` §22.5 and `tasks.md`):
- Switch-account confirmation in the frontend (T-17).
- Admin masked profile and audited reveal (T-24).
- Profile deletion on revoke (T-14). There is no revoke path yet.
- Explicit account linking (T-28).

Implemented Sapino-side refresh work still requiring staging evidence:
- Per-user status refresh, direct-login TTL and stale-grace downgrade (T-21, T-22); TahlilApp endpoint availability, contract confirmation and real round trip are open.

Verification gaps to close:
- Log-sink capture test for AC-72.
- A test that the JWT carries `amr: tahlilapp`.
- A direct test of the repository expiry guard.
- A real process-restart test for Data Protection.
- A 429 test for the launch endpoint's own limit.
- PostgreSQL concurrency for the provisioning race (this environment has no Docker).

Behaviour to decide and communicate:
- Admin-set lapsed windows now read as Free (section 9).
- Approved catalog: Free `25`, Plus `260`, Pro `2600`; the migration is not applied to live databases (section 10).
- Admin `SetSubscriptionAsync` accepts a null plan code, which leaves an account unassigned and skips capability gating (billing review F-14). Not changed here.

Error-handling deviation: a provisioning or billing-sync exception surfaces as HTTP 500 through the global handler, not as `launch-unavailable` (503). No session is created in that case, so the failure is fail-closed, but the status code differs from Design §14.

Design deviations recorded in `Design.md` §22.9: no row-version column on the two new identity tables; UTC `DateTime` for the replay-guard timestamps; the null-email direct insert.

---

Final status: **`PARTIAL`**.
