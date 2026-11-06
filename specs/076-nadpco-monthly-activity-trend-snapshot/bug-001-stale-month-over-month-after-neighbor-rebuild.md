# BUG-001 — Stale month-over-month and 12-month average after a neighbouring snapshot is rebuilt

## Status

`FIXED IN CODE (uncommitted, 2026-11-06) — NOT DEPLOYED; STORED SNAPSHOTS STILL NEED THE REBUILD`

Read path (chat, mobile, Telegram) is correct as soon as the fix is deployed, with no data change. The scanner
metric and quality ranking read stored MoM and stay stale until the rebuild in Fix plan step 4 runs.

- Severity: High
- Priority: P1 (a wrong financial number is shown to users)
- Component: Spec 076 snapshot calculation (`CompanyMonthlyActivityTrendSnapshotCalculator`); made visible by the
  spec 077 read path (`MonthlyActivityTrendQueryUseCase`)
- Detected: 2026-11-06 (production, sapioai.ir)
- Related: [specs/bugfix-kesaveh-monthly-sales-double-counting/InvestigationReport.md](../bugfix-kesaveh-monthly-sales-double-counting/InvestigationReport.md)
  (commit `13430cfb`, 2026-11-01)

## Summary

The answer to `روند فروش ماهانه کگهر را نشان بده` showed `تغییر نسبت به ماه قبل: -52.1٪` for Mehr 1405, while the
chart in the same answer showed Shahrivar 13,147 and Mehr 12,911 billion toman, about -1.8%. The 12-month average
in the summary (10,432.341) was also wrong.

Each snapshot persists comparison fields (`SalesAmountMomGrowthPercent`, `Average12MonthSalesAmount`,
`SameMonthPreviousYearSalesAmount`, `SalesAmountYoYGrowthPercent`) computed from **neighbouring snapshots at the
moment it is calculated**. When a neighbouring month is recalculated later, nothing refreshes the dependent months.
The summary reads those stale persisted fields, while the chart reads every month's current `MonthlySalesAmount`.

## Reproduction

- Conversation: `274df1ec-98e6-4d1f-bb14-768a04676162`
- Symbol: کگهر (معدنی و صنعتی گهر زمین), `ExternalCompanyId = 12795`, provider `NoavaranCurrentApi`
- Latest period 1405/07, calculated 1405/08/06, unit billion toman

## Expected vs actual (Mehr 1405)

| Field | Shown (wrong) | Expected from accepted snapshots |
|---|---:|---:|
| Monthly sales | 12,911.313 | 12,911.313 |
| Previous month (Shahrivar) used | 26,957.738 (implied) | 13,147.382 |
| Change vs previous month | -52.1% | **-1.8%** (-1.7955%) |
| Same month previous year | 7,417.919 (+74.1%) | 7,417.919 (+74.1%) |
| 12-month average | 10,432.341 (+23.8%) | **9,281.478 (+39.1%)** |

## Evidence (read-only production queries, 2026-11-06)

`CompanyMonthlyActivityTrendSnapshots`, company 12795 (amounts in million rial):

| Period | MonthlySalesAmount | Stored MoM % | Stored Avg12 | CalculatedAtUtc |
|---|---:|---:|---:|---|
| 1405/05 | 92,156,748 | -40.7620 | 85,049,138.92 | 2026-09-23 09:34 |
| 1405/06 | 131,473,818 | 42.6633 | 88,236,951.42 | **2026-11-01 12:45** |
| 1405/07 | 129,113,133 | **-52.1054** | **104,323,410.17** | **2026-10-28 14:12** |

`MonthlyReports` for 1405/06 (`ProductSales`, `OutputType = 0`), same `ExternalReportId`
`ProductSales:12795:1405-06:output-0`:

| IsAccepted | RevisionStatus | Line items | Sum SalesAmount | LastSynchronizedAt |
|---|---|---:|---:|---|
| true | Accepted | 24 | 131,473,818 | 2026-09-29 14:55 |
| false | Pending | 20 | 138,103,563 | 2026-10-22 07:42 |

Reconciliation:

- 131,473,818 + 138,103,563 = **269,577,381**, and 129,113,133 / 269,577,381 − 1 = **−52.1054%**, exactly the stored
  MoM. The Mehr snapshot was calculated on 2026-10-28, before commit `13430cfb` added `IsAccepted` to the calculator,
  so it read a Shahrivar snapshot that summed both revisions.
