# Tasks — Missing-Answer Feedback and Auto-Improvement Pipeline

## Acceptance Gate

All tasks and acceptance criteria in the user story must be complete and verified. No feedback collection or
persistence without tests. Query performance (scanner execution time) must not regress.

## Task List

### Domain & Application Contracts

**Task 1.1: Add `MissingAnswerFeedback` domain model**
- Location: `src/backend/FinancialCopilot.Domain/Financial/MissingAnswer/`
- Create `MissingAnswerFeedbackClassification` enum: `MetricGap`, `CalculationGap`, `DataCoverageGap`,
  `DataQualityGap`, `ParserLimitation`, `UnknownGap`.
- Create `MissingAnswerFeedback` value object or lightweight aggregate: immutable, properties:
  - `Id` (Guid PK)
  - `ActorId` (string, correlation to `ActorContext.ActorId` from `002`)
  - `QueryText` (string, user query, max 500 chars)
  - `Classification` (enum above)
  - `RequestedMetricCode` (string?, canonical metric code if applicable)
  - `AffectedDataCodeOrName` (string?, the metric/data that was missing)
  - `SymbolCountTotal` (int, size of symbol universe at query time)
  - `SymbolCountMatched` (int, how many symbols matched filters, if applicable)
  - `SubmittedAt` (DateTimeOffset)
  - `Context` (string?, JSON serialization of conditions/metrics checked)
  - `FrequencyCount` (int, default 1; incremented on coalesce)
  - `ResolvedAt` (DateTimeOffset?, when the miss was addressed, if known)

**Task 1.2: Add `IMissingAnswerFeedbackCollector` Application interface**
- Location: `src/backend/FinancialCopilot.Application/FinancialData/Scanning/`
- Method: `CollectAsync(ActorId, QueryText, Classification, RequestedMetricCode, SymbolCountTotal, SymbolCountMatched, Context, CancellationToken)`
- Must not throw; swallow exceptions and log. Must not await persistence.
- Return type: `Task` (fire-and-forget).

**Task 1.3: Add `IMissingAnswerFeedbackRepository` Application interface**
- Location: `src/backend/FinancialCopilot.Application/FinancialData/Scanning/`
- Methods:
  - `QueryAsync(DateFrom, DateTo, Classification?, MetricCode?, ActorId?, Skip, Take, CancellationToken)` →
    `IAsyncEnumerable<MissingAnswerFeedback>` (paged).
  - `GetCountByClassificationAsync(DateFrom, DateTo, CancellationToken)` → `Dictionary<Classification, int>`.
  - `GetMostRecentAsync(Take, CancellationToken)` → `IAsyncEnumerable<MissingAnswerFeedback>`.
  - `UpsertAsync(MissingAnswerFeedback, CancellationToken)` (idempotent coalesce on
    ActorId+QueryHashSha256+Classification+DateBucket).

### Infrastructure Implementation

**Task 2.1: Add `EfCoreMissingAnswerFeedbackRepository`**
- Location: `src/backend/FinancialCopilot.Infrastructure/FinancialData/Scanning/`
- Implement `IMissingAnswerFeedbackRepository` using `ScannerCoreDbContext` (add new DbSet below).
- Upsert logic: compute `QueryHashSha256 = SHA256(QueryText)` and `DateBucket = SubmittedAt.Date`,
  check if row exists for `(ActorId, QueryHashSha256, Classification, DateBucket)`, increment count if found,
  else insert.

**Task 2.2: Add `MissingAnswerFeedbackRow` EF persistence model**
- Location: `src/backend/FinancialCopilot.Infrastructure/Scanner/Persistence/ScannerRows.cs`
- Table `MissingAnswerFeedbacks` columns:
  - `Id` (Guid PK)
  - `ActorId` (string, indexed)
  - `QueryText` (string, max 500)
  - `QueryHashSha256` (string, indexed for coalesce matching)
  - `Classification` (string, indexed)
  - `RequestedMetricCode` (string?, indexed)
  - `AffectedDataCodeOrName` (string?)
  - `SymbolCountTotal` (int)
  - `SymbolCountMatched` (int)
  - `SubmittedAt` (DateTimeOffset, indexed)
  - `DateBucket` (DateOnly, indexed — derived from SubmittedAt for coalesce window)
  - `Context` (string?, max 2000)
  - `FrequencyCount` (int, default 1)
  - `ResolvedAt` (DateTimeOffset?)
