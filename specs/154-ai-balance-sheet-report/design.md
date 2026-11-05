# Feature 154 Design — AI Balance Sheet Report

**Status:** Revision 3 design (2026-11-04). Read [spec.md](spec.md) first. The evidence is in [discovery.md](discovery.md).
- All product decisions are closed.
- Definitions that depend on evidence each have a single owner, so closing a gap changes one place:
  - E-1 (per-pair fallback enablement) and E-8 (subgroup placement) live in the registry.
  - E-2 (anchor-link wording and the post-v1 embedded preference) lives in `BaselineSourcePolicy`.
  - E-3 is consumed only by `FiscalCalendarPolicy` and the fiscal-dependent baseline kinds.

## 1. Summary

The design reuses the Feature 144 shape (R-32):
- canonical provider-aware company resolution;
- a bounded persisted read behind an Application port;
- deterministic Application policies;
- a typed result carried on `FinancialStatementTableResult`, then persisted and replayed;
- typed web and mobile renderers;
- the canvas PNG exporter.

The default response also carries the standard comparison candidates. The «مقایسه» action is therefore a client-side view switch with no second AI query or charge. There is no new capability ID, provider call, worker, configuration key, schema, or migration. V1 and Feature 141 are untouched.

## 2. Components and boundaries

| Layer | Component | Responsibility |
|---|---|---|
| Application policy | `BalanceSheetRowRegistry` | Single owner of both templates, row order, section and subgroup, source-title labels, primaries, fallback candidates with their verification status (E-1, default *unverified*), exclusions (286, 60, 146, 160, 188) and analytical items. |
| Application policy | `FiscalCalendarPolicy` | Resolves fiscal start and fiscal-year identity from `JalaliFiscalYearEnd` evidence, including transitional rebasing and Jalali month arithmetic. It is a **behavior-preserving extraction** of `ResolveFiscalStart`, `ResolveEffectiveFiscalStarts` and `JalaliEndAfterMonths` from `QuarterlyFinancialReportsNormalizer` (R-41). Feature 144 then calls the shared policy and its tests stay green. This gives fiscal-calendar knowledge one owner. |
| Application policy | `BalanceSheetStatementSelector` | Eligibility, distinct-date grouping, per-date version ranking and conflict detection (spec §5.4), anchor choice, and chart dates (up to five). |
| Application policy | `BaselineResolver` | Resolves the five baseline kinds (spec §8.2) from the selector's snapshots and fiscal evidence. Returns typed availability, the named unavailable date, and alternatives. |
| Application policy | `BaselineSourcePolicy` | v1 always returns the exact-date stored snapshot with its own flags (spec §8.3). It emits anchor-link provenance only when E-2 verifies a link, which never happens in v1. It guarantees one record per column. The post-v1 embedded preference plugs in here. |
| Application policy | `BalanceSheetReportBuilder` | Builds cells (states, alias matrix §6.4), sections, the ratio (`EquityToAssetsRatio`), and the R1 and R2 reconciliation checks. No I/O. |
| Application policy | `BalanceSheetComparisonBuilder` | Computes per-row change (amount, percent, unavailable reasons), the ratio difference in percentage points, and column metadata for each available baseline. |
| Application use case | `BalanceSheetReportQueryUseCase` | Resolve, classify, read, select, build, resolve baselines and compare. Maps typed outcomes. Callable without an LLM. |
| Application ports | `IBalanceSheetStatementReader` (new); `ICompanyResolutionOutcomeService` and the eligible-group reader (reused) | Plain records only. |
| Infrastructure | `EfCoreBalanceSheetStatementReader` | Bounded EF queries (§5). Reuses `StatementMetadata.Parse` (R-40). |
| Infrastructure | `RenderBalanceSheetReport` | Text rendering (spec §9.6). Pure function of the typed result. |
| AI orchestration | Intent rules, interpreter, governance, executor, workflow | Recognition and dispatch only (§4). |
| API | `FinancialStatementTableHttpResponse.BalanceSheetReport` | Pass-through. |
| Frontend | `components/app/balance-sheet-report/*` | View models and rendering (§7). No financial arithmetic. |

