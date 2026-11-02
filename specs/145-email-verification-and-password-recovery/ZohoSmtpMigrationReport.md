# Feature 145 — Zoho Mail SMTP migration report

Date: 2026-11-02 · Status: **ZOHO_SMTP_MIGRATION_COMPLETE_PENDING_LIVE_TEST** · Live test (rerun, section 14): **ZOHO_SMTP_LIVE_TEST_FAILED**: STARTTLS succeeds, but Zoho rejects SMTP authentication (`535 5.7.8`).

Authentication email (registration verification, email-enrollment verification, password reset) moves from the personal Gmail sender to `no-reply@sapioai.ir` via Zoho Mail SMTP. No deployment, commit, push, DNS change, or server edit was made. No SMTP password appears in this report or in any changed file.

## 1. Existing email implementation

| Concern | Location |
|---|---|
| Port (policy-owned) | `IEmailSender` / `EmailMessage` in `Infrastructure/Authentication/OwnedIdentityContracts.cs` |
| Only caller | `OwnedIdentityService` (single send site, used by registration verification, email enrollment, and password reset). Delivery failures are logged and swallowed so the public response stays generic (anti-enumeration). |
| SMTP adapter | `SmtpEmailSender` (MailKit 4.16.0) in `Infrastructure/Authentication/SmtpEmailSender.cs` |
| Options | `SmtpEmailOptions`, section `Authentication:Smtp`; `AuthEmailOptions`, section `Authentication:Email` |
| Registration / startup validation | `Infrastructure/ServiceCollectionExtensions.cs` (`ValidateOnStart`) |
| Container mapping | `docker-compose.yml` `api` service: `AUTH_SMTP_*` → `Authentication__Smtp__*`; the `worker` clears SMTP credentials and disables email |

No other subsystem uses this sender; Telegram and notification integrations are separate and were not touched.

## 2. Root cause of Gmail being the sender

The tracked code never contained the Gmail address. The active sender came from the **deployment server's `.env`** (inspected read-only, values other than non-secret SMTP keys redacted):

```
AUTH_SMTP_HOST=smtp.gmail.com
AUTH_SMTP_USERNAME=kamran.abdi@gmail.com
AUTH_SMTP_SENDER_ADDRESS=kamran.abdi@gmail.com
AUTH_SMTP_PASSWORD=<redacted>
```

Compose maps these into `Authentication__Smtp__*`, which override `appsettings.json`. The tracked `appsettings.json` additionally shipped `"Host": "smtp.gmail.com"`.

A design trap made the repository defaults ineffective in Docker: compose used `${AUTH_SMTP_HOST:-}` etc., and an **empty** environment variable still overrides a JSON value in .NET configuration. Any key missing from `.env` therefore became empty rather than falling back to `appsettings.json`. `AUTH_SMTP_SENDER_NAME` also defaulted to `Financial Copilot`.

Other locations found (not active for delivery):

- Development User Secrets of `FinancialCopilot.API` contain Gmail values under a root-level `Smtp:` section (no password). Nothing binds a root `Smtp` section, so they are inert. Left unchanged; the owner may remove them with `dotnet user-secrets remove`.
- `appsettings.Development.Local.json` (Git-ignored optional overlay) is wired in `Program.cs` but does not exist on this workstation.
- Local repository `.env` contains no SMTP keys.

## 3. Files changed

| File | Change |
|---|---|
| `src/backend/FinancialCopilot.API/appsettings.json` | `Authentication:Smtp` defaults → Zoho host, user name and sender `no-reply@sapioai.ir` (no password) |
| `src/backend/FinancialCopilot.Infrastructure/Authentication/SmtpEmailSender.cs` | SMTP validation rule moved into `SmtpEmailOptions.IsValid`; single security-mode resolver (`StartTls`/`SslOnConnect` only); internal `ISmtpTransport` seam with `MailKitSmtpTransport` so tests use a fake; failure log now records the SMTP stage (`Connect`/`Authenticate`/`Send`/`Disconnect`) plus exception type only; `SenderName` code default `Sapio AI` |
| `src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs` | Startup validation delegates to `SmtpEmailOptions.IsValid` (rule unchanged) |
| `docker-compose.yml` | `api` SMTP defaults now mirror `appsettings.json` instead of empty strings; password default stays empty |
| `.env.example` | Zoho values and `<zoho-application-specific-password>` placeholder, with host/data-centre guidance |
| `README.md` | Feature 145 SMTP example updated to Zoho |
| `specs/145-.../LocalProductionConfiguration.md` | Gmail description marked superseded |
| `tests/FinancialCopilot.IntegrationTests/OwnedIdentityEndpointTests.cs` | Fixture sets `Authentication:Smtp:UserName` to empty (it uses an unauthenticated fake sender; the shipped user name now requires a password) |
| `tests/FinancialCopilot.UnitTests/Feature145SmtpEmailSenderTests.cs` | New focused tests (section 9) |

