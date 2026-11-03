# Feature 125 — Slice 3 Review

## Scope

Reviewed only the current Slice 3 implementation for:

- latest valid fact selection;
- freshness validation;
- source barrier construction;
- calculation snapshot persistence/versioning;
- source provenance and idempotency.

This follow-up addresses only the Slice 3 blockers from the previous review.

## Verification

| Requirement | Result | Evidence |
|---|---|---|
| 1. No same-date/provider-generation requirement remains | Pass | `IndustryRelativeValuationCalculationInputBuilder` explicitly selects facts independently per company/metric and does not join provider dates or generations ([InputBuilder](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/IndustryRelativeValuationCalculationInputBuilder.cs:16)). The barrier uses persisted/freshness evidence rather than a provider business date ([SourceBarrier](../../src/backend/FinancialCopilot.Domain/Financial/RelativeValuation/IndustryRelativeValuationSourceBarrier.cs:99)). |
| 2. Latest valid selection is deterministic | Pass | Candidates are filtered for availability, freshness, identity, positive operands, and `PersistedAtUtc` bounds, then ordered by `PersistedAtUtc`, observation timestamp, observation id, source version, and fact id ([SourceBarrier](../../src/backend/FinancialCopilot.Domain/Financial/RelativeValuation/IndustryRelativeValuationSourceBarrier.cs:54)). The barrier hash also canonicalizes selection order. |
| 3. `PersistedAtUtc` freshness semantics are consistent | Pass with limitation | The production mapper sets persisted facts’ freshness through `PersistedAtUtc`, and the barrier enforces `calculatedAtUtc - window <= PersistedAtUtc <= calculatedAtUtc` ([SourceBarrier](../../src/backend/FinancialCopilot.Domain/Financial/RelativeValuation/IndustryRelativeValuationSourceBarrier.cs:99)). However, the domain fact still carries an independently trusted `IsFresh` flag; direct engine callers can bypass the persisted-time rule. The calculation path is safe only when all inputs come through the barrier. |
| 4. Every calculation snapshot records exact source facts used | Pass | The calculation stores barrier evidence with source fact id, source version, observation id/timestamp, persisted time, and watermark; company rows store the same evidence per metric plus the exact current/reference values used ([SnapshotWriter](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/IndustryRelativeValuationCalculationSnapshotWriter.cs:59), [Rows](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/IndustryRelativeValuationRows.cs:41)). |
| 5. Historical reproducibility is possible | Pass with deployment gap | Versioned calculation rows retain membership/barrier hashes, calculation inputs, algorithm/rank versions, and source references. The current worktree contains no migration for the new Slice 3 tables/columns, so reproducibility is not deployable until schema migration is added. |
| 6. Missing/stale source behavior is deterministic | Pass | Missing or stale facts are omitted from selections; completeness is false with the fixed reason `MissingOrStaleLatestValidMetricObservation`, and the engine receives only selected facts ([SourceBarrier](../../src/backend/FinancialCopilot.Domain/Financial/RelativeValuation/IndustryRelativeValuationSourceBarrier.cs:88)). |
| 7. Barrier cannot accidentally accept partial provider generations | Pass for the generation-free contract | Completeness requires exactly `member count × metric count` selections, and publication requires both a complete barrier and all benchmarks available ([SourceBarrier](../../src/backend/FinancialCopilot.Domain/Financial/RelativeValuation/IndustryRelativeValuationSourceBarrier.cs:88), [SnapshotWriter](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/IndustryRelativeValuationCalculationSnapshotWriter.cs:33)). No provider-generation equality is required or used. |
| 8. Snapshot versioning and current selection are safe | **Pass** | Allocation is performed after a transaction-scoped PostgreSQL advisory lock for the calculation identity; the model has unique version and separate filtered latest-evaluation/current-published indexes ([SnapshotWriter](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/IndustryRelativeValuationCalculationSnapshotWriter.cs), [Rows](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/IndustryRelativeValuationRows.cs)). Only a successful `Published` write clears the prior published pointer. |
| 9. Idempotency is preserved | **Pass** | Same-barrier requests are checked while holding the identity lock and return the winning row. Concurrent writers receive distinct versions, and sequential retries return the existing calculation without another insert. |

## Blocking findings

1. **Resolved:** calculation version allocation and current-selection replacement are serialized by a database transaction-scoped advisory lock and protected by unique constraints/indexes.

2. **Resolved:** `IsLatestEvaluation` and `IsSelectedCurrent` are separate model states. Inconclusive, failed, and pending attempts cannot replace the published pointer; only a successful Published commit can do so.

3. **Resolved:** `20260812063122_Feature125Slice3Persistence` adds the Slice 3 source-fact, calculation, metric, company-result, watch, and outbox tables, indexes, foreign keys, filtered uniqueness constraints, and a reversible `Down` method. It was generated but not applied.

## Test execution

```text
dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj \
  --configuration Release --no-restore \
  --filter "FullyQualifiedName~IndustryRelativeValuation"
```

Result: 36 passed, 0 failed, including concurrent version allocation, duplicate/retry idempotency, published-pointer protection, and failed/pending/inconclusive lifecycle coverage.

Migration verification: the generated migration and model snapshot contain the required Slice 3 tables, unique version/current/latest indexes, foreign keys, and reversible `Down` operations. No migration was applied, no backfill was run, and no worker was started.

## Verdict

APPROVED
