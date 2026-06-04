# Tasks — Symbol Metric Point Lookup

## Domain

- Add `SymbolLookup` value to the `AiIntentType` enum in the Application layer.
- Define `SymbolLookupRequest` record: `IReadOnlyCollection<(string SymbolName, MetricCode MetricCode)>
  Pairs`, `DateOnly AsOf`, optional `ActorId`/`QueryText` for feedback.
- Define `SymbolLookupTableResult` record: re-use `ScannerTableResult` shape
  (`PlanId`→`LookupId`, `Columns`, `Rows`, `ExecutionFacts`, `MissingDataWarnings`).
  Add `IReadOnlyCollection<string> UnresolvedSymbols` for names that could not be matched.

## Application — Intent Detection

- Extend `IAiIntentDetector`'s LLM system prompt to recognise `SymbolLookup` intent:
  queries that name one or more specific symbols AND ask for the value of a metric (no
  threshold/operator). The prompt must distinguish:
  - `Scanner`: "find companies where P/E < 10"
  - `SymbolLookup`: "what is the P/E of حفاری?"
  - `Clarification`: genuinely ambiguous requests
- Update the LLM structured-output schema for intent detection to include the new value.
- Add unit tests for intent classification of Persian and English point-lookup phrases.

## Application — Symbol Lookup Parser

- Define `ISymbolLookupParser` interface:
  `Task<SymbolLookupParseResult> ParseAsync(string message, string language, CancellationToken)`.
- `SymbolLookupParseResult` carries a list of `{ RawSymbolName, ResolvedMetricCode?, OriginalMetricTerm }`
  and a `Status` (Parsed / ClarificationRequired).
- Implement `LlmSymbolLookupParser`: single LLM structured-output call with the user's message;
  the LLM returns raw symbol names (exactly as the user wrote them) and metric terms; the backend
  resolves both using `ISymbolNameResolver` and `IMetricAliasResolver`.
- Metric term resolution re-uses the existing `IMetricAliasResolver` (same BCP-47 normalisation).
- Return `ClarificationRequired` with a helpful message when no valid (symbol, metric) pair can
  be extracted.
- Add unit tests: Persian symbol + Persian metric, English symbol + English metric, mixed,
  unresolvable symbol, unresolvable metric, empty output from LLM.

## Application — Symbol Name Resolver

- Define `ISymbolNameResolver` interface:
  `Task<SymbolCode?> ResolveAsync(string rawName, CancellationToken)`.
- Implement `EfCoreSymbolNameResolver` against `FinancialIngestionDbContext`:
  - Case-insensitive exact match on `Symbols.SymbolCode`.
  - Case-insensitive match on `Companies.ExternalCompanyId`.
  - Case-insensitive substring/trim match on `Companies.Name`.
  - Return the best match `SymbolCode` or `null`.
- Add unit tests for exact-code match, name match, no match, and ambiguous multi-match
  (returns `null` and logs a warning).

## Application — Symbol Metric Lookup Service

- Define `ISymbolMetricLookupService` interface:
  `Task<SymbolLookupTableResult> LookupAsync(SymbolLookupRequest, CancellationToken)`.
- Implement `EfCoreSymbolMetricLookupService`:
  - Resolve each `SymbolCode` to `SymbolId` via `Symbols` table.
  - Query `DerivedMetrics` for the latest row per `(SymbolId, MetricCode)` by `PeriodEnd`.
  - Supplement price-class metrics (`LATEST_PRICE`, `MARKET_CAP`) from `LatestMarketQuotes`
    using the same `IMarketQuoteResolver` used by the scanner.
  - Build `ScannerTableColumn` list from the resolved metric codes (one column per metric,
    plus Symbol and Company Name columns).
  - Build `ScannerTableRow` list with `ScannerTableCell` entries using the existing freshness
    status contract (`Live` / `PreviousTradingDay` / `Persisted` / `Missing`).
  - Populate `UnresolvedSymbols` with raw names whose `SymbolCode` could not be found.
  - Set `ExecutionFacts.MatchingSymbolCount` = number of symbols with at least one non-Missing cell.
- Add integration tests: single symbol + single metric found, symbol not found, metric not found,
  multiple symbols + multiple metrics, price-class metric uses `LatestMarketQuotes`.

## Application — Orchestration

- Extend `AiQueryOrchestrationService` with the `SymbolLookup` intent branch:
  1. Call `ISymbolLookupParser.ParseAsync`.
  2. If `ClarificationRequired`, persist clarification message and return.
  3. Call `ISymbolMetricLookupService.LookupAsync`.
  4. Persist the result in the assistant conversation message (same structured payload as scanner).
  5. Call `IBillingFacadeHook.TryReserveAsync` before lookup and `FinalizeAsync` after
     (same billing lifecycle as scanner; use operation code `AiQuery.Scanner` for Phase 1).
  6. Collect missing-answer feedback via `IMissingAnswerFeedbackCollector` for unresolved
     symbols and metrics with no data (`DataCoverageGap` classification).
- Add integration tests through `POST /api/ai/v1/query`:
  - "PE حفاری چقدر است؟" → `SymbolLookup` intent, حفاری resolved, PE_TTM value returned.
  - Unknown symbol → `UnresolvedSymbols` list, non-empty `MissingDataWarnings`.
  - Unknown metric → `ClarificationRequired` response.
  - Billing entry created per lookup.

## API Contracts

- Extend `AiQueryHttpResponse` with `SymbolLookupTable?: ScannerTableResponse` (nullable, same
  type as `ScannerTable` — the frontend renders both identically).
- Map `SymbolLookupTableResult.UnresolvedSymbols` into `MissingDataWarnings` on the HTTP response.
- Update `mapAssistantBlock` in `chat.functions.ts` to read `symbolLookupTable` from the API
  response and populate `block.table` (re-uses the existing `ScannerResultTable` component).

## Frontend

- No new component required; `ScannerResultTable` renders the lookup result unchanged.
- Extend `mapAssistantBlock` to read `result.symbolLookupTable` and fall back to
  `result.scannerTable` — whichever is present populates `block.table`.
- Add a label or badge to distinguish lookup results ("مقدار مستقیم") from screener results
  ("اسکنر") in the table header area so the user knows which mode returned the result.
  This label is optional and can be added to `ScannerExecutionFacts` as a `ResultKind` enum
  (`Screener` / `PointLookup`) or inferred on the frontend from the number of conditions.

## Tests Summary

- Unit: intent classification (≥ 3 cases), lookup parser (≥ 6 cases), symbol name resolver (≥ 4),
  lookup service (≥ 5).
- Integration through `POST /api/ai/v1/query`: found, not-found symbol, not-found metric,
  multi-symbol multi-metric, billing entry.
- Architecture: lookup service and parser must not import controllers, HTTP types, or Billing
  persistence directly.
