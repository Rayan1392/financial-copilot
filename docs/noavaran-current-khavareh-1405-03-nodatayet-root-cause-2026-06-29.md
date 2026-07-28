# Noavaran Current API RCA: `khavareh` (`ExternalCompanyId=353`) missing `1405/03` monthly report

Date: 2026-06-29

## Summary

The missing `1405/03` monthly production/sales report for company `353` is **not** caused by:

- wrong month selection in `POST /api/v1/admin/noavaran-current/monthly-backfill`
- Jalali-to-Gregorian period conversion
- `MonthlyReports` persistence conflicts
- post-save verification looking at the wrong period
- failed idempotency keys blocking retries

The runtime evidence points to a narrower root cause:

1. The company-month backfill request for `nadpco-monthlybf-140503-353` reached the worker and executed.
2. The request finished with `ProcessedRecords = 0`.
3. The run checksum points to an **empty monthly-activity envelope**:
   `{"productSalesType0":"[]","productSalesType1":"[]","productSalesType2":"[]","productSalesType3":"[]","productSalesType4":"[]","serviceSales":"[]"}`
4. `NadpcoApiMonthlyActivityNormalizer` would only return `0` processed records when every fetched payload slot is empty.
5. The same company also had multiple broad full-sync runs on **2026-06-28** that completed with exactly `70` processed records, which matches months through `1405/02` only and still excludes `1405/03`.

Highest-confidence conclusion:

- The application request path is receiving an **empty vendor payload** for company `353` / month `1405/03`, so no normalized report groups are produced and `MonthlyReports` never gets a `2026-05-22` to `2026-06-21` row.

Important limitation:

- The raw-payload store deduplicates by `(ProviderName, Checksum)` and keeps the **first** row only. Because the empty envelope is reused across many companies, the stored raw-payload row for checksum `1629A26A...AEDD` now points to `ExternalReference = 1293`, not `353`. That does **not** change the run result, but it weakens request-specific auditability.

## Expected behavior

Calling:

- `POST /api/v1/admin/noavaran-current/monthly-backfill`

should enqueue a bounded monthly request for:

- provider: `NoavaranCurrentApi`
- dataset: `MonthlyProductionSales`
- company: `353`
- Jalali window: `1405/03/01` through `1405/03/31`

and should persist five `ProductSales` rows for:

- `PeriodStart = 2026-05-22`
- `PeriodEnd = 2026-06-21`
- `ExternalReportId` like `ProductSales:353:1405-03:output-{0..4}`

## Actual behavior

- `ProviderSyncRuns` contains `nadpco-monthlybf-140503-353`
- status: `Failed`
- `ProcessedRecords = 0`
- error: `NoDataYet - no monthly report rows were persisted for this company/month.`
- `MonthlyReports` contains `1405/01` and `1405/02` rows for company `353`, but no `1405/03`
- broader full-sync runs for company `353` on **2026-06-28** also stop at `1405/02`

## ProviderSyncRuns evidence

Observed live row:

```text
IdempotencyKey: nadpco-monthlybf-140503-353
Status: Failed
ProcessedRecords: 0
ErrorMessage: NoDataYet - no monthly report rows were persisted for this company/month.
SourcePayloadChecksum: 1629A26ACAB1A320C5957A31B433F2725CBD18B716E7465C6BC318B38768AEDD
ProviderName: NoavaranCurrentApi
SourceDateRangeStartJalali: 1405/03/01
SourceDateRangeEndJalali: 1405/03/31
SourceMode: CurrentIncremental
```

For the same company, recent broad full-sync runs show:

```text
nadpcoapi-sync-MonthlyProductionSales-353-20260628082537-full        Completed  ProcessedRecords=70
nadpcoapi-sync-MonthlyProductionSales-353-20260628063123-full-bf1404 Completed  ProcessedRecords=70
nadpcoapi-sync-MonthlyProductionSales-353-20260628062846-full        Completed  ProcessedRecords=70
```

`70` is consistent with `14 months x 5 ProductSales output types`, which covers:

- `1404/01` through `1405/02`

and still excludes:

- `1405/03`