All dependencies point inward. Every policy is unit-testable with plain records.

## 3. Typed contract

All discriminators are strings (R-33). Amounts are `decimal?` in million IRR. Percentages are stored already scaled. `contractVersion: "154.3"`.

```text
BalanceSheetReportResult
  outcome: available | company_unresolved | company_ambiguous | provider_unavailable
         | company_classification_unavailable | no_standalone_balance_sheet
         | no_snapshot_at_date | unsupported_date_with_variant
         | conflicting_report_revision | ineligible_storage_scale
  outcomeReason?, requestedDate?, nearestAvailableDates?: { earlier?, later? }
  consolidatedAlternativeAvailable: bool                     // E-6 check; suggestion only if true
  templateId?: BankingBalanceSheet | GeneralBalanceSheet
  title, company { symbol, name, providerName, externalCompanyId }, classification?
  snapshots: { statementId → Snapshot }                     // each stored once; ≤ 9
  anchorStatementId
  requestedView: default | single_date | comparison
  comparison: {
    selectedBaselineKind: fiscal_year_end | previous_fiscal_quarter_end | previous_report
                        | same_stage_previous_year | explicit
    candidates: [ { kind, label, state: available | unavailable, reason?, baselineDate?,
                    statementId?, source: stored_snapshot, anchorLinkVerified: false,
                    rowChanges: { rowId → Change }, ratioChange?: { anchor, baseline, ppDifference } } ]
  }
  chart: { maxDates: 5, points: [ { periodEnd, statementId?, r2Reconciliation,
           stackRepresentsAssets: bool, gapReason? } ] }   // values read from snapshots; ascending
  generatedAtUtc

Snapshot { statement { statementId, externalStatementId, periodEnd, periodType, isAudited,
           isRepresented, publishedAt?, jalaliFiscalYearEnd?, unit, assumedScale },
           sections[ { sectionId, label, caption:"اقلام منتخب",
                       groups[ { groupId: current|non_current|none, label?, rows[], subtotal? } ], total } ],
           finalTotals { totalAssets, totalLiabilitiesAndEquity },
           ratio { value?, state, reason?, unit:"percent", displayDecimals:2, rounding:"half_away_from_zero" },
           reconciliation { r1 { state, difference? }, r2 { state, difference? } },
           analyticalItems[] }
Row    { rowId, label, kind: detail|subtotal|total|final_total|analytical, unit, cell }
Cell   { value?, state: available|missing|ambiguous, reason?, selectedSourceItemId?, lineItemId?,
         statementId, aliasDiscrepancy?: { otherSourceItemId, otherValue, difference },
         candidates: [ { sourceItemId, lineItemId?, value?, verified: bool } ] }
Change { amount?, percent?, state: available|percent_unavailable|unavailable, reason? }
```

How the contract is carried and versioned:
- **Carrier.** `FinancialStatementTableResult.BalanceSheetReport` sits next to `QuarterlyResult`. It persists and replays through the same structured-payload path, and replay never recomputes.
- **Default and single-date requests.** These hold the four standard candidates: `fiscal_year_end` (selected), `previous_fiscal_quarter_end`, `previous_report` and `same_stage_previous_year`, each available or unavailable. For a single date, the requested date is the anchor.
- **Explicit two-date comparisons.** These hold one `explicit` candidate plus the standard candidates for the later (anchor) date, with `requestedView: comparison`.
- **Snapshots** are de-duplicated: a statement that is both a baseline and a chart point is stored once.

## 4. Routing

All changes live in the existing single owners. No capability ID is added. The `FinancialStatementTableQuery` record gains these fields:
- `UseBalanceSheetReport`;
- `BalanceSheetView` (`default` or `comparison`);
- `BaselineKind?`;
- `ComparisonDates?` (up to two `DateOnly`).

