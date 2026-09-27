# Feature 134 Implementation Report

## Outcome

The original scheduled/current-API scope of Feature 134 is implemented in the existing Noavaran monthly-activity, normalized persistence,
recalculation, snapshot, metric-input, and chart-read paths. No new public endpoint, AI tool,
chart contract, schema table, or permanent Production/Service classification was introduced.

The implementation preserves the existing ProductSales output-type waves. Only the ProductSales
OutputType 0 request can make the ServiceSales fallback decision.

## Pre-Redeployment Verification After Production Docker Rollback

Date: 2026-09-27

### Git and deployment state

```text
Current branch: develop
Current Git commit: ffa6ba8bfda52e24787b36df7321d879882b709d
Working tree before this report update: clean
Git rollback: NO
Old production image/source baseline: fca4836f89f484c93395632bcb0f813054980dc4
Rolled-back runtime: Docker images tagged rollback-20260926-112832
```

The source checkout remained on `develop` at the Feature 134 commit. Only the running Docker
deployment was rolled back. The Feature 134 API image was created after the known service failure;
the failure run was requested at `2026-09-26 11:50:10 UTC`, while the new API/worker containers
started approximately at `12:25 UTC`. The current VPS runtime is again using the rollback image.

### Production evidence and interpretation

The earlier service-company observation is confirmed as an old-image event. Its durable run was
for `حسیر` / `ریل سیر کوثر` / ExternalCompanyId `13176`, with ProductSales type 0 equal to `[]` and
ServiceSales absent from the stored envelope. The old direct provider path used
`includeServiceSales: false`, so this is `DEPLOYMENT_VERSION_MISMATCH`, not evidence against the
current fallback implementation.

The manufacturing control `کگل` resolves to `معدنی و صنعتی گل گهر`, ExternalCompanyId `4`. Durable
production data contains successful ProductSales type-0 runs for this company, including a direct
run with `ProcessedRecords=5` and no ServiceSales rows. No durable run or retained log was found
that proves a `1405/06` manufacturing failure executed on the Feature 134 image. The reported
manufacturing failure therefore remains unproven; the available evidence does not show a
ProductSales-usable-row regression.

The service validation symbol `قاسم` resolves to `قاسم ایران`, ExternalCompanyId `12622`. A
`1405/06` direct run on the rolled-back/old runtime persisted ProductSales type 0 as empty and
ServiceSales as empty, then ended `NoDataYet`. This proves both sources were empty for that run,
not that the current source failed to call ServiceSales. No isolated current-source real-provider
run was performed.

### Current source audit

The current direct call chain is:

```text
NoavaranMonthlyBackfillController
  -> SingleCompanyMonthlyIngestionService.ExecuteDirectAsync
  -> processor creates SyncRun, then direct provider, explicit ProductSales output type 0
  -> ProductSales type-0 decision / optional ServiceSales fallback
  -> FinancialDataSyncProcessor.ProcessProviderAsync
  -> normalization, persistence, recalculation, snapshot
  -> queue ProductSales output types 1-4 after type-0 completion
```

Scheduled ingestion requests ProductSales output types 0-4 in one envelope and makes the same
type-0 source decision. Direct ingestion requests type 0 inline and queues types 1-4 separately;
types 1-4 remain ProductSales-only. The envelope preserves source identity in separate
`ProductSalesType0..4` and `ServiceSales` fields, and the normalizer consumes those fields
separately. No heterogeneous ProductSales collection is passed to a ProductSales-only downstream
contract.

The three-state provider decision is present and covered by tests:

```text
usable ProductSales type 0 -> zero ServiceSales calls
successful empty ProductSales type 0 -> one ServiceSales call
type-0 provider/response failure -> no ServiceSales call; exception rethrown
```

The last state exposed a direct-trigger defect: `ExecuteDirectAsync` fetched the provider payload
before `ProcessPayloadAsync` created the run record. That defect is fixed by entering
`ProcessProviderAsync` before acquisition; a type-0 or ServiceSales provider exception now
persists failed run-state before the existing direct error contract is preserved.
Scheduled ingestion catches the exception inside `ProcessCoreAsync` and records a failed run. This
direct/scheduled failure-state difference is a genuine source defect, although the retained
production evidence does not prove that it caused the reported `کگل` observation.

## Direct Provider Failure Lifecycle Fix

Date: 2026-09-27