That means the miss is not isolated to the manual backfill endpoint.

## DB evidence

For `ExternalCompanyId = 353` and `ProviderName = NoavaranCurrentApi`:

- rows exist for `1404/01` through `1405/02`
- no row exists for `PeriodStart = 2026-05-22`
- no row exists for `ExternalReportId LIKE '%353:1405-03%'`

Latest persisted rows are:

- `ProductSales:353:1405-02:output-0`
- `ProductSales:353:1405-02:output-1`
- `ProductSales:353:1405-02:output-2`
- `ProductSales:353:1405-02:output-3`
- `ProductSales:353:1405-02:output-4`

The live `WarningsJson` evidence on `1405/02` rows confirms the canonical symbol and company title already match the target company:

- `bourseSymbol = "خکاوه"`
- `externalCompanyId = "353"`

## Code paths inspected

### Admin endpoint and request creation

File:

- `src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs`

Method:

- `StartMonthlyActivityBackfill`

Behavior:

- calls `IMonthlyActivityBackfillCoordinator.StartAsync(...)`
- request body has no month/company override

### Backfill coordinator

File:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyActivityBackfillCoordinator.cs`

Methods:

- `StartAsync`
- `GetProgressAsync`

Key behavior:

- computes months using `ShamsiMonthCalculator.DescendingMonths(...)`
- selects companies via `NoavaranCompanyScope.EligibleCompanyIdsAsync(...)`
- creates idempotency keys as `nadpco-monthlybf-{yyyyMM}-{companyId}`
- publishes one `DataSyncRequest` per company-month with:
  - `ProviderDataset.MonthlyProductionSales`
  - `ProviderName = NoavaranCurrentApi`
  - `SourceDateRangeStartJalali = fromDate`
  - `SourceDateRangeEndJalali = toDate`
- skips only company-months that already have a **completed run with persisted rows**
- failed or completed-but-empty runs remain retryable

### Company eligibility scope

File:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NoavaranCompanyScope.cs`

Method:

- `EligibleCompanyIdsAsync`

Behavior:

- uses the authoritative Noavaran eligibility filter
- company `353` is in scope, proven by the fact that backfill and full-sync runs already exist for it

### Worker request processing and `NoDataYet`

File:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs`

Methods:

- `ProcessAsync`
- `ProcessCoreAsync`
- `MonthlyReportExistsForRunAsync`
- `TryResolveRunPeriod`

Exact behavior:

1. fetch provider payload
2. store raw payload
3. normalize
4. set `run.ProcessedRecords = outcome.ProcessedRecords`
5. for `MonthlyProductionSales`, verify whether a `MonthlyReports` row exists for:
   - `ExternalCompanyId == run.ExternalReference`
   - resolved `PeriodStart`
   - resolved `PeriodEnd`
   - optional `ProviderName`
6. if not found, mark the run failed with `NoDataYet`

Current source code now writes:

```text
NoDataYet - vendor returned no monthly report rows for this company/month.
```

But the live DB still contains the older wording:

```text
NoDataYet - no monthly report rows were persisted for this company/month.
```

This wording drift does not change the execution branch. Both indicate the same post-normalization failure path.

### Provider client

File:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs`

Method:

- `FetchMonthlyReportsAsync`

Behavior:

- converts bounded Jalali dates to `yyyyMM` query tokens
- for one-month backfill `1405/03/01` to `1405/03/31`, request becomes:
  - `fromDate=140503`
  - `toDate=140503`
- POSTs 6 requests total:
  - `api/v2/MonthlyActivity/ProductSales?...&outputTypeId=0`
  - `api/v2/MonthlyActivity/ProductSales?...&outputTypeId=1`
  - `api/v2/MonthlyActivity/ProductSales?...&outputTypeId=2`
  - `api/v2/MonthlyActivity/ProductSales?...&outputTypeId=3`
  - `api/v2/MonthlyActivity/ProductSales?...&outputTypeId=4`
  - `api/v3/MonthlyActivity/ServiceSales?...`
- request body contains only:
  - `{"companyIds":[353]}`
