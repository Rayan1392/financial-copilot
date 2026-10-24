# Feature 139 â€” Implementation Tasks

Source of truth: `Design.md` Revision 6 (§22 remains authoritative for Revision 4 identity/integration reconciliation; §§23–24 and AC-88…AC-99 govern the credit lifecycle and activation contract). Slices S-1…S-14 follow `Design.md` §21. Ownership: **SapioAI** (this repository), **TahlilApp** (external, not verified here), **Operations** (secrets, proxy, key-ring backup, rollout).

Status legend (updated at the end of this increment, from test results):
- `[x]` implemented and verified by a passing test in this increment.
- `[~]` implemented, with a verification gap stated in the task.
- `[ ]` not implemented in this increment.
- `[D]` deferred with a reason (see `ImplementationReport.md`).

Focused verification for this recovery includes the Feature 139 slices, native email/password journeys, forwarded-header security, approved billing catalog, and the frontend redirect regression. Exact final counts are recorded in `ImplementationReport.md` after the full regression runs.

Integration handoff artifacts: [`TahlilAppIntegrationContract.md`](TahlilAppIntegrationContract.md) and [`StagingReadinessChecklist.md`](StagingReadinessChecklist.md). They describe the implemented SapioAI behavior, the unverified TahlilApp contract, and the remaining staging/operations gates. No checklist item is considered complete without evidence.

## S-1 Foundation and contracts

- [x] **T-01** Application ports, records and launch use case (`FinancialCopilot.Application/Authentication/TahlilApp/`). Gateway, attempt-store, provisioner, subscription-synchroniser and session-issuer ports; launch outcome enum; malformed codes rejected before the replay guard. ACs: AC-07, AC-09, AC-24, AC-41, AC-56. Tests: `TahlilAppLaunchUseCaseTests` (fakes only, 12 facts and theories). Owner: SapioAI.
- [x] **T-02** Domain value objects and policies (`FinancialCopilot.Domain/Identity/TahlilApp/`): `ExternalUserId`, `IranianNationalId` (Persian and Arabic digit normalisation), `IranianMobile` (E.164), `PersonName`, `TahlilAppSubscriptionPolicy` (fails closed to Free), `ProfileFieldMergePolicy`, `ProfileMasking`. ACs: AC-29, AC-57â€“AC-65, AC-86, AC-37. Tests: `TahlilAppDomainTests`. Owner: SapioAI.
- [x] **T-03** Options with startup validation (`Authentication:TahlilApp`): HTTPS base URL, bounded timeouts, canonical plan targets, environment-variable name for the credential, response size bound, writable key ring when enabled. ACs: AC-42, AC-80. Tests: `TahlilAppOptionsValidatorTests`. Owner: SapioAI.
- [~] **T-04** Architecture: Application still references only Domain; 11 of 12 architecture tests pass. The failing `MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction` assertion inspects an unrelated semantic executor source, but no clean baseline was rerun here, so it is not classified as pre-existing. ACs: AC-56. Owner: SapioAI.

## S-5 Data Protection key ring

- [x] **T-05** `AddDataProtection` with fixed application name `FinancialCopilot` and persistent key ring (`DataProtection:KeyRingPath`). Startup fails when the feature is enabled and the path is missing or not writable. ACs: AC-78, AC-80. Tests: `TahlilAppOptionsValidatorTests.EnabledRejectsNonWritableKeyRing`; `TahlilAppProfileProtectionTests.Ciphertext_DecryptsWithAFreshProviderOverTheSameKeyRing` (a fresh provider over the same ring decrypts). Gap: the test simulates restart with a second provider, not a real process restart. Owner: SapioAI.
- [ ] **T-06** Key-ring backup and restore scripts, `/health` protect/unprotect self-check, key-loss runbook, restore drill. ACs: AC-79, AC-81. Owner: **Operations**. Not done here: requires the production volume and a drill.

## S-4 Gateway and launch endpoint

