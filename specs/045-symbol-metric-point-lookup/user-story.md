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
- Resolved (symbol, metric) pairs are looked up in `DerivedMetrics` (latest `PeriodEnd`) and
  supplemented by `LatestMarketQuotes` for price-class metrics (`LATEST_PRICE`, `MARKET_CAP`).
- The response includes a structured `SymbolLookupTable` (same column/cell/freshness contract as
  the scanner table) inside the existing `AiQueryResponse`.
- When a requested metric has no data for a symbol the cell shows `Missing` freshness — no error.
- Confidence, citations, and billing accounting follow the same rules as scanner results.
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
- The existing `MissingAnswerFeedbackCollector` records lookup gaps (unresolved symbol names,
  metrics with no data) using the `DataCoverageGap` classification.