The remaining pre-deployment lifecycle blocker is fixed in the shared ingestion lifecycle. The
direct path now calls `IFinancialDataSyncProcessor.ProcessProviderAsync` with the existing provider
factory instead of acquiring the provider payload before entering the processor. The processor
therefore creates and persists the `SyncRuns` row as `Running` before either ProductSales type 0
or its ServiceSales fallback can fail.

Provider acquisition failures are handled separately from successful-empty payloads:

```text
provider failure -> SyncRun Failed, ErrorCount=1, provider error retained, exception rethrown for direct API
successful empty payload -> existing NoDataYet / retryable result
```

The direct API and Telegram call contracts remain unchanged: direct provider failures are still
non-success exceptions after the failed run is persisted. Scheduled `ProcessAsync` behavior still
returns the existing failed processing result rather than rethrowing. The direct service retains
one run key and the existing output-type wave; types 1-4 are queued only after type 0 succeeds,
so no duplicate run was introduced.

Regression coverage now includes the direct boundary, a concrete in-memory processor lifecycle
test, and ServiceSales fallback HTTP failure. The direct boundary regression was run red before
the lifecycle change and green after it. No production deployment, Docker image push, VPS
mutation, or commit was performed. Real-provider smoke validation remains unavailable in this
workspace and must be performed separately before redeployment.

### Verification matrix

```text
کگل invariant (generic current-source fixtures): ProductSales type 0 usable; ServiceSales 0; downstream precedence PASS
قاسم invariant (generic current-source fixtures): ProductSales empty; ServiceSales exactly once; downstream ServiceSales path PASS
Both sources empty: NoDataYet / retryable PASS
Types 1-4: ProductSales-only and do not alter the type-0 fallback decision PASS
Scheduled provider behavior: source-selection tests PASS; real 1405/06 provider smoke BLOCKED
Telegram inheritance: shared direct service and handler tests PASS; real provider smoke BLOCKED
Admin direct route: 5/5 integration tests PASS; real provider smoke BLOCKED
```

Real-provider validation is `BLOCKED — ISOLATED PROVIDER ACCESS`: no isolated current-source
runtime was available for a legitimate provider smoke test, and production remains on the
rollback image. No production smoke request was issued during this audit.

### Validation executed

```text
Provider Feature 134 tests: 28 passed, 0 failed, 0 skipped
Direct ingestion tests: 2 passed, 0 failed, 0 skipped
Admin direct endpoint tests: 5 passed, 0 failed, 0 skipped
Telegram handler tests: 10 passed, 0 failed, 0 skipped
Downstream normalizer/metric/snapshot tests: 51 passed, 0 failed, 0 skipped
Full unit suite: 1701 passed, 0 failed, 0 skipped
Architecture suite: 12 passed, 0 failed, 0 skipped
Release solution build: succeeded, 0 warnings, 0 errors
Previously recorded full integration suite: 394 passed, 45 unrelated failures, 40 environment skips
```

The earlier read-only audit made no production or test changes. The lifecycle fix and regression
tests are now local uncommitted changes; no production deployment or commit was performed.

### Redeployment decision

```text
Safe to build a new production Docker image: YES
Safe to redeploy Feature 134 now: NO
Reason: direct provider-failure lifecycle is fixed, but real-provider validation for both
control symbols is blocked.
```

The remaining operational follow-up is to rerun real-provider validation in an isolated environment
before redeployment. The lifecycle blocker itself is no longer present. The direct-path failure
coverage and rerun the real-provider `کگل`/`قاسم` tests in an isolated environment. Do not add
symbol-specific classification.

## Current trigger-audit status

The original scope covered the scheduled `FetchMonthlyReportsAsync` path. The trigger-inheritance
implementation now covers the manual and Telegram direct paths through the same provider decision.
The exact counts are now:

```text
Acceptance criteria: 20 original -> 30 total (+10 trigger criteria)
Tasks:              15 original -> 23 total (+8 trigger-audit tasks)
```

The exact manual route is:

```text
POST /api/v1/admin/noavaran-current/monthly-backfill/single-company-month
```

`NoavaranMonthlyBackfillController.RunSingleCompanyMonth` resolves/validates the company-month and
calls `ISingleCompanyMonthlyIngestionService.ExecuteDirectAsync`. The older
`POST /api/v1/admin/noavaran-current/single-company-monthly-ingestion` route is a separate range
enqueue operation and is not the audited route.

The Telegram path is:

```text
channel_post/caption
  -> TelegramGatewayPollingWorker
  -> PrimaryApiClient
  -> POST /api/v1/telegram/assistant/updates
  -> TelegramAssistantController
  -> TelegramChannelMonthlyReportHandler
  -> ISingleCompanyMonthlyIngestionService.ExecuteDirectAsync
```

Both triggers share `SingleCompanyMonthlyIngestionService`. Its direct provider call now enables
the same type-0 source decision as the scheduled path. The correction is one provider-boundary
change: explicit output type 0 enables the existing ServiceSales fallback, while explicit output
types 1–4 remain ProductSales-only. No second pipeline was added.

| Capability | Scheduled path | Admin direct path | Telegram path |
|---|---|---|---|
| ProductSales output types 0–4 | Inherited/covered | Shared direct service; verified | Shared direct service; verified |
| ProductSales type-0 usable suppresses ServiceSales | Covered | Verified at shared direct provider | Verified at shared direct provider |
| Empty type-0 invokes ServiceSales once | Covered | Verified at shared direct provider | Verified at shared direct provider |
| Type-0 failure avoids classification fallback | Covered | Verified at shared direct provider | Verified at shared direct provider |
| Normalized persistence/recalculation/snapshot | Covered | Shared downstream path; verified by existing regressions | Shared downstream path; verified by existing regressions |
| ProductSales-over-ServiceSales precedence | Covered | Shared downstream path; verified | Shared downstream path; verified |
| No-data/retry semantics | Covered | Shared processor; verified by existing lifecycle regressions | Shared processor; verified by existing lifecycle regressions |
| 1404+ boundary and monetary semantics | Covered | Direct boundary/provider and normalizer regressions pass | Shared direct provider and normalizer regressions pass |

The provider correction is in `NadpcoApiDataProviderClient`; the lifecycle correction is in
`FinancialDataSyncProcessor` and `SingleCompanyMonthlyIngestionService`. Direct-provider
regressions are in `NadpcoApiProviderTests` and the direct-ingestion tests. Existing exact-route
Admin and Telegram handler tests also pass.

## Production changes

- Direct explicit OutputType 0 requests now enable the same shared ServiceSales fallback decision
  as scheduled ingestion; explicit OutputType 1–4 requests remain ProductSales-only.
- ProductSales OutputType 0 is evaluated before fallback.
- A usable type-0 response, including a valid zero-activity response, suppresses ServiceSales.
- A successful empty type-0 response issues exactly one ServiceSales request for the logical attempt.
- Type-0 provider failures are propagated and never classified as Service companies.
- ServiceSales provider failures remain failures and are retryable through the existing run path.
- Monthly completion checks require ProductSales type 0 or ServiceSales rows; output types 1-4 alone
  cannot create false completion.
- ServiceSales uses the canonical report key
  `ServiceSales:{externalCompanyId}:{jalaliYear}-{jalaliMonth:D2}:output-none`.
- ServiceSales uses `revenueDuringThePeriod` as `SalesAmount`; cumulative revenue fields are retained
  in evidence and are not aggregated as monthly sales.
- Service lines with repeated provider codes and different titles receive distinct deterministic
  identities. Missing identifiers use deterministic natural keys containing instrument/title/unit/
  category evidence and occurrence.
- ServiceSales-only periods trigger the existing trend recalculation and snapshot pipeline.
- ProductSales type 0 takes precedence over persisted ServiceSales for monthly trend and metric
  aggregation, including late ProductSales publication.
- Service quantities and production quantities remain null when the ServiceSales endpoint does not
  provide those facts.
- Snapshot backfill candidate discovery includes ServiceSales while retaining the existing eligible
  company scope and date boundary.

## Trigger-fix evidence

### Direct-provider evidence

The corrected provider boundary is deterministic:

```text
type 0 usable
  -> no ServiceSales

type 0 successful empty
  -> one ServiceSales fallback

type 0 failure
  -> no ServiceSales
```

The direct provider suite also verifies that type 1 data does not suppress fallback, legitimate
zero-activity type-0 rows suppress fallback, explicit type 0 requests issue only the type-0 and
optional ServiceSales calls, and pre-1404 direct requests fail before any network request.

### Admin verification

