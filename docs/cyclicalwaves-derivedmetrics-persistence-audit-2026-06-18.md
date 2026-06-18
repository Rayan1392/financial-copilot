# CyclicalWaves DerivedMetrics Persistence Audit

Date: 2026-06-18  
Scope: CyclicalWaves `/api/custom-filtering/ticker/{ticker}` ingestion, normalized persistence, and `DerivedMetrics` recalculation.

## A. Current DerivedMetrics Flow

CyclicalWaves data reaches `DerivedMetrics` through this path:

1. `CyclicalWavesFullSyncService.ExecuteAsync`
   - Selects eligible NADPCO-owned companies through `NoavaranCompanyScope.EligibleCompanies(...)`.
   - Uses `Ticker ?? CompanySymbol ?? TseSymbol`, keeps Persian tickers only, and calls `SyncTickerAsync`.
   - Does not use the removed/deprecated `Symbols` table.

2. `FinancialDataSyncProcessor.ProcessAsync`
   - Processes two `DataSyncRequest`s per ticker:
     - `ProviderDataset.FinancialStatements`
     - `ProviderDataset.MonthlyProductionSales`
   - Routes provider payloads through normalizers by provider + dataset.

3. CyclicalWaves provider client
   - `CyclicalWavesDataProviderClient.FetchFinancialStatementsAsync(ticker)`
   - `CyclicalWavesDataProviderClient.FetchMonthlyReportsAsync(ticker)`
   - Both call `GET custom-filtering/ticker/{Uri.EscapeDataString(ticker)}`.
   - Raw payloads are stored via `IProviderRawPayloadStore` before normalization.

4. Normalized storage
   - `CyclicalWavesFinancialStatementNormalizer` writes:
     - `FinancialStatements`
     - `FinancialStatementLineItems`
   - `CyclicalWavesMonthlyReportNormalizer` writes:
     - `MonthlyReports`
     - `MonthlyReportLineItems`

5. Recalculation
   - `MetricRecalculationProcessor` drains `MetricRecalculationRequests`.
   - It reads normalized inputs through `NormalizedMetricInputReader`.
   - It executes calculators through `DerivedMetricCalculationService`.
   - `PersistedDerivedMetricResultStore.StoreAsync` upserts into `DerivedMetrics`.

## B. Trigger and Interval

CyclicalWaves full sync is admin-triggered:

```http
POST /api/v1/admin/cyclicalwaves/full-sync
```

Controller: `AdminDataOperationsController.RunCyclicalWavesFullSync`.

I did not find an automatic scheduled CyclicalWaves full-sync worker. The automatic part is the provider-agnostic derived-metric recalculation worker:

Class: `DerivedMetricRecalculationWorker`  
Config section: `DerivedMetricRecalculation`

Defaults:

```json
{
  "IntervalSeconds": 60,
  "BatchSize": 100
}
```

The sync service uses `MaxConcurrency = 1`, even though `CyclicalWavesDataProviderClient` also has an internal throttle of 10. The effective full-sync concurrency is currently 1.

## C. Endpoint and Request Parameters

Provider options:

Class: `CyclicalWavesProviderOptions`  
Config section: `CyclicalWaves`

Important keys:

- `ProviderName`
- `BaseAddress`
- `UserName`
- `Password`
- `TimeoutSeconds`
- `RetryCount`
- `CircuitBreakSeconds`
- `CircuitFailureThreshold`

Credentials are present in local appsettings files and are intentionally not repeated in this report.

HTTP client:

Class: `CyclicalWavesDataProviderClient`

Endpoints:

| Method | Endpoint | Used by |
|---|---|---|
| `GET` | `custom-filtering/tickers` | `FetchSymbolsAsync` |
| `GET` | `custom-filtering/ticker/{ticker}` | `FetchFinancialStatementsAsync`, `FetchMonthlyReportsAsync` |
| `POST` | `auth/login` | `CyclicalWavesAuthHandler` / health check |