- Unique index: `(ActorId, QueryHashSha256, Classification, DateBucket)` for coalescing.

**Task 2.3: Add `MissingAnswerFeedbackRowConfiguration`**
- Location: `.../Scanner/Persistence/ScannerRowConfigurations.cs` (or new file)
- Configure table, indexes, column types, constraints per above.

**Task 2.4: Add `ScannerCoreDbContext.MissingAnswerFeedbacks` DbSet**
- Update `ScannerCoreDbContext.cs` in Infrastructure.

**Task 2.5: Add `NoOpMissingAnswerFeedbackCollector` default implementation**
- Location: `src/backend/FinancialCopilot.Infrastructure/FinancialData/Scanning/`
- No-op: `CollectAsync` is empty, returns completed task.
- Wired in DI by default so Phase 1 has no feedback collection overhead.

**Task 2.6: Add `AsyncFireAndForgetMissingAnswerFeedbackCollector` wrapper**
- Location: `.../FinancialData/Scanning/AsyncFireAndForgetMissingAnswerFeedbackCollector.cs`
- Wraps a real `IMissingAnswerFeedbackRepository` and calls `UpsertAsync` in `Task.Run()` fire-and-forget.
- Swallows exceptions, logs them, never re-throws. Never awaits the background task in the caller.
- Used when real feedback collection is enabled.

### Scanner Integration

**Task 3.1: Update `EfCoreScannerExecutionService`**
- Add dependency: `IMissingAnswerFeedbackCollector collector`.
- After `ExecuteAsync` returns `ScannerTableResult`, check:
  - If `result.Rows.Count == 0`: emit feedback `Classification = MetricGap` or `DataCoverageGap`
    (heuristic: if requested metric code is in catalog but no DerivedMetrics exist, it is `CalculationGap`;
    if not in catalog, `MetricGap`; if in catalog and DerivedMetrics exist but symbol count is 0, it is
    `DataCoverageGap`).
  - If `result.Rows.Count > 0` but `result.Rows.Count < (SymbolUniverse.Count * 0.5)` and query had
    metric-dependent conditions: emit feedback `Classification = DataCoverageGap`.
- Call `collector.CollectAsync(...)` fire-and-forget (do not await).
- Provide context: JSON serialize the conditions, metric references, and symbol count.

**Task 3.2: Update `LlmScannerQueryParser` (optional for Phase 1)**
- If clarification is requested and returned to user but no follow-up is received (session times out,
  user abandons), mark the original intent as `ParserLimitation` feedback.
- Implementable only if conversation state tracks clarifications awaiting response. Document if deferred.

### Testing

**Task 4.1: Add `MissingAnswerFeedbackClassificationTests`**
- Location: `tests/FinancialCopilot.UnitTests/`
- Test classification logic:
  - Metric gap: metric not in registry → `MetricGap`.
  - Calculation gap: metric in registry, no DerivedMetrics rows → `CalculationGap`.
  - Data coverage gap: metric has rows but symbol count < 50% of universe → `DataCoverageGap`.
  - Unknown: fallback case → `UnknownGap`.
- Test coalescing: same (ActorId, QueryHash, Classification, DateBucket) increments count, not row count.

**Task 4.2: Add `MissingAnswerFeedbackRepositoryTests`**
- Location: `tests/FinancialCopilot.IntegrationTests/`
- In-memory `ScannerCoreDbContext` (new, see Task 2.8 below).
- Test upsert idempotency; test queries by date range, classification, metric code, actor.
- Test deduping within same date bucket.

**Task 4.3: Add scanner integration test**
- Location: `tests/FinancialCopilot.IntegrationTests/ScannerExecutionWithFeedbackTests.cs`
- Test scenario: query with no matches → feedback is collected (verify actor ID, query text, classification).
- Test scenario: query with sparse data → feedback is collected (verify SymbolCountMatched < Total).
- Test scenario: collector failure (repo throws) → scanner result is unaffected, exception is logged.
- Confirm scanner execution time is not degraded (collector call is fire-and-forget).

