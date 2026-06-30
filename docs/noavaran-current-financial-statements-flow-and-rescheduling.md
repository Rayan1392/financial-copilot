# Noavaran Current API Financial Statements Flow, Rescheduling, and Root-Cause Notes

## Scope

This document explains how the system currently fetches financial statements and fundamental indexes from the Noavaran Amin current API, with special focus on these vendor endpoints:

- `api/v2/FS/IncomeStatement/Values`
- `api/v2/FS/BalanceSheet/Values`
- `api/v2/FS/CashFlow/Values`
- `api/v2/CompanyFundamentalIndex/Values`

It answers these questions from the implemented code:

1. Which service calls these endpoints
2. Whether they are called automatically, manually, or both
3. At what interval they are called
4. Which parameters and request bodies are used
5. Whether a previously completed run prevents a later re-fetch
6. Which code paths can explain why `12 ماهه اصلی` and `12 ماهه تلفیقی` for `غالبر` are not in the database even if the vendor returns them now

This is a documentation-only analysis. No code was changed.

## Short Answer

The system **does allow these endpoints to be called again** after a previous successful run. A prior completed NADPCO/current-API run is **not** a permanent "done forever" marker for a company-year.

However, there are two separate root-cause classes that can still explain missing `12 ماهه` rows:

1. The company may never be re-enqueued for current-API per-company fetches:
   - excluded from `NoavaranCompanyScope`
   - no successful rerun since the vendor added the new statement
   - request failed during processing

2. Even when the vendor returns multiple variants for the same period, the current financial-statement normalizer **does not persist all variants**:
   - it groups by `(StatementType, Company, PeriodType, PeriodEnd)`
   - then keeps only one row
   - current selection prefers audited, then non-represented, then consolidated

So:

- "completed once, never called again" is **not** the current behavior
- but "vendor returned both parent and consolidated, system kept only one" **is** current behavior

## Main Flow

### Public and admin entry points

The main current-API flows are:

1. Automatic worker-based scheduled sync
2. Manual scheduled sync trigger
3. Manual full current-API backfill

Relevant entry points:

- [NadpcoScheduledSyncWorker.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Worker/NadpcoScheduledSyncWorker.cs:12)
- [AdminDataOperationsController.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs:741)
- [AdminDataOperationsController.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs:444)

### Automatic scheduled sync

The background worker:

- wakes up every `CadenceSeconds`
- checks current scheduled-sync status
- runs a new sync when due

Code:

- [NadpcoScheduledSyncWorker.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Worker/NadpcoScheduledSyncWorker.cs:14)
- [NadpcoScheduledSyncWorker.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Worker/NadpcoScheduledSyncWorker.cs:17)
- [NadpcoScheduledSyncWorker.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Worker/NadpcoScheduledSyncWorker.cs:32)

Default cadence in appsettings:

- [appsettings.json](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/appsettings.json:133)

Current default:

- `Enabled = true`
- `CadenceSeconds = 86400`

So the default automatic schedule is **once every 24 hours**.

### Manual scheduled sync

DataAdmin can manually trigger the same scheduled-sync coordinator through:

- `POST /api/v1/admin/nadpcoapi/scheduled-sync/run`

Code:

- [AdminDataOperationsController.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs:741)

The request body is only:

```json
{
  "reason": "optional text"
}
```

Contract:

- [AdminDataOperationsContracts.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Contracts/AdminDataOperationsContracts.cs:273)

Important behavior:

- manual run uses `TriggerSource = Manual`
- manual run sets `Force = true`

So manual trigger can run even if scheduled sync is disabled in config, as long as another active run is not already holding the lease.

### Manual current-API backfill

There is also a separate DataAdmin endpoint:

- `POST /api/v1/admin/noavaran-current/backfill`

Code:

- [AdminDataOperationsController.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs:444)

Request contract:

- [AdminDataOperationsContracts.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Contracts/AdminDataOperationsContracts.cs:138)

Body:

```json
{
  "fromShamsiYear": 1400
}
```

This path calls the same current-API orchestrator in **full reload** mode:

- [CurrentApiIngestion.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CurrentApiIngestion.cs:106)
- [CurrentApiIngestion.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CurrentApiIngestion.cs:108)

This matters because if `غالبر` is eligible and you need to force a re-fetch of historical statement coverage, this is the current manual path that re-enqueues company-scoped statement/index requests across the current-API company scope.

## Scheduled Sync Orchestrator

The scheduled-sync coordinator does two things:

1. optional company catalog refresh
2. per-company incremental current-API sync

Code:

- [NadpcoScheduledSync.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoScheduledSync.cs:408)

It reads `DatasetSelection` and decides:

