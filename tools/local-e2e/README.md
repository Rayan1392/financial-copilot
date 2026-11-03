# Feature 139 local browser-to-browser scenario

This stack uses three local HTTPS services and a dedicated PostgreSQL database. It sets only child-process environment variables; it does not write to appsettings, production credentials, or any existing database. The local API key, TLS export and Data Protection keys are created under ignored `tools/local-state/`.

## Prerequisites

- Windows 10/11, PowerShell 7, .NET 10 SDK, Node.js/npm, Docker Desktop (Linux containers), and a browser.
- From an elevated PowerShell terminal (one time), trust the .NET development certificate:

```powershell
dotnet dev-certs https --trust
```

- Install frontend packages once:

```powershell
Set-Location D:\Source\TahlilApp-AI\src\frontend
npm ci
```

## Start the isolated stack

In a PowerShell 7 terminal:

```powershell
Set-Location D:\Source\TahlilApp-AI
.\tools\local-e2e\Start-LocalStack.ps1
```

Startup creates and starts `sapioai_feature139_local` in its own Docker volume on `127.0.0.1:55439`, then starts the mock at `https://localhost:7443`, SapioAI API at `https://localhost:7044`, and the real SapioAI frontend at `https://localhost:5173`. The API applies the repository’s existing migrations to this isolated database at startup. Feature 141 and its production data worker are disabled in this local process. Data Protection keys persist under `tools/local-state/dataprotection`.

The mock UI opens at `https://localhost:7443`. If the browser warns about a certificate, rerun `dotnet dev-certs https --trust` and restart the browser.

## Browser scenario

1. Open the mock UI and select one of the synthetic users.
2. Choose Free, Plus or Pro; set the status and subscription dates; keep `effectiveFrom` in the past and `expiresAt` in the future for an active paid plan.
3. Leave launch simulation set to “کد معتبر (یک‌بار مصرف)”. Click **ورود به SapioAI**.
4. The browser follows the synthetic TahlilApp launch URL to the real `/launch?code=…` route. SapioAI exchanges the code over HTTPS, creates or resolves the same local identity, syncs Billing, establishes the ordinary SapioAI refresh-cookie/JWT session, then navigates to `/chat`.
5. Refresh or repeat the launch with that synthetic user to confirm the external ID resolves to the same local account. Use the other simulation options for invalid, expired, and already-consumed codes.
6. Change the subscription and increment `revision`, then wait for the local revalidation worker (10-second poll; one-minute TTL) or launch again to observe the change in the existing account.
7. Try setting `revision` to an older value to exercise stale revision handling. Use the failure controls to return HTTP 429/5xx or delay the exchange/status response beyond the gateway timeout.

All names, IDs, national IDs and mobiles are synthetic constants in the mock. Do not enter real personal data. The mock never logs request bodies, codes, API keys or profile values.

## Contract smoke test

After `/health` responds, run in another PowerShell 7 terminal:

```powershell
Set-Location D:\Source\TahlilApp-AI
.\tools\local-e2e\Test-MockContract.ps1
```

It makes real HTTPS calls and checks Free/Plus/Pro v2 payloads, `effectiveFrom`/revision, PII-free status, wrong-key denial, required v2 negotiation, expiry and replay rejection. The browser steps above exercise `/launch` through `/chat` against the real API and frontend; identity and Billing use PostgreSQL with the existing migrations.

The current development host has PostgreSQL services but no Docker Desktop Linux engine, so the dedicated-container database and full browser scenario could not be run here. Start Docker Desktop before running the full stack.

Logs are at `tools/local-state/{mock,api,frontend}.log`; the mock avoids logging codes, credentials and personal values. Inspect the API log by correlation ID only.

## Stop and reset

Stop the services while retaining the local database:

```powershell
Set-Location D:\Source\TahlilApp-AI
.\tools\local-e2e\Stop-LocalStack.ps1
```

To reset the database volume too, after stopping:

```powershell
docker compose -f .\tools\local-e2e\docker-compose.yml down --volumes
```

This removes only the dedicated `feature139-postgres-data` volume. Never point this stack at a shared or production database.