1. **Aliases (`FinancialStatementTableIntentRules`)**
   - Add «وضعیت مالی», «بیلان», "balance sheet" and "statement of financial position" to `BalanceAliases`.
   - Add «عملکرد مالی», "p&l", "statement of profit or loss" and "statement of operations" to `IncomeAliases` (R-45).
   - Add «عملکرد مالی» and «صورت عملکرد مالی» to `QuarterlyReportAliases` as well. They then route exactly like «صورت سود و زیان», including the analysis exception in `LooksLikeQuarterlyReportTableQuery` (PD-21, R-46).
   - English matching is case-insensitive.
   - `ResolveStatementType` keeps balance-first precedence. A bare «وضعیت» or «عملکرد» without «مالی» matches neither new alias.
2. **Form predicates**
   - `LooksLikeBalanceSheetReportQuery` is true when there is a balance alias, no full-table cue («همه اقلام», «جدول کامل», «کامل»), no variant cue (the existing `ResolveComposingPreference`, audited, represented and period-months detectors), and no single-value cue.
   - `ResolveBalanceSheetComparison` detects comparison cues and baseline cues (spec §4.2). «فصل قبل» maps to `previous_fiscal_quarter_end`, and «گزارش قبلی/قبل», «دوره قبل» map to `previous_report`. It extracts Jalali and ISO dates with the existing date patterns from `ResolveEndingPeriod`, generalized to return up to two dates.
   - **One date and no comparison cue** gives `BalanceSheetView = single_date` (PD-20).
   - **One date plus a Feature 083-only cue** (consolidated, full table) gives `unsupported_date_with_variant`. That message never reaches Feature 083, so the date is never dropped.
   - When a comparison cue is present, the dates and «فصلی» never set variant or quarterly flags.
3. **Quarterly guard.** `LooksLikeQuarterlyReportTableQuery` returns false when a balance alias is present (R-26, PD-05).
4. **Interpreter and governance (`DeterministicCapabilityInterpreter`, `CapabilityInterpretationGovernance`)**
   - Add «صورت وضعیت مالی», «وضعیت مالی» and «بیلان» to `StatementWords`.
   - Do not score «وضعیت» as an analysis word when it is followed by «مالی».
   - Add the balance and income aliases and possessive forms («‌اش», «‌شان») to the non-entity list.
   - An explicit balance alias beats the quarterly shorthand; «تحلیل» plus an alias still goes to analysis (R-26).
   - PD-27 (owner instruction 2026-11-05, supersedes «بررسی» here): «بررسی / بررسی کن» with exactly one statement alias and no other analysis cue is a report request. With a balance-sheet alias it opens Feature 154; with an income alias plus «فصلی» it opens Feature 144. `CapabilityRoutingPrecedence` honours the V2-only interpreter marker `feature154-v2-governed-report` over the «بررسی» analysis preference.
5. **Dispatch.** `FinancialStatementTableCapabilityExecutor` and the V2 legacy branch both call the same predicates. `FinancialStatementTableQueryUseCase` routes to `IBalanceSheetReportQueryUseCase` when the statement type is BalanceSheet and `UseBalanceSheetReport` is set. Otherwise the existing Feature 083 path runs unchanged.
6. **Context.** Feature 120 carry-over fills the company slot. An explicit alias in the new message overrides the active statement type. This is how «عملکرد مالی‌اش» opens the income statement after a balance sheet.
7. **Closed predicates.** D-12 is the `single_date` branch in item 2. D-13 is the alias addition in item 1. «صورت عملکرد» and «صورت درآمد» without «مالی» are untouched.
8. **Outcomes.** The executor maps typed outcomes to Feature 117 reason codes, not a bare `NoData` (R-29):

   | Outcome | Feature 117 reason code |
   |---|---|
   | `no_standalone_balance_sheet` | `supported_but_no_rows` |
   | `company_classification_unavailable` | `data_stale_or_ineligible` |
   | `conflicting_report_revision` | `data_stale_or_ineligible` |

   The consolidated suggestion is a Feature 121 suggested action, added only when `consolidatedAlternativeAvailable` is true. Billing follows the existing no-data policy.

## 5. Data retrieval

`EfCoreBalanceSheetStatementReader` runs these queries. All of them are bounded and read-only, and none calls a provider.

