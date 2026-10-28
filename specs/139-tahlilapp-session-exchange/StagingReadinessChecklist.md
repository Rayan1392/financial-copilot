# Feature 139 â€” SapioAI Staging Readiness Checklist

## Temporary Closed Registration policy

Public registration must remain disabled in deployed environments (`Authentication:OwnedIdentity:PublicRegistrationEnabled=false`). Verify an anonymous `POST /api/auth/v1/register` returns 403 with `REGISTRATION_DISABLED` and creates no Identity or Billing rows. Direct login and password recovery for existing users remain available. Newly onboarded users enter through trusted external providers; TahlilApp is the first. Keep Feature 145 email verification/password enrollment operational for provisioned users. Future native public registration requires a separate product decision.

This checklist records staging evidence. Repository code and local tests do not prove external service availability, secret rotation, migration execution, or operational readiness. Leave environment gates unchecked until evidence includes a run timestamp, environment, owner and artifact or command output.

## 1. Repository verification

Run from the repository root before creating a staging deployment:

~~~powershell
dotnet restore src/backend/FinancialCopilot.sln
dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release
dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release
dotnet test tests/FinancialCopilot.ArchitectureTests/FinancialCopilot.ArchitectureTests.csproj --configuration Release
dotnet build src/backend/FinancialCopilot.sln --configuration Release
dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~BillingServiceTests|FullyQualifiedName~SubscriptionCreditLifecycleTests|FullyQualifiedName~TahlilAppBillingSyncTests|FullyQualifiedName~TahlilAppEntitlementRefreshTests"
dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~TahlilApp|FullyQualifiedName~NativeEnrollmentIntegrationTests"
dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~BillingPersistenceTests|FullyQualifiedName~BillingEndpointTests"
Set-Location src/frontend
npm ci
npm run test -- --pool=threads
npm run build
~~~

Record output in ImplementationReport.md. Docker-backed PostgreSQL tests require a reachable Docker engine.

**Current local evidence (2026-10-23):** Billing unit filter passes 48/48; combined TahlilApp/native-enrollment integration passes 36/36; Billing persistence/endpoint integration passes 34/34; backend Release build passes with 0 warnings/errors. Full UnitTests passes 2,312/2,335 with 23 failures; all 23 names match the earlier pre-credit-lifecycle TRX under `%TEMP%\feature139-regression-20261023\unit.trx`. Full sequential integration evidence remains 518 passed, 69 failed, 48 skipped; 6 PostgreSQL tests report Docker unavailable. Architecture and frontend remain as recorded in `ImplementationReport.md`. Do not treat repository verification as passed until the failures are resolved and rerun.

## 2. Credential and secret configuration

### SEC-01 â€” Rotate credentials previously present in repository history

- [ ] **Owner:** Operations / security
- Rotate and revoke exposed database, signing, SMTP, provider and service credentials at their issuers. Removing a value from the current file does not revoke it from repository history.
- Put replacement values in the staging secret store and grant access only to each process identity.
- **Evidence / run ID:** ______________________________

### SEC-02 â€” SapioAI application secrets

Set these through the secret manager or process environment. Do not put values in tracked JSON, command history, build logs or the frontend bundle.

