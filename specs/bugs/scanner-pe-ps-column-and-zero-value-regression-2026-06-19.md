# Scanner PE/PS Column and Zero-Value Regression

Date: 2026-06-19
Status: Root cause confirmed, spec corrected, implementation required
Scope: Scanner result table column selection, valuation-ratio zero-value filtering, quote enrichment boundary

## Observed Behavior

User query:

```text
لیست نمادهای با پی به ای زیر 4 و پی به اس زیر 1
```

Current incorrect response table columns:

- `نماد`
- `شرکت`
- `آخرین قیمت`
- `Change %`
- `ارزش بازار`
- `PE_TTM`
- `PS_TTM`
- `symbols`

Current incorrect row data:

- Many rows have `PE_TTM = 0`
- Many rows have `PS_TTM = 0`

## Expected Behavior

Table columns must be exactly:

1. `نماد`
2. `شرکت`
3. `PE_TTM`
4. `PS_TTM`

No `آخرین قیمت`, no `Change %`, no `ارزش بازار`, no `symbols`, no other unrequested column.

Returned rows must have:
- Valid non-zero `PE_TTM` in range `(0, 4)`
- Valid non-zero `PS_TTM` in range `(0, 1)`

No row with `PE_TTM = 0` or `PS_TTM = 0`.

## Root Cause 1: Scanner Column Policy Adds Unrequested Quote Columns

`IScannerResultColumnPolicy` currently treats `LATEST_PRICE`, `DAILY_CHANGE_PCT`, and `MARKET_CAP`
as default columns for scanner results. This was specified in `008-scanner-execution-engine` as
"default displayed columns are symbol, latest price, price change percentage, market capitalization,
and the metrics relevant to the user's question."

That original rule did not distinguish between:
- direct single-symbol point lookup (where quote enrichment makes sense for valuation questions)
- scanner/filter queries (where quote columns should only appear when explicitly requested)

The `ShouldIncludeMarketContext(...)` logic in `EfCoreSymbolMetricLookupService` suppresses quote
columns only for monthly-activity metrics. For PE/PS scanner queries, quote columns are added
automatically, which is incorrect for the scanner path.

**Spec corrected in:** `008-scanner-execution-engine/user-story.md`, `008-scanner-execution-engine/tasks.md`, `docs/scanner-mvp-scope.md`, `045-symbol-metric-point-lookup/user-story.md`, `045-symbol-metric-point-lookup/tasks.md`.

## Root Cause 2: Internal `symbols` Column Leaks Into User-Facing Output

An internal/debug column named `symbols` appears in the rendered scanner table. This column is not
a user-facing financial metric and must never be rendered in the output. It is likely being added
by the column projection or frontend mapping without a filter for internal column names.

## Root Cause 3: Zero PE/PS Values Pass Valuation Ratio Filters

Scanner execution does not apply a validity gate on ratio/valuation metrics. A stored value of
`0` for `PE_TTM` or `PS_TTM` represents missing, uncomputed, or economically undefined data —
not a true ratio of zero. Without a validity gate, rows with `PE_TTM = 0` satisfy the condition
`PE_TTM < 4`, producing misleading scanner results.

**Spec corrected in:** `008-scanner-execution-engine/user-story.md`, `008-scanner-execution-engine/tasks.md`, `docs/scanner-mvp-scope.md`.

## Affected Queries

- `لیست نمادهای با پی به ای زیر 4 و پی به اس زیر 1`
- Any scanner/filter query on `PE_TTM`, `PS_TTM`, `PB`, or other valuation ratio metrics
- Any scanner query that does not explicitly request price, daily change, or market cap

## Acceptance Criteria (all must pass before closing this bug)

1. Scanner result tables always include `نماد` and `شرکت` as the first two columns.
2. Scanner result tables include only the metrics explicitly requested, filtered, sorted, or otherwise named by the user.
3. Scanner result tables must not automatically add `LATEST_PRICE`, `DAILY_CHANGE_PCT`, or `MARKET_CAP` for PE/PS/PB filters.
4. Quote columns may appear in scanner results only when the user explicitly asks for them or they are part of a filter/sort condition.
5. The `symbols` column (or any other internal/debug column) must never be rendered in user-facing scanner tables.
6. For PE/PS valuation screening, `0` values must not satisfy `<`, `<=`, or "below" filters.
7. `PE_TTM = 0` and `PS_TTM = 0` must be treated as missing/invalid for valuation-screening match eligibility.
8. The query `لیست نمادهای با پی به ای زیر 4 و پی به اس زیر 1` must:
   - Return only rows with valid non-zero `PE_TTM` and valid non-zero `PS_TTM`.
   - Render only these columns: `نماد`, `شرکت`, `PE_TTM`, `PS_TTM`.