- `CompanyCatalog`
- everything else through `ExecuteAsync(fullReload: false, ...)`

Code:

- [NadpcoScheduledSync.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoScheduledSync.cs:412)
- [NadpcoScheduledSync.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoScheduledSync.cs:422)
- [NadpcoScheduledSync.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoScheduledSync.cs:427)

Default datasets in config/options are:

- `CompanyCatalog`
- `Symbols`
- `FinancialStatements`
- `FundamentalIndexes`
- `MonthlyProductionSales`

Code:

- [NadpcoScheduledSync.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoScheduledSync.cs:20)
- [appsettings.json](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/appsettings.json:137)

## Which Companies Are Queried

This is a critical filter.

Per-company current-API requests do **not** run against every company in the NADPCO company catalog. They run only against the authoritative eligibility scope:

- `PrecedencyRight = 0`
- `MarketId` in بورس / فرابورس / بازار پایه

Code:

- [NoavaranCompanyScope.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NoavaranCompanyScope.cs:7)
- [NoavaranCompanyScope.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NoavaranCompanyScope.cs:31)

The scheduled current-API service enumerates companies only through:

- [NadpcoApiScheduledSyncService.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiScheduledSyncService.cs:301)

That means:

- company catalog refresh is unscoped
- statement/index/monthly per-company calls are scoped

If `غالبر` is not inside `NoavaranEligibleCompanies`, then:

- the system can know `غالبر` exists in the company catalog
- but still never enqueue the per-company statement endpoints for it

That is one possible explanation for missing `12 ماهه` rows.

## Exact Vendor Calls

### Current provider defaults

Provider config defaults:

- [NadpcoApiProviderOptions.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiProviderOptions.cs:40)
- [appsettings.json](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/appsettings.json:111)

Current defaults are:

- `StatementFromYear = 1400`
- `StatementToYear = null`
- `StatementPeriodTypeId = null`
- `StatementIsAudited = null`
- `StatementIsRepresented = null`
- `StatementIsComposing = null`
- `FundamentalIndexFromYear = 1400`
- `FundamentalIndexToYear = null`
- `FundamentalIndexPeriodTypeId = null`
- `FundamentalIndexIsAudited = null`
- `FundamentalIndexIsRepresented = null`
- `FundamentalIndexIsComposing = null`
- `OrchestrationOverlapDays = 7`

Because the nullable values are omitted from the query string, the current system effectively calls:

- statements with `?fromYear=1400`
- fundamental indexes with `?fromYear=1400`

and does **not** add:

- `toYear`
- `perTId`
- `isAudited`
- `isRepresented`
- `isComposing`

unless those settings are explicitly changed.

### Income statement

Endpoint builder:

- [NadpcoApiDataProviderClient.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:62)
- [NadpcoApiDataProviderClient.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:340)

Body contract:

- [NadpcoApiPayloadModels.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiPayloadModels.cs:79)

Mapped item ids:

- [NadpcoApiStatementItemMaps.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiStatementItemMaps.cs:9)

Current code sends, for company `30`, effectively:

```http
POST api/v2/FS/IncomeStatement/Values?fromYear=1400
Content-Type: application/json

{
  "companyIds": [30],
  "items": [15, 300, 143, 140, 139, 160, 168, 12, 336]
}
```

Important:

- the system does **not** currently send `"items": []`
- it sends only the curated item ids needed by the product
- but because `toYear`, `perTId`, and variant flags are omitted, the vendor can still return all available matching periods/variants for those items from year `1400` onward

### Balance sheet

Endpoint builder:

- [NadpcoApiDataProviderClient.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:56)

Mapped item ids:

- [NadpcoApiStatementItemMaps.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiStatementItemMaps.cs:24)

Current code sends:

```http
POST api/v2/FS/BalanceSheet/Values?fromYear=1400
Content-Type: application/json

{
  "companyIds": [30],
  "items": [147, 188]
}
```

### Cash flow

Endpoint builder:

- [NadpcoApiDataProviderClient.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:68)

Mapped item ids:

- [NadpcoApiStatementItemMaps.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiStatementItemMaps.cs:34)

Current code sends:

```http
POST api/v2/FS/CashFlow/Values?fromYear=1400
Content-Type: application/json

{
  "companyIds": [30],
  "items": [1]
}
```

### Curated fundamental indexes

Endpoint builder:

- [NadpcoApiDataProviderClient.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:179)
- [NadpcoApiDataProviderClient.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:369)

Body contract:

- [NadpcoApiPayloadModels.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiPayloadModels.cs:83)

Mapped index ids:

- [NadpcoApiFundamentalIndexMap.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiFundamentalIndexMap.cs:17)

Current code sends:

