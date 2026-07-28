# Scanner PE/PS Result Shaping Diagnostic — 2026-06-19

**Status:** Investigation complete — root causes confirmed, no production code changed  
**Scope:** Scanner result table column selection, valuation-ratio zero-value filtering,
clarification hallucination, prose/table symbol divergence  
**Related spec:** `specs/bugs/scanner-pe-ps-column-and-zero-value-regression-2026-06-19.md`

---

## 1. Executive Summary

A Persian-language scanner query for symbols with PE < X and PS < Y produces four distinct
failure modes simultaneously:

| # | Symptom | Severity |
|---|---------|----------|
| 1 | LLM asks "در کدام بازار؟" before scanning | High — blocks the query entirely |
| 2 | Rows with PE_TTM = 0 or PS_TTM = 0 appear in results | High — misleading results |
| 3 | Table includes unrequested columns: `آخرین قیمت`, `Change %`, `ارزش بازار`, `symbols` | High — cluttered, confusing output |
| 4 | Prose narrative lists different symbols than the table | Medium — erodes trust |

All four bugs are independent in root cause but co-occur on this query class. None of them
require a database change. Bug 3 requires the most significant backend change (column policy
rewrite). Bug 2 requires a targeted predicate guard. Bug 1 requires a system-prompt update.
Bug 4 requires passing the full page symbol list to the explanation generator.

---

## 2. Trigger Query

```text
لیست نمادهای با pe کمتر از 5 و ps کمتر از 2
```

Variant also observed:

```text
لیست نمادهای با پی به ای زیر 4 و پی به اس زیر 1
```

Both are unambiguous scanner filter queries. No market scope is needed; the scanner defaults
to the full market universe. No clarification should be required.

---

## 3. Observed vs. Expected Behavior

### Observed

**Step 1:** LLM returns `clarificationRequired = true` with message:
> "لطفاً مشخص کنید که این سهام‌ها در کدام بازار مورد نظر شما هستند؟"
> ("Please specify which market these stocks are in?")

**Step 2 (after user answers or re-submits):** Scanner executes and returns a table with columns:

| نماد | شرکت | آخرین قیمت | Change % | ارزش بازار | PE_TTM | PS_TTM | symbols |
|------|------|------------|----------|------------|--------|--------|---------|
| خچرخش | ... | 4,250 | 1.2% | 8.3T | 0 | 0 | خچرخش |
| خزر | ... | 12,100 | -0.4% | 2.1T | 0 | 0.6 | خزر |
| دتولید | ... | 9,870 | 0.8% | 5.7T | 3.8 | 0 | دتولید |

**Step 3:** Prose narrative mentions: "از میان 247 نماد یافت شده، نمادهایی مانند شپنا، پالایش، وبملت..."
(symbols not present in the table)

### Expected

No clarification step. Table columns: `نماد`, `شرکت`, `PE_TTM`, `PS_TTM` only.
No row with `PE_TTM = 0` or `PS_TTM = 0`. Prose narrative names only symbols from the
current table page.

---

## 4. Architecture Map — Relevant Code Paths

```
POST /api/ai/v1/query
        │
        ▼
AiFacadeController.QueryAsync()
        │
        ▼
AiQueryOrchestrationService
        │
        ├──[Parse]──► LlmScannerQueryParser.ParseAsync()          ◄── Bug 1 lives here
        │                    │
        │                    └─ Returns clarificationRequired=true if LLM decides so
        │
        ├──[Execute]──► EfCoreScannerExecutionService.ExecuteAsync()
        │                    │
        │                    ├─ Loads displayMetricCodes (always adds MARKET_CAP, LATEST_PRICE)
        │                    ├─ PassesCondition() — pure numeric, no zero guard  ◄── Bug 2
        │                    └─ ScannerResultColumnPolicy.BuildColumns()          ◄── Bug 3
        │
        └──[Explain]──► LlmScannerExplanationGenerator.GenerateAsync()            ◄── Bug 4
                             │
                             └─ Receives only Take(5) of paginated result symbols
```

**Column flow to frontend:**

