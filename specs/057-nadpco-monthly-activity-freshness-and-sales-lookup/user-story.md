# NADPCO Monthly Activity Freshness and Monthly Sales Lookup

## User Story

As a scanner user, I want monthly production/sales data for manufacturing companies backfilled
month-by-month from the Noavaran Amin current API (newest month first) and then kept fresh on
the correct Shamsi-calendar cadence, and I want AI questions about a company's latest sales,
sales quantity, sales rate, and production answered from the normalized Noavaran
monthly-activity tables — so answers reflect the most recently published monthly report instead
of quarterly statement figures or stale data.

## Where Monthly Activity Data Is Stored Today (verified)

The NADPCO monthly-activity flow already exists (specs `042`, `043`, `044`, `053`):

| Concern | Implementation |
|---|---|
| Source endpoints | `POST api/v2/MonthlyActivity/ProductSales`, `POST api/v3/MonthlyActivity/ServiceSales` (`NadpcoApiDataProviderClient.FetchMonthlyReportsAsync`) with body `companyIds`, `fromDate`, `toDate`, `outputType` |
| Company ids | Iterated from PostgreSQL `Companies.ExternalCompanyId` where `ProviderName = NoavaranCurrentApi` (vendor `coID`, e.g. `13150` = غگلپا) — correct vendor ids are already used |
| Normalized storage | `MonthlyReports` (`NormalizedMonthlyReportRow`: provider, external company id, report id, Gregorian period window, checksum, vendor/source mode) + `MonthlyReportLineItems` (`NormalizedMonthlyReportLineItemRow`: `ProductCode`, `ProductionQuantity`, `SalesQuantity`, `SalesAmount`) |
| Derived metrics | `MonthlySalesMetricInputSource` aggregates `SalesAmount` per month into `MONTHLY_SALES`; recalculation publishes `MONTHLY_SALES_GROWTH_YOY`, `MONTHLY_SALES_GROWTH_MOM`, `TTM_SALES` into `DerivedMetrics` |
| Permission bound | Current-API monthly activity is permitted from Shamsi `1404/01/01` onward only (clamped in the client); earlier months come from the frozen archive (`051`/`052`) |

## Gaps This Story Closes

### Gap 1 — No month-by-month acquisition model (ingestion side)

Vendors publish a month's production/sales report from the **1st of the following Shamsi
month**. Today (20 Khordad 1405) the latest fully published month is **Ordibehesht 1405
(`140502`)**; the permitted history starts at **Farvardin 1404 (`140401`)**.

Today every scheduled run requests the static configured range `MonthlyActivityFromDate =
1404/01/01` → now in a single sweep. There is no:

- one-time **backfill** that walks the permitted months **newest-first**
  (`140502 → 140501 → 140412 → … → 140401`) so the most decision-relevant months land first,
- DataAdmin-triggered manual operation to run and monitor that backfill,
- **completion marker** recording that the backfill finished, after which
- the **steady-state** refresh requests **only the previous Shamsi month** from the 1st of
  each new month (in Khordad → only `140502`; never re-sweeping back to `140401`).

### Gap 2 — Persian sales questions never reach monthly-activity data (read side)

- The only Persian alias for sales, `فروش`, resolves to `REVENUE` — a cumulative quarterly
  statement metric. `MONTHLY_SALES` has **no Persian alias**, so questions like
  «آخرین فروش غگلپا چقدر است؟» answer from quarterly statements (or nothing), never from the
  Noavaran monthly-activity tables.
- There are **no governed metrics at all** for monthly sales quantity (مقدار فروش), monthly
  production quantity (مقدار تولید), or sales rate (نرخ فروش = sales amount ÷ sales quantity),
  even though `MonthlyReportLineItems` already stores `SalesQuantity` and `ProductionQuantity`.
- `NormalizedMonthlyReportLineItemRow` does not persist the vendor's product title, unit, or
  per-product rate as normalized columns (they survive only in raw payload evidence), so a
  per-product نرخ فروش cannot be computed from normalized data without a model audit.

## Acceptance Criteria

### Phase A — Reverse-chronological backfill (manual, DataAdmin-triggered)

1. A Shamsi calendar utility produces the descending month sequence from the latest fully
   published month (previous month relative to "now", with year rollover: in Farvardin 1405
   the latest is `1404/12`) down to the permitted floor `1404/01`, and computes "previous
   Shamsi month" for steady-state use. Pure, unit-tested (rollover, floor clamp, mid-month).
2. A **separate, explicitly invoked backfill operation** exists behind the existing
   AdminDataOperations surface (DataAdmin policy, consistent with the other
   `/api/v1/admin/...` sync endpoints). It is never started by a scheduler; it runs only when
   called manually.
3. The backfill iterates months **newest-first** (`140502`, `140501`, `140412`, …, `140401`),
   requesting each month for the known NADPCO company-id batches (existing batching/
   concurrency limits) and normalizing through the existing monthly-activity pipeline.
   Per-month progress is persisted, so an interrupted backfill resumes from the next
   unfinished month instead of restarting.