```http
POST api/v2/CompanyFundamentalIndex/Values?fromYear=1400
Content-Type: application/json

{
  "companyIds": [30],
  "companyIndexIds": [65, 4069, 4071, 4100, 4101, 4106, 4117, 41105]
}
```

This is the recurring product path for governed/currently reviewed indexes.

### Coverage catch-up fundamental indexes

There is also a separate DataAdmin catch-up flow for the same endpoint that requests **all** vendor indexes, not just the curated allowlist.

Code:

- [NadpcoApiDataProviderClient.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:195)

That path sends:

```http
POST api/v2/CompanyFundamentalIndex/Values?fromYear=1403&toYear=1405
Content-Type: application/json

{
  "companyIds": [30],
  "companyIndexIds": []
}
```

Important:

- this is a **separate** coverage/catch-up flow
- it writes the non-scannable coverage table, not the curated `DerivedMetrics` read path
- it is not the recurring financial statement ingestion path

## Does a Completed Run Block Future Re-fetch?

## Orchestration run level

No.

`NadpcoScheduledSyncRuns` records orchestration history and scheduling state:

- [NadpcoScheduledSync.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoScheduledSync.cs:51)

This table tracks:

- when a scheduled/manual run started
- whether it completed
- whether another run is active
- last successful execution time

It does **not** permanently mark `(company, year)` as completed and suppress all future vendor calls.

## Child data-sync request level

Also no, for new runs.

Per-company work is persisted in `ProviderSyncRuns` and deduplicated only by **exact idempotency key**:

- [FinancialDataSyncProcessor.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs:52)
- [FinancialIngestionConfigurations.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs:182)

But the NADPCO/current-API orchestrator generates a **new timestamped idempotency key on every run**:

- [NadpcoApiScheduledSyncService.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiScheduledSyncService.cs:366)
- [NadpcoApiScheduledSyncService.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiScheduledSyncService.cs:380)

The key includes:

- dataset
- company id
- run start timestamp
- overlap stamp
- optional backfill suffix
- optional monthly window suffix

So a later scheduled/manual run creates a different key and is allowed to execute again.

## Important nuance

The current incremental service does **not** narrow statement/index vendor calls with a vendor-side modified-since filter.

The `overlapFrom` timestamp is used for run bookkeeping and idempotency-key generation:

- [NadpcoApiScheduledSyncService.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiScheduledSyncService.cs:58)
- [NadpcoApiScheduledSyncService.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiScheduledSyncService.cs:289)

But the actual vendor statement/index endpoints are still built from `fromYear`/`toYear` settings, not from that overlap time:

- [NadpcoApiDataProviderClient.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:340)
- [NadpcoApiDataProviderClient.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:369)

So for statements and curated fundamental indexes, the current system effectively re-fetches the configured year range again on each run.

## Why "called once, then never again" is not the current behavior

For a company like `30`, if the system schedules or manually triggers a new run today, the statement endpoint can be called again with a new request key, for example conceptually:

```text
nadpcoapi-sync-FinancialStatements-30-20260630090000-20260623090000
nadpcoapi-sync-FinancialStatements-30-20260701090000-20260624090000
```

Those are different runs, so they are not blocked by the prior completed record.

Therefore, if the vendor now returns a new `12 ماهه`, the current architecture **does allow** a future re-fetch to bring it in, provided the company is actually re-enqueued and the request/normalization succeeds.

## High-Risk Root Cause: Variant Collapse in the Financial Statement Normalizer

This is a major behavior in the current code.

The normalizer does **not** persist every returned statement variant as separate rows. Before writing rows, it first applies:

- [NadpcoApiStatementSelectionPolicy.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiStatementSelectionPolicy.cs:12)

It groups by:

- `StatementType`
- `ComID`
- `PeriodType`
- `PeriodEnd`

Code:

- [NadpcoApiStatementSelectionPolicy.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiStatementSelectionPolicy.cs:18)

Then from each group it keeps only **one** row:

- audited first
- non-represented first
- then `preferComposing = true`, so consolidated is preferred over non-consolidated
- then latest announcement date
- then highest statement id

Code:

- [NadpcoApiStatementSelectionPolicy.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiStatementSelectionPolicy.cs:26)
- [NadpcoApiStatementSelectionPolicy.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiStatementSelectionPolicy.cs:29)

The normalizer then persists only the selected rows:

- [NadpcoApiFinancialStatementNormalizer.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiFinancialStatementNormalizer.cs:31)
- [NadpcoApiFinancialStatementNormalizer.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiFinancialStatementNormalizer.cs:34)

### Consequence

If the vendor returns both:

- `12 ماهه اصلی`
- `12 ماهه تلفیقی`

