# Feature 134 Tasks — Noavaran Service Sales Fallback and Monthly Trend

## Status

All 23 tasks are complete, including the manual and Telegram trigger-inheritance scope.

These tasks record the implemented Feature 134 behavior and its verification evidence.

## Task 1 — Confirm inherited boundaries and current behavior

Audit the existing contracts and implementation before changing behavior:

- specs `042`, `053`, `057`, `059`, and `076`–`078`;
- `NadpcoApiScheduledSyncService`;
- `NadpcoApiDataProviderClient`;
- `NadpcoApiMonthlyActivityNormalizer`;
- `CompanyMonthlyActivityTrendSnapshotCalculator`;
- trend snapshot backfill/rebuild;
- monthly report persistence/upsert and provider run-state handling.

Document in implementation notes that current-API monthly activity is 1404+ only, pre-1404 data
belongs to the archive source, and per-company enumeration uses `NoavaranCompanyScope` mirrored by
the `NoavaranEligibleCompanies` view.

## Task 2 — Preserve ProductSales output-type ingestion

Keep the existing spec `059` behavior that fetches ProductSales output types 0–4 and persists them
with their respective `OutputType` values. Do not make output types 1–4 part of the monthly-sales
source-selection decision.

Verify that empty output types remain non-errors and that existing output-type identity and
idempotency rules remain unchanged.

## Task 3 — Define the ProductSales type-0 outcome

Introduce or reuse an explicit outcome for the ProductSales `OutputType = 0` request that
distinguishes:

- request succeeded with usable monthly rows;
- request succeeded with a valid empty/no-usable result;
- request failed because of transport, timeout, authorization, or invalid payload.

Do not infer this outcome from a missing/null payload slot if that would conflate failure with an
empty successful response.

Valid zero-activity reports remain usable data under spec `042` and must not trigger fallback.

## Task 4 — Coordinate fallback once per company-month

Add the smallest coordination compatible with the existing queue/run architecture so one logical
`(ExternalCompanyId, ShamsiYear, ShamsiMonth)` attempt follows this sequence:

1. ProductSales type 0 is evaluated.
2. Usable type-0 data means ProductSales is authoritative and no ServiceSales call is made.
3. Successful empty/no-usable type-0 data causes exactly one ServiceSales attempt.
4. A type-0 failure does not cause ServiceSales classification fallback and remains retryable.

The coordination must ensure that:

- ServiceSales is not called once per ProductSales output type;
- concurrent workers cannot call ServiceSales more than once for one logical attempt;
- ServiceSales is not called before the type-0 result is known;
- other ProductSales output types may still be ingested independently;
- the company-month cannot be completed before the selected source outcome is resolved.

Reuse existing idempotency keys, run state, and message coordination where possible. Do not create
a general orchestration framework.

## Task 5 — Implement ServiceSales provider selection

Reuse the existing credential/configuration boundary and verified bounded request contract for
`POST /api/v3/MonthlyActivity/ServiceSales`.

Ensure ServiceSales is called once per fallback decision without `outputTypeId`, with the same
company ID and Shamsi month window as ProductSales. Do not issue current-API ServiceSales calls for
pre-1404 periods owned by the archive path.

Preserve provider failures as failures. They must not be converted into empty successful responses
for fallback classification or completion purposes.

## Task 6 — Extend ServiceSales DTO and evidence mapping

Reuse or extend the current ServiceSales DTO and normalizer mapping to retain:

- company identity, TSE symbol/title, and instrument code;
- year/month, publication/fiscal metadata, `publishDateTime`;
- category and industry metadata;
- `serviceTitle`;
- `revenueDuringThePeriod`;
- optional `revenueFromTheBeginning` and `revenueEndOfLastPeriod`.

Use `revenueDuringThePeriod` as `SalesAmount` for the monthly fact. Keep cumulative fields in raw
payload/evidence or dedicated normalized fields if required by the existing model; never promote
them as additional monthly sales.

Do not expose credentials in source, logs, fixtures, or documentation.

## Task 7 — Define and implement stable service-line identity

Use the existing canonical logical report key:

```text
ServiceSales:{externalCompanyId}:{jalaliYear}-{jalaliMonth:D2}:output-none
```