Unchanged by design: token generation/hashing, token formats, verification and reset URLs, templates/subjects, anti-enumeration, cooldowns, database schema (no migration). Pre-existing unrelated working-tree changes (Feature 151/152) were preserved; the `.env.example` and `docker-compose.yml` edits touch only SMTP lines.

## 4. Final effective configuration keys

| appsettings key (`Authentication:Smtp:`) | Container env (`.env`) | Default | Secret |
|---|---|---|---|
| `Host` | `AUTH_SMTP_HOST` | `smtppro.zoho.com` | no |
| `Port` | `AUTH_SMTP_PORT` | `587` | no |
| `Security` | `AUTH_SMTP_SECURITY` | `StartTls` (`SslOnConnect` for 465) | no |
| `UserName` | `AUTH_SMTP_USERNAME` | `no-reply@sapioai.ir` | no |
| `Password` | `AUTH_SMTP_PASSWORD` | empty | **yes** |
| `SenderAddress` | `AUTH_SMTP_SENDER_ADDRESS` | `no-reply@sapioai.ir` | no |
| `SenderName` | `AUTH_SMTP_SENDER_NAME` | `Sapio AI` | no |
| `TimeoutSeconds` | `AUTH_SMTP_TIMEOUT_SECONDS` | `15` | no |

Precedence: `appsettings.json` → `appsettings.{Environment}.json` → User Secrets (Development) → optional local overlay (Development) → environment variables → command line.

## 5. SMTP host, port, and security mode