~~~powershell
$env:ASPNETCORE_ENVIRONMENT = "Staging"
$env:FINANCIAL_COPILOT_CONNECTION_STRING = "<staging PostgreSQL connection string>"
$env:Authentication__JwtBearer__Issuer = "SapioAI"
$env:Authentication__JwtBearer__SigningKey = "<new high-entropy signing key>"
$env:Authentication__TahlilApp__Enabled = "false"
$env:Authentication__TahlilApp__BaseUrl = "https://<confirmed-tahlilapp-staging-host>"
$env:Authentication__TahlilApp__ApiKeyEnvironmentVariable = "TAHLILAPP_EXCHANGE_API_KEY"
$env:TAHLILAPP_EXCHANGE_API_KEY = "<staging service key>"
$env:DataProtection__KeyRingPath = "<mounted durable key-ring path>"
$env:Authentication__Email__Enabled = "true"
$env:Authentication__Email__FrontendBaseUrl = "https://<sapioai-staging-origin>"
$env:Authentication__Email__TokenHashSecret = "<new random secret of at least 32 characters>"
$env:Authentication__Smtp__Host = "<approved SMTP host>"
$env:Authentication__Smtp__Port = "587"
$env:Authentication__Smtp__Security = "StartTls"
$env:Authentication__Smtp__UserName = "<staging SMTP user>"
$env:Authentication__Smtp__Password = "<staging SMTP password>"
$env:Authentication__Smtp__SenderAddress = "<verified sender address>"
$env:Authentication__Smtp__SenderName = "SapioAI"
~~~

Keep the TahlilApp feature flag false until all gates pass. API-key issuance, access policy, rotation overlap and revocation require coordination with TahlilApp.

- [ ] **Owner:** SapioAI operations
- [ ] Secret-store access and startup validation pass; no secret value appears in logs or browser assets.
- **Evidence / run ID:** ______________________________

### SEC-03 â€” Trusted reverse proxies and public rate limits

- [ ] **Owner:** Operations + SapioAI
- Record staging proxy addresses/CIDRs. Set ForwardedHeaders:Enabled=true, configure KnownProxies or non-global KnownNetworks and a bounded ForwardLimit.
- From an untrusted source, send forged X-Forwarded-For values. Confirm the launch limiter partitions by the actual client and POST /api/auth/v1/tahlilapp/launch returns 429 after its configured limit.
- Verify the frontend and API are same-site for the Secure, HttpOnly, SameSite=Strict refresh cookie.
- **Evidence / run ID:** ______________________________

## 3. Persistent Data Protection key ring

- [ ] **Owner:** Operations
- Mount a durable volume at DataProtection:KeyRingPath. It must survive container replacement and be shared by API replicas that read profile data.
- Restrict access to the API runtime identity and backup operator. Keep keys outside database backups and container images.
- Restart/redeploy; protect a test value on replica A and unprotect it on replica B.
- Back up after each rotation and at least daily. Restore the ring before the database in an isolated environment and decrypt a previously stored profile value.
- Document key-loss recovery and alert ownership. Do not enable TahlilApp login before the restore drill succeeds.
- **Evidence / run ID:** ______________________________

## 4. Auth and Billing migrations

No migration is run by this checklist. Run only against the named staging database after an approved backup and DBA change window. The design-time factory reads FINANCIAL_COPILOT_CONNECTION_STRING.

~~~powershell
dotnet tool update --global dotnet-ef --version 10.0.13
$env:ASPNETCORE_ENVIRONMENT = "Staging"
$env:FINANCIAL_COPILOT_CONNECTION_STRING = "<staging PostgreSQL connection string>"

dotnet ef migrations list --project src/backend/FinancialCopilot.Infrastructure/FinancialCopilot.Infrastructure.csproj --startup-project src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --context AuthDbContext
dotnet ef migrations list --project src/backend/FinancialCopilot.Infrastructure/FinancialCopilot.Infrastructure.csproj --startup-project src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --context BillingDbContext

New-Item -ItemType Directory -Force .artifacts | Out-Null
dotnet ef migrations script --idempotent --project src/backend/FinancialCopilot.Infrastructure/FinancialCopilot.Infrastructure.csproj --startup-project src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --context AuthDbContext --output .artifacts/feature139-auth.sql
New-Item -ItemType Directory -Force .artifacts | Out-Null
dotnet ef migrations script --idempotent --project src/backend/FinancialCopilot.Infrastructure/FinancialCopilot.Infrastructure.csproj --startup-project src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --context BillingDbContext --output .artifacts/feature139-billing.sql
dotnet ef migrations script 20261018092758_UpdateApprovedMonthlyAiCredits 20261023200709_AddSubscriptionCreditLifecycle --idempotent --project src/backend/FinancialCopilot.Infrastructure/FinancialCopilot.Infrastructure.csproj --startup-project src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --context BillingDbContext --output .artifacts/feature139-credit-lifecycle.sql

