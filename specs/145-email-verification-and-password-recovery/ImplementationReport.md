# Feature 145 — Post-implementation verification and release-readiness report

## Decision

**READY_FOR_STAGING.** The 86 reported backend failures and three frontend failure points were reproduced against the pre-feature `develop` commit. No reported failure was introduced by Feature 145. Focused identity tests and frontend auth tests pass. The full regression suites remain red; staging must complete the database, PostgreSQL concurrency, and real SMTP checks below before production rollout.

Baseline: detached worktree at `eeb0265a7a988287e27ab3b17e21637b1915fb11` (`develop` HEAD before the uncommitted Feature 145 work). Both trees used the same .NET 10 and Node 22.13.0 environment, Release backend builds, the same frontend lockfile, and the same installed frontend dependencies. The backend comparison used per-test TRX results. [The backend audit](backend-regression-audit.md) records evidence for each of the 86 failed tests.

## Regression classification

| Suite | Feature 145 regression | Pre-existing failure | Environment failure | Inconclusive | Evidence |
|---|---:|---:|---:|---:|---|
| Backend, original 86 failures | 0 | 69 | 17 | 0 | All 86 test names failed in both worktrees. Six PostgreSQL tests explicitly report unavailable Docker/Testcontainers in both; eleven tests explicitly report missing `Feature141:SchemaNavigator:HierarchyPath` in both. The other 69 have matching failure signatures, except one Telegram test whose assertion wording changed while both runs still observed HTTP 400 rather than the expected success. |
| Frontend, original three failure points | 0 | 2 | 1 | 0 | Both worktrees fail the same chart message assertion and disclosure date assertion. Both fail collection of `scripts/quarterly-financial-table-png.browser.test.mjs` with esbuild's `new TextEncoder().encode("") instanceof Uint8Array` invariant under Vitest's global jsdom environment. Native Node reports the invariant as true, so this is a test-runner environment interaction. |

The baseline backend run reported integration **64 failed / 464 passed / 48 skipped**, unit **22 failed / 2,148 passed**, and architecture **1 failed / 11 passed**. The first Feature 145 run reported integration **64 failed / 470 passed / 48 skipped**, unit **21 failed / 2,149 passed**, and architecture **1 failed / 11 passed**. The baseline-only unit failure passed in the current tree. The final full backend rerun after fixes reported integration **65 failed / 474 passed / 48 skipped**, unit **21 failed / 2,150 passed**, and architecture **1 failed / 11 passed**. Its additional integration failure, `FinancialStatementsByPeriod_InvalidPeriodIsRejectedBeforeQueueing`, returned HTTP 429 instead of 400; it passed in the initial current run, in the baseline run, and on an isolated rerun. This intermittent extra result is **INCONCLUSIVE** and outside the original 86. The endpoint uses the pre-existing authenticated-actor rate policy (60 requests/window), and the test class sends many requests; the available runs do not establish a Feature 145 cause.

The baseline frontend run reported **2 failed / 95 passed tests** and **3 failed / 13 passed files**. Both current full runs reported **2 failed / 98 passed tests** and **3 failed / 14 passed files**; the additional three passing tests are the new auth tests. The `TextEncoder` issue is a failed suite/file, not a failed test assertion. It reproduces at baseline and does not block the production build.

## Feature 145 defects found and fixed in this review

1. A public verification resend could set `EmailVerificationRequired=true` on a migrated, unconfirmed legacy user and block their next login. Resend now leaves that flag unchanged. A regression test verifies login before and after resend and checks that the database still holds `EmailConfirmed=false` and `EmailVerificationRequired=false`.
2. Resend and forgot-password returned different generic messages. Both now use the same public response. The focused test compares known and unknown accounts across both endpoints.
3. A loopback HTTP frontend URL was accepted in any environment. Configuration validation now permits HTTP loopback only in Development and requires HTTPS elsewhere.
4. With email disabled, the service still persisted action-token/email-key records using a hardcoded fallback HMAC key. Disabled delivery now returns before token accounting, and the fallback key was removed.

The concurrent-reset test initially shared one SQLite connection across requests, causing a fixture-only transaction error. The fixture now gives each EF context its own connection while keeping the in-memory database anchored. Five repeated focused concurrency runs each allowed exactly one successful reset and rejected the competing use.

## Identity, migration, and security verification