9. Direct single-symbol PE/PS point lookup (e.g., `پی به ای کگل چقدر است؟`) via `SymbolLookup`
   intent is unaffected; its existing quote enrichment behavior (`LATEST_PRICE`, `DAILY_CHANGE_PCT`
   for non-monthly metrics) remains unchanged.

## Required Implementation Tasks

### Backend — Scanner Column Policy

1. Revise `IScannerResultColumnPolicy` / its implementation:
   - Always output `نماد` (symbol) and `شرکت` (company name) as columns 1 and 2.
   - Do not add `LATEST_PRICE`, `DAILY_CHANGE_PCT`, or `MARKET_CAP` unless they are present as
     explicit user filter/sort conditions or the user explicitly requested them in their message.
   - Remove any automatic quote-enrichment logic that adds these columns for PE/PS scanner queries.

2. Remove internal column `symbols` from all scanner result projections and frontend column mapping.

### Backend — Valuation Ratio Zero-Value Gate

3. Add a validity predicate to `IScannerExecutionService` condition evaluation:
   - For ratio metrics (`PE_TTM`, `PS_TTM`, `PB`, and other valuation multiples), treat stored
     value `0` as missing/invalid.
   - Append `AND MetricValue > 0` to the SQL or EF Core predicate for any `<` or `<=` condition
     on these metrics.
   - Do not apply the zero-exclusion rule to `>` or `>=` conditions (positive thresholds already
     exclude zero naturally, and explicit "zero or above" queries remain valid).

### Frontend — Column Mapping

4. Review frontend scanner table column mapping (`mapAssistantBlock` / `ScannerResultTable`) for
   any logic that adds columns beyond what the backend response provides.
5. Add a guard that filters out any column with a key matching internal identifiers (e.g., `symbols`).

### Tests

6. Add regression tests for `IScannerResultColumnPolicy`:
   - PE/PS filter-only query → columns are `نماد`, `شرکت`, `PE_TTM`, `PS_TTM` only.
   - PE/PS filter + explicit `همراه با آخرین قیمت` → columns include `LATEST_PRICE`.
   - PE/PS filter + market-cap filter condition → columns include `MARKET_CAP`.
   - `symbols` or other internal columns are never present in output.

7. Add unit tests for valuation-ratio zero-value exclusion:
   - `PE_TTM = 0`, condition `PE_TTM < 4` → row excluded.
   - `PS_TTM = 0`, condition `PS_TTM < 1` → row excluded.
   - `PE_TTM = 3.5`, condition `PE_TTM < 4` → row included.
   - `PS_TTM = 0.8`, condition `PS_TTM < 1` → row included.

8. Add API-level integration test for the exact Persian query:
   `لیست نمادهای با پی به ای زیر 4 و پی به اس زیر 1`
   - Assert: no `LATEST_PRICE` column.
   - Assert: no `DAILY_CHANGE_PCT` column.
   - Assert: no `MARKET_CAP` column.
   - Assert: no `symbols` column.
   - Assert: columns are exactly `نماد`, `شرکت`, `PE_TTM`, `PS_TTM`.
   - Assert: no row has `PE_TTM = 0`.
   - Assert: no row has `PS_TTM = 0`.

9. Add regression test that direct point lookup (`پی به ای کگل چقدر است؟` → `SymbolLookup` intent)
   still includes quote columns when quote data exists (proving scanner fix does not weaken
   the existing point-lookup behavior).

## Important: Do Not Confuse These Two Query Classes

### Direct point lookup (unaffected)

```text
پی به ای کگل چقدر است؟
```

Handled by: `SymbolLookup` intent → `EfCoreSymbolMetricLookupService` → `ShouldIncludeMarketContext`
Quote enrichment: allowed for non-monthly-activity metrics
Spec owner: `045-symbol-metric-point-lookup`

### Scanner/filter query (this bug)

```text
لیست نمادهای با پی به ای زیر 4 و پی به اس زیر 1
```

Handled by: `Scanner` intent → `IScannerExecutionService` → `IScannerResultColumnPolicy`
Quote enrichment: not allowed unless explicitly requested or part of filter/sort
Spec owner: `008-scanner-execution-engine`

## Confirmed: No Production Code Changed In This Investigation

Only spec files were updated. No production code, tests, or migrations were modified.

## Spec Files Updated

- `specs/008-scanner-execution-engine/user-story.md` — corrected scanner column rules, added zero-value validity rule
- `specs/008-scanner-execution-engine/tasks.md` — added implementation and regression test tasks
- `docs/scanner-mvp-scope.md` — corrected default column description, added zero-value and internal-column rules
- `specs/045-symbol-metric-point-lookup/user-story.md` — clarified scope boundary between point-lookup quote enrichment and scanner column policy
- `specs/045-symbol-metric-point-lookup/tasks.md` — added `ShouldIncludeMarketContext` scope note