- request body intentionally does **not** include `fromDate`, `toDate`, or `outputType`

Relevant tests confirm this request shape:

- `tests/FinancialCopilot.UnitTests/NadpcoApiProviderTests.cs`
  - `DataProvider_FetchMonthlyReports_PostsBoundedCompanyDateAndOutputTypeRequests`
- `tests/FinancialCopilot.UnitTests/NoavaranCurrentApiBoundaryTests.cs`
  - `MonthlyActivityWindow_BoundsBothEndpointsToOneShamsiMonthInTheQueryString`

### Normalizer

File:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs`

Method:

- `NormalizeAsync`

Behavior:

- deserializes the 6-slot envelope
- reads all non-empty `ProductSales` slots plus `ServiceSales`
- groups items by report identity
- upserts `MonthlyReports` and line items
- returns `new NormalizationOutcome(groupedReports.Length, canonicalId)`

Consequence:

- if every slot is `[]`, `items` is empty, `groupedReports.Length == 0`, and `ProcessedRecords = 0`
- if the payload had real rows but persistence later failed, this method would either:
  - insert/upsert report groups and return `> 0`, or
  - throw

That is why the observed `ProcessedRecords = 0` strongly points to an empty vendor payload rather than a persistence failure.

### Raw payload persistence

File:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/Persistence/ProviderRawPayloadPersistence.cs`

Method:

- `ProviderRawPayloadStore.StoreAsync`

Behavior:

- deduplicates raw payloads by `(ProviderName, Checksum)`
- if the same payload text was already stored once for the same provider, later runs do not get their own raw-payload row

This is why the checksum for the `353` run resolves to an older row with:

- `ExternalReference = 1293`
- `ReceivedAt = 2026-06-14`

even though the failing run belongs to company `353`.

## Endpoint behavior

`POST /api/v1/admin/noavaran-current/monthly-backfill`:

- does not take a month or company filter
- enqueues all eligible companies across the planned month window
- for `1405/03`, company `353` was definitely enqueued
- failed runs are **not** considered terminal completion
- re-running the endpoint should re-enqueue `nadpco-monthlybf-140503-353` because only completed runs with persisted rows are skipped

## Exact source of `NoDataYet`

Source file:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs`

Trigger condition:

- after normalization, `MonthlyReportExistsForRunAsync(run)` returns `false`

What it checks:

- post-save database existence of a matching `MonthlyReports` row
- keyed by:
  - `ExternalCompanyId`
  - `PeriodStart`
  - `PeriodEnd`
  - `ProviderName` when present

What it does **not** check directly:

- response emptiness before normalization
- raw row count before mapping
- duplicate checksum collisions

However, in this case `ProcessedRecords = 0` plus the empty stored envelope shows that normalization saw no rows to persist.

## Period conversion verification

File:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/JalaliDateResolver.cs`

Method:

- `ResolveMonth(int jalaliYear, byte jalaliMonth)`

For `1405/03`, code resolves:

- `PeriodStart = 2026-05-22`
- `PeriodEnd = 2026-06-21`

This matches the expected period supplied in the bug report.

No evidence was found for:

- off-by-one end date
- Gregorian month approximation
- verification querying the wrong period

## Whether API returned no data, normalizer dropped data, persistence skipped data, or verification failed

### Most likely: API path returned no rows to the application

Evidence:

- failing run has `ProcessedRecords = 0`
- same run checksum resolves to a fully empty six-slot envelope
- no `1405/03` rows exist under direct report-id search or period search
- full-sync runs for the same company also stop at `1405/02`

### Not supported: normalizer dropped non-empty data

Why:

- no evidence of partially populated envelope
- no exception path was recorded
- normalizer would have returned `> 0` if any grouped report existed

### Not supported: persistence skipped or rolled back real rows

Why:

- no `1405/03` row exists under alternate searches
- no duplicate `ExternalReportId` evidence exists
- no verification-period mismatch evidence exists
- full-sync runs also miss the same month

### Not supported: verification query failed despite successful insert

Why:

- expected `2026-05-22` period has zero rows
- `ExternalReportId LIKE '%353:1405-03%'` returns zero rows
- period conversion logic matches the report expectation