# Only after DBA review, backup verification and change approval:
dotnet ef database update --project src/backend/FinancialCopilot.Infrastructure/FinancialCopilot.Infrastructure.csproj --startup-project src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --context AuthDbContext
dotnet ef database update --project src/backend/FinancialCopilot.Infrastructure/FinancialCopilot.Infrastructure.csproj --startup-project src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --context BillingDbContext
~~~

Review Auth migration `20261017135632_AddTahlilAppIdentityAndProfile`, Billing catalog migration `20261018092758_UpdateApprovedMonthlyAiCredits`, and forward-only lifecycle migration `20261023200709_AddSubscriptionCreditLifecycle`. The lifecycle migration adds `billing_subscription_credit_periods` plus nullable `SubscriptionCreditsApplied`; its Up path does not rewrite existing wallet/ledger data. Verify `dotnet ef --version` reports `10.0.13` to match the EF runtime before listing or generating SQL. Before any staging application, snapshot account/wallet balances, open reservations and ledger counts, identify all wallets without period rows, and obtain an approved legacy-wallet reconciliation plan. Do not bulk-grant or reset existing balances. Approved monthly allowances are Free 25, Plus 260 and Pro 2600.

**Repository schema review evidence (2026-10-23):** `dotnet ef migrations list --no-connect --project src/backend/FinancialCopilot.Infrastructure/FinancialCopilot.Infrastructure.csproj --startup-project src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --context BillingDbContext` lists `20261023200709_AddSubscriptionCreditLifecycle`; the tool reports applied status as unknown because no database was contacted. An idempotent delta script was generated at `%TEMP%\feature139-credit-lifecycle.sql` from `20261018092758_UpdateApprovedMonthlyAiCredits` to the new migration and inspected. Its operations are `ALTER TABLE ... ADD SubscriptionCreditsApplied`, create the period table and indexes, and insert the EF migration-history row; there is no wallet/ledger balance update or backfill. This is local schema-generation evidence only, not migration-application evidence.

- [ ] **Owner:** SapioAI + DBA
- [ ] Backup/restore verified, SQL reviewed, migrations applied in staging, migration history and before/after evidence attached.
- **Evidence / run ID:** ______________________________

## 5. SMTP verification

- [ ] **Owner:** Operations + SapioAI
- Verify sender domain and SPF/DKIM/DMARC. Send verification and recovery messages to staging mailboxes.
- Verify token expiry/replay and delivery-failure behavior; ensure tokens and addresses do not appear in logs.
- **Evidence / run ID:** ______________________________

## 6. TahlilApp contract and permission approval

### TA-01 â€” Contract approval

- [ ] **Owner:** TahlilApp + SapioAI
- Approve exchange/status paths, schemas, status codes, TLS, 8 KB response cap, launch-code lifetime/single-use, timeouts and API-key rotation.
- TahlilApp determines external user eligibility and Active/Suspended/revoked status. SapioAI Billing maps the approved subscription plan to internal capabilities.
- Contract version 2 now requires TahlilApp `subscription.effectiveFrom` in exchange and status responses; SapioAI sends `X-SapioAI-Contract-Version: 2`. TahlilApp deployment and real round trips remain unverified.
- permissions[] is informational only. No external permission code is authoritative in SapioAI. Before permission-dependent authorization can be enabled, both teams and Product/Security must approve the external code set and mapping in a versioned contract.
- **Evidence / contract version:** ______________________________

### TA-02 â€” Real staging launch, exchange and status contract tests