for the same company, statement type, and period end, the current normalizer will **not** store both.

It will keep only one.

By current ordering, the consolidated row is preferred over the standalone row when the other higher-priority conditions tie.

### Important clarification

The database unique key itself is **not** the thing collapsing variants.

The table natural key is:

- `(ProviderName, ExternalStatementId, StatementType)`

Code:

- [FinancialIngestionConfigurations.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs:96)

So if both variants had been allowed through the selection stage and they had distinct `statementID` values, the table could store both.

The collapse happens **earlier**, in `NadpcoApiStatementSelectionPolicy.SelectAll(...)`.

## Root-Cause Branches for `غالبر`

If the vendor now returns `12 ماهه اصلی` and `12 ماهه تلفیقی` for `غالبر`, but the database does not contain them, current code leaves these main explanations:

### Branch 1: `غالبر` is not being re-enqueued at all

Possible reasons:

- `غالبر` is outside `NoavaranCompanyScope`
- the company catalog row has `PrecedencyRight != 0`
- the company catalog row has `MarketId` outside بورس / فرابورس / بازار پایه

Effect:

- no per-company statement request is issued
- so the new 12-month rows never reach normalization

### Branch 2: `غالبر` is eligible, but no successful rerun happened after the vendor published 12-month data

Because statement calls are rerunnable, this is possible if:

- the worker did not run
- scheduled sync was disabled for some time
- runs were skipped due to active lease
- company batch enqueue failed
- downstream worker processing failed

### Branch 3: `غالبر` was re-fetched, but the normalizer kept only one variant

If the vendor returned both parent and consolidated `12 ماهه`, the current statement selection policy would collapse them and keep only one.

So even after a successful rerun, the current architecture still cannot preserve both variants side by side from the same current-API fetch.

This is especially relevant if the business expectation is:

- both `اصلی`
- and `تلفیقی`

must be queryable from persisted Noavaran current-API rows.

### Branch 4: request succeeded but the specific 12-month row still was not in the returned vendor payload at that time

Because the system fetches from the vendor at run time, if a prior run happened before the vendor added the 12-month row, the database can legitimately stop at `3/6/9 ماهه` until a later rerun occurs.

## What the current code proves

The current code proves these points:

1. A previously completed NADPCO/current-API run does **not** permanently block future financial-statement or fundamental-index calls for the same company/year.
2. Scheduled and manual runs both generate new company-scoped child requests with new idempotency keys.
3. Current statement requests are effectively broad year-range fetches from `fromYear=1400` onward, not "one year once only forever".
4. The current statement normalizer collapses multiple same-period variants into one persisted row before storage.
5. Company eligibility scope can prevent any per-company current-API request from ever being sent for a symbol that still exists in the catalog.

## Most important conclusion for the `غالبر` investigation

If the question is:

> "Is the system failing because it called `api/v2/FS/.../Values` once in the past, marked that year completed, and will now never call it again?"

the answer from current code is:

**No. That is not how the current system works.**

If the question is:

> "Can the current system still fail to have both `12 ماهه اصلی` and `12 ماهه تلفیقی` in the database even when the vendor returns them now?"

the answer is:

**Yes.**

And the strongest code-backed reasons are:

1. the company may not be in `NoavaranCompanyScope`, so no per-company request is sent
2. no successful rerun may have happened after the vendor published the row
3. even if both variants are returned, `NadpcoApiStatementSelectionPolicy.SelectAll(...)` collapses them to one row before persistence

## Relevant Files

- [NadpcoScheduledSyncWorker.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Worker/NadpcoScheduledSyncWorker.cs:12)
- [NadpcoScheduledSync.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoScheduledSync.cs:266)
- [NadpcoApiScheduledSyncService.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiScheduledSyncService.cs:47)
- [NoavaranCompanyScope.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NoavaranCompanyScope.cs:16)
- [NadpcoApiDataProviderClient.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:48)
- [NadpcoApiProviderOptions.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiProviderOptions.cs:10)
- [NadpcoApiPayloadModels.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiPayloadModels.cs:79)
- [NadpcoApiStatementItemMaps.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiStatementItemMaps.cs:3)
- [NadpcoApiFundamentalIndexMap.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiFundamentalIndexMap.cs:13)
- [NadpcoApiFinancialStatementNormalizer.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiFinancialStatementNormalizer.cs:19)
- [NadpcoApiStatementSelectionPolicy.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiStatementSelectionPolicy.cs:10)
- [FinancialDataSyncProcessor.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs:47)
- [FinancialIngestionConfigurations.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs:86)
- [AdminDataOperationsController.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs:444)
- [AdminDataOperationsController.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs:741)