For line items, implement deterministic identity with this precedence:

1. unique provider service ID/code;
2. provider ID/code plus normalized `serviceTitle` when the same ID/code is reused for different
   legitimate titles;
3. a deterministic natural key using instrument code, normalized title, unit/category evidence,
   and a deterministic occurrence discriminator when provider identifiers are absent or
   insufficient.

Category, industry, and service title must never enter the logical report key. Verify that the
existing line-item upsert does not collapse multiple legitimate service titles under one
`instCode`.

## Task 8 — Reuse normalized persistence and correction behavior

Persist ServiceSales through the existing `MonthlyReports` and `MonthlyReportLineItems` path.

Ensure corrected publications replace/reconcile the logical report's current line items without
stale rows or double-counting. Preserve provider raw payload and publication evidence. Avoid a
schema migration unless the existing normalized/evidence model cannot preserve a required field.

## Task 9 — Define ProductSales-over-ServiceSales precedence

Keep both normalized sources when useful for provenance, but make the analytical source selection
explicit:

- usable ProductSales type 0 wins for the same company-month;
- ServiceSales is used only when no usable ProductSales type-0 data exists;
- aggregation must never add both sources for the same company-month.

Handle the late-publication sequence where ServiceSales was persisted first and ProductSales type 0
arrives later. Recalculation and snapshot rebuild must produce the same ProductSales-authoritative
result regardless of run order.

## Task 10 — Preserve the existing no-data/retry lifecycle

Align provider outcome, normalized-row outcome, and run-state outcome with specs `053` and `057`:

- HTTP success is not equivalent to usable data;
- ProductSales type 0 empty followed by ServiceSales empty is `NoDataYet` / `Retryable`;
- no-data must not mark the company-month completed or make the backfill permanently complete;
- a company-month is complete only after usable monthly rows are persisted;
- provider failures remain failures and follow the existing retry path.

Use an existing lifecycle value; do not add a new state unless repository evidence proves none is
suitable.

## Task 11 — Extend incremental recalculation

When ServiceSales rows are newly inserted, corrected, or reconciled, publish the existing monthly
recalculation request for the affected company-month.

Reuse the existing dataset-to-metric mapping and do not add a ServiceSales-specific metric pipeline.
Ensure the recalculation input source applies ProductSales-over-ServiceSales precedence and preserves
the existing Noavaran monetary conversion policy.

## Task 12 — Extend trend snapshot calculation

Update the existing trend calculator so it can select a ServiceSales-only company-month while
retaining ProductSales `OutputType = 0` as the preferred source.

The calculator must:

- include ServiceSales monthly revenue when ProductSales type 0 has no usable rows;
- exclude ServiceSales when usable ProductSales type-0 rows exist;
- keep missing months as null/missing, not zero;
- not invent service production or quantity values;
- preserve the existing snapshot schema, chart payload, completeness flags, and unit contract.

## Task 13 — Extend snapshot backfill/rebuild

Update trend snapshot backfill/rebuild candidate discovery and processing to include ServiceSales-only
company-months through the same `CompanyMonthlyActivityTrendSnapshots` table and calculator.

Use the existing eligible-company scope and current-API date boundary. Verify that backfill and
rebuild apply the same source-precedence rule as incremental recalculation.

## Task 14 — Verify AI and chart compatibility

Do not change semantic routing, AI tools, public endpoints, or chart contracts. Verify that the
existing monthly trend query reads the persisted snapshot and returns the same structure for
ProductSales and ServiceSales authoritative months.

Verify that existing unit display behavior remains unchanged: ServiceSales values are compatible
with ProductSales values in storage, metrics, API payloads, and frontend/channel rendering.

## Task 15 — Add focused tests and regressions

Add or update tests for:

- ProductSales type-0 usable data suppressing ServiceSales;
- ProductSales type-0 empty with ServiceSales fallback;
- ProductSales type-0 empty while output types 1–4 contain data still triggering fallback;
- ProductSales type-0 provider failure not triggering fallback;
- exactly-once fallback under repeated/concurrent orchestration;
- both sources empty producing `NoDataYet` / `Retryable`, not completion;
- multiple ServiceSales lines with unique IDs;
- repeated/missing IDs or codes with different service titles remaining distinct;
- corrected ServiceSales publication idempotency;
- ServiceSales revenue using `revenueDuringThePeriod` only;
- ProductSales-over-ServiceSales precedence after late ProductSales publication;
- incremental trend recalculation and snapshot calculation for ServiceSales-only months;
- snapshot backfill/rebuild for ServiceSales-only months;
- current-API 1404+ boundary and archive ownership before 1404;
- monetary/unit compatibility and null missing months;
- existing ProductSales/output-type, metric, AI, and chart regressions.

