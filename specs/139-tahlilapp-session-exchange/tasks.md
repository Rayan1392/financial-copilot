# Feature 139 — Implementation Tasks

Source of truth: `Design.md` Revision 4 (§22 authoritative; §22.9–22.10 record findings and remaining work). Slices S-1…S-14 follow `Design.md` §21. Ownership: **Sapino** (this repository), **TahlilApp** (external, not verified here), **Operations** (secrets, proxy, key-ring backup, rollout).

Status legend (updated at the end of this increment, from test results):
- `[x]` implemented and verified by a passing test in this increment.
- `[~]` implemented, with a verification gap stated in the task.
- `[ ]` not implemented in this increment.
- `[D]` deferred with a reason (see `ImplementationReport.md`).

Test commands used for verification: `dotnet test` on `FinancialCopilot.UnitTests` (filter `TahlilApp`: 78 passed), `FinancialCopilot.IntegrationTests` (filter `TahlilAppLaunch|OwnProfile`: 20 passed), `npx vitest run --pool=threads` on the two frontend files (10 passed), and `npm run build` (exit 0).

## S-1 Foundation and contracts

- [x] **T-01** Application ports, records and launch use case (`FinancialCopilot.Application/Authentication/TahlilApp/`). Gateway, attempt-store, provisioner, subscription-synchroniser and session-issuer ports; launch outcome enum; malformed codes rejected before the replay guard. ACs: AC-07, AC-09, AC-24, AC-41, AC-56. Tests: `TahlilAppLaunchUseCaseTests` (fakes only, 12 facts and theories). Owner: Sapino.
- [x] **T-02** Domain value objects and policies (`FinancialCopilot.Domain/Identity/TahlilApp/`): `ExternalUserId`, `IranianNationalId` (Persian and Arabic digit normalisation), `IranianMobile` (E.164), `PersonName`, `TahlilAppSubscriptionPolicy` (fails closed to Free), `ProfileFieldMergePolicy`, `ProfileMasking`. ACs: AC-29, AC-57–AC-65, AC-86, AC-37. Tests: `TahlilAppDomainTests`. Owner: Sapino.
- [x] **T-03** Options with startup validation (`Authentication:TahlilApp`): HTTPS base URL, bounded timeouts, canonical plan targets, environment-variable name for the credential, response size bound, writable key ring when enabled. ACs: AC-42, AC-80. Tests: `TahlilAppOptionsValidatorTests`. Owner: Sapino.
- [x] **T-04** Architecture: Application still references only Domain. Architecture suite: 11 of 12 pass; the one failure (`MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction`) is pre-existing per the Feature 145 baseline and is unrelated to this change. ACs: AC-56. Owner: Sapino.

## S-5 Data Protection key ring

- [x] **T-05** `AddDataProtection` with fixed application name `FinancialCopilot` and persistent key ring (`DataProtection:KeyRingPath`). Startup fails when the feature is enabled and the path is missing or not writable. ACs: AC-78, AC-80. Tests: `TahlilAppOptionsValidatorTests.EnabledRejectsNonWritableKeyRing`; `TahlilAppProfileProtectionTests.Ciphertext_DecryptsWithAFreshProviderOverTheSameKeyRing` (a fresh provider over the same ring decrypts). Gap: the test simulates restart with a second provider, not a real process restart. Owner: Sapino.
- [ ] **T-06** Key-ring backup and restore scripts, `/health` protect/unprotect self-check, key-loss runbook, restore drill. ACs: AC-79, AC-81. Owner: **Operations**. Not done here: requires the production volume and a drill.

## S-4 Gateway and launch endpoint

- [x] **T-07** Typed gateway `TahlilAppIdentityGateway`: dedicated `HttpClient` with **no resilience handler and no retry**, `AllowAutoRedirect = false`, HTTPS base URL, per-call timeout, credential from an environment variable sent as `X-Api-Key`, `X-Correlation-Id`, JSON content-type check, 8 KB body cap, schema validation through the Domain value objects, unknown fields ignored, `permissions[]` counted only. ACs: AC-08, AC-25, AC-26, AC-85. Tests: `TahlilAppIdentityGatewayTests` (11 facts and theories: success with headers, 400 → invalid, 5xx / 401 / 403 / 429 / 3xx → unavailable and sent once, wrong content type, oversized body, missing credential sends nothing, timeout sent once). Owner: Sapino.
- [x] **T-08** Replay guard `auth_tahlilapp_launch_attempts` (unique SHA-256 code digest, inserted before the exchange, outcome recorded after, 24 h purge). ACs: AC-05, AC-82, AC-83. Tests: `TahlilAppLaunchIntegrationTests.ReplayedCode_IsRejectedAndNeverExchangedTwice`, `TahlilAppUnavailable_…_AndTheCodeIsNotRetried`, `ConcurrentLaunches_WithOneCode_ExchangeOnceAndCreateOneUser` (5 parallel requests, one exchange, one success). Owner: Sapino.
- [x] **T-09** `POST /api/auth/v1/tahlilapp/launch`: anonymous, `PublicAuthentication` rate-limit policy, feature flag returns 404 when off, ProblemDetails `launch-invalid` (401), `launch-unavailable` (503), `launch-account-disabled` (403), no session on failure, presented refresh cookie revoked. ACs: AC-16–AC-18, AC-20, AC-31, AC-39, AC-43, AC-84. Tests: `TahlilAppLaunchDisabledIntegrationTests` (404), `TahlilAppLaunchIntegrationTests` (invalid, unavailable, suspended, revocation). Gap: the 429 path is configured but not directly tested (the test host raises the limit to 1000). Owner: Sapino.