- The stored Mehr average also matches that inflated Shahrivar:
  (1404/08..1404/12 + 1405/01..1405/05 + 269,577,381 + 129,113,133) / 12 = 104,323,410.17.
- On 2026-11-01 12:45 UTC the Kesaveh fix rebuilt only the 215 company-months that had rejected revisions
  (Shahrivar for کگهر). Mehr had no rejected revision, so it was not rebuilt, and its comparison fields stayed stale.
- The chart reads `MonthlySalesAmount` of each month, which is correct after the rebuild. So **the chart is right
  and the summary is wrong.** The source data is not at fault for the -52.1%.

## Root cause

1. `CompanyMonthlyActivityTrendSnapshotCalculator.RecalculateAsync` derives MoM, YoY and the trailing average from
   the previous-month, previous-year and trailing snapshots that exist at calculation time
   (`CompanyMonthlyActivityTrendSnapshotCalculator.cs:158-190`) and persists them. After upserting a month
   (`:255`), it never refreshes the later snapshots whose comparison fields depend on it.
2. `MonthlyActivityTrendQueryUseCase.ExecuteAsync` passes the persisted `SalesAmountMomGrowthPercent`,
   `Average12MonthSalesAmount` and YoY straight to the response (`MonthlyActivityTrendQueryUseCase.cs:96-121`),
   while the chart is built from each month's current `MonthlySalesAmount` (`:147-198`). Summary and chart therefore
   have different bases, with no single owner for "previous month".
3. The trigger was the double-counting defect fixed in `13430cfb`, plus a targeted rebuild that did not include
   dependent months.

Hypotheses checked:

- Double counting: **confirmed** as the trigger, on Shahrivar only (Accepted + Pending revision), no longer present in
  the current Shahrivar snapshot.
- "Previous month" = previous stored row instead of the previous calendar month: rejected. The calculator uses the
  calendar month (`DecrementMonth`), and Shahrivar exists.
- Different basis (OutputType, consolidated, unit): rejected. Both use `OutputType = 0` and million rial → billion
  toman (`× 0.0001`).
- Stale calculation: **confirmed**. Mehr `CalculatedAtUtc` (2026-10-28) is older than Shahrivar's (2026-11-01).

## Blast radius

Read-only consistency query over all 8,735 snapshots (536 companies), comparing stored fields with values
recomputed from the current neighbouring snapshots:

| Stale field | Snapshots | Companies | Of which latest month 1405/07 |
|---|---:|---:|---:|
| `SalesAmountMomGrowthPercent` | 259 (157 differ by ≥ 1 pt) | 226 | 156 |
| `Average12MonthSalesAmount` | 322 | 227 | 162 |
| `SameMonthPreviousYearSalesAmount` | 65 | 13 | 6 |

In all 259 MoM cases the previous month was recalculated after the current month (the 2026-11-01 12:45 rebuild).
Worst examples on 1405/07: قلرست, بسویچ (stored -59.5%, current +203.9%), غچین, غبهار, اروند, سبهان, فافزا.

Surfaces:

- Web chat summary (desktop and mobile): wrong MoM, 12-month average and "vs average" percent; the chart's average
  line uses the stale average too.
- Telegram assistant: does not show MoM, but its caption shows the stale "نسبت به میانگین ۱۲ ماهه", and its chart
  image draws the stale average line (`TelegramAssistantResponseRenderer.cs:364-366`).
- Scanner metric `MONTHLY_SALES_GROWTH_MOM` (`SnapshotMonthlyActivitySymbolMetricLookupService.cs:329`) and monthly
  sales quality ranking (`MonthlySalesQualityRankingUseCases.cs:199`) read the stale stored MoM; they need the rebuild.

## Separate findings (not fixed here)

- **Shahrivar 1405 revision stuck in `Pending` (ingestion, spec 136 revision handling).** The 2026-10-22 revision
  (138,103,563) is newer, and Mehr's provider report gives cumulative-through-previous-month = 854,705,544, which
  equals the *Pending* revision's YTD, not the accepted one's (849,186,605). It became `Pending` because, before
  `a94999e0` (2026-11-02), `CompareRevisionEvidence` returned 0 when neither revision had a provider publish time.
  `ReconcilePendingRevisionAsync` only re-evaluates on replay of the same checksum, so older `Pending` rows are never
  promoted. If the newer revision is promoted, Shahrivar becomes 13,810.356 and Mehr MoM becomes -6.5%. Needs an
  owner decision and a separate fix.