Ticker handling:

- Full sync passes Persian tickers from the NADPCO-owned `Companies` catalog.
- `Uri.EscapeDataString(ticker)` is used for Persian symbols such as `کچاد`.
- The provider response DTO is `CyclicalWavesTickerDetailResponse` / `CyclicalWavesTickerData`.

Failure handling:

- Non-success HTTP responses throw `FinancialProviderException(RemoteUnavailable)`.
- Request exceptions are logged by `CyclicalWavesDataProviderClient`.
- Per-ticker full-sync failures are caught and reported in `CyclicalWavesFullSyncResult.FailedTickers`.

## D. Current Field-to-Metric Mapping

For the attached `کچاد` sample:

| Provider field | Expected MetricCode | Expected value for `کچاد` | Current behavior in code | Persisted table | PeriodType | Period date source | Gap/Bug? |
|---|---:|---:|---|---|---|---|---|
| `last_month_sale` | `MONTHLY_SALES` | `90,879,722,000,000` | Stored as `MonthlyReportLineItems.ProductCode=REVENUE`; recalculated as `MONTHLY_SALES` | `MonthlyReports` -> `DerivedMetrics` | Monthly | `last_month_sale_date` if parsed; otherwise fallback as-of | Date parse bug for `yyyyMMdd` |
| `penultimate_month_sale` | source for `MONTHLY_SALES_GROWTH_MOM` | `52,144,839,000,000` | Stored as M1 `REVENUE`; growth calculator can use it | `MonthlyReports` -> `DerivedMetrics` | Monthly | offset from latest month | Depends on date parse |
| `last_year_same_month_sale` | source for `MONTHLY_SALES_GROWTH_YOY` and same-month lookup | `69,220,219,000,000` | Stored as M12 `REVENUE`; growth/same-month can use it | `MonthlyReports` -> `DerivedMetrics` | Monthly | offset from latest month | Depends on date parse |
| `average_12_month_sale` | `AVG_12M_MONTHLY_SALES` | `57,549,286,500,000` | Stored as `AVG_12M` line item on M0; passthrough to `AVG_12M_MONTHLY_SALES` | `MonthlyReportLineItems` -> `DerivedMetrics` | Monthly | M0 period | Correct if recalculation runs |
| `last_year_average_12_month_sale` | `AVG_12M_MONTHLY_SALES` historical M12 | `0` in sample | Stored as `AVG_12M` line item on M12 when non-null | `MonthlyReportLineItems` -> `DerivedMetrics` | Monthly | M12 period | Can create zero-valued historical average |
| `last_month_sale_date` | period marker | `20260521` | DTO has field, but parser only accepts `yyyy-MM-dd` | `MonthlyReports.VendorPeriodDate` | n/a | provider date | **Bug: real sample format not parsed** |
| `last_quarter_sale` | `REVENUE` | `249,211,279,000,000` | Stored as Q0 `REVENUE`; passthrough to `DerivedMetrics` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | `last_quarter_date` if parsed | Date parse bug for `yyyyMMdd` |
| `penultimate_quarter_sale` | `REVENUE` | `656,759,065,000,000` | Stored as Q1 `REVENUE`; passthrough to `DerivedMetrics` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | offset from latest quarter | Depends on date parse |
| `last_year_same_quarter_sale` | `REVENUE` | `206,545,150,000,000` | Stored as Q4 `REVENUE`; passthrough to `DerivedMetrics` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | offset from latest quarter | Depends on date parse |
| `average_4_quarter_sale` | `AVG_4Q_REVENUE` | `265,915,619,500,000` | Stored on Q0 as `AVG_4Q_REVENUE`; passthrough to `DerivedMetrics` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | Q0 period | Correct if recalculation runs |
| `last_quarter_date` | period marker | `20260320` | DTO has field, but parser only accepts `yyyy-MM-dd` | `FinancialStatements.VendorPeriodDate` | n/a | provider date | **Bug: real sample format not parsed** |
| `last_quarter_net_profit` | `NET_PROFIT` | `75,257,854,000,000` | Stored as Q0 `NET_PROFIT`; passthrough to `DerivedMetrics` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | Q0 period | Correct if recalculation runs |
| `last_quarter_gross_profit` | `GROSS_PROFIT` | `62,289,927,000,000` | Stored as Q0 `GROSS_PROFIT`; passthrough to `DerivedMetrics` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | Q0 period | Correct if recalculation runs |
| `last_quarter_operating_profit` | `OPERATING_PROFIT` | `54,150,691,000,000` | Stored as Q0 `OPERATING_PROFIT`; passthrough to `DerivedMetrics` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | Q0 period | Correct if recalculation runs |
| `last_quarter_net_profit_margin` | `NET_PROFIT_MARGIN` | `30.2` | Stored as Q0 `NET_PROFIT_MARGIN`; passthrough to `DerivedMetrics` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | Q0 period | Correct if recalculation runs |
| `last_quarter_gross_profit_margin` | `GROSS_PROFIT_MARGIN` | `24.99` | Stored as Q0 `GROSS_PROFIT_MARGIN`; passthrough to `DerivedMetrics` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | Q0 period | Correct if recalculation runs |
| `last_quarter_operating_profit_margin` | `OPERATING_PROFIT_MARGIN` | `21.73` | Stored as Q0 `OPERATING_PROFIT_MARGIN`; passthrough to `DerivedMetrics` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | Q0 period | Correct if recalculation runs |
| `pe` | `PE_TTM` via `PE_RATIO` | `9.73` | Stored as `PE_RATIO`; passthrough calculator writes `PE_TTM` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | Q0 period | Correct if recalculation runs |
| `ps` | `PS_TTM` via `PS_RATIO` | `2.14` | Stored as `PS_RATIO`; passthrough calculator writes `PS_TTM` | `FinancialStatementLineItems` -> `DerivedMetrics` | ThreeMonths | Q0 period | Correct if recalculation runs |