## S-6 Identity and profile persistence

- [x] **T-10** EF migration `20261017135632_AddTahlilAppIdentityAndProfile` (additive): `auth_external_identities` (unique `(Provider, ExternalUserId)` and `(Provider, LocalUserId)`), `auth_tahlilapp_launch_attempts` (unique `CodeHash`), `auth_user_profiles` (1:1, no PII index), nullable `AuthenticationMethod` on `auth_refresh_tokens`. Applied to the SQLite test database through `EnsureCreated`; **not applied to any live database.** Deviation: no row-version column on the two new tables (Design §22.9 I-2). Owner: Sapino.
- [x] **T-11** Provisioner `TahlilAppAccountProvisioner`: find-or-create keyed by `(TahlilApp, ExternalUserId)`; unique-index race resolved by rollback and re-read; Suspended or disabled local accounts denied. Null email handled by a direct insert, proven necessary (Design §22.9 I-1). ACs: AC-10, AC-11, AC-27–AC-31. Tests: first launch creates one user with no email; second launch resolves the same user; concurrent identical launches create one user. Gaps: `Revoked` status and different-code concurrent first launch are not directly tested (same code path). Owner: Sapino.
- [x] **T-12** Profile merge and encryption: `ProfileFieldMergePolicy` (overwrite valid changed, no-op identical, keep stored on absent, null or invalid) and Data Protection encryption of `NationalId` and `Mobile` with last-4 columns. ACs: AC-57–AC-64, AC-66. Tests: `TahlilAppDomainTests` (merge matrix); `TahlilAppLaunchIntegrationTests.FirstLaunch_…EncryptsPii…` (stored value is ciphertext); `ReturningLaunch_KeepsAbsentFields_AndOverwritesAChangedValidValue`. Owner: Sapino.
- [x] **T-13** Null-email tolerance: JWT omits the `email` claim when absent, `OwnedIdentityProfile.Email` is nullable, `/me` returns an empty email. ACs: AC-30, AC-73. Tests: `TahlilAppLaunchIntegrationTests.EmailLessSession_CanReadMe_WithAnEmptyEmail`. Owner: Sapino.
- [ ] **T-14** Lifecycle: delete the profile when the local account is deleted or the TahlilApp identity is revoked (AC-77). **Not implemented**: no revoke path exists in this increment. Owner: Sapino.

## S-7 Session and frontend

- [x] **T-15** `CreateSessionForLinkedUserAsync` shares the session path with password login. Refresh rows store `AuthenticationMethod` (`tahlilapp` or `pwd`) and refresh copies it. Each launch issues a new refresh token. ACs: AC-13, AC-39, AC-50. Tests: `TahlilAppLaunchIntegrationTests.Launch_RevokesAPresentedRefreshToken_AndIssuesANewOne` (asserts the stored method). Gap: the `amr` claim value in the JWT is not asserted in a test. Owner: Sapino.
- [x] **T-16** Frontend: `/launch` (scrubs `?code=` with `history.replaceState` before any request, `no-referrer` meta, POST with `credentials: include`, stores the access token through the shared session path, `location.replace('/chat')` as a constant, StrictMode-safe single exchange) and `/launch-failed` (closed reason enum, correlation id shown as text, no automatic redirect, link to `/auth`). ACs: AC-14, AC-15, AC-40, AC-41. Tests: `launch.test.ts` (7 cases: pure helpers and the request contract); `npm run build` exit 0 regenerated `routeTree.gen.ts`. Gaps: no rendered-page test; the "return to TahlilApp" button is not rendered (Design §22.10). Owner: Sapino.
- [D] **T-17** Switch-account confirmation UI when an existing session belongs to another user. Deferred: the backend revokes the presented refresh cookie (T-09); the UI needs a `/me`-based session check. Residual risk: login-CSRF bounded by TTL and single use (Design §13.2). Owner: Sapino.

## S-8 Subscription synchronisation (Billing)