- [ ] **Owner:** TahlilApp + SapioAI QA
- Run only after TA-01 approval with a disposable allow-listed account. Obtain a fresh one-time code through TahlilApp's authenticated launch UI. Never paste the code into shell history or ticket comments.

~~~powershell
# Sign in to TahlilApp staging and use its approved Open SapioAI action.
# Confirm redirect to approved SapioAI /launch with a short-lived code.

$launchBody = @{ code = "<fresh one-time code>" } | ConvertTo-Json
$launch = Invoke-RestMethod -Method Post -Uri "https://<sapioai-staging-api>/api/auth/v1/tahlilapp/launch" -ContentType "application/json" -Body $launchBody -SessionVariable sapioSession
$headers = @{ Authorization = "Bearer $($launch.accessToken)" }
Invoke-RestMethod -Method Get -Uri "https://<sapioai-staging-api>/api/auth/v1/me" -Headers $headers
Invoke-RestMethod -Method Get -Uri "https://<sapioai-staging-api>/api/auth/v1/profile" -Headers $headers

# Re-submit the same code: expect 401 launch-invalid and no second exchange.
# Repeat with disposable invalid, expired, suspended and revoked identities.
~~~

Use TahlilApp's approved server-side harness for exchange and status tests. Load the service key from the secret manager, include a correlation ID and redact PII. Verify valid/invalid/expired/replayed code; Active/Suspended/404 status; timeout; wrong key; rotation overlap; single-use concurrency; one outbound attempt; and no redirect following.

- **Evidence / contract version / run ID:** ______________________________

## 7. End-to-end authentication and Billing

- [ ] **Owner:** SapioAI QA + TahlilApp QA
- Verify first/repeat launch, launch with an existing refresh cookie, email-less /me, profile masking/reveal, native email verification/password setup and direct login.
- Confirm TahlilApp launch and native login resolve the same local SapioAI user, tenant and Billing account.
- Repeated launch/login and identical entitlement sync must not increase wallet balance or reset consumed ledger rows.
- Verify Free/Plus/Pro capabilities and exact 30-day grant/expiry boundaries, no rollover, Freeâ†’paid forfeiture, repeated login/status idempotency, and that expired paid entitlements resolve to Free. Do not enable Plusâ†’Pro credit movement until Product approves its proposed balance and period treatment. Confirm activation-anchor/immediate-paid semantics under TA-01.
- Verify refund idempotency for active and expired periods. Product must approve whether a refund of subscription-funded usage after expiry remains spendable as general wallet credit before staging allocation is enabled.
- Confirm names, national IDs and phone numbers never appear in logs, tokens, AI prompts or status payloads.
- **Evidence / run ID:** ______________________________

## 8. Final enablement gate

- [ ] **Owner:** Product + Security + Operations
- [ ] All previous gates have evidence and owners; contract and permission mapping are approved; rollback owner and feature-flag procedure are tested.
- Enable Authentication:TahlilApp:Enabled=true only in staging after written change approval. Production requires a separate readiness decision.
- **Evidence / approval:** ______________________________

### Key-ring mount and recovery example

Use a dedicated persistent volume in the staging deployment. Example container path:

~~~yaml
services:
  sapioai-api:
    environment:
      DataProtection__KeyRingPath: /var/lib/sapioai/dpkeys
    volumes:
      - sapioai-data-protection:/var/lib/sapioai/dpkeys
volumes:
  sapioai-data-protection:
~~~

For a Linux host bind mount, provision and permission the directory before starting containers:

~~~bash
sudo install -d -o 10001 -g 10001 -m 0700 /srv/sapioai/data-protection
~~~

Replace UID/GID with the actual API runtime identity. Back up the volume outside the database backup system, then verify the archive:

~~~bash
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
tar --acls --xattrs -czf "/secure-backups/sapioai-dpkeys-$stamp.tar.gz" -C /srv/sapioai data-protection
tar -tzf "/secure-backups/sapioai-dpkeys-$stamp.tar.gz"
~~~