## E. Expected Mapping Summary

Current metric naming mostly matches the current specs:

- Monthly sales:
  - `last_month_sale` -> `MONTHLY_SALES`
  - `average_12_month_sale` -> `AVG_12M_MONTHLY_SALES`
  - `penultimate_month_sale` and `last_year_same_month_sale` -> source rows for growth and same-period lookup
- Quarterly:
  - `last/penultimate/last_year_same_quarter_sale` -> `REVENUE`
  - `average_4_quarter_sale` -> `AVG_4Q_REVENUE`
  - profit fields -> `NET_PROFIT`, `GROSS_PROFIT`, `OPERATING_PROFIT`
  - margin fields -> `NET_PROFIT_MARGIN`, `GROSS_PROFIT_MARGIN`, `OPERATING_PROFIT_MARGIN`
- Valuation:
  - `pe` -> `PE_RATIO` input -> `PE_TTM`
  - `ps` -> `PS_RATIO` input -> `PS_TTM`

## F. Monthly Metrics Validation

Expected values for the attached `کچاد` sample:

| MetricCode | Formula/source | Expected value |
|---|---|---:|
| `MONTHLY_SALES` | `last_month_sale` | `90,879,722,000,000` |
| `AVG_12M_MONTHLY_SALES` | `average_12_month_sale` | `57,549,286,500,000` |
| `MONTHLY_SALES_GROWTH_MOM` | `(last_month_sale - penultimate_month_sale) / penultimate_month_sale * 100` | `74.283254%` |
| `MONTHLY_SALES_GROWTH_YOY` | `(last_month_sale - last_year_same_month_sale) / last_year_same_month_sale * 100` | `31.290717%` |

The existing `PercentageGrowthMetricCalculator` computes MoM/YoY correctly when the M0, M1, and M12 periods line up. The main risk is period resolution: if `last_month_sale_date = 20260521` is not parsed, M0 is based on the ingestion timestamp rather than the vendor period marker, which can shift all three monthly periods.

