# Tasks — NADPCO Monthly Activity Freshness and Monthly Sales Lookup

Implementation must keep dependency direction (domain → application → infrastructure), reuse the
existing orchestration/recalculation paths, and introduce no query-time vendor calls.

## A. Shamsi month sequencing (shared)

1. **Shamsi month calculator (Domain/Application).** Add a small pure utility that, given a
   UTC "now":
   - returns the latest fully published Shamsi month = previous month with year rollover
     (20 Khordad 1405 → `1405/02`; Farvardin 1405 → `1404/12`), and
   - enumerates the descending month sequence from that month down to the permitted floor
     `1404/01` (`140502, 140501, 140412, …, 140401`).
   Unit-test rollover, floor clamp, mid-month behavior, and sequence order explicitly.
2. **Verify vendor month-bound format.** Against the live API (gated, DataAdmin-triggered),
   confirm whether `api/v2/MonthlyActivity/ProductSales` accepts `fromDate=140502&toDate=140502`
   (year+month) or requires day-granular `1405/02/01` bounds, for both the POST body and any
   query-string form. Document the verified format in this spec and the provider client.

## B. Phase A — Reverse-chronological backfill (manual, DataAdmin)

3. **Backfill operation contract (Application).** Define a monthly-activity backfill service
   contract (e.g. `IMonthlyActivityBackfillService`) that walks the descending month sequence
   from task 1, requesting one Shamsi month at a time (`fromDate = toDate = that month`) for
   the known NADPCO company-id batches, through the existing monthly-activity
   normalization pipeline. No new normalization path.
4. **Per-month durable progress.** Persist backfill state per month (pending / completed /
   failed with diagnostics, row counts, started/completed timestamps) so an interrupted or
   failed run resumes from the next unfinished month — never restarting from `140502`.
   Record a durable **backfill-complete marker** when every month down to `1404/01` is done.
5. **AdminDataOperations endpoint.** Expose the backfill behind the existing DataAdmin admin
   surface (consistent with the other `/api/v1/admin/...` sync operations): one endpoint to
   start (idempotent — starting while complete or in-progress is a no-op with a clear
   response), one to read progress (months completed/remaining, failures). DataAdmin policy,
   audited, never scheduler-invoked.
6. **Throttling and failure isolation.** Reuse the existing NADPCO batching/concurrency limits;
   one failed month or company batch must not abort the whole backfill — it is recorded and
   retryable on the next manual invocation.

## C. Phase B — Steady-state previous-month refresh

7. **Refresh-window gating in the scheduled sync.** Change the scheduled monthly-activity
   dataset behavior (`NadpcoApiScheduledSyncService` / orchestration request contract):
   - backfill-complete marker **present** → request **only the previous Shamsi month**
     (task 1) for all company batches;
   - marker **absent** → skip the monthly-activity full sweep entirely (log a warning that
     the manual backfill has not completed); the backfill operation owns history.
   The static `MonthlyActivityFromDate` full sweep remains only inside the manual full-reload
   path, never the scheduler.
8. **Idempotent same-month re-runs.** Test that re-fetching the previous month with unchanged
   payloads produces zero row churn and no recalculation requests; with changed values (late
   publications/corrections during the month) it updates rows in place and publishes
   recalculation only for affected companies.
9. **Run-state evidence.** Persist the requested Shamsi month on sync run history rows so the
   data console / DataAdmin endpoints show which month each run requested.

## D. Phase C — Read path (monthly metrics + Persian aliases)

10. **Line-item model audit.** Confirm which vendor fields (`rate/نرخ`, unit, product title)
    are currently raw-payload-only. If `MONTHLY_SALES_RATE` cannot be computed from
    `SalesAmount ÷ SalesQuantity` at acceptable fidelity (mixed units), extend
    `NormalizedMonthlyReportLineItemRow` with additive nullable columns + EF migration and
    backfill them during the Phase A backfill pass (no destructive migration).
11. **Metric definitions (semantic catalog).** Register `MONTHLY_SALES_QUANTITY`,
    `MONTHLY_PRODUCTION_QUANTITY`, `MONTHLY_SALES_RATE` in the semantic catalog
    (`MetricCategory.SalesAndProduction`, `FiscalPeriodType.Monthly`) with versioned
    calculation policies, mirroring how `MONTHLY_SALES` is defined.
12. **Input sources + calculators.** Add `INormalizedMetricInputSource` implementations that
    aggregate `MonthlyReportLineItems` per company-month for the new codes (sum for
    quantities; documented weighted-average policy for rate). Wire them into the metric
    registry and the `MetricRecalculationProcessor` `MonthlyProductionSales` dataset mapping.
13. **Persian/English aliases.** Add aliases so the parser resolves: «فروش ماهانه», «آخرین
    فروش» → `MONTHLY_SALES`; «مقدار فروش» → `MONTHLY_SALES_QUANTITY`; «نرخ فروش» →
    `MONTHLY_SALES_RATE`; «تولید», «مقدار تولید» → `MONTHLY_PRODUCTION_QUANTITY` (plus English
    equivalents). Decide and document the bare «فروش» policy (stays on quarterly `REVENUE` or
    moves to `MONTHLY_SALES`) inside the semantic layer; the parser must not special-case it.
14. **Symbol-lookup verification.** Integration test: seeded Noavaran monthly rows for a test
    company; question «آخرین فروش <نماد> چقدر است؟» through `POST /api/ai/v1/query` returns
    the grouped latest-sales view from persisted aggregate facts: latest one-month sales
    (`OutputType = 0`), same reporting month in the previous fiscal year when available,
    fiscal-year-to-date sales (`OutputType = 1`), and fiscal-year-to-previous-month sales
    (`OutputType = 4`). Each value has monthly period evidence and non-Missing freshness when
    seeded; a quarterly `REVENUE` value is never substituted for an explicit monthly ask.
    The same API-boundary test must assert that monthly production/sales responses do not include
    `LATEST_PRICE` or `DAILY_CHANGE_PCT`.
15. **Explainability.** Confirm citations show the Noavaran provider, the Shamsi month
    (Gregorian window), and the calculation policy version for the new metrics.

## E. Verification gate

16. `dotnet test src/backend/FinancialCopilot.sln --configuration Release` passes (unit,
    integration, architecture).
17. Manual evidence, in order:
    - DataAdmin backfill started manually; progress endpoint shows months completing
      newest-first (`140502` first, `140401` last); backfill-complete marker recorded.
    - After the 1st of the next Shamsi month, one scheduled run's history shows **only** the
      previous month requested (no full sweep) and new `MonthlyReports` rows for at least one
      known company (e.g. غگلپا / vendor id `13150`).
    - The AI answer for «آخرین فروش غگلپا» reflects that month and includes the prior fiscal-year
      same-month value when the persisted comparable row exists.
    - Monthly production/sales answers omit latest price and daily price change.