## Failed idempotency key behavior

Failed keys do **not** block retry.

Evidence from code:

- `MonthlyActivityBackfillCoordinator.StartAsync` only skips keys returned by `QueryCompletedKeysWithPersistedRowsAsync(...)`
- `FinancialDataSyncProcessor.IsEffectivelyCompletedAsync(...)` only short-circuits true completed runs
- failed runs re-enter processing and update the same `ProviderSyncRuns` row

Practical implication:

- re-running the endpoint should retry the same idempotency key `nadpco-monthlybf-140503-353`
- no code path was found that treats a failed monthly-backfill run as completed/skipped

## Root cause hypotheses ranked by confidence

### 1. Highest confidence: the application currently receives an empty vendor envelope for `353` / `1405/03`

Why this is strongest:

- direct runtime evidence from `ProcessedRecords = 0`
- checksum resolves to an all-empty envelope
- full-sync runs for the same company also omit `1405/03`
- no persistence or period-query evidence contradicts it

What would explain the user’s Postman success:

- Postman request differs in a material way not captured by app telemetry

Possible differences:

- different credential pair or token identity
- different endpoint version/path
- extra request headers
- different query parameters
- different request body shape
- different `outputTypeId` handling

### 2. Medium confidence: Postman and application are not actually making equivalent requests

Why this remains plausible:

- source code request shape is explicit and test-covered
- yet live runtime still sees empty envelopes
- current raw-payload design does not preserve per-run request metadata beyond a generic endpoint label

Most important unverified parity dimensions:

- exact auth user/token used by Postman vs app
- exact base URL
- actual query string on the live request
- whether Postman includes additional parameters or body fields

### 3. Lower confidence: vendor data was published after the app runs but before manual verification

Why lower:

- user explicitly states the report is now available and the backfill was called manually
- but the stored run timestamps span June 24 to June 29, so publish timing should still be rechecked with exact Postman timestamps if needed

### 4. Low confidence: internal persistence or verification defect

Why low:

- evidence points earlier in the pipeline
- both bounded backfill and broad full-sync miss the month
- no stray `1405/03` row exists anywhere in `MonthlyReports`

## Recommended fix plan

Do not implement in this task. Recommended next steps:

1. Add request-level monthly-activity audit logging for `FetchMonthlyReportsAsync`:
   - company id
   - concrete endpoint URI per output type
   - auth identity context if safe
   - response length / top-level item count per slot
   - checksum

2. Change raw-payload persistence so request provenance is not lost on duplicate checksums:
   - either store per-run raw payload rows
   - or store a separate run-to-payload link table with endpoint, external reference, and received timestamp

3. Reproduce the app request outside the worker using the exact configured app credentials:
   - same base URL
   - same query params
   - same body
   - same token flow
   - all `outputTypeId` variants

4. Compare that request byte-for-byte against the successful Postman request.

5. If the vendor truly returns `[]` only for the app credential/request shape:
   - fix the request parity issue
   - then rerun `POST /api/v1/admin/noavaran-current/monthly-backfill`

6. If the vendor returns real `1405/03` rows to the app but normalization still fails:
   - capture and inspect the non-empty raw payload
   - then re-open normalizer/schema analysis

## Risks

- Current raw-payload dedupe by checksum can mislead investigations because the first company to produce an empty envelope owns the only stored row.
- The system currently cannot prove request parity against Postman for a specific company-month after the fact.
- Because many `1405/03` backfill runs are failing, this may be broader than company `353`.

## Follow-up validation steps

After fixing or instrumenting:

1. rerun `POST /api/v1/admin/noavaran-current/monthly-backfill`
2. confirm `nadpco-monthlybf-140503-353` ends as `Completed`
3. confirm `ProcessedRecords > 0`
4. confirm five `MonthlyReports` rows exist for:
   - `ExternalCompanyId = '353'`
   - `PeriodStart = DATE '2026-05-22'`
   - `PeriodEnd = DATE '2026-06-21'`