## G. NULL Monthly Quantity/Rate Metrics

These `MetricCode`s can be created with null values for CyclicalWaves:

- `MONTHLY_PRODUCTION_QUANTITY`
- `MONTHLY_SALES_QUANTITY`
- `MONTHLY_SALES_RATE`

Reason:

- `MetricRecalculationProcessor.SourceMetricsByDataset` includes them for `MonthlyProductionSales`.
- Their input sources scan monthly report line items.
- CyclicalWaves only creates a synthetic `REVENUE` line item and does not provide quantity/rate fields.
- The aggregate input sources therefore produce null, and calculators persist missing/null rows.

This is explainable, but noisy. It is acceptable only if the product wants an explicit MissingData trace for unsupported operational metrics. If not, CyclicalWaves recalculation should skip unsupported quantity/rate metrics instead of persisting null rows.

## H. Confirmed Bugs and Design Gaps

### 1. Real provider date format is not parsed

The attached sample uses:

- `last_month_sale_date = "20260521"`
- `last_quarter_date = "20260320"`

Both `CyclicalWavesFinancialStatementNormalizer.ParseVendorDate` and `CyclicalWavesMonthlyReportNormalizer.ParseVendorDate` accept only:

```csharp
DateOnly.TryParseExact(raw, "yyyy-MM-dd", ...)
```

Impact:

- `VendorPeriodDate` is null for real `yyyyMMdd` payloads.
- Periods fall back to request timestamp.
- Monthly growth and prior-year matching can be assigned to the wrong period if ingestion happens after the vendor period.

### 2. Tests hide the date-format bug

`CyclicalWavesNormalizerTests` uses `last_quarter_date = "2026-03-20"` and `last_month_sale_date = "2026-05-31"`, while the real sample uses compact `yyyyMMdd`.

### 3. Provider mixing risk in normalized metric input readers

`LineItemMetricInputSource` and `MonthlyReportAggregateInputSource` read by `ExternalCompanyId` and metric code, not provider.

`DerivedMetrics` unique key is:

```text
ExternalCompanyId + MetricCode + MetricVersion + CalculationPolicyVersion + PeriodEnd
```

Provider identity is not part of the key. Provider provenance is stored in `SourceEvidenceJson`, but it does not isolate rows at the key level.

Risks:

- `MONTHLY_SALES` for the same company/period can mix CyclicalWaves, Noavaran, and CodalDB inputs.
- A shared policy such as `monthly-sales-source-v1` can overwrite or combine provider-origin rows for the same period.
- Symbol lookup can combine `AVG_12M_MONTHLY_SALES` from CyclicalWaves with `MONTHLY_SALES` from another provider if latest periods differ or provider evidence is mixed.

### 4. CyclicalWaves full-sync concurrency does not match older spec wording

Spec 020 mentions max 10 concurrent ticker calls. The client throttle is 10, but `CyclicalWavesFullSyncService` sets `MaxConcurrency = 1`, so the effective full-sync concurrency is 1.

This may be intentional conservatism, but the docs/spec should say so if it is the intended behavior.

### 5. Unsupported quantity/rate metrics are persisted as missing rows

For CyclicalWaves, null rows for production quantity, sales quantity, and sales rate are expected from the current generic monthly metric pipeline, not from provider data. This can make diagnostics look worse than the actual provider coverage.

## I. Spec Gaps or Ambiguities

1. Spec 071 requires all supported CyclicalWaves snapshots in `DerivedMetrics`, but does not clearly state whether unsupported quantity/rate metrics should be absent or present as null.
2. Spec 020 still contains older language about symbol-sync creating/updating normalized company/symbol rows, but later change-request text supersedes this. Current code follows the later post-068 company-catalog rule.
3. Date format expectations are underspecified. Specs mention vendor period markers but do not explicitly require support for both `yyyyMMdd` and `yyyy-MM-dd`.
4. Provider isolation is underspecified for `DerivedMetrics`: evidence records provider origin, but read/upsert keys do not isolate provider-origin rows.

