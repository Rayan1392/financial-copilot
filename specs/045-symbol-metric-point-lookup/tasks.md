# Tasks — Symbol Metric Point Lookup

## Post-068 Compatibility Override

The original resolver tasks below are superseded by spec `068-companies-first-refactor`.

* Do not implement or reintroduce `ISymbolNameResolver`, `EfCoreSymbolNameResolver`, `Symbols`,
  or `SymbolId` lookup.
* Use `CompanyResolverService` against `Companies`.
* All lookup reads use `ExternalCompanyId`.
* `EfCoreSymbolMetricLookupService` queries `DerivedMetrics` by (`ExternalCompanyId`,
  `MetricCode`) and never joins `Symbols`.
* Monthly-sales requests containing `فروش`, `آخرین فروش`, `فروش ماه`, `فروش ماهانه`,
  `فروش این ماه`, `فروش YTD`, or `متوسط فروش 12/۱۲ ماهه` resolve to `MONTHLY_SALES`, not
  `REVENUE`, unless the user explicitly asks for `درآمد فصلی` or `فروش فصلی`.
* Monthly-sales snapshot responses use the monthly-sales renderer and must not include
  `LATEST_PRICE` or `DAILY_CHANGE_PCT`.

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
  resolves both using `CompanyResolverService` and `IMetricAliasResolver`.
- Metric term resolution re-uses the existing `IMetricAliasResolver` (same BCP-47 normalisation).
- Return `ClarificationRequired` with a helpful message when no valid (symbol, metric) pair can
  be extracted.
- Add unit tests: Persian symbol + Persian metric, English symbol + English metric, mixed,
  unresolvable symbol, unresolvable metric, empty output from LLM.

## Application — Symbol Name Resolver

- Use the existing `CompanyResolverService`:
  - Case-insensitive exact match on `Companies.CompanySymbol/TseSymbol`.
  - Case-insensitive match on `Companies.ExternalCompanyId`.
  - Case-insensitive substring/trim match on `Companies.Name`.
  - Return the best match `ExternalCompanyId` or `null`.
- Add unit tests for exact-code match, name match, no match, and ambiguous multi-match
  (returns `null` and logs a warning).

## Application — Symbol Metric Lookup Service

- Define `ISymbolMetricLookupService` interface:
  `Task<SymbolLookupTableResult> LookupAsync(SymbolLookupRequest, CancellationToken)`.
- Implement `EfCoreSymbolMetricLookupService`:
  - Resolve each raw symbol name to a company-backed symbol context: resolved `ExternalCompanyId`,
    `CompanyId`, display symbol, and company name.
  - Use `Companies.TseSymbol` as the response `symbolCode` whenever it exists; use
    `Companies.Name` as the response `companyName`. Fall back to `Companies.CompanySymbol` and
    only then `Companies.CompanySymbol/TseSymbol` if a legacy/test row has no `TseSymbol`.
  - Query `DerivedMetrics` for the latest row per (`ExternalCompanyId`, `MetricCode`). Do not join
    to `Symbols`, do not use `SymbolId`, and do not fan out through provider symbol rows.
  - Supplement price-class metrics (`LATEST_PRICE`, `MARKET_CAP`) from `LatestMarketQuotes`
    using the same `IMarketQuoteResolver` used by the scanner. The resolver and any underlying
    canonical table queries must **not** filter rows by `ProviderName`; the best available
    price record for the resolved instrument must be returned regardless of which provider
    populated it (`TsetmcWebService`, `StockMarketDb`, or any future source).
  - `ShouldIncludeMarketContext(...)` applies **only** to the `SymbolLookup` (point lookup) path.
    It must not be applied to scanner/filter results. Scanner column selection is governed by
    `IScannerResultColumnPolicy` in `008-scanner-execution-engine`, which requires that quote
    columns be present only when the user explicitly requested them or they are part of a filter
    or sort condition in the scanner query.
  - Build `ScannerTableColumn` list from the resolved metric codes (one column per metric,
    plus Symbol and Company Name columns).
  - Build `ScannerTableRow` list with `ScannerTableCell` entries using the existing freshness
    status contract (`Live` / `PreviousTradingDay` / `Persisted` / `Missing`).
  - Populate `UnresolvedSymbols` with raw names whose `ExternalCompanyId` could not be found.
  - Set `ExecutionFacts.MatchingSymbolCount` = number of symbols with at least one non-Missing cell.