Run the focused suites and relevant regressions for specs `042`, `057`, `059`, and `076`–`078`.
Record actual results in this file and the implementation checklist only when implementation is
complete.

## Implementation status

The original 15-task scheduled/current-API scope is implemented and recorded in the implementation
report. The additional tasks below were added by the audit of the manual direct endpoint and Feature
133 Telegram trigger; they are now implemented and verified through the shared direct provider
correction and focused regressions.

## Additional trigger-audit tasks

### Task 16 — Reconcile the exact manual trigger contract

Audit and preserve the exact route
`POST /api/v1/admin/noavaran-current/monthly-backfill/single-company-month` and its
`NoavaranMonthlyBackfillController.RunSingleCompanyMonth` behavior. Keep it distinct from the
older `single-company-monthly-ingestion` range-enqueue operation. Verify symbol resolution,
eligible-company validation, Shamsi year/month validation, direct run status, and no-data/error
reporting.

### Task 17 — Make the shared direct provider inherit Feature 134

Update the shared direct ingestion/provider contract so `ExecuteDirectAsync` uses the same
ProductSales type-0 source decision as scheduled `FetchMonthlyReportsAsync`:

- usable ProductSales type 0 suppresses ServiceSales;
- successful empty/no-usable ProductSales type 0 calls ServiceSales once;
- ProductSales output types 1–4 do not suppress that fallback;
- type-0 provider failure does not call ServiceSales;
- ServiceSales provider failure remains failure/retryable.

Use one shared decision point for the admin and Telegram paths. Do not add a second fallback
implementation or a permanent company classification.

### Task 18 — Verify direct-path persistence and precedence

Verify that direct ServiceSales fallback rows use the existing normalized persistence, recalculation,
snapshot, metric-input, no-data/retry, and monetary contracts. Verify that a later usable
ProductSales type-0 publication supersedes persisted ServiceSales without double-counting.

### Task 19 — Trace and preserve the Feature 133 Telegram path

Verify the existing `channel_post`/caption → gateway → primary API →
`TelegramAssistantController` → `TelegramChannelMonthlyReportHandler` chain. The handler must
continue to resolve the eligible company, call the shared direct ingestion service once for the
recognized company-month, and preserve its existing authorization, freshness, billing,
conversation, renderer, delivery, and replay behavior.

### Task 20 — Add admin direct-path regression coverage

Add focused coverage for the exact admin route and direct service/provider boundary covering usable
ProductSales, empty ProductSales with ServiceSales fallback, type-0 failure, both sources empty,
output types 1–4 present, late ProductSales precedence, 1404+ boundary, and monetary/source
compatibility. Keep existing admin authorization and direct response assertions.

### Task 21 — Add Telegram direct-path regression coverage

Add focused Feature 133/134 coverage proving the Telegram path receives the same provider behavior:
usable ProductSales suppression, exactly-once ServiceSales fallback, type-0 failure without
classification fallback, both sources empty, output types 1–4 present, and late ProductSales
precedence. Verify disabled/replay/failure/freshness/billing/rendering regressions remain unchanged.

### Task 22 — Verify cross-feature boundaries

Run the relevant Feature 042/053/057/059/076–078 and Feature 133/130 regressions. Confirm no
pre-1404 current-API ServiceSales request, no new public/AI/chart contract, no duplicate Telegram
pipeline, no permanent Production/Service classification, and no change to Feature 133's
channel-post authorization or delivery semantics.

### Task 23 — Record trigger-audit evidence

Record exact route/path evidence, the shared direct-provider gap, the production correction,
focused test requirements, and the Scheduled/Admin/Telegram capability matrix in the Feature 134
implementation report. Do not mark the added acceptance criteria complete until the correction and
regressions are implemented and verified.
