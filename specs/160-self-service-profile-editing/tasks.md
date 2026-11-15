# Tasks: Self-Service Profile Editing

Status: Gate B: Approved by the owner on 2026-11-14

## Feature Folder

`160-self-service-profile-editing`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner on 2026-11-14.
- [x] No prerequisite feature is outstanding; current profile/email/mobile-PWA integration points are implemented. Separate Feature 139 billing and deployment rollout follow-ups are outside this feature's dependency scope.
- [x] MelliPayamak response contract assumption recorded: `code` is the OTP and non-empty `status` describes send failure; missing code, provider error status, or unsuccessful HTTP response is a safe failure.
- [x] PostgreSQL Auth profile row selected for normalized avatar storage, with the approved 1 MiB cap.
- [x] Read this feature's design and story, Feature 139/145/153 references, API architecture docs, and `.claude/rules/*`.

## Expected Areas

- Backend API: `src/backend/FinancialCopilot.API/Controllers/ProfileController.cs`, `AuthController.cs`, and `Contracts/OwnedIdentityContracts.cs`.
- Application/Identity: `src/backend/FinancialCopilot.Application/Authentication/Profile/`, `src/backend/FinancialCopilot.Infrastructure/Authentication/OwnedIdentityService.cs`, `OwnedIdentityContracts.cs`, and dependency registration.
- Persistence: `src/backend/FinancialCopilot.Infrastructure/Authentication/Persistence/AuthDbContext.cs`, `AuthPersistenceModels.cs`, their entity configurations, and a new Auth migration.
- Frontend: `src/frontend/src/routes/_app.profile.tsx`, `m.account.profile.tsx`, shared profile components/integration helpers under `src/frontend/src/components/app/` and `src/frontend/src/integrations/financial-copilot/`.
- Operations/docs: API `appsettings.json`, root `.env.example`, `docker-compose.yml`, and the applicable README/deployment notes.
- Tests: corresponding Identity/profile unit and integration test projects and frontend component tests.

## Implementation Tasks

### 1. Backend and persistence

- [x] Finalize API contract for local profile read/update, email change/confirmation, current-password change, mobile-code request/confirmation, and avatar upload/delete/read.
- [x] Add Auth-owned local profile fields for contact mobile verification and normalized avatar storage, keeping TahlilApp-sourced fields untouched; add EF Core migration and indexes.
- [x] Implement self-scoped use cases and validation for local names, email pending-confirmation semantics, password change, local mobile verification, and avatar lifecycle.
- [x] Implement hashed, purpose-bound, one-use SMS verification token lifecycle with expiry, cooldown, attempt limits, concurrency-safe consumption, and redacted audit logging.
- [x] Implement `ISmsOtpSender` and MelliPayamak adapter for `POST /api/send/otp/{token}` with `{ to }` JSON payload; enforce HTTPS, bounded timeout, safe response/error mapping, and redaction of the token-bearing request URI.
- [x] Add typed `Authentication:Sms` options and startup validation for enabled/disabled configuration; bind the endpoint token from environment/secrets only.
- [x] Implement image byte/content limits, raster decoding, normalization, metadata stripping, delete, and retrieval behavior.
- [x] Revoke refresh sessions after password change and preserve the documented re-authentication behavior.

### 2. Frontend

- [x] Add shared API types/client functions and profile refresh behavior.
- [x] Build accessible Persian RTL edit forms and verification states for name, email, password, mobile, and avatar.
- [x] Integrate desktop `/_app/profile` and mobile `/m/account/profile`, preserving their existing layout/navigation.
- [x] Handle loading, validation, stale/duplicate contact, expired/reused code, resend cooldown, upload errors, and provider outage states.

### 3. Configuration, operations, and docs

- [x] Add non-secret SMS defaults/options to `src/backend/FinancialCopilot.API/appsettings.json`.
- [x] Add documented environment placeholders, including an empty endpoint-token variable, to root `.env.example` only; never add the supplied token.
- [x] Map each runtime SMS setting through API service environment in `docker-compose.yml`; keep `.env` untracked and secrets external.
- [x] Document local/deployment configuration, enabling/disabling SMS, required MelliPayamak account values, and migration/deployment order in `README.md` or the approved operational doc.
- [x] Update `specs/README.md`, add a Persian user-facing line under `CHANGELOG.md` `## [Unreleased]`, and record remaining deployment prerequisites.

## Tests

- [ ] Unit/integration coverage for authorization, email/mobile token lifecycle, rate limits, password/session changes, image validation, and provider failure behavior.
- [ ] Use a fake SMS sender; no live-provider calls in automated checks.
- [ ] Frontend component coverage for desktop/mobile form behavior and all verification/error states.
- [ ] Configuration binding/startup-validation coverage and migration/model consistency checks.

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` passes with no new failure beyond `specs/test-baseline.md`.
- [x] For database work, `dotnet ef migrations has-pending-model-changes --context AuthDbContext` reports no pending model changes.
- [ ] Frontend lint, test, build, and typecheck pass per `specs/_template/tasks.md` and existing baseline.
- [ ] Acceptance criteria in `user-story.md` are verified.
- [x] Configuration documentation is complete; no credentials or live SMS content are committed.
- [x] Reference docs, feature index, and Unreleased changelog entry are current.
- [x] Remaining operational dependency on MelliPayamak account setup/sender registration is reported.

## Verification Evidence

- `dotnet build src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --configuration Release --no-restore` — passed before the final profile projection adjustment; the updated API and IntegrationTests projects compile as part of the filtered profile integration run.
- `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --no-restore --filter FullyQualifiedName~MelliPayamakOtpSenderTests` — 5 passed.
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --no-restore --filter FullyQualifiedName~OwnProfileIntegrationTests` — 10 passed.
- `dotnet ef migrations has-pending-model-changes --context AuthDbContext` — no pending model changes.
- Targeted ESLint on changed frontend files — passed. Full frontend lint/typecheck/build and full backend suite remain constrained by repository baseline/environment failures; see the implementation handoff for exact outcomes.
- Full backend suite stopped after 33 unit failures (3,497 passed); the repository baseline lists 32 known failures, and one unrelated regex-timeout failure occurred during concurrent verification. The suite did not reach IntegrationTests, which were built and the profile integration class was run separately.
- Full Vitest run: 274 passed, 15 failed, 12 suite errors; failures included the repository's unresolved Phosphor icon imports and worker/esbuild environment errors. Full `npm run lint` is dominated by CRLF/Prettier errors across the pre-existing repository. `npm run typecheck` fails only in files outside this feature after feature-file issues were corrected. `npm run build` stops on unresolved `@phosphor-icons/react/dist/csr/ChatCircleDots` in the existing mobile icon module.
- No live SMS was sent. Automated tests replace the sender with a fake; deployment still needs an active MelliPayamak account and secret configuration.
