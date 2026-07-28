# User Story — Missing-Answer Feedback and Auto-Improvement Pipeline

> Depends on `007` (query parser), `008` (scanner execution), `009` (explainability).
> Optional dependency on `012` (admin endpoints).

## Story

As a platform architect and data engineer,
I want every unanswered or partially answered financial query to be captured, classified, and logged
as structured feedback with actionable root causes,
so that query misses drive metric catalog expansion, data ingestion prioritization, data quality remediation,
and AI model improvements instead of being silently ignored.

## Context — the feedback gap

The scanner returns empty or partial results when:
1. **Metric gap** — a queried metric is not defined in the semantic catalog (`IMetricRegistry`).
2. **Calculation gap** — a metric is defined but never calculated for any symbol (no `DerivedMetrics` rows exist).
3. **Data coverage gap** — raw data exists but not for the filtered symbols (e.g., CodalDB statements ingested for 1,000 of 2,362 companies).
4. **Data quality gap** — required values are null, sparse, or statistically implausible (zero revenue, null cost-of-goods).
5. **Parser limitation** — user query was ambiguous; clarification was requested but user did not resolve it (incomplete conversation).

Today, these cases are logged ad-hoc and not systematically connected to engineering roadmap signals. A metric that
is missing for 30 months has the same visibility as one missing for 1 day. Data coverage issues are not tracked.
Parser failures do not surface what disambiguation rules are missing.

This story captures these signals in a queryable, classified feedback log, so:
- **Product/Engineering** can see which metrics are most frequently missing and prioritize catalog expansion.
- **Data Engineering** can see which data sources or companies have poor coverage and plan ingestion/compliance.
- **ML/AI** can see which user intents the parser struggles with and retrain on real user patterns.
- **Quality** can see data quality issues and plan validation/correction.

## End-to-end scenario: user queries "revenue growth" but metric is missing

```
User: "list companies with 50% revenue growth"
  → Parser resolves to REVENUE_GROWTH_YOY (not found in catalog)
  → ScannerExecutionService.ExecuteAsync → finds 0 symbols → returns empty result
  → MissingAnswerFeedbackCollector captures:
      - Feedback classification: "MetricGap"
      - Requested metric code: "REVENUE_GROWTH_YOY"
      - Context: user query text, language, original filter conditions
      - Timestamp: when query was submitted
  → ProviderAgnosticMissingAnswerFeedbackRepository persists to PostgreSQL
  → Admin dashboard shows: "REVENUE_GROWTH_YOY missing in 3 queries (user IDs X, Y, Z)"
  → Data Engineer updates catalog or creates a task: "Implement REVENUE_GROWTH_YOY calculator"
```

## Acceptance Criteria

- **Feedback domain model** (`FinancialCopilot.Domain` or new Feedback bounded context): `MissingAnswerFeedback`
  immutable value object or aggregate root with classification enum (`MetricGap`, `CalculationGap`, `DataCoverageGap`,
  `DataQualityGap`, `ParserLimitation`, `UnknownGap`), original query text, affected metric/data codes, symbol
  count (total vs. matched), actor ID, timestamp, and optional context/notes.
- **Collection seam** (`IFinancialMissingAnswerFeedbackCollector` Application interface): accepts a
  `MissingAnswerFeedbackRequest` (query, result count, classification, root cause) and returns without blocking.
  Default no-op implementation for Phase 1; real implementation persists asynchronously or fire-and-forget.
- **Enrichment in scanner execution**: `EfCoreScannerExecutionService` calls collector after execution when
  result is empty (`ScannerTableResult.Rows.Count == 0`) or condition-metric-dependent rows are sparse
  (count < symbol universe / 2); classifier identifies whether the miss is due to metric gap, calculation,
  data coverage, or data quality.
- **Enrichment in parser/clarification**: `LlmScannerQueryParser` or clarification flow logs feedback
  when user query language matches no known metric or when clarification was requested but user did not
  respond (incomplete conversation, optional for Phase 1).