```
ScannerResultColumnPolicy.DefaultColumns
        │ (always: SYMBOL, COMPANY, LATEST_PRICE, DAILY_CHANGE_PCT, MARKET_CAP)
        ▼
AiFacadeController.MapScannerTable()
        │ (maps Columns list verbatim to ScannerTableColumnResponse[])
        ▼
JSON response { columns: [...] }
        │
        ▼
chat.functions.ts: ScannerTable.columns[]
        │ (no filtering — renders all backend-provided columns)
        ▼
ScannerResultTable component — displays every column in the array
```

The frontend adds no columns of its own. The bug is entirely in the backend column list.

---

## 5. Bug 1 — Hallucinated Market-Scope Clarification

### Root Cause

`LlmScannerQueryParser` system prompt (lines 50–67 of
`src/backend/FinancialCopilot.Application/Scanner/LlmScannerQueryParser.cs`):

```csharp
// System prompt excerpt (paraphrased from code):
"Set clarificationRequired=true if the query is ambiguous or cannot be resolved to a
 valid scanner plan."
```

The prompt does not enumerate which situations count as "ambiguous." It gives the LLM
discretion. The LLM's training data associates Iranian stock-market queries with two
possible markets (TSE and OTC/فرابورس), and it independently decides that "which market?"
is a necessary disambiguation — even though the backend has no per-market filtering logic
and would scan the full universe regardless.

No backend code enforces market-scope as a required field. The `ScannerQueryPlan.Universe`
has a `Market` string field, but `EfCoreScannerExecutionService` does not filter by it in
the current implementation. The clarification is 100% a hallucination from the LLM.

### Evidence

- `LlmScannerQueryParser.cs` lines 172–234: `BuildPlan()` sets `clarificationRequired = true`
  only when the LLM returns it OR when metric resolution returns `Ambiguous`/`NotFound`.
- `EfCoreScannerExecutionService.cs`: no `WHERE Market = ...` clause in the main query.
- The `ScannerQueryPlan.Universe.Market` field exists but is never consumed in execution.

### Failure Chain

```
User: "لیست نمادهای با pe کمتر از 5"
        │
        ▼
LlmScannerQueryParser calls LLM with system prompt
        │
        ▼
LLM returns { clarificationRequired: true, clarificationMessage: "در کدام بازار؟" }
        │  (LLM associates TSE/OTC ambiguity from training data)
        ▼
BuildPlan() sees clarificationRequired=true → returns ClarificationRequired plan
        │
        ▼
User sees clarification dialog — query blocked
```

### Hypothesis Confidence: High