- [x] **T-18** `ExternalSubscriptionSynchronizer`: idempotent (identical plan and window is a no-op: no revision bump, no audit), revision-checked (one retry on concurrency), audit `billing.subscription.synced`, **grants no credits**. ACs: AC-12, AC-36, AC-38. Tests: `TahlilAppBillingSyncTests.Sync_IsIdempotent_AndGrantsNoCredits`, `Sync_DowngradeToFree_ClearsWindowAndBumpsRevisionOnce`; `ExpiredPaidSubscription_IsSyncedToFree`. Owner: Sapino.
- [x] **T-19** Read-side expiry guard: `SubscriptionEffectiveness.ResolveEffectivePlanCode` (lapsed window reads as Free) applied in `PlanCapabilityService` (both reads) and `SubscriptionPlanRepository`. ACs: AC-35, AC-87. Tests: `TahlilAppBillingSyncTests.ExpiryRule_*`, `CapabilityCheck_LapsedPaidAccount_LosesPaidCapability`. Gap: the repository guard is not tested directly. **Behaviour change for all accounts:** an admin-set window that has already lapsed now reads as Free; this is intended and listed in the implementation report. Owner: Sapino.
- [x] **T-20** Launch-time subscription sync: exchange → policy → synchroniser, with unknown plan, inactive, expired and missing-expiry all becoming Free. ACs: AC-12, AC-36, AC-37, AC-86. Tests: `TahlilAppDomainTests.Subscription_*` (fail-closed matrix); `SubscriptionSync_IsIdempotent_AndGrantsNoCredits`; `ExpiredPaidSubscription_IsSyncedToFree`. Owner: Sapino.
- [D] **T-21** Background revalidation worker and batch status. Deferred: requires the TahlilApp batch endpoint (Q4). Owner: TahlilApp contract and Sapino.
- [D] **T-22** Direct-login TTL refresh and stale-grace downgrade on login. Deferred. The stale-grace rule (Design §11) is **not implemented** in this increment. Owner: Sapino.

## S-9 Profile views

- [x] **T-23** Self-only `GET /api/auth/v1/profile` (names in full, `NationalId` and `Mobile` masked, `ProfileSyncedAtUtc`, `Cache-Control: no-store`) and `POST /api/auth/v1/profile/reveal` (`nationalId` or `mobile` only, the caller's `sub` only, no admin permission, `no-store`, field-name-only log). ACs: AC-68, AC-69, AC-74. Tests: `OwnProfileIntegrationTests` (6 cases: auth required, names in full with masked identifiers, reveal own values, unknown field → 400, absent field → 404, reveal auth required). The endpoints accept no user id, so another user cannot be addressed. Owner: Sapino.
- [D] **T-24** Admin masked views and audited reveal (`admin.users.pii.read`). Deferred: no admin UI and the audited reveal needs its own review. The permission constant is **not** added in this increment. ACs: AC-70, AC-71. Owner: Sapino.

## S-10 Observability and privacy

- [~] **T-25** PII redaction: `ToString()` overrides on `PersonName`, `IranianNationalId`, `IranianMobile`, `IncomingProfileField` and `TahlilAppExchangeSnapshot`; logs carry field names, reason codes and `LocalUserId` only. Tests: `TahlilAppDomainTests.PersonalIdentifiers_AreRedactedWhenConvertedToString`. Gap: the log-sink capture test (AC-72) is **not written**, so log safety is by code review, not by test. Owner: Sapino.

## S-11 / S-12 / S-13 Native credentials, parity, linking

- [D] **T-26** Native email enrollment (`email_enrollment` purpose, `PendingEmail` column, `POST credentials/email`, confirm, password set). Deferred. Designed in Design §22.6 Diagram 3 and D1, reusing the Feature 145 token mechanism. The increment does **not** provide native login for TahlilApp users.
- [D] **T-27** Direct-login parity and `/me` extension. Deferred with T-22.
- [D] **T-28** Explicit account linking and admin unlink. Deferred post-release (Q6).

## S-14 Integration and regression

- [x] **T-29** Integration scenarios for the implemented paths (`TahlilAppLaunchIntegrationTests`, 13 cases): disabled → 404, invalid, replay, malformed (no exchange), unavailable (one exchange across two attempts), suspended (no cookie), first launch (one user, no email, encrypted PII), second launch (same user), returning launch merge, refresh revocation, email-less `/me`, subscription idempotency, expiry to Free, concurrent identical launches. The TahlilApp test double lives only in the test project; the production gateway is never replaced except in tests. Owner: Sapino.
- [x] **T-30** Regression against the Feature 145 baseline (see `ImplementationReport.md` §13): unit 2,236 passed / 18 failed (all 18 on the pre-existing list); integration 504 passed / 54 failed / 48 skipped (53 pre-existing, 1 documented intermittent 429). The existing auth, billing and admin integration tests are among the passing set.

## Documentation

- [x] **T-31** `user-story.md`, `tasks.md` (this file), `design-review.md`, `tasks-review.md` and `ImplementationReport.md`. Owner: Sapino.
