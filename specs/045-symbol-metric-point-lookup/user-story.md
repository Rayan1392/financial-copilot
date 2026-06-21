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

### Post-068 Compatibility

- Spec `068-companies-first-refactor` supersedes the original symbol lookup storage model. The
  legacy `Symbols` table, `SymbolId`, `ISymbolNameResolver`, and `EfCoreSymbolNameResolver` are
  no longer part of the runtime lookup path.
- Symbol resolution follows this path:

  ```text
  User Symbol
  -> Companies
  -> ExternalCompanyId
  -> DerivedMetrics / FinancialStatements / MonthlyReports
  ```

- Metric lookup must operate exclusively through `ExternalCompanyId`. No `SymbolId`-based lookup,
  Symbols join, or company-to-symbol fan-out may remain in the active implementation.

- `POST /api/ai/v1/query` handles `SymbolLookup` intent: one or more (symbol, metric) pairs
  extracted from the user's natural language message.
- The LLM extracts symbol codes (e.g., `حفاری`, `فملی`, `کگل`) and metric terms (e.g., `P/E`,
  `نسبت بدهی به حقوق`) from the message. Metric terms are resolved to canonical `MetricCode`
  values through the existing `IMetricAliasResolver` (same resolver used by the scanner parser).
- Symbol names are resolved by `CompanyResolverService` against `Companies` fields such as
  `CompanySymbol`, `TseSymbol`, `Ticker`, `EnTicker`, ISIN/instrument identifiers, normalized
  company names, and `Companies.ExternalCompanyId`. Unresolved symbols are listed in the response
  as missing; the lookup proceeds for resolved ones.
- Display rows must be company-backed, not provider-symbol-backed. The table's symbol value must
  use `public."Companies"."TseSymbol"` whenever the resolved symbol has a company row, and the
  company column must use `public."Companies"."Name"`. Provider identifiers such as ISIN,
  instrument codes, or `Symbols.SymbolCode` are linkage keys and must not be shown as the primary
  symbol when a company display symbol exists.
- Resolved (company, metric) pairs are looked up in `DerivedMetrics` by `ExternalCompanyId`
  (latest `PeriodEnd`) and supplemented by `LatestMarketQuotes` for price-class metrics
  (`LATEST_PRICE`, `MARKET_CAP`). The `LatestMarketQuotes` lookup (and any underlying canonical
  table lookup in `IntradayTradeSnapshots` or `DailyInstrumentTrades`) must **not** filter rows
  by `ProviderName`. The API runtime `PrimarySourceName` setting determines sync priority only;
  it must not cause valid quote rows stored under a different `ProviderName` to be skipped,
  resulting in `Missing` cells when actual data exists.
- Cross-provider lookup works because all provider observations are keyed to the same
  `ExternalCompanyId`; no `Symbols` rows need to be evaluated.
- Monthly sales routing rule: user intents containing `فروش`, `آخرین فروش`, `فروش ماه`,
  `فروش ماهانه`, `فروش این ماه`, `فروش YTD`, `متوسط فروش 12 ماهه`, or
  `متوسط فروش ۱۲ ماهه` resolve to `MONTHLY_SALES` and the monthly-sales snapshot renderer, not to
  generic quarterly `REVENUE`. `REVENUE` is selected only when the user explicitly asks for
  revenue, quarterly revenue/sales, `درآمد فصلی`, or `فروش فصلی`.
- Monthly sales responses must never include `LATEST_PRICE`, `DAILY_CHANGE_PCT`, `آخرین قیمت`, or
  `تغییر روزانه %`. Price context is allowed only for valuation metrics, trading metrics, and
  market quote queries.
- Quote context enrichment (`LATEST_PRICE`, `DAILY_CHANGE_PCT`) for non-monthly-sales point
  lookups (e.g., PE, PS, EPS) applies **only** to the Symbol Metric Point Lookup path
  (`SymbolLookup` intent). It does **not** apply to scanner/filter queries. Scanner result tables
  must not inherit point-lookup quote enrichment rules. See `008-scanner-execution-engine` for
  scanner column policy.
- Renderer ownership is explicit:
  - `MonthlySalesSnapshotRenderer` owns monthly-sales snapshot responses. In Noavaran mode it
    renders `فروش ماهانه`, `فروش ماه مشابه دوره قبل`, `فروش YTD`, and
    `فروش YTD تا ماه قبل`. In CyclicalWaves default mode it renders `فروش ماهانه`,
    `متوسط فروش ۱۲ ماهه`, `فروش YTD`, and `فروش YTD تا ماه قبل`.
  - `GenericMetricRenderer` owns PE, PS, EPS, revenue, net profit, margins, price metrics, and
    other non-monthly point lookups. It must not render monthly-sales snapshot responses.
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
- Use `CompanyResolverService`: Application-layer service that matches a raw name against
  `Companies` only (case-insensitive, accent-insensitive for Persian). Returns the resolved
  company and `ExternalCompanyId`, or `null` if not found.
- Create `ISymbolMetricLookupService`: queries `DerivedMetrics` + `LatestMarketQuotes` for
  the resolved (`ExternalCompanyId`, `MetricCode`) pairs; returns a `SymbolLookupTableResult` using the
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

## Change Request - 2026-06-20 - Period-Aware CyclicalWaves Direct Metrics

Spec `073-cyclicalwaves-direct-period-metric-query-coverage` extends this lookup path. When a user
asks a direct point-lookup question for a persisted CyclicalWaves snapshot field, the parser and
lookup service must carry a period selector in addition to the canonical metric code.

Required examples include margin questions for latest quarter, previous quarter, and same quarter
last year; monthly sales questions for latest month, previous month, and same month last year;
average 12-month sales for the latest snapshot and the last-year same-month snapshot; and PE/PS
valuation-ratio questions.

The lookup remains `Companies`/`ExternalCompanyId` based and must not reintroduce the legacy
`Symbols` table. If a period-specific persisted value is missing, the response must show Missing/null
with a data-coverage warning instead of silently substituting the latest period.
