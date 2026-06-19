# User Story — Scanner Execution Engine

## Story

As an investor,  
I want screening questions submitted in my Conversation to return matching symbols,
so that I can quickly find fundamental and valuation candidates without selecting backend services.

## Acceptance Criteria

- Scanner Use Case executes a validated scanner plan after Tool Routing selects it.
- Support AND conditions for Phase 1.
- Support operators: <, <=, >, >=, =, between.
- Support sorting and limit.
- When an AI response returns a list of stocks, return that list through a structured result table contract.

### Scanner Result Table Column Rules

#### Mandatory identity columns
The first two columns of every scanner result table are always:
1. `نماد` (symbol)
2. `شرکت` (company name)

These identity columns are not configurable; they cannot be removed or reordered.

#### Metric columns: only what was requested
Scanner result tables include only the metrics explicitly requested, filtered, sorted, or otherwise named by the user in their query. No other columns are added automatically.

Examples:
- Query: `لیست نمادهای با پی به ای زیر 4 و پی به اس زیر 1`
  → Columns: `نماد`, `شرکت`, `PE_TTM`, `PS_TTM` (no others)
- Query: `لیست نمادهای با پی به ای زیر 4 و پی به اس زیر 1 همراه با آخرین قیمت`
  → Columns: `نماد`, `شرکت`, `PE_TTM`, `PS_TTM`, `LATEST_PRICE` (explicitly requested)
- Query: `نمادهای با پی به ای زیر 4، پی به اس زیر 1 و ارزش بازار بالای 10 همت`
  → Columns: `نماد`, `شرکت`, `PE_TTM`, `PS_TTM`, `MARKET_CAP` (filter metric, so included)

#### No automatic quote enrichment for scanner tables
Scanner result tables must not automatically add quote-related columns such as `LATEST_PRICE`, `DAILY_CHANGE_PCT`, or `MARKET_CAP` for valuation-metric filters (PE, PS, PB, etc.).

Quote enrichment columns may appear in scanner result tables **only** when:
- The user explicitly asks for price, market cap, daily change, or other quote context in their query, **or**
- The metric is part of a filter or sort condition in the query (e.g., `ارزش بازار بالای 10 همت`).

This is distinct from direct single-symbol valuation lookup (e.g., `پی به ای کگل چقدر است؟`), which is handled by the Symbol Metric Point Lookup path and may include quote context according to `045-symbol-metric-point-lookup` rules.

#### No internal or debug columns
Internal/debug columns such as `symbols` must never be included in user-facing scanner result tables.

#### Column limit
The table contains at most 10 displayed data columns. Explicit user overrides are validated against this limit.

### Valuation Ratio Zero-Value Handling

For valuation ratio and financial ratio metrics, a stored value of `0` represents a missing, invalid, uncomputed, or economically undefined value — **not** a valid ratio of zero.

For scanner filters using `<`, `<=`, `>`, `>=`, or `between` on the following metric types, zero values must be excluded from matching:
- `PE_TTM` (price-to-earnings)
- `PS_TTM` (price-to-sales)
- `PB` (price-to-book)
- Other valuation multiples and ratio metrics where zero can represent missing/invalid data

Rules:
- A row with `PE_TTM = 0` must not satisfy a condition `PE_TTM < 4` (or any `<`/`<=` filter).
- A row with `PS_TTM = 0` must not satisfy a condition `PS_TTM < 1` (or any `<`/`<=` filter).
- Zero-value rows for these metrics must be excluded from scanner results unless the user explicitly requests zero values.
- The scanner must treat zero PE/PS/PB values as missing/invalid for valuation-screening eligibility.

Example: For the query `لیست نمادهای با پی به ای زیر 4 و پی به اس زیر 1`:
- Returned rows must have valid non-zero `PE_TTM` in range `(0, 4)`.
- Returned rows must have valid non-zero `PS_TTM` in range `(0, 1)`.
- No row with `PE_TTM = 0` or `PS_TTM = 0` must appear in the result.

### Remaining Acceptance Criteria

- Latest price and price change use available live/low-latency market data first; when unavailable, use the latest statistics from the previous completed trading day and identify that fallback in row/source metadata. (Applies only when these columns are included per the rules above.)
- Return symbol, company, selected metric values, score, matched conditions, and source metadata as needed by the table contract.
- Handle missing data with warnings.
- Never execute raw SQL produced by AI.
- Query performance is acceptable for normalized datasets.
- Scanner results are returned inside the Explainable Answer produced by `POST /api/ai/v1/query`.
- Scanner execution is not exposed as a frontend-facing public endpoint.
- Scanner execution supplies billable execution facts and cache eligibility/outcome metadata to Billing; it does not compute charges or update balances.

## Technical Notes

- Start with EF Core expression building or predefined query handlers.
- Prefer deterministic query model over arbitrary dynamic SQL.
- `IScannerExecutionService` and `IScannerResultRanker` belong in the Application layer behind `IAiQueryOrchestrator`.
- Price, daily change, valuation, capitalization, and growth displayed in result tables must come from normalized or freshness-controlled market data, not from generated text.
- Implement column selection through an `IScannerResultColumnPolicy` and price-source selection through an `IMarketQuoteResolver` or equivalent Application interface; do not let the LLM assemble arbitrary table projections.
- Fetch price/source data in batches for the result set and enforce row/column limits before calling external live-data providers to control response latency and provider usage.
- Keep usage pricing and reservations in `FinancialCopilot.Billing`; Scanner reports deterministic execution facts only.