For a restore drill, stop the staging API, restore the key-ring archive to the mounted path, restore the database snapshot, start SapioAI, and decrypt a previously stored test profile. Keep archive checksums and restore logs with the staging change record. Never test restore against production.

### TA-02 server-to-server exchange and status command examples

After TahlilApp confirms the proposed paths and DTO fields in TA-01, run these against disposable staging identities only. Enter the one-time code interactively so it is not saved in PowerShell command history. These commands send no profile PII:

~~~powershell
$env:TAHLILAPP_STAGING_BASE_URL = "https://<confirmed-tahlilapp-staging-host>"
$env:TAHLILAPP_EXCHANGE_API_KEY = "<read from staging secret manager>"
$secureCode = Read-Host "Fresh disposable staging LaunchCode" -AsSecureString
$codePointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureCode)
try { $launchCode = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($codePointer) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($codePointer) }
$headers = @{ "X-Api-Key" = $env:TAHLILAPP_EXCHANGE_API_KEY; "X-Correlation-Id" = [guid]::NewGuid().ToString("N") }
$exchangeBody = @{ code = $launchCode } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$env:TAHLILAPP_STAGING_BASE_URL/api/financial-copilot/session-exchange" -Headers $headers -ContentType "application/json" -Body $exchangeBody
Invoke-RestMethod -Method Get -Uri "$env:TAHLILAPP_STAGING_BASE_URL/api/financial-copilot/users/<disposable-external-user-id>/status" -Headers $headers
~~~

Expected exchange DTO and status response must first match the approved contract version. Capture only HTTP status, elapsed time, correlation ID, and redacted field-presence/schema checks. Never save the response body if it contains profile data. Repeat with wrong/rotated keys, expired/replayed code, 404 status, suspension, timeout and parallel single-use requests. These commands are a runbook only; no real TahlilApp service is configured or called from this repository session.


## Explicit Phase 1 deferrals

- Admin masked profile and audited cross-user reveal (T-24): not required for Phase 1; there is no admin profile UI. Its internal permission is registered but unused.
- Explicit linking/unlinking of existing native accounts (T-28): not required for launch; accounts remain separate and support reconciles them under the approved policy.
- Batch status endpoint: unnecessary for the current bounded per-user worker; revisit only if measured scale requires it.
- Plus→Pro carry of remaining Plus credits into the 2600-credit Pro period is approved and implemented; PostgreSQL concurrency evidence and TahlilApp staging execution remain pending.
- The authoritative activation timestamp decision is approved. TahlilApp must deploy and return the UTC `effectiveFrom` field in both contract paths before staging credit allocation is enabled.
- Refunds of subscription-funded charges after period expiry follow the existing general-wallet refund behavior; Product confirmation is required before staging.
- Existing-wallet period reconciliation: unresolved; old balances and ledgers remain untouched until a reviewed mapping is approved.

## Revision 6 billing policy and rollout gate

Approved: Free 25, Plus 260 and Pro 2600 credits per exact 30-day period; unused credits expire without rollover; Freeâ†’paid forfeits the remainder; Plusâ†’Pro transfers the unused Plus remainder into a Pro period containing 2600 new credits; transferred credits expire with that Pro period; Proâ†’Plus is prohibited; refunds are unsupported; repeated authentication and synchronization do not duplicate allocation.

TahlilApp contract version 2 must return `subscription.effectiveFrom` as a valid UTC timestamp in both exchange and status responses. SapioAI sends `X-SapioAI-Contract-Version: 2`; legacy responses remain readable for eligibility but cannot activate paid credits without a verified anchor. The Billing account persists external `revision` and ignores stale/conflicting replay. Existing balances and histories are not backfilled.

