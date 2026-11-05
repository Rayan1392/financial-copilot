# Feature 154: E-5 bank GroupId check per environment (T23 configuration check record)

**Why it matters (spec §6.1, E-5, AC-M01).** The template is chosen by GroupId alone. A company whose eligible `NoavaranEligibleCompanies` row has GroupId equal to `QuarterlyFinancialReports:BankingIndustryGroupId` gets `BankingBalanceSheet`; every other company gets `GeneralBalanceSheet`.

GroupIds can differ between environments. An unverified environment is therefore a release blocker **for that environment only**.

Feature 154 adds no configuration key; it reuses Feature 144's existing key.

**Where the effective value comes from** (corrected per F154-V-07):

| Source | Value |
|---|---|
| `src/backend/FinancialCopilot.API/appsettings.json` | `c96520f9-1d2e-444a-ab46-5bdffd3797d2` |
| **Override:** `docker-compose.yml:71` (api service) | `QuarterlyFinancialReports__BankingIndustryGroupId: ${QuarterlyFinancialReports__BankingIndustryGroupId:-c96520f9-…}`, i.e. taken from the server's `.env` file when it is set there, with the same GUID as the fallback |
| `.env.example:207` | Template value `c96520f9-1d2e-444a-ab46-5bdffd3797d2` |

The value that counts in a compose-deployed environment is therefore **that server's `.env` entry**, not the repository's `appsettings.json`. Each environment's check must read it.

**The view also differs per environment.**
- `docker-compose.yml:70` sets `Database__ApplyMigrationsOnStartup: "true"`, so a compose-deployed database (production) most likely carries the **migration's** `NoavaranEligibleCompanies` definition: 3 MarketIds, no INNER JOINs.
- Development carries a drifted definition: 4 MarketIds plus INNER JOINs (F154-V-02).
- The Development result therefore cannot stand in for any other environment. Each check must query **that environment's deployed view**, not the Development predicate and not the migration text.

## Environments found in the repository

| Environment | Source in the repo | E-5 status | Date | Method / check to run |
|---|---|---|---|---|
| **Development** (local `financial_copilot`, localhost:5432) | `appsettings.Development.json`, local PostgreSQL | **Verified** | 2026-11-04, re-checked 2026-11-05 | **Method:** read the deployed view definition from `pg_views` (4 MarketIds plus INNER JOINs to `IndustryGroups`/`Industries`; it differs from the migration, see F154-V-02). Then apply that live predicate over `Companies` as read-only `feature141_app_reader`.<br>**Result:** 452 has one eligible row with GroupId `c96520f9…`, matching the configuration. 167 (`97ac765e…`) and 290 (`baab0118…`) have one row each and are non-bank. The bank GroupId has 17 eligible rows.<br>The live V2 runs (`live-development-verification.md`) selected `BankingBalanceSheet` for 452 and `GeneralBalanceSheet` for 167, 290, 13097 and 12548.<br>Evidence: `T01-development-evidence.md`, `T01-queries.sql`, `live-development-discovery-output.txt`. |
| **Production** (`https://sapioai.ir`) | `.github/workflows/deploy-prod.yml` (tag `v*` → `ops/deploy/prod-deploy.sh`), `docs/deployment/deploy-prod-ci.md`; compose directory `/opt/sapio/financial-copilot`, PostgreSQL in the compose stack, `ApplyMigrationsOnStartup=true` | **PENDING** | — | Before enabling or releasing Feature 154 there, run the check below on the production database in a read-only session. Also read the effective `QuarterlyFinancialReports__BankingIndustryGroupId` from the server `.env` (or its absence, which means the compose default). |
| **Staging** | Documented by spec 139: `specs/139-tahlilapp-session-exchange/StagingReadinessChecklist.md` (`ASPNETCORE_ENVIRONMENT=Staging`, a staging PostgreSQL connection string) | **PENDING; existence to be confirmed by the owner** | — | If the environment exists: run the same check against its database and read its effective configuration (environment variable or secret store). If it does not exist: record "not deployed". |
| VPS `develop` deployment | `docs/deployment/deploy-develop-to-vps.md` (manual `git pull` of `develop` + `docker compose` build) | **PENDING, or same as production** | — | This document targets the same compose directory as the production script. The repository does not establish it as a separate database. If the owner confirms it is separate, run the same check there. Otherwise production's result covers it. |

## Check to run in each environment (read-only)

Use a read-only session, for example `PGOPTIONS='-c default_transaction_read_only=on'` with a role that can read the view. Never use a write-capable session for this check.

```sql
SHOW default_transaction_read_only;                                   -- must be: on

-- 1. The deployed view definition (it can differ from the migration; Development has 4 MarketIds + INNER JOINs).
SELECT definition FROM pg_views WHERE schemaname = 'public' AND viewname = 'NoavaranEligibleCompanies';

-- 2. Classification of the reference companies through the deployed view.
SELECT "ExternalCompanyId", "CompanySymbol", "GroupId", count(*) OVER (PARTITION BY "ExternalCompanyId") AS rows_per_company
FROM "NoavaranEligibleCompanies"
WHERE "ProviderName" = 'NoavaranCurrentApi' AND "ExternalCompanyId" IN ('452', '167', '290')
ORDER BY 1;

-- 3. How many eligible rows carry the configured bank GroupId.
SELECT count(*) FROM "NoavaranEligibleCompanies"
WHERE "ProviderName" = 'NoavaranCurrentApi' AND "GroupId" = 'c96520f9-1d2e-444a-ab46-5bdffd3797d2';
```

Also confirm the **effective** `QuarterlyFinancialReports:BankingIndustryGroupId` in that environment:

- **Compose deployments:** the server `.env` entry `QuarterlyFinancialReports__BankingIndustryGroupId`, which `docker-compose.yml:71` passes to the api container. If it is absent, the compose default `c96520f9-…` applies. `docker compose config` on the server shows the resolved value.
- **Other hosts:** the environment variable or secret store, which overrides `appsettings.json`.

Report only the GUID, never a secret.

Queries 1–3 must run against **the deployed view** in that database. A compose-deployed database most likely carries the migration's 3-MarketId definition, while Development carries the drifted 4-MarketId one.

**Pass when all of these hold:**

- `rows_per_company` = 1 for 452, 167 and 290.
- 452's GroupId equals the effective configured value.
- 167's and 290's GroupIds differ from it.
- Query 3 returns > 0.

**Otherwise:** keep Feature 154 disabled for that environment. Either correct the environment's configured GroupId (no code change needed) or record the discrepancy.

## Record

| Environment | Checked by | Date | View definition matches Development? | 452 = configured? | 167 / 290 non-bank? | Result |
|---|---|---|---|---|---|---|
| Development | Implementation Agent | 2026-11-04 / 2026-11-05 | n/a (Development is the baseline: 4 MarketIds + INNER JOINs) | Yes | Yes | **Verified** |
| Production | — | — | — | — | — | **PENDING** |
| Staging (spec 139) | — | — | — | — | — | **PENDING**: owner to confirm the environment exists |