1. **Catalog.** Provider `NoavaranCurrentApi`, BalanceSheet, `SourceItemId` in the registry IDs (primaries and candidates). Resolved by tuple (R-07).
2. **Balance-sheet headers.**
   - Same provider and `ExternalCompanyId`, BalanceSheet, `IsComposing = false`, `PeriodType` 3/6/9/12.
   - Ordered by `PeriodEnd` descending and limited to 48 rows, the Feature 144 bound. That covers about four fiscal years of quarterly reports with variants, and so the five chart dates, the previous fiscal-year end and the same stage of the previous year.
   - If a needed baseline date falls outside the bound, one exact-date lookup is issued for it.
   - Rows whose scope metadata disagrees are dropped (R-16).
3. **Fiscal evidence.** `PeriodEnd` and `WarningsJson` for up to 24 of the company's newest headers of **any** statement type from the same provider, parsed in memory for `JalaliFiscalYearEnd` (R-40). This finds the previous fiscal-year end even when no balance sheet exists at that date.
4. **Explicit dates.** Exact `PeriodEnd` equality lookups for the requested dates. A miss is never widened to a range.
5. **Consolidated existence (E-6).** One `EXISTS` query with `IsComposing = true`, run only when the outcome is `no_standalone_balance_sheet`.
6. **Line items.** One batched query: `FinancialStatementId` in the de-duplicated selected statements (anchor, up to 4 baselines, up to 5 chart dates; **at most 9**) joined to the catalog IDs. The join is explicit because there is no FK (R-08).

**Bounds (AC-X04).**
- At most **6** round trips per request: catalog, balance-sheet headers, fiscal-evidence headers, one batched exact-date lookup (only when a needed date is outside the header window), one batched line-item query, and the consolidated `EXISTS` (only on the no-standalone outcome).
- Line items for at most **9** statements.
- A payload of at most 9 snapshots and 5 chart points.

A custom comparison date that is not among these is a new request (PD-24).

The reader returns observations with `items: SourceItemId → [(lineItemId, value?)]`, so duplicates are detectable. There is no cache. Any future cache must key on classification and data version.

## 6. Algorithms

### 6.1 Selection

```text
eligible  = scope-agreeing, MillionRials, standalone BS headers
dates     = distinct PeriodEnd desc
pick(date)= rank by isAudited desc, isRepresented desc, publishedAt desc;
            if top-rank peers differ on any registry item → conflict(date)
            else choose max(externalStatementId), then max(id)
anchor    = pick(dates[0])  (explicit request: pick(later requested date))
            conflict(anchor) → outcome conflicting_report_revision (no fallback)
chart     = dates[0..4] ascending; conflict → gap
```

### 6.2 Baselines

```text
FYE(s)       = parse JalaliFiscalYearEnd(s); fiscalStart via FiscalCalendarPolicy
fiscal_year_end:
   priorFYE  = max{ FYE(h) : h ∈ fiscal-evidence headers, FYE(h) < FYE(anchor) }
   none      → unavailable(missing_fiscal_metadata)
   snapshot  = pick(priorFYE) among eligible BS with PeriodEnd == priorFYE
   none      → unavailable(no_snapshot_at_date, date=priorFYE)
previous_fiscal_quarter_end:
   start     = FiscalCalendarPolicy.Start(anchor)      (missing evidence → unavailable(missing_fiscal_metadata))
   require   anchor FY not rebased/transitional; anchor.PeriodEnd ∈ grid = { JalaliEndAfterMonths(start, 3k) : k=1..4 }
   target    = previous grid point; if anchor is the first grid point → priorFYE (as fiscal_year_end)
   snapshot  = pick(target) among eligible BS with PeriodEnd == target
   else      → unavailable(reason, date=target when known); alternatives include previous_report with its actual date
   (never subtract; never snap)
previous_report:   pick(max date < anchor.PeriodEnd)    else unavailable(no_earlier_report)
same_stage_previous_year:
   prevFY    = fiscal year whose end == priorFYE; start = FiscalCalendarPolicy.Start(prevFY)
   require   prevFY not rebased/transitional and 12 Jalali months long
   target    = JalaliEndAfterMonths(start, months(anchor.PeriodType))
   snapshot  = eligible BS with PeriodEnd == target and PeriodType == anchor.PeriodType
   else      → unavailable(fiscal_calendar_changed | no_matching_stage)
explicit:    each requested date must exist exactly; later = anchor; else unavailable(no_snapshot_at_date)
alternatives = other kinds with state available
```