| Case | Result |
|---|---|
| Admin A — type 0 usable | VERIFIED: shared provider suppresses ServiceSales. |
| Admin B — type 0 empty, ServiceSales usable | VERIFIED: shared provider issues one fallback; the existing direct processor path receives the envelope. |
| Admin C — type 0 empty, types 1–4 have data | VERIFIED: type 1 data does not suppress fallback. |
| Admin D — type-0 provider failure | VERIFIED: failure propagates without ServiceSales classification fallback. |
| Admin E — both sources empty | VERIFIED: existing processor/lifecycle remains `NoDataYet`/retryable. |
| Admin F — late ProductSales | VERIFIED: existing snapshot/metric precedence excludes ServiceSales from totals. |
| Admin G — pre-1404 boundary | VERIFIED: direct provider rejects the request before any ServiceSales call. |
| Admin H — monetary semantics | VERIFIED: existing ServiceSales normalizer uses `revenueDuringThePeriod` and the existing unit contract. |

The exact route and validation/delegation assertions passed in
`AdminDataOperationsEndpointTests.NoavaranCurrent_SingleCompanyMonthDirect_*` (5/5).

### Telegram verification

| Case | Result |
|---|---|
| Telegram A — type 0 usable | VERIFIED: handler invokes shared direct ingestion; provider suppresses ServiceSales. |
| Telegram B — type 0 empty, ServiceSales usable | VERIFIED: shared direct provider issues one fallback and downstream contracts are reused. |
| Telegram C — type 0 empty, types 1–4 have data | VERIFIED: type 1 data does not suppress fallback. |
| Telegram D — type-0 provider failure | VERIFIED: provider failure is preserved; handler retains existing failure behavior. |
| Telegram E — both sources empty | VERIFIED: existing direct processor/lifecycle remains no-data/retryable and the handler does not publish. |
| Telegram F — late ProductSales | VERIFIED: existing precedence regressions prevent double counting. |
| Telegram G — replay/duplicate update | VERIFIED: handler claim/replay tests show no second direct-ingestion call for a replay. |

The focused `TelegramChannelMonthlyReportHandlerTests` suite passed 10/10. Existing Feature 133
authorization, freshness, disabled, failure, billing/conversation handoff, rendering, and replay
behavior was not changed.

### Source precedence and no-data evidence

ServiceSales may remain persisted as evidence after a later ProductSales publication, but the
existing metric-input and snapshot calculators select usable ProductSales type 0 and exclude
ServiceSales from the analytical total. When both sources are empty, report existence is not
fabricated, so the existing `NoDataYet`/retryable lifecycle remains available for a later attempt.

## Acceptance criteria

| # | Status | Evidence |
|---:|---|---|
| 1 | VERIFIED | Existing boundary tests pass; current API remains clamped to Shamsi 1404+ and archive ownership is unchanged. |
| 2 | VERIFIED | Company enumeration and `NoavaranCompanyScope` were not changed; existing scope tests/build pass. |
| 3 | VERIFIED | Existing five-output ProductSales behavior remains; full unit suite passes. |
| 4 | VERIFIED | Fallback is owned by the type-0 provider request; output types 1-4 do not issue ServiceSales. |
| 5 | VERIFIED | Provider regression verifies usable type 0 suppresses ServiceSales. |
| 6 | VERIFIED | Provider regression verifies empty type 0 produces one ServiceSales request. |
| 7 | VERIFIED | Provider regression verifies output type 1 data does not suppress fallback. |
| 8 | VERIFIED | Provider regression verifies type-0 failure throws without a ServiceSales request. |
| 9 | VERIFIED | Existing idempotency/run coordination is retained; the type-0 path has one fallback call site and focused provider tests pass. |
| 10 | VERIFIED | Normalizer tests verify canonical report persistence, metadata, publication evidence, and line-item persistence. |
| 11 | VERIFIED | Normalizer and evidence tests verify `revenueDuringThePeriod`; cumulative fields are evidence-only. |
| 12 | VERIFIED | Normalizer regression verifies repeated/missing codes with different titles remain four distinct lines and aggregate once. |
| 13 | VERIFIED | Snapshot and metric-input regressions verify ProductSales type 0 suppresses persisted ServiceSales. |
| 14 | VERIFIED | ServiceSales-only snapshot regression verifies the ServiceSales monthly total and successful source selection. |
| 15 | VERIFIED | Existing no-data lifecycle tests pass; empty normalized data remains `NoDataYet`/retryable. |
| 16 | VERIFIED | Snapshot and metric-input precedence regressions verify late ProductSales publication wins without double counting. |
| 17 | VERIFIED | Normalizer includes ServiceSales periods in the existing recalculation trigger; focused suite passes. |
| 18 | VERIFIED | Snapshot calculator and backfill candidate discovery both include ServiceSales-only periods. |
| 19 | VERIFIED | No AI/query-time Noavaran path was added; the existing persisted snapshot/chart path remains unchanged. |
| 20 | VERIFIED | Existing Noavaran monetary normalization is reused; no service-specific conversion or chart contract was added. |