- Add integration tests: single symbol + single metric found, symbol not found, metric not found,
  multiple symbols + multiple metrics, price-class metric uses `LatestMarketQuotes`, display symbol
  comes from `Companies.TseSymbol`, company name comes from `Companies.Name`, and a metric stored on
  the same `ExternalCompanyId` is still returned.

## Application — Orchestration

- Extend `AiQueryOrchestrationService` with the `SymbolLookup` intent branch:
  1. Call `ISymbolLookupParser.ParseAsync`.
  2. If `ClarificationRequired`, persist clarification message and return.
  3. Call `ISymbolMetricLookupService.LookupAsync`.
  4. Compute deterministic top-level `ConfidenceScore` from the lookup table and generated
     narrative consistency; do not rely on scanner `ExplainableAnswer.Confidence`.
  5. Persist the result and top-level `ConfidenceScore` in the assistant conversation message
     (same structured payload family as scanner).
  6. Call `IBillingFacadeHook.TryReserveAsync` before lookup and `FinalizeAsync` after
     (same billing lifecycle as scanner; use operation code `AiQuery.Scanner` for Phase 1).
  7. Collect missing-answer feedback via `IMissingAnswerFeedbackCollector` for unresolved
     symbols and metrics with no data (`DataCoverageGap` classification).
- Add integration tests through `POST /api/ai/v1/query`:
  - "PE حفاری چقدر است؟" → `SymbolLookup` intent, حفاری resolved, PE_TTM value returned.
  - Unknown symbol → `UnresolvedSymbols` list, non-empty `MissingDataWarnings`.
  - Unknown metric → `ClarificationRequired` response.
  - Billing entry created per lookup.
  - Structured PE lookup with matching narrative/table value returns a non-zero, high top-level
    confidence score.

## API Contracts

- Extend `AiQueryHttpResponse` with `SymbolLookupTable?: ScannerTableResponse` (nullable, same
  type as `ScannerTable` — the frontend renders both identically).
- Ensure `AiQueryHttpResponse.ConfidenceScore` is populated for `SymbolLookup` responses even when
  `ExplainableAnswer` is null.
- Map `SymbolLookupTableResult.UnresolvedSymbols` into `MissingDataWarnings` on the HTTP response.
- Update `mapAssistantBlock` in `chat.functions.ts` to read `symbolLookupTable` from the API
  response and populate `block.table` (re-uses the existing `ScannerResultTable` component).

## Frontend

- No new component required; `ScannerResultTable` renders the lookup result unchanged.
- Extend confidence mapping to prefer backend `result.confidenceScore` before any
  `result.explainableAnswer.confidence` fallback. A present `symbolLookupTable` with structured
  financial values must never display `0%` merely because `explainableAnswer` is absent.
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

## Change Request Tasks - 2026-06-20 - Period-Aware CyclicalWaves Direct Metrics

- [ ] Extend the internal symbol lookup parse result with an optional period selector for Q0, Q1,
      Q4, M0, M1, and M12 style requests, as specified in `073`.
- [ ] Update the lookup service to apply the period selector while querying `DerivedMetrics` by
      `ExternalCompanyId` and `MetricCode`.
- [ ] Ensure `last_year_average_12_month_sale` resolves to M12 `AVG_12M_MONTHLY_SALES` and is not
      calculated from the latest average.
- [ ] Return Missing/null for absent period-specific values without substituting another period.
- [ ] Add direct AI regression tests for margins, monthly sales, average sales, PE, and PS listed in
      spec `073`.
