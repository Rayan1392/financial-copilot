# Feature 154 — Implementation tasks (revision 3)

This is a design package. No task may start until the Product Owner approves [spec.md](spec.md) revision 3.
- A **Gate** names an open decision (D-xx, spec §13) or an evidence gap (E-xx, [discovery.md §8](discovery.md#evidence-gaps)).
- A gated task may build and test everything else against synthetic fixtures. It may not enable the gated behavior on live data.

Tasks are listed in dependency order. AC IDs refer to spec §12.

## Gate and evidence

### 154-T00 — Decisions (complete)
- [x] D-01 to D-13 were closed by the Product Owner on 2026-11-04 (spec §13). No product decision gates any task.
- Only evidence gaps E-1 to E-8 remain. Each affects only the behavior listed in spec §14.

### 154-T01 — Read-only evidence collection
- **When:** This runs first, before the code it informs, and is independent of T11.
- **Do:** Use authorized read-only Development access (PostgreSQL `default_transaction_read_only=on`, as in Feature 144's investigations). Production is not inspected. Collect:
  - **E-1:**
    - For the three fixture companies plus at least five more banks and ten more non-banks, record per BalanceSheet statement which primary and candidate items exist (135–148 against 298–305, 306 against 293, 307 against 308) and where they disagree.
    - Obtain the provider definition of the «- کدال» items and of 293 and 308.
    - Get analyst sign-off. Equal values alone are not evidence.
  - **E-2:**
    - Inventory BalanceSheet records with `IsRepresented=true` for the fixtures and several restating issuers: `PeriodEnd`, `ExternalStatementId`, `PublishedAt`, and the matching original records.
    - Determine whether a **deterministic** link from a publishing report to its restated comparative exists, and document the provider's meaning of `isRepresented` and `statementID`.
  - **E-3:** Check that `JalaliFiscalYearEnd` is present and parseable for 452, 167 and 290 and their prior fiscal years, and find a transitional-year issuer.
  - **E-4:** Confirm the supplied fixture values, statement IDs (409865, 403733), the flags and fiscal evidence of 403733, and the fixture GroupIds.
  - **E-6:** Confirm that the consolidated `EXISTS` check returns the expected answers for at least one company with consolidated statements and one without.
  - **E-8:** For rows 1, 306, 18, 19, 21, 307 and 45, collect provider or Codal-taxonomy evidence of their current or non-current placement, or a financial analyst's sign-off citing the Codal statement layout.
- **Depends on:** —
- **Informs:**
  - T03: per-pair fallback flags (E-1) and subgroup moves (E-8).
  - T05 and T08: fiscal evidence (E-3).
  - Post-v1 only: anchor-link wording and the embedded preference (E-2).
  - T11: the consolidated suggestion (E-6).
- **Does not block** any v1 behavior outside the affected item. Each gap only keeps its behavior disabled (spec §14).
- **ACs:** these must pass before the live runs of AC-M09, AC-G02, AC-C02, AC-C03, AC-C06, AC-C13, AC-S01, AC-M03, AC-M04 and AC-S05.
- **Evidence:** `specs/154-ai-balance-sheet-report/evidence/*.md|json` with queries, results, and the conclusion per gap. A gap that remains unverified is recorded as such, and its behavior stays disabled.

### 154-T02 — Capture the routing baseline
- **Do:** Before any routing change, record the capability, intent and answer type on both V2 paths for:
  - every preserved phrase: bare «عملکرد X» and «وضعیت X»; «صورت سود و زیان X» and «تحلیل صورت سود و زیان X», which are the reference route for D-13; «صورت عملکرد X» and «صورت درآمد X»; Feature 083 full-table and variant requests; analysis, single-value, cash-flow, income and quarterly requests; disclosure listing; Feature 131/132;
  - the current golden entries (R-24).

  Store them as test data.
- **Depends on:** —
- **ACs:** AC-R05, AC-R09, AC-R11, AC-X03.
- **Evidence:** baseline tests green on unchanged code.

## Application policy

### 154-T03 — Row registry
- **Do:** `BalanceSheetRowRegistry` (design §2): both templates, source-title labels, primaries, fallback candidates with `verified=false` by default, exclusions (286, 60, 146, 160, 188), analytical items, and banking subtotals.
  - PD-22 placements: only 47 (current) and 51 (non-current) in subgroups; rows 1, 306, 18, 19, 21, 307 and 45 at section level.
  - Unit tests fix order, IDs, membership and placement.
- **Depends on:** —
- **Evidence-gated changes:** a fallback is flipped to verified only from T01 E-1 evidence, one pair at a time. A row moves into a subgroup only from T01 E-8 evidence.
- **ACs:** AC-M01, AC-M02, AC-M05, AC-M08, AC-M10, AC-M11.
- **Evidence:** registry tests.

### 154-T04 — Typed contract and replay
- **Do:**
  - Add the `BalanceSheetReportResult` records with string discriminators and `contractVersion 154.3` (design §3). Snapshots are stored once by `statementId`. Add the `single_date` view and the `no_snapshot_at_date` and `unsupported_date_with_variant` outcomes.
  - Add `FinancialStatementTableResult.BalanceSheetReport`, the HTTP pass-through, and persistence and replay.
- **Depends on:** —
- **ACs:** AC-X01, AC-C12, AC-X04 (payload caps).
- **Evidence:** round-trip tests for `0` vs `null`, states, candidates, changes, provenance and chart. Earlier Feature 083 and 144 messages still replay.

### 154-T05 — Extract the shared fiscal-calendar policy
- **Do:** Extract `ResolveFiscalStart`, `ResolveEffectiveFiscalStarts` and `JalaliEndAfterMonths` into `FiscalCalendarPolicy` without changing behavior. Feature 144 calls it.
- **Depends on:** —
- **ACs:** AC-X03, plus prerequisites of AC-C02, AC-C03 and AC-C06.
- **Evidence:** the Feature 144 unit and PostgreSQL suites are unchanged and green (non-Esfand regression included), and new policy unit tests pass.

### 154-T06 — Statement selector
- **Do:** Eligibility, distinct dates before ranking, the PD-09 ranking, conflict detection without fallback, the stable ID for identical versions, the exact-date anchor for single-date requests with nearest-date options, and chart dates (up to five).
- **Depends on:** T03.
- **ACs:** AC-S02, AC-S03, AC-S04, AC-S06, AC-S07, AC-S09, AC-G01, AC-G08.
- **Evidence:** unit tests, including a synthetic consolidated statement dated later, same-date IS and CF, and equal-ranked conflicting and identical versions.

### 154-T07 — Report builder
- **Do:** Cell states, the alias matrix (spec §6.4), reported totals only, `EquityToAssetsRatio`, and the separate R1 and R2 checks.
- **Depends on:** T03, T04.
- **ACs:** AC-M03, AC-M04, AC-M06, AC-M07, AC-M09, AC-M12, AC-Q01, AC-Q02.
- **Evidence:** unit tests on the §11.1 fixtures (full-precision ratios), every alias-matrix row, S-8, and synthetic R1 and R2 failures.

### 154-T08 — Baseline resolver and source policy
- **Do:**
  - The five baseline kinds (design §6.2), alternatives, unavailable reasons with named dates.
  - The v1 `BaselineSourcePolicy`: the exact-date stored snapshot, one record per column, `anchorLinkVerified=false`.
  - Missing fiscal evidence disables only the three fiscal-dependent kinds.
- **Depends on:** T05, T06. T01 (E-3) is needed for live runs only. E-2 affects only post-v1 work.
- **ACs:** AC-C02 to AC-C08, AC-C13.
- **Evidence:** unit tests S-1 to S-7 and S-9: Esfand, non-Esfand, transitional, missing evidence (with previous report still available), missing exact date, reverse-order dates, previous report more than three months back, «فصل قبل» unavailable with the previous report offered, a first-quarter anchor, and restated versus original labelling.

### 154-T09 — Comparison builder
- **Do:** Per-row change: exact amount; percent only when the baseline is greater than 0; reasons; alias discrepancies preserved. The ratio difference in pp before rounding. Column metadata.
- **Depends on:** T07, T08.
- **ACs:** AC-C05, AC-C09, AC-C10, AC-C11 (data).
- **Evidence:** unit tests showing S-1 gives +0.01 pp (not 0.02), S-2 gives +2.10 pp, and S-4 zero and negative baselines are handled.

## Infrastructure, use case and routing

### 154-T10 — EF reader
- **Do:** The bounded batched queries in design §5: catalog by tuple, balance-sheet headers (48), fiscal-evidence headers of any type (24), one batched exact-date lookup, consolidated `EXISTS`, and one batched line-item query for at most 9 statements. That is at most 6 round trips. It returns plain records.
- **Depends on:** T04.
- **ACs:** AC-S01, AC-S08, AC-M02, AC-X04.
- **Evidence:** PostgreSQL tests in T20, including the command-count interceptor. The query plans use the existing selection index.

### 154-T11 — Use case and outcomes
- **Do:** Resolve the company with provider awareness, classify by GroupId, then read, select, build, resolve baselines and compare. Map typed outcomes to Feature 117 codes. Add the consolidated suggestion only when `consolidatedAlternativeAvailable`.
- **Depends on:** T06 to T10.
- **ACs:** AC-S01, AC-S05, AC-S08, AC-M01, AC-C01 (the default response carries candidates).
- **Evidence:** use-case tests with fakes: blank `CompanyId`/452, classification unavailable, ambiguous company, no standalone statement (with and without a consolidated one).

### 154-T12 — Routing
- **Do:**
  - Alias additions, including «عملکرد مالی» and «صورت عملکرد مالی» on the «صورت سود و زیان» route (PD-21).
  - Form predicates: `single_date` (PD-20) and `unsupported_date_with_variant`.
  - Comparison and baseline cues with two-date extraction; «فصل قبل» is separate from «گزارش قبلی».
  - The quarterly guard, interpreter and governance precedence, non-entity words, the context override, and dispatch on both V2 paths (design §4).
- **Depends on:** T02, T11.
- **ACs:** AC-R01 to AC-R12.
- **Evidence:** scripted-model and deterministic-only routing tests, a T02 baseline diff limited to the intended phrases, and golden dataset updates.

### 154-T13 — Text rendering and Telegram
- **Do:** `RenderBalanceSheetReport` for the default view, comparisons and every outcome.
- **Depends on:** T11.
- **ACs:** AC-P13, plus the value semantics of AC-M06.
- **Evidence:** renderer tests and a Telegram adapter test (3,900-character split).

## Frontend

### 154-T14 — Types, view models and message branch
- **Do:** TypeScript types, `balance-sheet-report-model.ts` formatters («۰», «—», parentheses, two-decimal percent, «واحد درصد»), the `message-list.tsx` branch, and layout selection.
- **Depends on:** T04.
- **ACs:** AC-M06, AC-Q01 (display), AC-X01.
- **Evidence:** vitest.

### 154-T15 — Wide default
- **Do:** Two-sided RTL layout with independent columns, aligned final totals, the full-width yellow ratio row, R1 and R2 notices, the analytical strip, the «مقایسه» action, and the chart above.
- **Depends on:** T14, T18.
- **ACs:** AC-P01, AC-P02, AC-P09, AC-Q03, AC-M10 to AC-M12, AC-C01.
- **Evidence:** vitest and a 1280 px screenshot.

### 154-T16 — Wide comparison and baseline selector
- **Do:**
  - Two sides with four columns each, dated headers with badges, and the baseline line above.
  - Absolute change in accessible row details; neutral direction coloring.
  - The full-width ratio row with the pp difference, and stacked mode below the initial 1100 px value. That value is one constant, validated in T22.
  - A selector with disabled unavailable candidates and their reasons.
- **Depends on:** T14, T18.
- **ACs:** AC-P03, AC-P04, AC-C04, AC-C11, AC-C12.
- **Evidence:** vitest (grid, accessibility, selector) and screenshots at 1440 px and 900 px. Visual review against the Codal screenshots once supplied (E-7).

### 154-T17 — Compact default and comparison
- **Do:**
  - Collapsed sections with totals; whole-header toggle buttons with `aria-expanded`, `aria-controls`, keyboard support and 44 px height.
  - The final total and ratio always visible; wrapping labels and no-wrap amounts.
  - Comparison secondary lines and the selector above.
  - The chart below the table.
- **Depends on:** T14, T18.
- **ACs:** AC-P05 to AC-P08, AC-P09.
- **Evidence:** vitest accessibility assertions and 320, 360 and 390 px screenshots for both views with وبملت amounts.

### 154-T18 — Funding-structure chart
- **Do:**
  - Sign-aware stacking and the percent line with `connectNulls={false}`.
  - Tooltip with reported assets and the conditional «= جمع کل دارایی‌ها».
  - Negative-equity note, non-reconciliation marker, and conflict gaps.
  - Chronological axis and contrast-checked tokens.
- **Depends on:** T14.
- **ACs:** AC-G01 to AC-G08.
- **Evidence:** vitest on chart data and a screenshot.

### 154-T19 — PNG export
- **Do:**
  - Extract the Feature 144 brand header and footer into a shared helper without changing behavior.
  - Implement the default PNG (1200 px) and the comparison PNG (initial 1600 px, two-line label wrapping, validated in T22), both with the chart above. The chart is shown by default; `QuarterlyFinancialTable:ShowChart` is not read.
  - Wire «دانلود تصویر» in all layouts.
- **Depends on:** T15, T16, T17, T18.
- **ACs:** AC-P10, AC-P11, AC-P12.
- **Evidence:** image unit tests, and browser exports of both views from desktop and mobile for وبملت and فاذر with no clipping. The Feature 144 PNG scripts stay green.

## Verification and release

### 154-T20 — PostgreSQL integration
- **Do:** Seed the isolated suite with the §11.1 fixtures and the S-1 to S-10 synthetic data. Assert selection, single-date matching, baselines (including «فصل قبل»), provenance, cell and change states, and ratios. Add the command-count and payload-size assertions.
- **Depends on:** T10, T11.
- **ACs:** AC-S01 to AC-S09, AC-M01 to AC-M12, AC-Q01, AC-Q02, AC-C02 to AC-C10, AC-C13, AC-X04.
- **Evidence:** suite output with counts.

### 154-T21 — V2 endpoint, Billing and regressions
- **Do:**
  - V2 response for the default, single-date and comparison views, with persistence and replay. Replay restores the standard comparison.
  - One Billing charge, and «مقایسه» issues no new query. A custom date is a separately billed request.
  - With `QuarterlyFinancialTable:ShowChart` true and false, Feature 154 shows its chart and Feature 144's chart behavior is unchanged.
  - Authorization; CyclicalWaves unavailability; V1 and Feature 141 untouched.
  - The full R-39 suites.
- **Depends on:** T12, T13, T20.
- **ACs:** AC-R12, AC-C01, AC-X01 to AC-X03.
- **Evidence:** test output. Pre-existing failures are classified, not hidden.

### 154-T22 — Browser verification
- **Do:**
  - The screenshot matrix in design §10 for every layout and both PNGs.
  - Validate the initial 1100 px container and 1600 px PNG values with the longest registry labels and the widest وبملت amounts. Record any adjusted value in spec §9.2 and §9.5.
  - Run the visual review against the two-sided screenshot once it is supplied (E-7).
- **Depends on:** T15 to T19.
- **ACs:** AC-P01 to AC-P12.
- **Evidence:** screenshots in `specs/154-ai-balance-sheet-report/evidence/`.

### 154-T23 — Documentation and configuration readiness
- **Do:**
  - Add Feature 154 to `specs/README.md` and `implementation-checklist.md`.
  - Update the «گزارش ترازنامه به‌زودی…» landing copy (R-03).
  - Document the terminology and the coexistence with Feature 083.
  - Verify `QuarterlyFinancialReports:BankingIndustryGroupId` in each environment (E-5).
- **Depends on:** T21, T22.
- **ACs:** release readiness, AC-M01 (per environment).
- **Evidence:** updated documents and a configuration check record.

## Traceability

| AC group | Tasks |
|---|---|
| Routing AC-R01–R12 | T02, T12, T21 |
| Selection AC-S01–S09 | T01, T06, T10, T11, T20 |
| Rows AC-M01–M12 | T01, T03, T07, T15, T20, T23 |
| Ratio AC-Q01–Q03 | T07, T14, T15 |
| Comparison AC-C01–C13 | T01, T05, T08, T09, T11, T16, T20 |
| Presentation AC-P01–P13 | T13, T15, T16, T17, T19, T21, T22 |
| Chart AC-G01–G08 | T06, T18 |
| Cross-cutting AC-X01–X04 | T02, T04, T05, T10, T20, T21 |