The system prompt's open-ended "if ambiguous" instruction with no enumerated valid-ambiguity
cases gives the LLM too much discretion. Adding explicit negative examples ("market scope
is NOT required to clarify — assume all markets") would suppress this.

---

## 6. Bug 2 — Zero-Valued Ratios Pass `<` Filters

### Root Cause

`PassesCondition()` in
`src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreScannerExecutionService.cs`
(lines 365–375):

```csharp
private static bool PassesCondition(decimal value, ConditionOperator op, decimal threshold) =>
    op switch
    {
        ConditionOperator.LessThan        => value < threshold,
        ConditionOperator.LessThanOrEqual => value <= threshold,
        ConditionOperator.GreaterThan     => value > threshold,
        ConditionOperator.GreaterThanOrEqual => value >= threshold,
        ConditionOperator.Equal           => value == threshold,
        ConditionOperator.NotEqual        => value != threshold,
        _ => false
    };
```

No zero-value guard exists. `0 < 5 = true`, so any row where `PE_TTM` or `PS_TTM` is
stored as `0` satisfies a `< threshold` filter.

### What `0` Means in the Data

In `DerivedMetrics`, a stored value of `0` for `PE_TTM` or `PS_TTM` does not mean "the
company's valuation ratio is zero." It means one of:

- The denominator (earnings or sales) is negative or zero — ratio is economically undefined
- The metric was never computed for this company in this period
- The ETL wrote a sentinel `0` instead of `NULL` for missing data

None of these cases should satisfy a `< threshold` valuation filter. They represent invalid
or absent data, not a genuine ratio of zero.

### Failure Chain

```
Company row: { ExternalCompanyId: "X", PE_TTM: 0, PS_TTM: 0 }
        │
        ▼
EfCoreScannerExecutionService: filters companies
        │
        ▼
PassesCondition(0, LessThan, 5) → 0 < 5 → true ✓
PassesCondition(0, LessThan, 2) → 0 < 2 → true ✓
        │
        ▼
Company "X" included in result set despite having no valid PE or PS
```

### Scope of Affected Metrics

The zero-exclusion rule applies to all valuation multiple metrics where a zero value is
economically meaningless:

| Metric | Reason zero is invalid |
|--------|------------------------|
| PE_TTM | Negative/zero earnings → ratio undefined |
| PS_TTM | Negative/zero revenue → ratio undefined |
| PB | Negative book value → ratio undefined |
| EV_EBITDA | Negative EBITDA → ratio undefined |

Non-ratio metrics (sales, net profit, production volume) may legitimately be zero or
negative and must not be filtered.

### Hypothesis Confidence: Confirmed

`PassesCondition()` is a pure in-memory function with no zero guard. The data model stores
`0` for missing/invalid valuations. The combined effect is deterministic.

---

## 7. Bug 3 — Unrequested Columns in Scanner Table

### Root Cause A: `DefaultColumns` in `ScannerResultColumnPolicy`

`src/backend/FinancialCopilot.Application/Scanner/ScannerExecutionServices.cs`, lines 5–12:

```csharp
private static readonly IReadOnlyCollection<ScannerTableColumn> DefaultColumns =
[
    new ScannerTableColumn("SYMBOL",           "Symbol",       ScannerColumnType.Symbol),
    new ScannerTableColumn("COMPANY",          "Company",      ScannerColumnType.CompanyName),
    new ScannerTableColumn("LATEST_PRICE",     "Latest Price", ScannerColumnType.LatestPrice),
    new ScannerTableColumn("DAILY_CHANGE_PCT", "Change %",     ScannerColumnType.DailyChangePercent),
    new ScannerTableColumn("MARKET_CAP",       "Market Cap",   ScannerColumnType.MarketCap)
];
```

`BuildColumns()` (lines 42–85) starts by selecting all 5 `DefaultColumns` and then appends
condition metrics. For a PE/PS-only query, the result is always 7 columns:
`SYMBOL`, `COMPANY`, `LATEST_PRICE`, `DAILY_CHANGE_PCT`, `MARKET_CAP`, `PE_TTM`, `PS_TTM`.

### Root Cause B: `EfCoreScannerExecutionService` Unconditionally Loads Quote Data

`src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreScannerExecutionService.cs`,
lines 39–41 (approximate):

```csharp
displayMetricCodes.Add("MARKET_CAP");
displayMetricCodes.Add("LATEST_PRICE");
```

These two lines run unconditionally before the column list is evaluated. Even if
`BuildColumns()` were fixed to exclude quote columns, the infrastructure service would still
fetch them and attempt to populate cells. Both layers need coordinated fixes.

### Root Cause C: `symbols` Column — Unconfirmed Source

The `symbols` column appearing in the user-facing table is not in `DefaultColumns` (which
uses the identifier `SYMBOL`, not `symbols`). Candidates for its origin:

| Hypothesis | Evidence | Confidence |
|------------|----------|------------|
| `ScannerQueryPlan.Universe.Symbols` list serialized as a column by LLM | LLM-generated plans include a `symbols` field in the `universe` object | Medium |
| A `RequestedColumns` entry from the LLM returning `{ identifier: "symbols" }` | `BuildColumns()` lines 65–82 appends `plan.RequestedColumns` without filtering internal names | Medium |
| A separate debug/instrumentation code path not captured in source reads | Not found in source | Low |

**Action required:** Inspect a real API response JSON payload with `scannerTable.columns`
to confirm which `identifier` value maps to the `symbols` display column. The LLM system
prompt in `LlmScannerQueryParser.cs` includes `"universe": { "symbols": [] }` in its
output schema — the LLM may be reflecting this back as a `RequestedColumns` entry.

### Column Mapping Chain (Bug 3 confirmed portion)

```
DefaultColumns (5 entries including LATEST_PRICE, DAILY_CHANGE_PCT, MARKET_CAP)
        │
        ▼ BuildColumns() — always starts with all DefaultColumns
        ▼ then appends: PE_TTM, PS_TTM (from plan.Conditions)
        ▼ then appends: any plan.RequestedColumns (including possibly "symbols")
        │
        ▼
ScannerTableColumn[] with 7–8 entries
        │
        ▼ LocalizeDefaultColumn() maps LATEST_PRICE → "آخرین قیمت" (Persian)
        │                                DAILY_CHANGE_PCT → "Change %" (English)
        │                                MARKET_CAP → "ارزش بازار" (Persian)
        ▼
AiFacadeController.MapScannerTable()
        │ maps all columns verbatim to ScannerTableColumnResponse[]
        ▼
JSON { columns: [ {identifier: "SYMBOL"}, {identifier: "COMPANY"},
                  {identifier: "LATEST_PRICE"}, {identifier: "DAILY_CHANGE_PCT"},
                  {identifier: "MARKET_CAP"}, {identifier: "PE_TTM"},
                  {identifier: "PS_TTM"}, {identifier: "symbols"} ] }
        │
        ▼
Frontend: ScannerTable.columns[] rendered as-is — no filtering
```

### Localization Inconsistency

`LocalizeDefaultColumn()` translates `LATEST_PRICE` to `آخرین قیمت` and `MARKET_CAP` to
`ارزش بازار` (Persian), but leaves `DAILY_CHANGE_PCT` as `"Change %"` (English) for a
Persian-language query. This is a secondary bug but visible in the output.

---

## 8. Bug 4 — Prose Narrative Lists Different Symbols Than Table

### Root Cause

`LlmScannerExplanationGenerator.BuildUserContent()` in
`src/backend/FinancialCopilot.Application/Scanner/LlmScannerExplanationGenerator.cs`,
lines 53–65:

```csharp
private static string BuildUserContent(ScannerExplanationRequest request)
{
    var symbolList = request.MatchedSymbols.Count > 0
        ? string.Join(", ", request.MatchedSymbols.Take(5))
        : "no symbols";
    return $"Query: \"{request.OriginalQuery}\"\n" +
           $"Filters: {filterList}\n" +
           $"Found {request.MatchedSymbolCount} symbol(s): {symbolList}";
}
```

`request.MatchedSymbols` is populated in `ExplainableAnswerServices.cs` (lines 115–122):

```csharp
MatchedSymbols = result.Rows.Select(r => r.SymbolCode).ToList()
```

`result.Rows` contains only the current paginated page (up to `pageSize` rows, default ≤ 100).
`request.MatchedSymbolCount` carries the total count across all pages (e.g., 247).

The LLM receives: `"Found 247 symbol(s): خچرخش, خزر, دتولید, شخارک, غدام"`

Given only 5 symbols but told there are 247, the LLM may:
1. Enumerate beyond the provided 5, drawing on training-data knowledge of Iranian stocks
2. Hallucinate plausible-sounding symbols that happen to not be in the actual result set
3. Produce a grammatically valid but factually wrong prose description

### Failure Chain

```
Scanner returns 247 matching rows; page 1 contains 20 rows
        │
        ▼
ExplainableAnswerServices: MatchedSymbols = page 1 symbols (20 entries)
        │
        ▼
BuildUserContent: Take(5) → passes only 5 symbols to LLM
        │         but passes MatchedSymbolCount = 247
        ▼
LLM prompt: "Found 247 symbol(s): خچرخش, خزر, دتولید, شخارک, غدام"
        │
        ▼
LLM: "از میان 247 نماد، نمادهایی مانند شپنا، پالایش، وبملت..."
     (LLM extrapolates from general knowledge — different symbols than page 1)
        │
        ▼
User sees: table has خچرخش, خزر, دتولید... but prose mentions شپنا, پالایش, وبملت
```

### Why `.Take(5)` Exists

The `.Take(5)` limit is likely a token-budget optimization — passing 247 symbols to the LLM
would be expensive. The fix is not to remove the limit but to frame the context accurately:
tell the LLM these are a sample, not an exhaustive list, and instruct it to describe only
what it was given.

---

## 9. Evidence Table

| Bug | File | Line(s) | Evidence |
|-----|------|---------|---------|
| 1 | `LlmScannerQueryParser.cs` | 50–67 | System prompt: "clarificationRequired=true if ambiguous" — no enumerated exceptions for market scope |
| 1 | `LlmScannerQueryParser.cs` | 172–234 | `BuildPlan()` propagates LLM's `clarificationRequired` unconditionally |
| 1 | `EfCoreScannerExecutionService.cs` | (no market filter) | `Universe.Market` field exists but is never applied as a WHERE predicate |
| 2 | `EfCoreScannerExecutionService.cs` | 365–375 | `PassesCondition()`: pure `value < threshold` with no zero guard |
| 2 | `DerivedMetrics` table | (schema) | `0` stored for missing/undefined valuation ratios — not NULL |
| 3 | `ScannerExecutionServices.cs` | 5–12 | `DefaultColumns` always includes `LATEST_PRICE`, `DAILY_CHANGE_PCT`, `MARKET_CAP` |
| 3 | `ScannerExecutionServices.cs` | 42–85 | `BuildColumns()` unconditionally prepends all `DefaultColumns` |
| 3 | `EfCoreScannerExecutionService.cs` | 39–41 | Unconditionally adds `MARKET_CAP`, `LATEST_PRICE` to `displayMetricCodes` |
| 3 | `AiFacadeController.cs` | 248–257 | `MapScannerTable()` maps all columns verbatim — no filtering |
| 3 | `chat.functions.ts` | 46–66 | `ScannerTable.columns` rendered as-is — frontend adds nothing, but filters nothing |
| 3 | `ScannerExecutionServices.cs` | 87–103 | `LocalizeDefaultColumn()`: `DAILY_CHANGE_PCT` stays English even for Persian queries |
| 3 (`symbols`) | `LlmScannerQueryParser.cs` | (system prompt schema) | `universe.symbols` in output schema — LLM may return it as `RequestedColumns` entry |
| 4 | `LlmScannerExplanationGenerator.cs` | 53–65 | `BuildUserContent()` calls `.Take(5)` on paginated symbols |
| 4 | `ExplainableAnswerServices.cs` | 115–122 | `MatchedSymbols` = page rows only; `MatchedSymbolCount` = total |

---

## 10. Backend vs. Frontend Responsibility

| Column / Behavior | Backend Responsible | Frontend Responsible |
|-------------------|:-------------------:|:--------------------:|
| `LATEST_PRICE` appearing in scanner output | ✓ DefaultColumns, BuildColumns() | — |
| `DAILY_CHANGE_PCT` appearing in scanner output | ✓ DefaultColumns | — |
| `MARKET_CAP` appearing in scanner output | ✓ DefaultColumns + EfCoreScanner lines 39–41 | — |
| `symbols` column appearing in output | ✓ Likely via RequestedColumns or plan schema | Possibly: no column filter guard |
| PE/PS = 0 rows passing filter | ✓ PassesCondition() no zero guard | — |
| Clarification for market scope | ✓ LLM system prompt too permissive | — |
| Prose/table symbol divergence | ✓ BuildUserContent Take(5) | — |
| `DAILY_CHANGE_PCT` English label in Persian context | ✓ LocalizeDefaultColumn() | — |
| Rendering extra columns beyond what backend sends | — | Not the cause — renders verbatim |

**Conclusion:** All observable bugs are backend-rooted. The frontend is a passive renderer
and is not at fault for any of these symptoms. However, the frontend lacks a defensive guard
that would reject unknown or internal-identifier columns (`symbols`, any identifier starting
with `_`). Adding such a guard would provide a safety net against future backend regressions.

---

## 11. Recommended Spec Updates

| Spec File | Section | Required Change |
|-----------|---------|-----------------|
| `specs/008-scanner-execution-engine/user-story.md` | Scanner Result Column Rules | Already corrected in Session 1 — no further change needed |
| `specs/008-scanner-execution-engine/tasks.md` | `IScannerResultColumnPolicy` task | Already corrected in Session 1 |
| `docs/scanner-mvp-scope.md` | Result Features | Already corrected in Session 1 |
| `specs/007-natural-language-scanner-parser/user-story.md` | Clarification Rules | Add: "Market scope is not a valid clarification reason. The scanner operates over the full universe by default. Set `clarificationRequired=false` for queries where only metric name and threshold are present." |
| `specs/007-natural-language-scanner-parser/tasks.md` | LLM System Prompt | Add: negative clarification examples — query with PE/PS filters only must not trigger clarification. |
| `specs/008-scanner-execution-engine/tasks.md` | Valuation Ratio Zero Gate | Already added — includes EfCore predicate update task |
| `specs/009-explainable-results/user-story.md` | Symbol List Accuracy | Add: "The explanation prompt must describe only the symbols it was given. The prompt must frame the sample as a sample (`نمادهایی مانند` not `نمادها عبارتند از`), and must not enumerate symbols not present in the provided list." |
| `specs/009-explainable-results/tasks.md` | `BuildUserContent` | Add: "Pass all page symbols (not just first 5). Frame context as 'sample of page N'. Instruct LLM: do not list symbols beyond what is provided." |

---

## 12. Recommended Implementation Tasks

| Priority | Task | File(s) | Description |
|----------|------|---------|-------------|
| P0 | Fix `DefaultColumns` | `ScannerExecutionServices.cs` | Remove `LATEST_PRICE`, `DAILY_CHANGE_PCT`, `MARKET_CAP` from `DefaultColumns`. Retain only `SYMBOL` and `COMPANY`. |
| P0 | Fix `BuildColumns()` | `ScannerExecutionServices.cs` | Start only from identity columns. Add metric columns from `plan.Conditions` and `plan.RequestedColumns`. Add quote columns only when: (a) user explicitly requested them, or (b) they appear as a filter/sort condition. |
| P0 | Fix `displayMetricCodes` loading | `EfCoreScannerExecutionService.cs` | Remove unconditional `.Add("MARKET_CAP")` and `.Add("LATEST_PRICE")`. Derive `displayMetricCodes` from the column list built by `IScannerResultColumnPolicy`. |
| P0 | Add zero-value guard | `EfCoreScannerExecutionService.cs` | In `PassesCondition()` (or before it is called), return `false` for valuation ratio metrics when `value == 0` and operator is `LessThan` or `LessThanOrEqual`. |
| P1 | Fix LLM system prompt | `LlmScannerQueryParser.cs` | Add explicit negative example: PE/PS filter-only query must not trigger clarification. State: market scope is not required. |
| P1 | Fix `BuildUserContent()` | `LlmScannerExplanationGenerator.cs` | Pass all page symbols (not just 5). Reframe prompt: "These are the symbols on this page of results. Describe only these. Do not enumerate other symbols." |
| P1 | Filter `symbols` / internal columns | `ScannerResultColumnPolicy` or `BuildColumns()` | When appending `plan.RequestedColumns`, filter out identifiers matching known internal/schema field names: `symbols`, `universe`, `conditions`, `sort`, `limit`. |
| P2 | Fix Persian localization | `ScannerExecutionServices.cs` `LocalizeDefaultColumn()` | Ensure `DAILY_CHANGE_PCT` gets a Persian display name (`تغییر روزانه %`) when `usePersianLabels` is true. |
| P2 | Add frontend column guard | `ScannerResultTable` or `chat.functions.ts` | Filter out any column whose `identifier` matches `symbols`, starts with `_`, or is not in the declared `ScannerColumnType` set. |

---

## 13. Recommended Regression Tests

| Test | Type | Assertion |
|------|------|-----------|
| PE/PS filter-only query → exactly 4 columns | Integration | `columns.length == 4` and identifiers are `SYMBOL`, `COMPANY`, `PE_TTM`, `PS_TTM` |
| No `LATEST_PRICE` in PE/PS filter result | Integration | `columns` does not contain identifier `LATEST_PRICE` |
| No `DAILY_CHANGE_PCT` in PE/PS filter result | Integration | `columns` does not contain identifier `DAILY_CHANGE_PCT` |
| No `MARKET_CAP` in PE/PS filter result | Integration | `columns` does not contain identifier `MARKET_CAP` |
| No `symbols` column in any scanner result | Integration | No column has identifier `symbols` (case-insensitive) |
| Explicit price request includes `LATEST_PRICE` | Integration | Query with `همراه با آخرین قیمت` → `LATEST_PRICE` column present |
| `PE_TTM = 0` row excluded from `PE < 5` scan | Unit | `PassesCondition(0, LessThan, 5, isValuationRatio: true)` returns `false` |
| `PS_TTM = 0` row excluded from `PS < 2` scan | Unit | `PassesCondition(0, LessThan, 2, isValuationRatio: true)` returns `false` |
| `PE_TTM = 3.8` row included in `PE < 5` scan | Unit | `PassesCondition(3.8m, LessThan, 5, isValuationRatio: true)` returns `true` |
| `PS_TTM = 0.7` row included in `PS < 2` scan | Unit | `PassesCondition(0.7m, LessThan, 2, isValuationRatio: true)` returns `true` |
| `PE_TTM = 0` row included in `PE > 0` scan | Unit | Zero exclusion does not apply to `>` operator |
| PE/PS filter query does not trigger clarification | Unit (parser) | Query `"pe کمتر از 5"` → `clarificationRequired == false` |
| Exact Persian query end-to-end | Integration (API) | `"لیست نمادهای با pe کمتر از 5 و ps کمتر از 2"` → no clarification, 4-column table, no zero rows |
| Prose does not invent symbols not on current page | Integration | Every symbol in prose narrative appears in `scannerTable.rows[*].symbolCode` |
| Point lookup (`پی به ای کگل`) still includes quote columns | Integration | `SymbolLookup` intent result includes `LATEST_PRICE` column (regression guard) |

---

## 14. Open Questions

1. **`symbols` column exact origin:** The source of the `symbols` column identifier in the
   API response could not be confirmed from source-code reads alone. Runtime payload
   inspection is needed. Inspect `scannerTable.columns` in a real API response JSON for a
   PE/PS query and check whether `identifier == "symbols"` appears, and if so whether
   `columnType == "Metric"` or something else. This will determine whether the fix is in
   `BuildColumns()` (filtering `plan.RequestedColumns`) or elsewhere.

2. **`displayMetricCodes` lines 39–41:** The exact variable name and location of the
   unconditional `MARKET_CAP`/`LATEST_PRICE` additions in `EfCoreScannerExecutionService.cs`
   should be confirmed before the fix is written, as they govern which cells are actually
   populated — not just which columns are declared. Both the column list and the data fetch
   must be fixed together.

3. **Valuation ratio metric list:** A canonical list of metrics that require zero-exclusion
   should be defined in domain terms (not as a hardcoded string set), ideally as a property
   on `MetricDefinition` or via the Financial Semantic Layer. This avoids the need to update
   a filter list when new ratio metrics are added.

4. **Prose symbol count context:** When passing symbols to `BuildUserContent()`, the
   pagination context (page number, page size, total pages) should also be passed so the
   LLM can frame its description accurately: "این صفحه ۲۰ نماد از ۲۴۷ را نشان می‌دهد."

---

## Confirmed: No Production Code Was Changed

This file is a diagnostic investigation report only. All findings are derived from
static source-code reads. No production code, tests, specs, or migrations were
modified as part of this investigation.

Related spec corrections (Session 1, same date) are in:
- `specs/008-scanner-execution-engine/user-story.md`
- `specs/008-scanner-execution-engine/tasks.md`
- `docs/scanner-mvp-scope.md`
- `specs/045-symbol-metric-point-lookup/user-story.md`
- `specs/045-symbol-metric-point-lookup/tasks.md`
- `specs/bugs/scanner-pe-ps-column-and-zero-value-regression-2026-06-19.md`