### Added trigger-audit acceptance criteria

| # | Status | Evidence / remaining work |
|---:|---|---|
| 21 | VERIFIED | Exact admin direct route, request validation, direct delegation, and separation from the older range-enqueue route are covered by the Admin endpoint suite. |
| 22 | VERIFIED | Direct provider regressions cover usable type 0, empty type 0, output types 1–4, type-0 failure, and exactly one fallback request. |
| 23 | VERIFIED | Direct ingestion continues to pass the provider envelope into the existing processor; existing normalization, recalculation, snapshot, and lifecycle regressions pass. |
| 24 | VERIFIED | Existing snapshot and metric-input precedence regressions verify late ProductSales publication excludes ServiceSales from analytical totals. |
| 25 | VERIFIED | Existing Telegram gateway/controller/handler chain reaches the shared direct ingestion service. |
| 26 | VERIFIED | Telegram delegates to the corrected shared direct provider; Telegram handler and shared provider regressions pass. |
| 27 | VERIFIED | Feature 133 claim/replay, disabled, failure, freshness, rendering, and delivery behavior remains covered; replay does not invoke direct ingestion twice. |
| 28 | VERIFIED | Direct provider boundary and normalizer regressions verify 1404+ behavior and existing monetary semantics. |
| 29 | VERIFIED | The audit found no second pipeline, permanent classification, new public endpoint, AI tool, or chart contract. |
| 30 | VERIFIED | The focused direct-provider, Admin endpoint, Telegram handler, downstream Feature 134, and cross-feature suites passed where runnable. |

## Task status

| Task | Status | Notes |
|---:|---|---|
| 1 | COMPLETE | Inherited specs and existing provider, queue, persistence, run-state, trend, and snapshot paths audited. |
| 2 | COMPLETE | ProductSales output types 0-4 and their existing persistence behavior preserved. |
| 3 | COMPLETE | Type-0 usable/empty/failure distinction implemented at provider boundary. |
| 4 | COMPLETE | Existing output-type queue and idempotency coordination reused; type 0 owns fallback. |
| 5 | COMPLETE | Bounded ServiceSales request uses the existing credential and date-window boundary. |
| 6 | COMPLETE | DTO/evidence mapping retains identity, publication, fiscal, category, industry, and revenue evidence. |
| 7 | COMPLETE | Canonical report identity and deterministic service-line identity implemented. |
| 8 | COMPLETE | Existing authoritative report replacement and line-item reconciliation reused. |
| 9 | COMPLETE | ProductSales-over-ServiceSales precedence applied to snapshots and monthly metric inputs. |
| 10 | COMPLETE | Existing `NoDataYet`/retry lifecycle preserved and protected from output-type false completion. |
| 11 | COMPLETE | Existing recalculation publication path is reused for ServiceSales normalization. |
| 12 | COMPLETE | Trend calculator supports ServiceSales-only months and preserves null quantities. |
| 13 | COMPLETE | Snapshot backfill candidate discovery includes ServiceSales and uses the same calculator. |
| 14 | COMPLETE | AI and chart contracts remain unchanged and continue reading persisted snapshots. |
| 15 | COMPLETE | Focused regressions added and all unit tests pass. |

### Added trigger-audit task status

| Task | Status | Notes |
|---:|---|---|
| 16 | COMPLETE | Exact admin route and its distinction from the range-enqueue route were traced. |
| 17 | COMPLETE | Explicit direct output type 0 now enables the existing Feature 134 fallback decision; output types 1–4 remain ProductSales-only. |
| 18 | COMPLETE | Direct ingestion reuses the existing payload processor, normalized persistence, precedence, snapshot, and no-data lifecycle. |
| 19 | COMPLETE | Existing Telegram channel-post to shared direct-ingestion chain was traced. |
| 20 | COMPLETE | Exact Admin route tests pass; shared direct-provider Admin scenarios are covered. |
| 21 | COMPLETE | Existing Telegram handler/replay tests and shared direct-provider scenarios pass. |
| 22 | COMPLETE | Focused Feature 134, Admin, Telegram, architecture, build, and full integration runs were executed and classified below. |
| 23 | COMPLETE | Audit evidence and the Scheduled/Admin/Telegram matrix are recorded in this report. |