4. Backfill run state is queryable from AdminDataOperations: months completed/remaining,
   per-month row counts, failures. When all months down to `1404/01` have completed, a
   durable **backfill-complete marker** is recorded.
5. The vendor date format actually accepted by `api/v2/MonthlyActivity/ProductSales` for
   month-granular bounds (`140502` year+month vs `1405/02/01`) is verified against the live
   API and documented; the client uses the verified format.
   **VERIFIED 2026-06-10 (live credentialed calls):** both endpoints take Shamsi bounds as
   year+month query-string tokens (`?fromDate=140502&toDate=140502`, plus `outputTypeId` on
   ProductSales) with `companyIds` in the JSON body. Dates in the JSON body make v3
   ServiceSales return HTTP 500. The live v2 response nests per-product facts under
   `productSales[]` (with `productId: 0` as a placeholder), and the live v3 response carries
   the month's service revenue as `revenueDuringThePeriod` — both shapes are encoded as
   regression fixtures.

### Phase B — Steady-state previous-month refresh

6. Once the backfill-complete marker exists, scheduled incremental monthly-activity runs
   request **only the previous Shamsi month** (from the 1st of each month onward). They must
   not re-request the full history back to `1404/01`. While the marker is absent, scheduled
   runs must not attempt the full sweep either — the backfill operation owns history.
7. Re-running the previous-month refresh within the same month is idempotent: unchanged
   payloads cause zero row churn and no recalculation requests; changed values (late or
   corrected publications during the month) update rows in place and publish recalculation
   for affected companies only.
8. Run telemetry/state records the requested Shamsi month per run so operators can verify
   "اردیبهشت data arrived after 1 Khordad" from sync history.

### Phase C — Read path (AI questions over monthly data)

9. Governed metric definitions exist for the monthly-activity facts, sourced from
   `MonthlyReportLineItems` aggregation per company-month:
   - `MONTHLY_SALES` (exists) — sales amount;
   - `MONTHLY_SALES_QUANTITY` — sum of `SalesQuantity`;
   - `MONTHLY_PRODUCTION_QUANTITY` — sum of `ProductionQuantity`;
   - `MONTHLY_SALES_RATE` — weighted average rate (`SalesAmount ÷ SalesQuantity`) with a
     documented policy for mixed-unit product lines.
10. Persian aliases route monthly questions to monthly metrics: «فروش ماهانه», «آخرین فروش»,
    «مقدار فروش», «نرخ فروش», «تولید», «مقدار تولید» (plus English equivalents). The bare
    ambiguous «فروش» keeps a documented resolution policy (quarterly `REVENUE` vs
    `MONTHLY_SALES`) decided in the semantic layer, not in the parser.
11. A symbol-lookup question such as «آخرین فروش غگلپا چقدر است؟» returns the latest
    month's value from `DerivedMetrics` (Noavaran-sourced, period = the Shamsi month's
    Gregorian window) with period and source evidence in the explainable answer — never a
    fabricated or quarterly value silently substituted for a monthly ask.
12. The line-item model audit either confirms rate/unit/title can be derived from existing
    normalized columns or extends `NormalizedMonthlyReportLineItemRow` (additive migration)
    so `MONTHLY_SALES_RATE` is computable from normalized data, not raw payloads.
13. Recalculation requests fire for the new metric codes when monthly rows change, reusing the
    existing `MetricRecalculationProcessor` dataset→metric mapping.

## Noavaran Company Scope (applies to ALL Noavaran current-API data stories)

Every per-company Noavaran current-API request — financial statements, fundamental indexes,
monthly activity, catch-ups, and this backfill — targets only the eligible company list:

```sql
"ProviderName" = 'NoavaranCurrentApi'
AND "PrecedencyRight" = 0          -- equities only; حق تقدم (rights) excluded
AND "MarketId" IN (
    '037c69ad-f519-419f-ae62-59003b6b2428',   -- بورس
    'a3ccb30a-caed-4f26-a84a-ac0eb8c78c76',   -- فرابورس
    '86c05022-632c-44cd-96c9-5c4f58c51ef5')   -- بازار پایه
```

The authoritative filter is `NoavaranCompanyScope` (Infrastructure); the
`NoavaranEligibleCompanies` PostgreSQL view mirrors it for operators. The company-catalog sync
itself stays unscoped — it is the source that populates the catalog these filters select from.

## Out Of Scope

- Query-time remote calls to the vendor (all reads stay on normalized PostgreSQL data).
- Archive (`NoavaranArchiveSql`) backfill changes — pre-1404 months stay frozen per `051`/`052`.
- Per-product drill-down answers in chat (company-month aggregates only in this story).
- Service-sales (`api/v3/MonthlyActivity/ServiceSales`) cadence changes beyond sharing the same
  month-sequence computation.
- Scheduling the backfill operation (it stays manual/DataAdmin-only by design).

## Dependencies

`042` (monthly activity sync), `043` (orchestration), `044`/`053` (scheduled current-API sync),
`012` (admin data operations), `015` (semantic layer/aliases), `006` (derived metrics engine),
`045` (symbol lookup).