**Task 4.4: Test fire-and-forget isolation**
- Verify collector exception does not propagate to caller.
- Verify collector does not block query response (call returns before persistence completes).

### Persistence & Migrations

**Task 5.1: Create EF migration**
- `dotnet ef migrations add AddMissingAnswerFeedbackTable --context ScannerCoreDbContext`
- Covers `MissingAnswerFeedbacks` table, indexes, and coalesce unique constraint.

### Admin Endpoints (Optional for Phase 1, assign to 012 if implemented)

**Task 6.1: Add `GET /api/v1/admin/missing-answer-feedback` endpoint**
- Location: `src/backend/FinancialCopilot.API/Controllers/DataAdminController.cs` (if new, reference 012).
- Query parameters: `dateFrom`, `dateTo`, `classification`, `metricCode`, `actorId`, `skip`, `take`.
- Returns paginated list of `MissingAnswerFeedbackResponse` DTOs.
- Requires `DataAdmin` policy (from 012).
- Document in API spec.

**Task 6.2: Add aggregate endpoint `GET /api/v1/admin/missing-answer-feedback/summary`**
- Returns counts by classification for a date range.
- Useful for dashboards and trend analysis.

### DI & Configuration

**Task 7.1: Update `ServiceCollectionExtensions`**
- Register `IMissingAnswerFeedbackRepository` as `EfCoreMissingAnswerFeedbackRepository` (scoped).
- Register `IMissingAnswerFeedbackCollector` as `NoOpMissingAnswerFeedbackCollector` (singleton, Phase 1 default).
- Optionally add configuration section `"MissingAnswerFeedback"` with `Enabled: bool` (default false)
  and `FireAndForgetImpl: bool` (default true). If enabled, wire the real collector wrapped in
  `AsyncFireAndForgetMissingAnswerFeedbackCollector`.

### Documentation

**Task 8.1: Update API documentation**
- Document admin endpoints (if implemented in Task 6).
- Document feedback classification enum and what each means.

**Task 8.2: Add architecture/design note**
- Explain why feedback collection is fire-and-forget and how failures are isolated.
- Note the coalescing strategy to avoid table bloat.

### EF DbContext Preparation

**Task 2.8: Create or extend `ScannerCoreDbContext` (Infrastructure)**
- If not yet created, add new `DbContext` for scanner persistence under `src/backend/FinancialCopilot.Infrastructure/Scanner/Persistence/`.
- Add `DbSet<MissingAnswerFeedbackRow>`.
- Apply `MissingAnswerFeedbackRowConfiguration` in `OnModelCreating`.
- Reference from DI and migration tooling.

## Verification Checklist

- [ ] `MissingAnswerFeedback` domain model compiles and has no framework deps.
- [ ] `IMissingAnswerFeedbackCollector` and `IMissingAnswerFeedbackRepository` interfaces defined in Application layer.
- [ ] `EfCoreMissingAnswerFeedbackRepository` and `AsyncFireAndForgetMissingAnswerFeedbackCollector` implementations complete.
- [ ] `MissingAnswerFeedbackRow` and configuration apply to PostgreSQL cleanly.
- [ ] `EfCoreScannerExecutionService` detects empty/sparse results and calls collector.
- [ ] Collector fire-and-forget behavior verified: does not block query, swallows exceptions.
- [ ] Unit tests for classification pass (metric gap, calculation gap, coverage gap).
- [ ] Integration tests for repository and scanner integration pass.
- [ ] Migration applies without errors; table exists in PostgreSQL.
- [ ] Admin API (if implemented) queries feedback; returns expected counts.
- [ ] Feedback coalescing works: duplicate within same day bucket increments count.
- [ ] Scanner execution performance is unaffected (fire-and-forget adds <1ms to query time).
- [ ] `dotnet test src/backend/FinancialCopilot.sln` passes; Unit/Integration/Architecture counts do not regress.