- Default: `smtppro.zoho.com:587`, STARTTLS, authentication required (Zoho paid organization account, per the brief and Zoho's SMTP help page).
- **Data-centre warning:** `sapioai.ir` DNS points to Zoho **EU** (`MX mx.zoho.eu/mx2/mx3`, `zoho-verification=…zmverify.zoho.eu`, `v=spf1 include:one.zoho.eu ~all`). Zoho's help page states the exact host depends on account type and data centre and is shown under the account's *Server Configurations Details*. The EU host is therefore very likely `smtppro.zoho.eu` (or `smtp.zoho.eu` for a free organization). The repository default was kept at `smtppro.zoho.com` as specified; set `AUTH_SMTP_HOST` on the server to the host shown in Zoho.
- Transport guarantees: MailKit `SecureSocketOptions.StartTls` fails if the server does not offer STARTTLS (no plaintext fallback); `None`, `Auto` and `StartTlsWhenAvailable` are rejected at startup and never connect. MailKit's default certificate validation is untouched (no custom callback). Authentication uses the full mailbox address; From = `"Sapio AI" <no-reply@sapioai.ir>`, matching the authenticated mailbox as Zoho requires (mismatch → "Relaying Disallowed").
- Outbound TCP 587 to `smtppro.zoho.com`, `smtppro.zoho.eu` and `smtp.zoho.eu` succeeded **from the developer workstation only**; the production host was not tested.

## 6. Secret provisioning

- Create a Zoho **Application-Specific Password** for `no-reply@sapioai.ir` (required when 2FA/MFA is on): Zoho Accounts → Security → App Passwords. Never use the account password.
- Store it only as `AUTH_SMTP_PASSWORD` in the server's Git-ignored `.env` (`**/.env` is ignored; only `.env.example` is tracked).
- With `AUTH_EMAIL_ENABLED=true`, a user name without a password fails startup (`Authentication SMTP settings are invalid.`). That is the intended fail-fast for a missing app password.
- No credential was found in any tracked file, so no leak-driven rotation is required. After cut-over, **revoke the Gmail app password** currently used by the server, since it will no longer be needed.

## 7. Docker environment mapping

`docker compose config` (local `.env`, SMTP keys only) resolves the `api` service to `smtppro.zoho.com`, `587`, `StartTls`, `no-reply@sapioai.ir` (user and sender), `Sapio AI`, `15`, and an empty password. The `worker` still forces `Authentication__Email__Enabled=false` and empty SMTP credentials. Credentials enter only at runtime through `env_file`/`environment`; nothing SMTP-related is built into image layers or the frontend bundle (the frontend has no SMTP configuration).

## 8. Registration and password-reset integration

No code change was needed: both flows already call `IEmailSender` through the single `OwnedIdentityService` send site, so they now use whatever `Authentication:Smtp` resolves to. Verification and reset links continue to be built from `Authentication:Email:FrontendBaseUrl` (server value `https://sapioai.ir`, unchanged).

## 9. Test results

| Suite | Result |
|---|---|
| `Feature145SmtpEmailSenderTests` (new, fake transport, no network) | **12/12 passed** |
| Integration: `OwnedIdentity*`, `NativeEnrollment*`, `*Authentication*`, `*PasswordReset*`, `*EmailVerification*` | **49/50 passed**; 1 unrelated failure |

New tests cover: binding the shipped `appsettings.json` (host, port, StartTls, user name, sender, display name, no password); environment override precedence; missing-password validation; rejection of plaintext/opportunistic modes; STARTTLS connect + authenticate as the full mailbox + From header/display name + unchanged subject/bodies + disconnect + disposal; port 465 implicit TLS; unsupported mode never connects; authentication failure logs stage and type only, with no password, user name, recipient, token, or server reply in the log or the thrown exception; caller cancellation propagates without a failure log.

Registration verification, password reset, token single-use/expiry, anti-enumeration, and session revocation are covered by the existing `OwnedIdentityEndpointTests` (all passed).

Notes:

- The real `FinancialCopilot.UnitTests` project does not currently compile because of unrelated files: `CompanyDisclosureFeed112Tests.cs` (broken at `HEAD` a94999e: missing `cache` constructor argument) and `TahlilAppTopUpTests.cs` (in-progress Feature 151: `TopUpWindowResponse` missing). Those files were not modified. The new test file was compiled and run in a temporary isolated project (same assembly name, same project references) outside the repository.
- The unrelated failure is `NativeEnrollmentIntegrationTests.EnrollConfirmPassword_ThenRepeatedDirectLogin_ReusesLaunchUserAndCreditGrant` (expects a 25-credit launch grant; the Free plan seeds 250). That suite runs with email disabled, so SMTP code is not exercised.

## 10. Live Zoho SMTP verification

First pass: **LIVE_ZOHO_SMTP_TEST_NOT_EXECUTED** (no credentials). Rerun: see section 14. Authentication failed and no email was sent.

## 11. SPF / DKIM / DMARC

Observed via public DNS on 2026-11-02 (read-only):

| Record | Status |
|---|---|
| MX | Zoho EU (`mx.zoho.eu`, `mx2.zoho.eu`, `mx3.zoho.eu`) |
| Zoho domain verification TXT | present |
| SPF | `v=spf1 include:one.zoho.eu ~all`, which authorises Zoho EU senders |
| DKIM | **Not verified.** The selector is set in Zoho, and no selector was guessed. |
| DMARC | **Missing**: `_dmarc.sapioai.ir` returns NXDOMAIN on 1.1.1.1 and 8.8.8.8 |

Operator steps (Zoho Mail Admin Console → Domains → `sapioai.ir` → Email Configuration): confirm SPF and MX are "Verified"; open **DKIM**, create or confirm a selector, publish the exact TXT value Zoho shows, and verify it there; then publish a DMARC record (start with `p=none` plus a reporting address the business controls, and tighten after reports are clean). Exact values must come from Zoho and the domain owner; none were invented or changed.

## 12. Remaining deployment steps

1. In Zoho, read the SMTP host from *Server Configurations Details* (expected `smtppro.zoho.eu`) and create an app password for `no-reply@sapioai.ir`.
2. On the server `.env`, replace the SMTP block with: `AUTH_SMTP_HOST=<host from step 1>`, `AUTH_SMTP_PORT=587`, `AUTH_SMTP_SECURITY=StartTls`, `AUTH_SMTP_USERNAME=no-reply@sapioai.ir`, `AUTH_SMTP_PASSWORD=<app password>`, `AUTH_SMTP_SENDER_ADDRESS=no-reply@sapioai.ir`, `AUTH_SMTP_SENDER_NAME=Sapio AI`. Leave the `AUTH_EMAIL_*` keys as they are.
3. Run `docker compose config -q` on the server, then deploy this build through the normal release process and recreate the `api` container.
4. Check API logs for startup options validation; then trigger one registration verification and one password reset to an approved mailbox. Confirm the From header, the links to `https://sapioai.ir`, and `spf=pass` / `dkim=pass` in the received headers. On failure, the log line `Authentication email delivery failed. Stage: …; FailureType: …` identifies connectivity (`Connect`), credentials or host data centre (`Authenticate`), or sender policy (`Send`).
5. Complete DKIM and DMARC (section 11); revoke the old Gmail app password; optionally remove the inert Gmail User Secrets on the developer machine.

## 13. Blockers

None for the code and configuration work. External items pending: the Zoho app password, confirmation of the data-centre SMTP host, DKIM/DMARC publication, server `.env` update, and the live delivery test. Superseded by section 14.7.

## 14. Live verification rerun — 2026-11-02

Result: **ZOHO_SMTP_LIVE_TEST_FAILED**. Transport and TLS are proven; SMTP authentication for `no-reply@sapioai.ir` is rejected by Zoho. No email was delivered, no account was created, and no database was touched.

### 14.1 Configuration found (secrets redacted; passwords compared by SHA-256 only)

| Source | Host | User / sender | Password |
|---|---|---|---|
| Local `.env` (used only by `docker compose`) | `smtp.gmail.com` | Gmail address | Gmail app password (16 lowercase letters) |
| Server `.env` copy (MobaXterm), block at lines 517–524 | `smtp.gmail.com` | Gmail address | Gmail app password; **overridden** |
| Server `.env` copy, block at lines 531–538 (**wins**: later duplicate keys override) | `smtppro.zoho.com` | `no-reply@sapioai.ir` | 12-character value, the provisioned Zoho credential |
| `appsettings.json` (working tree, as edited) | `smtp.zoho.eu` | `no-reply@sapioai.ir` | Contained the **Gmail** app password in plaintext; removed (see 14.5) |
| `.env.example` (working tree, as edited) | `smtppro.zoho.com` | `no-reply@sapioai.ir` | Contained the Zoho credential in plaintext; restored to a placeholder (see 14.5) |

How each runtime loads configuration: the API has no `.env` loader. `.env` takes effect only through `docker compose` (`env_file` plus interpolation). `dotnet run` uses `appsettings.json` → `appsettings.Development.json` → User Secrets → optional `appsettings.Development.Local.json` (absent) → environment variables (no SMTP variables set in the shell).

Effective local `dotnet run` (Development) configuration, measured with the API's source order: `smtp.zoho.eu:587`, `StartTls`, user and sender `no-reply@sapioai.ir`, display name `Sapio AI`, timeout 15 s. `Authentication:Email:Enabled=false`, so local `dotnet run` does not send email. Since the 14.5 fix there is no password. Enabling email without supplying one fails startup validation by design.

The expected configuration (`smtppro.zoho.eu`) matches **no** source: local Docker resolves to Gmail, the server copy to `smtppro.zoho.com`, and `appsettings.json` to `smtp.zoho.eu`.

### 14.2 TCP connectivity (from the developer workstation)

| Endpoint | Result |
|---|---|
| `smtppro.zoho.eu:587` | connected (185.230.212.129) |
| `smtp.zoho.eu:587` | connected (185.230.212.164) |

Connectivity from the production server was not tested.

### 14.3 STARTTLS negotiation (application `MailKitSmtpTransport`, `SecureSocketOptions.StartTls`, default certificate validation)

| Host | Secure | Protocol | Cipher | AUTH offered after TLS |
|---|---|---|---|---|
| `smtppro.zoho.eu` | yes | TLS 1.3 | TLS_AES_128_GCM_SHA256 | LOGIN, PLAIN |
| `smtp.zoho.eu` | yes | TLS 1.3 | TLS_AES_128_GCM_SHA256 | LOGIN, PLAIN |

### 14.4 SMTP authentication and delivery

The probe used the application's own `SmtpEmailSender` and `MailKitSmtpTransport` (no fake), wrapped so the Send stage is refused: no approved test recipient is configured, so delivery was suppressed before `MAIL FROM`. User `no-reply@sapioai.ir`; password = the provisioned 12-character Zoho credential (read in-process, never printed). Attempts were deliberately limited to two to avoid a lockout:

| Attempt | Host | Stages | Result |
|---|---|---|---|
| 1 | `smtppro.zoho.eu` | Connect → Authenticate (failed) | `AuthenticationException` |
| 2 | `smtp.zoho.eu` | Connect → Authenticate (failed) | `535: 5.7.8 Authentication Failed` |

The Gmail app password configured in `appsettings.json` was **not** tried against Zoho: hash comparison proved it is the Gmail credential.

Both EU hosts reject the same credential after a successful TLS handshake, so the failure is not the hostname or the network. Likely causes, which only the Zoho account admin can check:
1. The 12-character value is not a valid **Application-Specific Password** for `no-reply@sapioai.ir` (for example, it is the account login password while 2FA is on, or it was generated under a different user).
2. SMTP access is disabled for this mailbox or organization (Zoho Mail Admin Console → Users → `no-reply@sapioai.ir` → Mail settings / IMAP-POP-SMTP access, and organization-level security policies).
3. The mailbox is not yet activated, or it was created after the password was issued.

Sender logging worked as designed: the log line was `Authentication email delivery failed. Stage: Authenticate; FailureType: AuthenticationException`, with no credentials, recipient, or server text.

| Check | Result |
|---|---|
| Actual email delivery | **Not attempted**: authentication failed and no approved recipient is configured |
| Registration verification (live) | **Not run**: depends on authentication and an approved recipient |
| Password reset (live) | **Not run**: depends on authentication and an approved recipient |

### 14.5 Automated tests and fixes

The first rerun of `Feature145SmtpEmailSenderTests` gave **10/12**. Two tests failed, and both correctly detected the working-tree edit of `appsettings.json`:
- a plaintext SMTP password in a tracked file (and the Gmail one at that);
- the host changed from `smtppro.zoho.com` to `smtp.zoho.eu`.

Minimum fixes applied:
- Removed `Authentication:Smtp:Password` from `appsettings.json`. Nothing is lost: the same Gmail value is still in the local and server `.env` files, and it is invalid for Zoho anyway.
- Restored `AUTH_SMTP_PASSWORD=<zoho-application-specific-password>` in `.env.example`. The same value is still in the server `.env` Zoho block.
- Kept the user's EU host. The shipped-config test now accepts either Zoho EU SMTP host (`smtppro.zoho.eu` or `smtp.zoho.eu`), matching the domain's EU data centre.

Neither secret reached Git history: `HEAD` and `origin/develop` contain no SMTP password.

| Suite | Result |
|---|---|
| `Feature145SmtpEmailSenderTests` (isolated runner; the main unit-test project still fails to compile because of unrelated files, see section 9) | **12/12 passed** |
| Auth integration (`OwnedIdentity*`, `NativeEnrollment*`, `*Authentication*`, `*PasswordReset*`, `*EmailVerification*`) | **49/50 passed**; the same unrelated credit-grant failure (25 expected vs 250 seeded; runs with email disabled) |

### 14.6 Is Gmail still effective locally?

- `dotnet run`: **no**. The host is Zoho; the inert root-level `Smtp:` User Secrets still hold Gmail values but are never bound.
- Local `docker compose`: **yes**. The local `.env` still sets `AUTH_SMTP_HOST=smtp.gmail.com` with the Gmail user, sender, and password. Despite the task premise, this file was not updated. It was left unchanged because no working Zoho credential exists to replace the operational value.
- Server `.env` copy: Zoho block wins, but with `smtppro.zoho.com` (US data centre; the domain is EU) and the rejected credential. **Deploying in this state would fail authentication.**

### 14.7 Remaining blockers and actions

1. **Zoho authentication (blocking).** In Zoho, confirm SMTP access is enabled for `no-reply@sapioai.ir`, generate a new Application-Specific Password for that user, and read the SMTP host from *Server Configurations Details* (expect `smtppro.zoho.eu` for a paid organization, `smtp.zoho.eu` for a free one).
2. **Secret hygiene.** The Zoho credential sat in a tracked template and the Gmail app password in tracked `appsettings.json`. Neither was committed, but both values appeared in the AI-assistant session transcript during verification, so treat both as exposed. Revoke the Gmail app password, and revoke the current Zoho value rather than reusing it; it fails anyway.
3. **Where to put the new password.** Local `dotnet run`: `dotnet user-secrets set "Authentication:Smtp:Password" "<value>" --project src/backend/FinancialCopilot.API`. Docker and server: `AUTH_SMTP_PASSWORD` in the Git-ignored `.env`. Never put it in `appsettings.json` or `.env.example`.
4. **Server `.env`.** Delete the Gmail block (lines 517–524) so only one `AUTH_SMTP_*` block remains, and set `AUTH_SMTP_HOST` to the EU host from step 1.
5. **Local `.env`.** Replace the Gmail `AUTH_SMTP_*` values the same way if local Docker should use Zoho.
6. **Approved test recipient.** Name one so the delivery, registration-verification, and password-reset live checks can run after step 1.
7. **DKIM and DMARC** remain open (section 11).

## Temporary Gmail SMTP Fallback

Status: **GMAIL_SMTP_FALLBACK_PENDING_CREDENTIALS**. Gmail is the configured SMTP endpoint for the API in local Development and Docker. Authentication email remains enabled. The previously configured Gmail app password was cleared because it is compromised; no replacement credential is provisioned, so email sending fails closed at SMTP configuration validation until an operator supplies a new app password. No email was sent during this change.

### Effective local configuration

`appsettings.json` and API User Secrets resolve to `smtp.gmail.com:587`, `StartTls`, user and sender `kamran.abdi@gmail.com`, display name `Sapio AI`, timeout 15 seconds, and `Authentication:Email:Enabled=true`. The Development JSON file has no conflicting Authentication SMTP block. User Secrets contain the same non-secret SMTP values; `Authentication:Smtp:Password` is unset. Provide a newly generated Gmail App Password locally with:

```powershell
dotnet user-secrets set "Authentication:Smtp:Password" "<new-gmail-app-password>" --project src/backend/FinancialCopilot.API
```

### Effective Docker configuration

`docker compose config` resolves the `api` service to `smtp.gmail.com:587`, `StartTls`, user and sender `kamran.abdi@gmail.com`, display name `Sapio AI`, and `Authentication__Email__Enabled=true`. The SMTP password is empty until set in the ignored `.env`. The worker has authentication email disabled and does not send these messages. The `.env` contains a single `AUTH_SMTP_*` block with a blank password; no Zoho endpoint or identity overrides Gmail. To enable Docker SMTP after provisioning the new password, set `AUTH_SMTP_PASSWORD=<new-gmail-app-password>` in `.env` and recreate the API container.

### Configuration and test changes

Changed configuration files: `src/backend/FinancialCopilot.API/appsettings.json`, `.env`, `.env.example`, and `docker-compose.yml`. Local User Secrets were updated for the active provider and email-enabled setting; no password was set. The SMTP configuration tests now assert the Gmail endpoint and sender identity. This report was updated; `appsettings.Development.json` was inspected and left unchanged because it has no SMTP overrides.

Verification results:

- `docker compose config --quiet`: passed; resolved `api` values are Gmail/587/StartTls and no password is present.
- SMTP configuration and sender tests: **12/12 passed** using an isolated test runner referencing the existing Feature 145 test file. Coverage includes Gmail appsettings binding, environment overrides, missing-password validation, STARTTLS enforcement, sender identity, and secret-safe failure behavior.
- The normal `FinancialCopilot.UnitTests` project could not compile because of existing unrelated errors in `CompanyDisclosureFeed112Tests.cs` (missing cache constructor argument) and `TahlilAppTopUpTests.cs` (missing `TopUpWindowResponse`).
- Registration verification and password reset both use the shared `IEmailSender` injected into `OwnedIdentityService`; both therefore use this same configured SMTP sender. No live flow or recipient test was run.
- No Gmail delivery is claimed or verified. A real test requires a newly provisioned credential and an approved test recipient.

### Remaining credential requirement and Zoho reactivation

An operator must revoke the exposed Gmail and Zoho app passwords and provision a **new** Gmail App Password. Set it only in API User Secrets for local `dotnet run` or `AUTH_SMTP_PASSWORD` in the ignored Docker `.env`. Do not add it to tracked settings, `.env.example`, source, or this report. Until then, missing credentials fail startup validation with email enabled; STARTTLS remains required.

To reactivate Zoho later, change configuration only: set the `Authentication:Smtp:Host`, `UserName`, and `SenderAddress` values in User Secrets (local) or `AUTH_SMTP_HOST`, `AUTH_SMTP_USERNAME`, and `AUTH_SMTP_SENDER_ADDRESS` in `.env` (Docker) to the mailbox and SMTP host returned by Zoho Server Configuration Details; keep port `587`, security `StartTls`, sender name `Sapio AI`, and email enabled; put a newly provisioned Zoho App Password in `Authentication:Smtp:Password` or `AUTH_SMTP_PASSWORD`; then restart `dotnet run` or recreate the API container. No code or provider implementation change is needed.

Final status: **GMAIL_SMTP_FALLBACK_PENDING_CREDENTIALS**