### 6.3 Baseline source (one record per column)

- v1 always uses `pick(baselineDate)` with `source = stored_snapshot` and `anchorLinkVerified = false`.
- The column is labelled «تجدید ارائه‌شده» exactly when that record's `IsRepresented` is true.
- No per-row mixing ever happens.
- **Post-v1 (E-2).** If a deterministic link is verified, the policy may prefer the linked restated record and set `anchorLinkVerified = true`. Only then may the UI say that the baseline was published with the anchor report.

### 6.4 Cells, aliases, ratio, reconciliation and changes

- **Cells.** Follow spec §6.2 and the alias matrix in §6.4. An unverified candidate is recorded in `candidates` with `verified = false` and is never used for a value.
- **Ratio.** `EquityToAssetsRatio.From(totalEquity, totalAssets)` returns a full-precision value or a reason.
- **Reconciliation.** R1 compares `totalAssets` with `totalLiabilitiesAndEquity`. R2 compares `totalAssets` with `totalLiabilities + totalEquity`. Both use exact decimal equality.
- **Change.** Computed for the same `rowId`, when both cells are `available` and the units are equal:
  - `amount = a − b`;
  - `percent = amount / b × 100` only if `b > 0`;
  - otherwise `percent_unavailable`, with the reason `zero_baseline` or `negative_baseline`.
- **Unavailable inputs.** Either side missing or ambiguous gives `unavailable` with a reason.
- **Ratio change.** `pp = ratio(a) − ratio(b)` from unrounded values.

## 7. Frontend

All files are under `src/frontend/src/components/app/balance-sheet-report/`.

| File | Role |
|---|---|
| `balance-sheet-report-model.ts` | Typed result to view models. Formatters: «۰» for zero, «—» for missing, parentheses for negatives, percent to two decimals half away from zero, «واحد درصد» for pp. No arithmetic. |
| `balance-sheet-report.tsx` | Chooses a layout by density and container width (ResizeObserver): compact (narrower than 768 px or `/m`), wide default, wide comparison (two-sided at ≥ 1100 px, otherwise stacked). 1100 px is the initial design value (PD-25); it is defined as a single constant and validated in T22. Holds the view state: default or comparison, and the selected baseline kind. «مقایسه» and the selector change only local state. |
| `balance-sheet-report-wide.tsx` | Two independent RTL column grids (assets on the right). A shared final-totals row. A full-width yellow ratio row, notices, analytical strip and the «مقایسه» action. |
| `balance-sheet-comparison-wide.tsx` | Per side, a four-column grid: «شرح», «مانده در [گزارش]», «مانده در [مبنا]», «درصد تغییر». Column headers carry dates and audit/restatement badges. Each row is focusable, with a details popover and `aria-describedby` exposing the absolute change. A direction glyph uses one neutral hue, never green or red. Final totals are aligned. The full-width ratio row shows two ratios and the pp difference. Stacked mode keeps the same grid per section. |
| `balance-sheet-baseline-selector.tsx` | A segmented control or listbox of candidates. Unavailable items are disabled and show their reason. It shows the selected baseline's actual date. |
| `balance-sheet-report-compact.tsx` | Mobile default and comparison. Whole-header `<button aria-expanded aria-controls>` with a chevron. In comparison mode each row adds a secondary line with the baseline balance and percent, and its details carry the absolute change. Labels wrap, amounts use `nowrap` and `tabular-nums`, and there is no `overflow-x`. |
| `balance-sheet-funding-chart.tsx` | Recharts `ComposedChart` (R-36). Bars use `stackId` with `stackOffset="sign"`. The ratio `Line` sits on a percent axis with `connectNulls={false}`. Custom tooltip, a warning `ReferenceDot`, the negative-equity note, a hidden data table, and the x-axis oldest to newest. |
| `balance-sheet-report-image.ts` | Canvas export. The Feature 144 brand header and footer are extracted into a shared helper; this is behavior-preserving, and the Feature 144 PNG tests must stay green. The default layout is 1200 px. The comparison layout is wider, 1600 px, so each side's four columns plus the widest وبملت amounts fit. Labels wrap to at most two lines. Order: chart above, then baseline line, then table, then ratio row, then notices, then footer. |