- **Latent:** `BuildChartPoints` uses `previousSnap.SameMonthPreviousYearSalesAmount ?? previousSnap.MonthlySalesAmount`
  for the previous-year bar, which would show the value from two years earlier. No 1403 snapshots exist in production
  today, so there is no current impact.

## Fix plan

1. Add one pure Application rule, `MonthlySalesTrendComparison`, owning "previous month = immediately preceding Jalali
   calendar month", growth percent (no percent when the baseline is missing or zero), and the trailing average.
2. Spec 077 read path: recompute MoM, YoY and the 12-month average from the same snapshots that feed the chart, so
   summary, chart, mobile and Telegram share one calculation. When the previous month is missing, say
   `دادهٔ ماه قبل موجود نیست` instead of inventing a percent.
3. Spec 076 write path: after upserting a month, refresh the persisted comparison fields of later snapshots
   (next 12 months) from current snapshot amounts.
4. Recalculation of existing rows (not run on production): the existing trend backfill with
   `TrendSnapshotBackfill:ForceRebuild = true` is idempotent and processes each company in period order, so every
   month reads a freshly rebuilt previous month. Run it for the range 1404/01–1405/07 after deploy.

## Fix applied

- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/MonthlySalesTrendComparison.cs` (new): the one
  owner of previous calendar month, growth percent and trailing average.
- `CompanyMonthlyActivityTrendSnapshotCalculator.cs`: uses the shared rule; after each upsert,
  `RefreshDependentComparisonsAsync` rewrites MoM, YoY, trailing average and completeness of later snapshots
  (up to 12 months ahead) from current amounts.
- `MonthlyActivityTrendQueryUseCase.cs`: recomputes MoM, YoY and the 12-month average from the snapshots used for the
  chart; the chart's average line uses the same value. Adds `PreviousMonthSalesAmount` to the response.
- `FinancialCopilotWorkflowDefinition.cs`: says `دادهٔ ماه قبل موجود نیست.` when the previous calendar month has no
  snapshot. The other two copies of the summary builder (`AiQueryOrchestrationService`, `MessagePersistenceFunction`)
  do not print MoM; they read the corrected average and YoY from the response. Merging the three copies is left as
  debt.
- Trailing-average semantics are unchanged (current month plus up to 11 most recent earlier snapshots). The read path
  only sees the current and previous Jalali year, so for a company with gaps older than that window the read-time
  average can use fewer periods than a stored one.

Rebuild after deploy (do not run against production without owner approval): set
`TrendSnapshotBackfill__FromYear=1404`, `__FromMonth=1`, `__ToYear=1405`, `__ToMonth=7`, `__ForceRebuild=true`, then
`POST /api/v1/admin/noavaran-current/trend-snapshot-backfill`. It recalculates from accepted persisted reports only, in
period order per company, and is safe to repeat. Afterwards, rerun the consistency query from "Blast radius"; the
expected result is 0 stale rows.

## Test plan

- Regression test with کگهر-shaped data: a stale Mehr snapshot (MoM -52.1054, average 104,323,410.17) plus correct
  Shahrivar → response MoM -1.8%, average 9,281.478. Fails before the fix.
- Missing previous month → no MoM and an explicit "previous month data not available" sentence.
- Calculator: recalculating Shahrivar refreshes Mehr's persisted MoM and average.
- Existing 076, 077, Telegram and frontend chart tests stay green.

Results (2026-11-06): `MonthlyActivityTrendStaleComparisonBug076Tests` 3/3 failed before the fix (−52.1) and pass after
it. Full unit suite: 3,001 passed, 27 failed; the same 27 fail on a clean `HEAD` checkout (routing, subscriptions,
structured query, Telegram membership, schema navigator), so none come from this change. Frontend
`monthly-activity-trend-chart-view-model` and mobile `answer-models` tests: 12/12 pass. Architecture tests: 14/15, and
the failure (`MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction`) also fails on clean `HEAD`.