Repository migrations: `20261023200709_AddSubscriptionCreditLifecycle` and `20261024004630_AddAuthoritativeSubscriptionActivationRevision`. They are forward-only rollout artifacts and do not modify existing wallet balances or ledger history. This work did not apply them; production/staging applied state is unverified because no database connection was used. PostgreSQL concurrency coverage is added; its execution result is recorded in `ImplementationReport.md`.

External contract implementation, TahlilApp API-key issuance/rotation, real exchange/status contract tests, legacy wallet reconciliation, and all infrastructure gates remain unverified until staging evidence is attached.
## Revision 7 — Official TahlilApp contract and approved independent subscriptions

This revision supersedes conflicting assumptions in Revisions 1–6. Those sections remain as implementation history only.

The confirmed API uses HTTP Basic Auth with client ID `financial-copilot` and an environment supplied secret. The base is `https://api.tahlilapp.com/api`; exchange is `POST /partners/exchange` with `{ "code": "..." }`; status is `GET /partners/users/{user_id}/status`. Responses identify the account by root `user_id`, allow nullable `name`, and contain `subscription.plans[]` entries with `slug`, `starts_at`, and `expires_at`. Supported slugs are `sapio_plus` and `sapio_pro`. The contract does not supply `effectiveFrom`, subscription IDs, or revisions. Prior `effectiveFrom` and revision requirements are superseded. The new `starts_at` field is pending external contract delivery; no historical activation time may be inferred.

Each paid plan is an independent subscription. Plus and Pro can be active together; the effective plan is Pro whenever Pro is active, otherwise Plus when Plus is active, otherwise Free. Both valid paid purchases contribute grants. A Pro activation transfers only remaining eligible Plus credits, paired in the append-only ledger; consumed Plus credits are never granted again. Each plan-period grant is idempotent by account plus `slug + starts_at + expires_at` identity and 30-day period number. Consumption is attributed to source periods and uses earliest-expiry-first ordering. The wallet remains an aggregate projection; source period rows, grant references, charge allocations, refunds, expiry, and transfer ledger rows preserve accounting provenance. No migration rewrites historical wallet or ledger entries.

Paid access is computed from the active per-plan windows, independently from credit accounting. The last paid subscription expires at the greatest authoritative `expires_at` among the user's paid subscriptions when no paid window remains active. At that timestamp effective plan becomes Free and the 30-day Free-credit cooldown begins, even if SapioAI observes expiry later. The durable cooldown row is separate from authentication and plan state; repeat login/status sync cannot extend it. Reservation authorization denies AI usage during cooldown even if a balance remains. Authentication stays available. Reactivation permits paid reservations immediately; a later last-paid expiry uses the later authoritative expiry. Tracked Free credits are forfeited on paid activation; unexpired paid balances remain held during cooldown and expire by their own source period. No Free grant occurs during cooldown. Free allocation resumes after it, with a durable period identity to prevent duplicate historical grants.

Implementation adds per-plan subscription state, credit-period identity, immutable usage-source allocations, and cooldown persistence. The EF migration is `20261028104343_Feature139OfficialPlanSubscriptionsAndCooldown`; it has not been applied. A legacy period-key backfill preserves existing period uniqueness without altering wallet totals or ledger history. Existing legacy wallets still require a separately approved reconciliation before new subscription grants can be attributed.

Verification: focused Feature 139/Billing tests pass (121 unit tests; 20 integration tests pass and one PostgreSQL concurrency test is skipped because its database infrastructure is unavailable). The backend Release solution build passes with zero warnings and errors. The full unit suite has 20 unrelated failures: 18 match failures in the saved baseline TRX and two are in Nadpco/Telegram tests; details are in `ImplementationReport.md`. No production API call, deployment, or database migration was performed. TahlilApp delivery of `starts_at`, joint staging exchange/status verification, migration application, and operational readiness remain unverified.

Revision 7 acceptance criteria AC-100 through AC-106 are defined in tasks.md and apply to this document. Revision 7 controls over conflicting earlier assumptions.