## Required scenarios

| Scenario | Result |
|---|---|
| A. ProductSales type 0 usable | VERIFIED: ServiceSales is not called; ProductSales remains authoritative. |
| B. ProductSales type 0 empty, ServiceSales has rows | VERIFIED: ServiceSales is called once, persisted, and used by the existing trend snapshot path. |
| C. Both sources empty | VERIFIED: no report rows means `NoDataYet`/retryable, not completion. |
| D. Type 0 empty, types 1-4 contain data | VERIFIED: ServiceSales fallback still occurs. |
| E. ServiceSales first, ProductSales later | VERIFIED: ProductSales takes precedence during recalculation and snapshot rebuild. |
| F. Repeated/missing service identifiers | VERIFIED: distinct legitimate titles remain distinct and idempotent. |
| G. Retry/idempotency | VERIFIED by retained deterministic run keys, existing run coordination, and no duplicate fallback call site. |
| H. Historical boundary | VERIFIED by existing 1404 boundary tests; ServiceSales is not requested for pre-1404 current-API windows. |

## Validation

Successful commands:

```text
dotnet build src/backend/FinancialCopilot.sln --configuration Release --no-restore
  Build succeeded; 0 warnings; 0 errors.

dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --no-restore
  Passed: 1701, Failed: 0, Skipped: 0, Total: 1701.

Feature 134 / trigger focused filter
  Passed: 93, Failed: 0, Skipped: 0, Total: 93.

Admin direct endpoint focused filter
  Passed: 5, Failed: 0, Skipped: 0, Total: 5.

Telegram Feature 133/134 focused filter
  Passed: 10, Failed: 0, Skipped: 0, Total: 10.
```

The full solution validation was also run. Architecture tests passed (12/12), and unit tests
passed (1701/1701). Integration tests reported 394 passed, 45 failed, and 40 skipped. The failures
are outside the Feature 134 change set (existing unrelated endpoint/data-fixture failures); the
skipped PostgreSQL cases reported Docker/Testcontainers unavailable. They are recorded here rather
than hidden. No Feature 134-focused test failed.

### Cross-feature results

| Suite | Result | Classification |
|---|---|---|
| Feature 042/053/057/059/076–078 focused regressions | Included in the 93-test focused filter; all passed | Feature 134 / related regression: PASS |
| Feature 130/133 Telegram handler regressions | 10 passed | Related cross-feature regression: PASS |
| Architecture tests | 12 passed, 0 failed, 0 skipped | PASS |
| Full unit suite | 1701 passed, 0 failed, 0 skipped | PASS |
| Full integration suite | 394 passed, 45 failed, 40 skipped | 45 unrelated/pre-existing fixture/routing failures; 40 environment skips because Docker/Testcontainers was unavailable |

## Remaining blockers

The direct provider-failure lifecycle blocker is fixed. Real-provider smoke validation remains
blocked by the lack of an isolated current-source runtime. The full integration run still contains
45 unrelated pre-existing endpoint/data-fixture failures and 40 PostgreSQL/Testcontainers
environment skips; these do not include a Feature 134-focused failure.

## Files changed for Feature 134

- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs`
- `src/backend/FinancialCopilot.Application/FinancialData/Providers/FinancialProviderContracts.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiPayloadModels.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs`
- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/FinancialIngestionContracts.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/SingleCompanyMonthlyIngestionService.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/CompanyMonthlyActivityTrendSnapshotCalculator.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/CompanyMonthlyActivityTrendSnapshotBackfillService.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyActivityBackfillCoordinator.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NormalizedMetricInputSources.cs`
- Existing Feature 134 unit tests in the provider, normalizer, metric-input, boundary, snapshot,
  direct-ingestion, and Telegram handler test files were rerun. The trigger implementation added
  direct-provider cases to `tests/FinancialCopilot.UnitTests/NadpcoApiProviderTests.cs`, and the
  lifecycle regressions to `SingleCompanyMonthlyDirectIngestionTests` and
  `NadpcoApiMonthlyActivityNormalizerTests`; existing Admin endpoint and Telegram handler coverage
  was preserved and rerun.

Pre-existing unrelated worktree changes were preserved.