- [x] **T-07** Typed gateway `TahlilAppIdentityGateway`: dedicated `HttpClient` with **no resilience handler and no retry**, `AllowAutoRedirect = false`, HTTPS base URL, per-call timeout, credential from an environment variable sent as `X-Api-Key`, `X-Correlation-Id`, JSON content-type check, 8 KB body cap, schema validation through the Domain value objects, unknown fields ignored, `permissions[]` counted only. ACs: AC-08, AC-25, AC-26, AC-85. Tests: `TahlilAppIdentityGatewayTests` (11 facts and theories: success with headers, 400 â†’ invalid, 5xx / 401 / 403 / 429 / 3xx â†’ unavailable and sent once, wrong content type, oversized body, missing credential sends nothing, timeout sent once). Owner: SapioAI.
- [x] **T-08** Replay guard `auth_tahlilapp_launch_attempts` (unique SHA-256 code digest, inserted before the exchange, outcome recorded after, 24 h purge). ACs: AC-05, AC-19, AC-82, AC-83. Tests: `TahlilAppLaunchIntegrationTests.ReplayedCode_IsRejectedAndNeverExchangedTwice`, `TahlilAppUnavailable_â€¦_AndTheCodeIsNotRetried`, `ConcurrentLaunches_WithOneCode_ExchangeOnceAndCreateOneUser` (5 parallel requests, one exchange, one success). Owner: SapioAI.
- [x] **T-09** `POST /api/auth/v1/tahlilapp/launch`: anonymous, `PublicAuthentication` rate-limit policy, feature flag returns 404 when off, ProblemDetails `launch-invalid` (401), `launch-unavailable` (503), `launch-account-disabled` (403), no session on failure, presented refresh cookie revoked. ACs: AC-16â€“AC-18, AC-20, AC-31, AC-39, AC-43, AC-84. Tests: `TahlilAppLaunchDisabledIntegrationTests` (404), `TahlilAppLaunchIntegrationTests` (invalid, unavailable, suspended, revocation). A dedicated low-limit integration host verifies the launch endpoint returns 429 after the configured permit limit. Owner: SapioAI.

## S-6 Identity and profile persistence

- [x] **T-10** EF migration `20261017135632_AddTahlilAppIdentityAndProfile` (additive): `auth_external_identities` (unique `(Provider, ExternalUserId)` and `(Provider, LocalUserId)`), `auth_tahlilapp_launch_attempts` (unique `CodeHash`), `auth_user_profiles` (1:1, no PII index), nullable `AuthenticationMethod` on `auth_refresh_tokens`. Applied to the SQLite test database through `EnsureCreated`; **not applied to any live database.** Deviation: no row-version column on the two new tables (Design Â§22.9 I-2). Owner: SapioAI.
- [x] **T-11** Provisioner `TahlilAppAccountProvisioner`: find-or-create keyed by `(TahlilApp, ExternalUserId)`; unique-index race resolved by rollback and re-read; Suspended or disabled local accounts denied. Null email handled by a direct insert, proven necessary (Design Â§22.9 I-1). ACs: AC-10, AC-11, AC-27â€“AC-31. Tests: first launch creates one user with no email; second launch resolves the same user; concurrent identical-code launches create one user; concurrent different codes for one external identity also resolve to one local user. Revoked identities are denied and profile deletion is covered by T-14. Owner: SapioAI.
- [x] **T-12** Profile merge and encryption: `ProfileFieldMergePolicy` (overwrite valid changed, no-op identical, keep stored on absent, null or invalid) and Data Protection encryption of `NationalId` and `Mobile` with last-4 columns. ACs: AC-57â€“AC-64, AC-66. Tests: `TahlilAppDomainTests` (merge matrix); `TahlilAppLaunchIntegrationTests.FirstLaunch_â€¦EncryptsPiiâ€¦` (stored value is ciphertext); `ReturningLaunch_KeepsAbsentFields_AndOverwritesAChangedValidValue`. Owner: SapioAI.
- [x] **T-13** Null-email tolerance: JWT omits the `email` claim when absent, `OwnedIdentityProfile.Email` is nullable, `/me` returns an empty email. ACs: AC-30, AC-73. Tests: `TahlilAppLaunchIntegrationTests.EmailLessSession_CanReadMe_WithAnEmptyEmail`. Owner: SapioAI.
- [x] **T-14** Lifecycle: a revoked upstream identity is retained as a denial tombstone while its encrypted profile row is deleted in the same Auth save; local-user deletion cascades to external identity and profile rows. Integration test RevokedIdentity_DeletesProfile_AndDeletingLocalUserCascadesProfile. AC-77. Owner: SapioAI.