- **Persistence** (`ProviderAgnosticMissingAnswerFeedbackRepository` interface + `EfCoreMissingAnswerFeedbackRepository`
  in Infrastructure): PostgreSQL table `MissingAnswerFeedbacks` (Id, ActorId, QueryText, ClassificationCode,
  AffectedMetricCode, AffectedDataCode, SymbolCountTotal, SymbolCountMatched, SubmittedAt, Context,
  ResolvedAt nullable). Idempotent: duplicate (actor, query hash, classification) within 1 hour are coalesced
  into 1 row with count/frequency incremented.
- **No query-time regression**: collection is fire-and-forget; failures are logged but do not alter scanner
  results, execution time, or error handling. Query performance is unaffected.
- **Admin visibility** (optional for Phase 1, assigned to `012` if implemented): `GET /api/v1/admin/missing-answer-feedback`
  filters by date range, classification, metric code, actor ID; returns counts, most recent examples, trend.
  Data engineers can export for analysis. Requires `DataAdmin` policy.
- **Tests**: unit tests for feedback classification logic; integration tests for scanner execution + feedback
  collection (confirm empty result triggers feedback, sparse data is detected); test that collector failure
  does not disrupt query response.

## Technical Notes

- **Classification strategy** — use existing data at execution time: check if `MetricCode` exists in
  `IMetricRegistry`; if yes, check if any `DerivedMetrics` exist for it; if yes, check if symbol count
  matches (coverage); if not, it is a calculation gap. If user query contained an undefined metric name,
  it is a metric gap. If parser was called and returned `Clarification`, count as clarification incomplete.
- **Fire-and-forget implementation** — feedback collection should **not** await database writes. Use
  `_ = Task.Run(() => collector.CollectAsync(...))` or an async fire-and-forget helper, with exception
  swallowing and logging. Follows the "Billing outbox processor" pattern: queue the feedback, drain
  asynchronously, never block the query.
- **Coalescing/dedup** — to avoid flooding the feedback table, similar feedback (same actor, same query
  text hash, same classification, within 1-hour window) should increment a frequency count on the existing
  row rather than create a new row. Use an upsert strategy with `(ActorId, QueryHashSha256, Classification,
  DateBucket)` as the key.
- **Context payload** — store the original `ScannerCondition[]` and `MetricReference[]` as JSON (not
  normalized) so a data engineer can see exactly what was queried. Do not store PII (user name, org name);
  actor ID is sufficient.
- **Metric code matching** — handle both canonical (e.g., `NET_PROFIT_YOY`) and alias names (e.g.,
  "net profit growth"). Store whichever was used in the query; the classifier resolves to canonical if
  applicable.

## Dependencies

- `007`, `008`, `009` (scanner query, execution, explainability) — feedback is collected after execution.
- `015` (`IMetricRegistry`, `IMetricAliasResolver`) — to detect metric gaps.
- `003` (financial domain model) — `MissingAnswerFeedback` fits in the Financial domain or is a sibling Feedback domain.
- `012` (admin endpoints, optional) — for the visibility dashboard.
- `018` (telemetry, optional) — feedback events can be surfaced as correlated spans.

## Out of Scope (explicitly deferred)

- **Automatic metric catalog expansion** — feedback identifies missing metrics; a human architect/engineer
  decides whether to add them and updates the catalog. This story is not about self-healing.
- **Root-cause diagnosis beyond classification** — feedback says "metric is missing"; deeper analysis (e.g.,
  "metric is missing because it requires quarterly data which we don't ingest for CodalDB") is done
  manually by engineers reading the feedback log.
- **User-facing feedback** — scanner results do not say "we don't have revenue growth data" to the end user
  yet. Feedback is internal to the engineering team. (Future: `009` Explainability could offer suggestions
  based on feedback frequency.)
- **Metric calculator auto-generation** — feedback shows what metrics users want; writing the calculator is
  still manual.
- **ML retraining pipeline** — feedback is logged; ML engineers can export it and use it to improve parsers.
  Automatic retraining is out of scope.

## Verification

- `dotnet test` — new feedback classifier unit tests pass; scanner integration tests confirm no query-time regression.
- Feedback is persisted to PostgreSQL and queryable.
- Coalescing works (duplicate feedback increments count, not row count).
- Collector failure (e.g., database down) does not break query response.
- Admin API (if implemented) returns expected counts and examples.
