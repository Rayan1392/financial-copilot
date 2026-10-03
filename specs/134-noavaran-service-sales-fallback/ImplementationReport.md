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

## Production Snapshot Regression Investigation

Date: 2026-10-03

Production was not redeployed or modified during this investigation. The VPS remained on the
rollback images and the persisted Qasem evidence was queried read-only.

### Confirmed production evidence

The failed deployment's Qasem direct run is:

```text
run ID: b4d6161f-580f-40e0-a6d3-b0f80560a123
company: قاسم ایران
internal company ID: 93eab307-6eed-4603-aa42-71b33972247e
external company ID: 12622
symbol: قاسم
year/month: 1405/06
status: Completed
ProcessedRecords: 1
ErrorCount: 0
started: 2026-09-27 21:51:55.774629 UTC
completed: 2026-09-27 21:52:06.818653 UTC
```

The persisted report was:

```text
report ID: e8983d93-4acf-40a9-9dab-55e16432d7d9
report key: ServiceSales:12622:1405-06:output-none
report type: ServiceSales
line count: 4
persisted monetary total: 54,742,204
```

The four persisted line-item IDs were `48135bd8-7b38-4017-99c5-5ee83d37f1a2`,
`3f7f0f83-82e2-4a8b-a3fd-ca66db371c67`, `c89fdea6-0b6b-487f-9178-d44ca3e912d7`, and
`666cdfac-1c35-4462-87a9-c8c80c4ee32a`. They were not modified.

### Production execution reconstruction

| Stage | Result | Evidence |
|---|---|---|
| ServiceSales provider response | COMPLETED | Admin run completed with `ProcessedRecords=1`, `ErrorCount=0`; no provider error was recorded. |
| ServiceSales normalization | COMPLETED | Four normalized line items were persisted under the canonical ServiceSales report key. |
| Report and line-item persistence | COMPLETED | Report ID and four line-item IDs above; total reconciles to 54,742,204. |
| Monthly/revenue recalculation | PARTIAL | The API log shows monthly sales-quality ranking for 1405/06 completed at 21:52:06, but no trend snapshot-calculator invocation. |
| Snapshot candidate dispatch | SKIPPED | The normalizer's snapshot loop was restricted to ProductSales single-month groups. |
| Snapshot calculator | NOT ENTERED | No `CompanyMonthlyActivityTrendSnapshotCalculator.RecalculateAsync` call was made for the ServiceSales-only group. |
| Snapshot repository upsert | NOT ENTERED | No snapshot row existed for external company 12622 / 1405/06. |

The preserved failure log contains the ranking messages at 21:52:03–21:52:06 and no Qasem
trend-calculator or snapshot-upsert message. The source path is decisive: `NormalizeAsync` builds
`monthlyTrendGroups` including ServiceSales, but the subsequent snapshot loop iterates a separate
`singleMonthGroups` collection filtered to ProductSales output type 0 only.

### Catalog comparison

Qasem's production catalog is complete for this path:

```text
Companies: internal ID 93eab307-6eed-4603-aa42-71b33972247e
NoavaranEligibleCompanies: present, SourceMode=CurrentIncremental
InstrumentCode: 34540569618314880
TradingInstrument: present and linked to the same internal company
Market: present, external ID 2, فرابورس
```

KGL also has an eligible company, linked internal company, instrument, and market. Its
ProductSales type-0 report generated the expected snapshot. The relevant difference is not
catalog completeness: KGL entered the ProductSales-only snapshot group, while Qasem entered the
ServiceSales-only group.

### Root cause classification

Primary category: **D — Direct ingestion does not trigger snapshot generation.**

This is a code/lifecycle defect, not a catalog defect and not a transaction-timing defect.
Normalization commits the ServiceSales report and line items before downstream recalculation. The
existing snapshot calculator already supports ServiceSales fallback and ProductSales precedence;
the direct normalizer simply never called it for a ServiceSales-only group. The isolated evidence
had exercised the calculator directly after seeding complete catalog data, which did not prove
that the direct ingestion normalizer dispatched the calculator automatically.

### Regression and correction

Regression test:

```text
NadpcoApiMonthlyActivityNormalizerTests.Normalize_ServiceRows_CreatesTrendSnapshotThroughSharedLifecycle
```

Before the correction, the test reproduced production behavior: ServiceSales persistence passed,
but the snapshot query found no row. The minimal correction removes the ProductSales-only snapshot
group and iterates the existing `monthlyTrendGroups` collection. This preserves ProductSales
authority because the calculator itself applies ProductSales-over-ServiceSales precedence.

After the correction, the regression passes and verifies a ServiceSales snapshot with
`MonthlySalesAmount = 3,000,000` through the shared normalizer → calculator → repository path.

### Validation after the correction

```text
Focused Feature 134-related unit filter: 167 passed, 0 failed
Telegram-related focused filter: 18 passed, 0 failed
Admin direct endpoint filter: 5 passed, 0 failed
Full unit suite: 1702 passed, 0 failed, 0 skipped
Architecture suite: 12 passed, 0 failed, 0 skipped
Release solution build: succeeded, 0 warnings, 0 errors
```

The full AdminDataOperations class run still has unrelated environment/rate-limit failures; the
exact direct-company route filter passed 5/5. No production smoke, Docker operation, image build,
push, deployment, or commit was performed.

### Remaining deployment status

The local correction is validated and the rollback production state remains untouched. A new
isolated real-provider smoke run is still required before any redeployment. Required checks are
Qasem automatic ServiceSales snapshot creation and the KGL ProductSales-authority control.

## Final isolated real-provider validation (2026-10-03)