## S-7 Session and frontend

- [x] **T-15** Session issuance shares password-login identity/tenant/permissions; refresh rows store amr (tahlilapp or pwd) and rotation preserves it. Integration tests assert the amr=tahlilapp JWT claim and refresh-row method. ACs: AC-13, AC-39, AC-50. Owner: SapioAI.
- [x] **T-16** `/launch` scrubs the code before requests, uses a constant `/chat` target and exchanges once. `/launch-failed` has a closed reason enum, no automatic redirect, and renders the validated optional `VITE_TAHLILAPP_RETURN_URL` link. `.env.example` documents the setting. ACs: AC-14, AC-15, AC-18, AC-20, AC-23, AC-40, AC-41. Frontend launch/return-url tests and build pass. Owner: SapioAI.
- [x] **T-17** Before exchange, /launch checks for an existing SapioAI session and asks the user to continue and replace it or remain in SapioAI. Confirmation proceeds with the one-time exchange; cancel returns to /chat. Existing refresh sessions are still replaced server-side. ACs: AC-39, AC-84. Owner: SapioAI.

## S-8 Subscription synchronisation (Billing)

- [~] **T-18** `ExternalSubscriptionSynchronizer`: applies TahlilApp external subscription revisions, uses only authoritative `effectiveFrom` for paid period transitions, and leaves missing-anchor legacy eligibility without paid grants. Free→paid forfeiture and approved Plus→Pro transfer are audited and keyed. ACs: AC-12, AC-36, AC-38, AC-88…AC-99. Tests: TahlilApp sync and credit lifecycle suites pass; PostgreSQL race test is present but skipped because Docker is unavailable. Owner: SapioAI/TahlilApp.
- [x] **T-19** Read-side expiry guard applies in PlanCapabilityService and SubscriptionPlanRepository; direct repository test now asserts an expired Plus account resolves to Free and Free allowance. Capability loss test also passes. ACs: AC-35, AC-87. This behavior affects any account with a lapsed admin-set window. Owner: SapioAI.
- [x] **T-20** Launch-time subscription sync: exchange â†’ policy â†’ synchroniser, with unknown plan, inactive, expired and missing-expiry all becoming Free. ACs: AC-12, AC-36, AC-37, AC-86. Tests: `TahlilAppDomainTests.Subscription_*` (fail-closed matrix); `SubscriptionSync_IsIdempotent_AndGrantsNoCredits`; `ExpiredPaidSubscription_IsSyncedToFree`. Owner: SapioAI.
- [~] **T-21** Background entitlement revalidation worker and per-user status refresh. SapioAI claims due identities under a lease and performs one bounded `GET /api/financial-copilot/users/{externalUserId}/status` per identity; a TahlilApp batch endpoint is not a current dependency. ACs: AC-34, AC-35. Verification gap: the external endpoint, SLA and real staging round trip are unverified. Owner: SapioAI/TahlilApp/Operations.
- [x] **T-22** Direct-login TTL refresh and stale-grace downgrade on login. Implemented through the shared entitlement refresher: successful status is due again after the configured 15-minute TTL; unavailable status preserves the last-known paid state through the configured six-hour stale grace, then synchronizes Free. ACs: AC-32, AC-33, AC-35, AC-76. Verification gap: real TahlilApp status behavior and staging evidence remain open. Owner: SapioAI.

## S-9 Profile views