- The generated idempotent EF Core SQL adds `auth_users."EmailVerificationRequired" boolean NOT NULL DEFAULT FALSE` and creates `auth_email_action_tokens` plus indexes. It does not update `EmailConfirmed` or rewrite existing users. No migration was applied to a live or production database.
- Registration explicitly sets `EmailVerificationRequired=true`. Login and refresh deny an account only when this flag is true **and** `EmailConfirmed=false`. The new legacy-user test confirms an unconfirmed migrated account remains login eligible after a public resend.
- Tokens are 32 random bytes; only SHA-256 digests are persisted. Verification and reset use distinct purposes. The tests cover expired verification and reset tokens, verification and reset replay rejection, and password reset without confirming email.
- Token consumption uses a conditional relational update requiring `ConsumedAtUtc IS NULL` and `ExpiresAtUtc > now`. It runs in a transaction with the verification or password mutation. The concurrent reset test permits at most one success on separate SQLite connections. PostgreSQL-specific concurrency remains a staging check because Docker/Testcontainers was unavailable here.
- Password reset applies Identity validators and hasher, rotates the security stamp, and revokes all active refresh sessions. The focused test checks two sessions and rejection of a newly issued, pre-reset owned access token. Tokens issued before Feature 145 lack the security-stamp claim and can remain valid until expiry; the checked-in access lifetime is seven days.
- Email delivery, sender address, SMTP host/port/TLS/credentials, token-hash secret, and frontend root URL are configuration values. Email is disabled by default. SMTP password and token-hash secret must be supplied through deployment secrets; no real email was sent. The frontend uses fragment tokens and removes them from browser location after reading.
- Account/email cooldown is persisted, but simultaneous issuance across multiple API instances was not stress-tested on PostgreSQL. The public IP limiter is process-local; a multi-instance staging deployment needs shared gateway throttling.

## Verification commands and results

- `dotnet build src/backend/FinancialCopilot.sln --configuration Release --no-restore`: passed, 0 warnings/errors.
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --no-restore --filter FullyQualifiedName~OwnedIdentityEndpointTests`: **16/16 passed** after the review fixes.
- `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-build`: full-suite results and classification above.
- `npm test -- --run src/integrations/financial-copilot/__tests__/auth.test.ts`: **3/3 passed**.
- `npm test -- --run`: full-suite results and classification above.
- `npm run build`: passed.
- `dotnet ef migrations script --idempotent --context AuthDbContext`: generated SQL, inspected without applying it.

## Remaining blockers

- Production release still requires a staging migration, PostgreSQL concurrency verification, approved real SMTP delivery, and end-to-end checks. None was performed in this review.
- The repository-wide backend and frontend suites remain red with the baseline failures above. The additional intermittent admin HTTP 429 needs a separate suite-stability investigation if a green full-suite gate is required.
- Automatic approval review rejected deletion of the isolated baseline worktree, so it remains at `D:\Source\TahlilApp-AI-baseline145`; the shared frontend `node_modules` junction was not removed.

## Staging deployment checklist

1. Back up the staging auth database. Review the generated idempotent migration SQL and apply `20261014073944_AddEmailVerificationAndPasswordRecovery` to staging **before** deploying the new API. Confirm existing `auth_users.EmailConfirmed` values are unchanged and new `EmailVerificationRequired` values are false.
2. Configure `Authentication__Email__Enabled=true`, `Authentication__Email__FrontendBaseUrl` to the actual HTTPS staging origin, and a unique 32-character-or-longer `Authentication__Email__TokenHashSecret` in the secret store. Set token lifetimes/cooldowns intentionally. Verify startup rejects an HTTP frontend URL outside Development.
3. Configure `Authentication__Smtp__Host`, `Port`, `Security` (`StartTls` or `SslOnConnect`), `SenderAddress`, `SenderName`, `TimeoutSeconds`, and paired `UserName`/`Password` when required, using staging secrets. Verify SPF/DKIM/DMARC and approved sender policy with the mail operator.
4. With authorization to send to designated staging mailboxes, verify actual delivery and readable Persian HTML/plain text for verification and reset. Check links use the expected HTTPS origin and fragment token, and that logs contain no password, token, recipient address, or SMTP credential.
5. Exercise end to end: new registration cannot log in before verification; verification succeeds once and expires; resend responses are generic and cooldown applies; forgot responses are identical for known/unknown accounts; reset succeeds once, rejects expired/replayed tokens, preserves email verification state, and invalidates every refresh session.
6. Use staging PostgreSQL to race two reset submissions for one token and verify exactly one succeeds. Repeat across API instances; check persisted cooldown and configure shared gateway IP throttling. Verify a pre-migration unconfirmed user can log in before and after an attempted resend.
7. Check old access-token expiry exposure (up to seven days for tokens without a security-stamp claim), monitor SMTP failures and auth rate-limit responses, and decide whether the known baseline suite failures are acceptable for the staging gate.

No production migration or real email delivery was performed during this review.