## J. Recommended Implementation Fixes

1. Update CyclicalWaves vendor date parsing:
   - Accept `yyyyMMdd`.
   - Keep accepting `yyyy-MM-dd`.
   - Add tests using the exact attached sample values: `20260521`, `20260320`.

2. Add provider-aware source filtering for CyclicalWaves passthrough metrics:
   - At minimum, for CyclicalWaves-only metrics and policies, input readers should load only CyclicalWaves-origin rows.
   - Longer term, decide whether `DerivedMetrics` needs provider/source in its uniqueness model or whether policy version must encode provider identity for every provider-specific source.

3. Avoid persisting unsupported CyclicalWaves quantity/rate metrics, unless missing rows are an intentional diagnostic feature:
   - If intentional, document it in spec 071.
   - If not intentional, skip those metric definitions when the only inputs are CyclicalWaves rows without quantity/rate evidence.

4. Align full-sync concurrency docs:
   - Either make `MaxConcurrency` configurable and set the intended default, or update specs/docs to say CyclicalWaves full sync is serial.

5. Add a regression around provider mixing:
   - Seed Noavaran and CyclicalWaves `MONTHLY_SALES` for the same company/period.
   - Assert the derived row and lookup response use the intended provider source for the requested layout.

## K. Regression Tests to Add

1. `CyclicalWavesNormalizerTests`:
   - Parse compact vendor dates `20260521` and `20260320`.
   - Assert `MonthlyReports.VendorPeriodDate = 2026-05-21`.
   - Assert `FinancialStatements.VendorPeriodDate = 2026-03-20`.

2. `CyclicalWavesSyncAndRecalculation_PersistsFullDerivedMetricSnapshot`:
   - Use the real attached date formats.
   - Assert:
     - `MONTHLY_SALES = 90879722000000`
     - `AVG_12M_MONTHLY_SALES = 57549286500000`
     - `MONTHLY_SALES_GROWTH_MOM ~= 74.283254`
     - `MONTHLY_SALES_GROWTH_YOY ~= 31.290717`
     - `REVENUE = 249211279000000`
     - `AVG_4Q_REVENUE = 265915619500000`
     - `PE_TTM = 9.73`
     - `PS_TTM = 2.14`

3. Provider isolation test:
   - Seed same company/period from CyclicalWaves and Noavaran.
   - Assert no unintended overwrite or mixed evidence for `MONTHLY_SALES`.

4. Unsupported metric behavior test:
   - Decide expected behavior for `MONTHLY_PRODUCTION_QUANTITY`, `MONTHLY_SALES_QUANTITY`, and `MONTHLY_SALES_RATE`.
   - Assert either no rows are created or rows are created with `MissingData` evidence by design.

## L. SQL Diagnostics

All `DerivedMetrics` for `ExternalCompanyId = '3'`:

```sql
SELECT "MetricCode", "MetricVersion", "CalculationPolicyVersion",
       "PeriodType", "PeriodStart", "PeriodEnd", "Value", "Unit",
       "ObservedAt", "LastSynchronizedAt", "SourceEvidenceJson", "WarningsJson"
FROM public."DerivedMetrics"
WHERE "ExternalCompanyId" = '3'
ORDER BY "PeriodEnd" DESC, "MetricCode", "CalculationPolicyVersion";
```

CyclicalWaves-originated `DerivedMetrics`:

```sql
SELECT "MetricCode", "CalculationPolicyVersion", "PeriodType", "PeriodStart", "PeriodEnd",
       "Value", "SourceEvidenceJson"
FROM public."DerivedMetrics"
WHERE "ExternalCompanyId" = '3'
  AND "SourceEvidenceJson"::text ILIKE '%CyclicalWaves%'
ORDER BY "PeriodEnd" DESC, "MetricCode";
```

Expected non-null CyclicalWaves metrics that are null:

```sql
SELECT "MetricCode", "PeriodType", "PeriodEnd", "Value", "SourceEvidenceJson", "WarningsJson"
FROM public."DerivedMetrics"
WHERE "ExternalCompanyId" = '3'
  AND "MetricCode" IN (
    'MONTHLY_SALES', 'AVG_12M_MONTHLY_SALES', 'MONTHLY_SALES_GROWTH_MOM',
    'MONTHLY_SALES_GROWTH_YOY', 'REVENUE', 'AVG_4Q_REVENUE',
    'NET_PROFIT', 'GROSS_PROFIT', 'OPERATING_PROFIT',
    'NET_PROFIT_MARGIN', 'GROSS_PROFIT_MARGIN', 'OPERATING_PROFIT_MARGIN',
    'PE_TTM', 'PS_TTM'
  )
  AND "Value" IS NULL
ORDER BY "PeriodEnd" DESC, "MetricCode";
```

Unsupported/null monthly quantity-rate diagnostics:

```sql
SELECT "MetricCode", "PeriodEnd", "Value", "SourceEvidenceJson", "WarningsJson"
FROM public."DerivedMetrics"
WHERE "ExternalCompanyId" = '3'
  AND "MetricCode" IN (
    'MONTHLY_PRODUCTION_QUANTITY',
    'MONTHLY_SALES_QUANTITY',
    'MONTHLY_SALES_RATE'
  )
ORDER BY "PeriodEnd" DESC, "MetricCode";
```

Duplicate derived metric key check:

```sql
SELECT "ExternalCompanyId", "MetricCode", "MetricVersion",
       "CalculationPolicyVersion", "PeriodEnd", COUNT(*) AS count
FROM public."DerivedMetrics"
GROUP BY "ExternalCompanyId", "MetricCode", "MetricVersion",
         "CalculationPolicyVersion", "PeriodEnd"
HAVING COUNT(*) > 1
ORDER BY count DESC;
```

Period grouping for company 3:

```sql
SELECT "PeriodType", "PeriodEnd", COUNT(*) AS metric_count,
       ARRAY_AGG(DISTINCT "MetricCode" ORDER BY "MetricCode") AS metrics
FROM public."DerivedMetrics"
WHERE "ExternalCompanyId" = '3'
GROUP BY "PeriodType", "PeriodEnd"
ORDER BY "PeriodEnd" DESC, "PeriodType";
```

Compare normalized vendor period dates:

```sql
SELECT 'FinancialStatements' AS table_name,
       "ProviderName", "ExternalCompanyId", "ExternalStatementId",
       "PeriodStart", "PeriodEnd", "VendorPeriodDate", "LastSynchronizedAt"
FROM public."FinancialStatements"
WHERE "ProviderName" = 'CyclicalWaves'
  AND "ExternalCompanyId" = '3'
UNION ALL
SELECT 'MonthlyReports' AS table_name,
       "ProviderName", "ExternalCompanyId", "ExternalReportId",
       "PeriodStart", "PeriodEnd", "VendorPeriodDate", "LastSynchronizedAt"
FROM public."MonthlyReports"
WHERE "ProviderName" = 'CyclicalWaves'
  AND "ExternalCompanyId" = '3'
ORDER BY "PeriodEnd" DESC;
```

Provider mixing probe for monthly sales:

```sql
SELECT mr."ProviderName", mr."ExternalCompanyId", mr."PeriodEnd",
       mli."ProductCode", mli."SalesAmount", mr."ExternalReportId"
FROM public."MonthlyReports" mr
JOIN public."MonthlyReportLineItems" mli ON mli."MonthlyReportId" = mr."Id"
WHERE mr."ExternalCompanyId" = '3'
  AND mli."ProductCode" IN ('REVENUE', 'AVG_12M')
ORDER BY mr."PeriodEnd" DESC, mr."ProviderName", mli."ProductCode";
```

## M. Duplicate Endpoint Calls and Single-Fetch Feasibility