- [x] **T-23** Self-only `GET /api/auth/v1/profile` (names in full, `NationalId` and `Mobile` masked, `ProfileSyncedAtUtc`, `Cache-Control: no-store`) and `POST /api/auth/v1/profile/reveal` (`nationalId` or `mobile` only, the caller's `sub` only, no admin permission, `no-store`, field-name-only log). ACs: AC-68, AC-69, AC-74. Tests: `OwnProfileIntegrationTests` (6 cases: auth required, names in full with masked identifiers, reveal own values, unknown field â†’ 400, absent field â†’ 404, reveal auth required). The endpoints accept no user id, so another user cannot be addressed. Owner: SapioAI.
- [D] **T-24** Admin masked profile views and audited reveal remain deferred and are not required for Phase 1: no admin profile UI exists and cross-user PII reveal needs separate review. Per Design Â§22.5, the internal admin.users.pii.read permission is registered and seeded, but is not assigned to any default role or used by an endpoint. Owner: SapioAI.

## S-10 Observability and privacy

- [x] **T-25** PII redaction is verified with a capturing log provider on a real API launch containing unique first name, last name, national ID and mobile values; none appear in captured application log messages. Domain redaction tests remain. AC-72. Owner: SapioAI.

## S-11 / S-12 / S-13 Native credentials, parity, linking

- [x] **T-26** Native email enrollment (`email_enrollment` purpose, `PendingEmail` column, `POST credentials/email`, confirm, password set). Implemented through the existing Identity email-token flow; new accounts require verified email, and enrollment does not merge accounts. ACs: AC-44â€“AC-47, AC-51, AC-54. Verified by `NativeEnrollmentIntegrationTests` for enrollment, confirmation, cooldown, generic duplicate handling, races, token replay/expiry, and password setup.
- [x] **T-27** Direct-login parity and `/me` extension. Native login resolves the same local user created by TahlilApp launch; `/me`, refresh, password reset, and account-state behavior are covered by `NativeEnrollmentIntegrationTests` and the launch/auth integration scenarios. ACs: AC-48â€“AC-53, AC-55. Access tokens are short-lived and entitlement state remains server-side.
- [D] **T-28** Explicit account linking/unlinking remains deferred after Phase 1 and is not required for TahlilApp launch. Existing native and TahlilApp identities remain separate; support handles reconciliation under the approved policy. Owner: SapioAI.

## S-14 Integration and regression

- [x] **T-29** Feature 139 launch/auth/Billing focused coverage passes: combined TahlilApp/native-enrollment filter 36 integration tests, including launch and repeated native login resolving the same user without increasing the wallet grant; Feature 139 unit/security tests were previously 135/135 and the new Billing lifecycle filter is 48/48. Coverage includes revoked-profile deletion, actual log-sink PII checks, `amr`, low-limit 429, expiry, launch replay/concurrency and login parity. The TahlilApp service remains a test double; this increment did not re-run the full integration suite. Owner: SapioAI.
- [~] **T-30** Full sequential regression and Release build results are recorded in `ImplementationReport.md`. The current full unit run is 2,312 passed/23 failed/0 skipped; its 23 failed test names exactly match the earlier pre-credit-lifecycle TRX. Prior full integration evidence remains 518 passed/69 failed/48 skipped before a focused correction of five test-only JWT configuration failures; architecture had 1 failure; frontend had 156 passed/2 failed. This incrementâ€™s Feature139/Billing integration filters passed. Backend Release solution build succeeds with 0 warnings/errors. Owner: SapioAI.

## Documentation

- [x] **T-31** Updated `Design.md`, `user-story.md`, `tasks.md` (this file), `TahlilAppIntegrationContract.md`, `StagingReadinessChecklist.md`, `ImplementationReport.md` and Billing's `subscription-credit-periods.md` with the Revision 5 lifecycle policy, evidence and unresolved gates. Owner: SapioAI.

## S-15 Approved monthly credit lifecycle

- [~] **T-32** Reuse the existing wallet and append-only ledger for 30-day Free/Plus/Pro allocation, expiry/no rollover, Free→paid forfeiture, Plus→Pro remainder transfer plus 2600 Pro credits, and idempotent worker grants. The new forward-only migration adds only period/metadata fields and preserves existing balances. ACs: AC-88…AC-99. Verification gap: independent-connection PostgreSQL concurrency test is skipped because Docker is not running; production reconciliation remains open, and this work did not apply the migration. Database applied state is unverified.

## T-33 â€” Authoritative subscription activation contract and revision ordering

- [~] SapioAI sends contract version 2 on launch exchange and status requests; parses `subscription.effectiveFrom` as UTC; rejects invalid/future/post-expiry credit anchors; persists authoritative timestamps and external subscription revisions; and ignores stale/conflicting revisions. Legacy payloads remain readable for eligibility, but cannot create paid grants or use SapioAI observation time as an anchor. ACs: AC-96, AC-98, AC-99. Tests: policy/parser/synchronizer suites and PostgreSQL concurrent-upgrade integration test. Verification gap: TahlilApp has not confirmed or exercised version 2 in staging. Owner: SapioAI/TahlilApp.