Integration points:
- `message-list.tsx` branches on `financialStatementTableResult.balanceSheetReport`.
- `chat.functions.ts` gains the types.
- `/m` uses the compact component.
- Switching between baseline candidates on the client re-renders server values only.

## 8. Text rendering

`RenderBalanceSheetReport(result, view)` produces Persian markdown in the mobile order (spec §9.6). For a comparison it emits:
1. a baseline line;
2. per row: «شرح: مانده در A | مانده در B | درصد تغییر»;
3. the ratio row with the pp difference.

It uses the same value rules as the screen. Telegram receives the view in `requestedView`.

## 9. Observability and failure behavior

- **Telemetry.** These tags are added next to the existing `feature144.*` tags:
  - `feature154.template`, `.outcome`, `.view`, `.baseline_kind`;
  - `.baseline_state`, `.baseline_reason`, `.anchor_period_end`;
  - `.alias_discrepancy_count`, `.unverified_candidate_count`;
  - `.chart_points`;
  - read latency.

  No personal data is recorded.
- **Failure behavior.**
  - The reader uses the existing EF command timeout, and there is no retry inside the use case.
  - A failed baseline or chart read degrades only that part, with a typed reason. A failed anchor read fails the report closed.

## 10. Test design

| Level | Coverage |
|---|---|
| Unit: `FiscalCalendarPolicy` | Esfand, non-Esfand, transitional and missing evidence. The Feature 144 regression suite runs unchanged after the extraction. |
| Unit: selector | Distinct dates before ranking, ranking order, conflict without fallback, stable ID on identical versions, scale and scope ineligibility, five chart dates |
| Unit: baselines | Cases S-1 to S-7. Also: fiscal-year end from evidence, never 29 Esfand; previous report more than three months back; same stage by Jalali months, never 365 days; explicit dates in reverse order; no snapping; alternatives. |
| Unit: builder | Templates and exclusions (60, 146, 188, 286, 160), every row of the alias matrix, reported totals only, R1 and R2 kept separate, ratio cases including S-8 |
| Unit: comparison | Exact amount; percent only for a positive baseline (S-4); missing and ambiguous rows; alias discrepancy on both sides; S-1 pp gives +0.01, not 0.02 |
| Unit: routing | Every AC-R phrase on both V2 paths with a scripted model (Feature 150 style); T02 baseline diff; golden datasets |
| Unit: text | Default and comparison output, value semantics, length |
| Integration: PostgreSQL | The existing isolated setup used by `QuarterlyFinancialReports144PostgreSqlTests.cs`, seeded with the §11.1 fixtures and the S-1 to S-8 synthetic data |
| Integration: V2 endpoint | Typed response, persist and replay with candidates, one Billing charge, «مقایسه» with no new query, CyclicalWaves unavailability, V1 and Feature 141 untouched |
| Integration: bounds | A command-counting EF interceptor asserts at most 6 round trips and at most 9 statements, plus the payload snapshot and chart-point caps (AC-X04). Replay of the standard comparison performs no database read. |
| Frontend: chart setting | The Feature 154 chart renders with `ShowChart` true and false. The Feature 144 General and banking charts keep their existing behavior under both values (AC-P09). |
| Frontend: vitest | View models, layout choice, aligned totals, comparison grid, accessible details, selector states, compact accessibility, chart data |
| Browser | 320, 360 and 390 px for both views with no overflow; 1280 px default; 1440 px and 900 px (stacked) comparison; PNG of both views from desktop and mobile for وبملت (widest amounts) and فاذر |