5. confirm `ExternalReportId` values:
   - `ProductSales:353:1405-03:output-0`
   - `ProductSales:353:1405-03:output-1`
   - `ProductSales:353:1405-03:output-2`
   - `ProductSales:353:1405-03:output-3`
   - `ProductSales:353:1405-03:output-4`

## Diagnostic SQL queries

```sql
-- ProviderSyncRuns evidence for company 353 / 1405-03
SELECT
    r."ExternalReference",
    r."IdempotencyKey",
    r."Status",
    r."ProcessedRecords",
    r."ErrorMessage",
    r."CompletedAt"
FROM "ProviderSyncRuns" r
WHERE r."IdempotencyKey" = 'nadpco-monthlybf-140503-353'
ORDER BY r."CompletedAt" DESC;
```

```sql
-- Any MonthlyReports for company 353 / expected Khordad 1405 period
SELECT *
FROM public."MonthlyReports"
WHERE "ExternalCompanyId" = '353'
  AND "ProviderName" = 'NoavaranCurrentApi'
  AND "PeriodStart" = DATE '2026-05-22'
  AND "PeriodEnd" = DATE '2026-06-21'
ORDER BY "OutputType", "ExternalReportId";
```

```sql
-- Search by ExternalReportId pattern
SELECT *
FROM public."MonthlyReports"
WHERE "ProviderName" = 'NoavaranCurrentApi'
  AND "ExternalReportId" LIKE '%353:1405-03%'
ORDER BY "PeriodEnd" DESC;
```

```sql
-- Search by WarningsJson Jalali month/company evidence
SELECT *
FROM public."MonthlyReports"
WHERE "ProviderName" = 'NoavaranCurrentApi'
  AND "WarningsJson"::text LIKE '%"externalCompanyId":"353"%'
  AND "WarningsJson"::text LIKE '%"jalaliMonth":3%'
ORDER BY "PeriodEnd" DESC;
```

```sql
-- Show all monthly-production/sales runs for company 353
SELECT
    r."IdempotencyKey",
    r."Status",
    r."ProcessedRecords",
    r."ErrorMessage",
    r."RequestedAt",
    r."StartedAt",
    r."CompletedAt",
    r."SourceDateRangeStartJalali",
    r."SourceDateRangeEndJalali"
FROM "ProviderSyncRuns" r
WHERE r."ExternalReference" = '353'
  AND r."Dataset" = 'MonthlyProductionSales'
ORDER BY r."RequestedAt" DESC;
```

```sql
-- Find the raw payload row currently referenced by the failing run checksum
SELECT
    p."Id",
    p."ProviderName",
    p."Dataset",
    p."Endpoint",
    p."ExternalReference",
    p."ReceivedAt",
    p."Payload"
FROM "ProviderRawPayloads" p
WHERE p."Checksum" = (
    SELECT r."SourcePayloadChecksum"
    FROM "ProviderSyncRuns" r
    WHERE r."IdempotencyKey" = 'nadpco-monthlybf-140503-353'
);
```

```sql
-- Show how many runs currently share the same empty-envelope checksum
SELECT
    r."IdempotencyKey",
    r."ExternalReference",
    r."RequestedAt",
    r."CompletedAt"
FROM "ProviderSyncRuns" r
WHERE r."SourcePayloadChecksum" = '1629A26ACAB1A320C5957A31B433F2725CBD18B716E7465C6BC318B38768AEDD'
ORDER BY r."RequestedAt" DESC;
```

```sql
-- Gauge how broad the 1405/03 backfill failure is
SELECT
    r."Status",
    COUNT(*) AS run_count
FROM "ProviderSyncRuns" r
WHERE r."IdempotencyKey" LIKE 'nadpco-monthlybf-140503-%'
GROUP BY r."Status"
ORDER BY r."Status";
```

```sql
-- Count 1405/03 backfill runs with zero processed records
SELECT
    COUNT(*) FILTER (WHERE r."ProcessedRecords" = 0) AS zero_processed,
    COUNT(*) AS total_runs
FROM "ProviderSyncRuns" r
WHERE r."IdempotencyKey" LIKE 'nadpco-monthlybf-140503-%';
```