Production was not contacted, redeployed, or changed. The candidate is the dirty working tree at
`57bbed521327566f8d3a28bfd7e325a5cfd7259e`; no commit was created. The exact source correction is
in `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs`:
snapshot dispatch now iterates `monthlyTrendGroups`, including ServiceSales-only months, instead of
the ProductSales-only `singleMonthGroups`. The regression test uses the real normalizer, real trend
snapshot calculator, real EF repository, and `NormalizeAsync`; it does not invoke the calculator or
insert a snapshot manually.

The Worker Release build succeeded with 0 warnings and 0 errors. Test-only API and Worker Docker
builds were attempted, but Docker Desktop's Linux engine was unavailable because the
`dockerDesktopLinuxEngine` pipe was missing. Therefore no isolated container image was produced.
A local API process was used only as a bounded provider/persistence probe against the local
PostgreSQL database, with RabbitMQ, Redis, scheduled sync, and feature messaging disabled.

The catalog resolved Qasem (`قاسم`, company `قاسم ایران`, Noavaran external ID `12622`) and KGL
(`کگل`, external ID `4`). The real Admin route was invoked for Qasem 1405/06 and KGL 1405/05.
Both provider runs completed with `ProcessedRecords=1` and `ErrorCount=0` and persisted data before
the route returned HTTP 500 while attempting the remaining asynchronous output-type publishes;
the exact failure was `RabbitMQ data synchronization transport is disabled by configuration`.

Qasem evidence: ProductSales type 0 was a successful empty result, the provider envelope contained
non-empty ServiceSales data (4 rows), and the ServiceSales revenue total was `54,742,204`. One
ServiceSales report with four lines and the same total was persisted. The automatic trend snapshot
was created for 1405/06 with amount `54,742,204`, sourced from
`ServiceSales:12622:1405-06:output-none`. The ServiceSales group entered `monthlyTrendGroups`,
automatic recalculation occurred, and no manual recalculation was used. A deterministic database
read returned the snapshot with the correct amount; it did not require query-time Noavaran access.

KGL evidence: ProductSales type 0 was usable with 11 rows; the latest provider envelope had empty
ServiceSales data, so no ServiceSales fallback request was needed. One ProductSales type-0 report
and one automatic snapshot were present for 1405/05, both totaling `150,281,420`. Duplicate
recalculation dispatches, duplicate snapshots, duplicate report contributions, and double counting
were not observed. Existing automated coverage also confirms ProductSales precedence when both
sources exist and preserves both provider-failure lifecycle cases.

Automated validation remains green: focused Feature 134/provider/direct-ingestion/snapshot/trend
filters `167 passed, 0 failed`; Telegram-focused tests `18 passed, 0 failed`; exact Admin direct
route tests `5 passed, 0 failed`; full unit suite `1702 passed, 0 failed, 0 skipped`; architecture
suite `12 passed, 0 failed, 0 skipped`; Release solution build succeeded with 0 warnings and 0
errors. The full AdminDataOperations class has unrelated environment/rate-limit failures; its
exact Feature 134 direct-route filter passed.

The remaining blocker is completion of the real isolated containerized HTTP-200 route and worker
continuation with Docker/RabbitMQ enabled. No production deployment, rollback modification, image
push, or commit was performed during this validation.

## Infrastructure-dependent validation gate (2026-10-03)

Docker Desktop's Linux engine was restored successfully. The existing Compose infrastructure
provided healthy PostgreSQL, Redis, and RabbitMQ services. Dirty test-only images were built from
the current working tree:

```text
API:    feature134-final-isolated-api:test
        sha256:20543ad0a01acffd7ecd8ffc681ac1397ef355848cdc7b4ad34d5fcb9a456ec8
Worker: feature134-final-isolated-worker:test
        sha256:da0e937faee269e1c4d37f241cdf82094cae63cd60980838c6ea55afb5f79f28
```

The API health endpoint returned HTTP 200 (`Healthy`), the Worker remained running without a
restart, and the Worker connected to RabbitMQ with one active consumer. The legitimate company
catalog refresh completed through the Admin API and the Worker consumed the catalog message,
persisting 4,809 companies. The isolated database required the existing stable market references
used by the `NoavaranEligibleCompanies` view; those references were prepared only in the isolated
test database.

The real Qasem Admin request completed successfully:

```text
endpoint: POST /api/v1/admin/noavaran-current/monthly-backfill/single-company-month
symbol/company: قاسم / قاسم ایران
external company ID: 12622
year/month: 1405/06
HTTP status: 200
correlation ID: 9732c7bc-c661-4316-b91b-bfc6a67acee
run ID: f9241509-37f6-4213-84ab-df614358ef8d
ProductSales: successful empty, 0 rows
ServiceSales: one fallback request, HTTP 200, 4 rows
persisted ServiceSales total: 54,742,204
automatic snapshot: present, 54,742,204
```

The route published the remaining asynchronous output-type messages successfully. RabbitMQ
consumed all four messages, leaving zero ready and zero unacknowledged messages; no dead-letter
message or RabbitMQ transport error occurred. The remaining ProductSales output-type runs ended
with the provider's expected `NoDataYet` result and did not affect the successful ServiceSales
report or snapshot. Exactly one canonical ServiceSales report, one four-line contribution, and one
Qasem 1405/06 snapshot were present; no duplicate processing or double counting was observed.

The relevant container-dependent Admin integration filter passed 5/5. The broader Noavaran/monthly
activity integration filter passed 38/41; its three failures were unrelated pre-existing V2 intent
routing and isolated test-fixture market-FK issues. No Feature 134 code defect was discovered.

The isolated stack and its test volumes were removed after validation. No production deployment,
rollback modification, image push, or commit was performed. The infrastructure gate is complete;
the candidate is safe to commit and separately review for redeployment.