Current behavior:

- `FetchFinancialStatementsAsync(ticker)` calls
  `GET custom-filtering/ticker/{ticker}` and stores the result as
  `ProviderDataset.FinancialStatements`.
- `FetchMonthlyReportsAsync(ticker)` calls the same endpoint for the same ticker and stores the
  same result as `ProviderDataset.MonthlyProductionSales`.
- `CyclicalWavesFullSyncService.SyncTickerAsync` triggers those calls through two separate
  `DataSyncRequest`s because the provider-neutral ingestion abstraction models financial
  statements and monthly reports as separate datasets.

Payload coverage:

- The CyclicalWaves ticker-detail response is a single combined snapshot. The same
  `CyclicalWavesTickerDetailResponse.Data` object contains quarterly financial fields
  (`last_quarter_sale`, profit, margin, `pe`, `ps`) and monthly fields
  (`last_month_sale`, `average_12_month_sale`, prior/same-month sales).
- The split is therefore an ingestion abstraction split, not a provider-payload requirement.
  Both `CyclicalWavesFinancialStatementNormalizer` and `CyclicalWavesMonthlyReportNormalizer`
  can deserialize and use the same raw JSON body.

Impact:

- Network requests: current CyclicalWaves full sync performs two identical remote ticker-detail
  requests per ticker. For `N` tickers this is `2N` calls; the target behavior is `N` calls.
- Provider rate limits and throttling: the duplicate call consumes twice the endpoint quota and
  doubles exposure to provider-side throttling for the ticker-detail endpoint. The client has a
  throttle of 10, but the full-sync service currently runs with effective concurrency 1, so the
  duplicated requests are serial.
- Sync duration: for network-bound runs, removing the duplicate request should reduce the remote
  ticker-detail portion by approximately 50%, excluding authentication, database writes, and
  recalculation work.
- Raw payload storage: the current path attempts two raw-payload stores for the same JSON body.
  Checksum deduplication prevents duplicate persistence, but the second fetch, checksum, and
  store lookup still happen.

Required target behavior:

- Fetch `GET custom-filtering/ticker/{ticker}` once per ticker during CyclicalWaves full sync.
- Persist one `ProviderRawPayload` for the fetched ticker-detail body.
- Reuse that same raw payload for both:
  - `CyclicalWavesFinancialStatementNormalizer`
  - `CyclicalWavesMonthlyReportNormalizer`
- Preserve provider-neutral architecture by adding a generic ingestion path for one provider
  payload feeding multiple dataset normalizers. Do not hardcode a one-off shortcut that only
  works because the provider name is CyclicalWaves.
- Keep `DerivedMetrics` recalculation semantics unchanged. The same financial-statement and
  monthly-report recalculation requests must still be produced after normalization.

Regression tests to add for this change:

1. One remote request per ticker:
   - A CyclicalWaves full-sync test with a counting HTTP handler or provider stub must prove
     only one `custom-filtering/ticker/{ticker}` request is made for a ticker.
2. Both normalized datasets still populate:
   - The same test or a paired repository test must assert `FinancialStatements` and
     `MonthlyReports` are both created from the shared payload.
3. DerivedMetrics recalculation unchanged:
   - Regression coverage must assert that the recalculation requests/outputs for both
     `ProviderDataset.FinancialStatements` and `ProviderDataset.MonthlyProductionSales` remain
     present and that existing CyclicalWaves derived metrics continue to persist.

## N. Bottom Line

The current code does have the standard derived-metric framework wired for CyclicalWaves and should persist the major quarterly, monthly, average, margin, PE, and PS snapshots after full sync plus recalculation. The most important confirmed bug is vendor date parsing: real compact `yyyyMMdd` period markers are ignored, which can shift periods and therefore growth/same-period calculations. The second major design gap is provider mixing: normalized input sources and the `DerivedMetrics` upsert key are not provider-isolated, so rows for the same company/metric/period can mix evidence or overwrite behavior across providers.
