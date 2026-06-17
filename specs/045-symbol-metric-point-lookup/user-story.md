# User Story — Symbol Metric Point Lookup

## Story

As an investor,
I want to ask direct questions like "PE حفاری چقدر است؟" or "نسبت بدهی به حقوق صاحبان سهام فملی
و کگل را نشان بده" through the chat interface,
so that I can quickly retrieve the latest observed value of a specific financial metric for one or
more named symbols without framing the question as a screener filter.

## Background

The Phase 1 Scanner is a screening engine that answers **"which companies match condition X?"**.
It requires an operator and a threshold (e.g., `PE_TTM < 10`). When a user asks **"what is the PE
of حفاری?"**, the query has no threshold, so the scanner parser cannot produce a valid condition and
the AI falls back to a generic clarification prompt. This story closes that gap by adding a new
intent type and lookup execution path to the single AI facade endpoint.

## Acceptance Criteria

- `POST /api/ai/v1/query` handles `SymbolLookup` intent: one or more (symbol, metric) pairs
  extracted from the user's natural language message.
- The LLM extracts symbol codes (e.g., `حفاری`, `فملی`, `کگل`) and metric terms (e.g., `P/E`,
  `نسبت بدهی به حقوق`) from the message. Metric terms are resolved to canonical `MetricCode`
  values through the existing `IMetricAliasResolver` (same resolver used by the scanner parser).
- Symbol names are resolved to `SymbolCode` values in the normalized `Symbols` table using a
  case-insensitive match against `SymbolCode` and `Companies.Name`/`Companies.ExternalCompanyId`.
  Unresolved symbols are listed in the response as missing; the lookup proceeds for resolved ones.
- Display rows must be company-backed, not provider-symbol-backed. The table's symbol value must
  use `public."Companies"."TseSymbol"` whenever the resolved symbol has a company row, and the
  company column must use `public."Companies"."Name"`. Provider identifiers such as ISIN,
  instrument codes, or `Symbols.SymbolCode` are linkage keys and must not be shown as the primary
  symbol when a company display symbol exists.
- Resolved (symbol, metric) pairs are looked up in `DerivedMetrics` (latest `PeriodEnd`) and
  supplemented by `LatestMarketQuotes` for price-class metrics (`LATEST_PRICE`, `MARKET_CAP`).
- Metric lookup must evaluate all `Symbols` rows linked to the resolved company, because different
  vendors may populate different symbol rows for the same listed company. A metric stored on a
  CyclicalWaves-linked symbol must still answer a lookup resolved through a NADPCO/CodalDB symbol
  row for the same `Companies.Id`.
- The response includes a structured `SymbolLookupTable` (same column/cell/freshness contract as
  the scanner table) inside the existing `AiQueryResponse`.
- Numeric display formatting is deterministic:
  - Monetary, price, market-cap, volume, quantity, production, and sales amount cells must not
    show a decimal fraction when the displayed value is whole. Large financial values should be
    rendered as grouped whole numbers (for example `90,879,722`, not `90,879,722.00`).
  - P/E, P/S, P/B, percent, growth, margin, and other financial-ratio cells may show decimals
    when the decimal part is meaningful, but trailing zero-only fractions must be trimmed
    (`5.20` -> `5.2`, `5.00` -> `5`).
  - Raw numeric `value` remains unchanged; only `formattedValue` is affected.
- The response includes a top-level `ConfidenceScore` for the lookup result. A valid structured
  lookup must not depend on `ExplainableAnswer.Confidence`; symbol lookup responses may not have a
  scanner explainable answer.
- When a requested metric has no data for a symbol the cell shows `Missing` freshness — no error.
- Confidence, citations, and billing accounting follow the same deterministic backend rules as
  scanner results. The frontend must prefer backend `ConfidenceScore` and must not display `0%`
  solely because `ExplainableAnswer` is absent.
- The lookup result is persisted in the conversation message so it can be reloaded on return visits.
- No new public endpoint is added; lookup is always triggered through `POST /api/ai/v1/query`.
- The scanner screener path is unaffected; intent detection selects exactly one path.

## Out of Scope

- Time-series lookups ("show me PE over the last 3 years") — Phase 2.
- Computed cross-symbol comparisons ("compare PE of A with B") — Phase 2.
- Real-time streaming price updates for the lookup result.
- A separate lookup-specific API endpoint.

## Technical Notes

- Add `SymbolLookup` to `AiIntentType` and extend `IAiIntentDetector`'s LLM prompt to recognise
  the new intent category alongside `Scanner` and `Clarification`.
- Create `ISymbolLookupParser`: LLM structured-output call that returns a list of
  `{ symbolName, metricTerm }` pairs. Symbol name is passed as-is from the user; the
  backend resolves it to `SymbolCode`.
- Create `ISymbolNameResolver`: Application-layer service that matches a raw name against
  `Symbols.SymbolCode`, `Companies.Name`, and `Companies.ExternalCompanyId` (case-insensitive,
  accent-insensitive for Persian). Returns the best-match `SymbolCode` or `null` if not found.
- Create `ISymbolMetricLookupService`: queries `DerivedMetrics` + `LatestMarketQuotes` for
  the resolved (SymbolId, MetricCode) pairs; returns a `SymbolLookupTableResult` using the
  same `ScannerTableColumn`/`ScannerTableCell` contracts so the frontend renders it identically.
- Extend `AiQueryOrchestrationService` with the new intent branch; billing reservation and
  finalization follow the same hook pattern as the scanner path.
- `SymbolLookupTableResult` re-uses `ScannerTableResult` shape so the frontend `ScannerResultTable`
  component renders lookup results without modification. The `ExecutionFacts.MatchingSymbolCount`
  reflects the number of successfully resolved symbols.
- Confidence scoring for symbol lookup is deterministic and derived from source type, structured
  cell completeness, freshness, warnings/missing data, and consistency between generated prose and
  the structured table values. If the table contains a non-null `PE_TTM = 5.17` and the narrative
  repeats `5.17`, confidence should be high rather than falling back to zero.
- The existing `MissingAnswerFeedbackCollector` records lookup gaps (unresolved symbol names,
  metrics with no data) using the `DataCoverageGap` classification.
