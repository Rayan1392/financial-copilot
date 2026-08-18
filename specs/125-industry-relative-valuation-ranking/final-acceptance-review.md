# Feature 125 — Final Acceptance Review

> **Superseded scope notice (2026-08-17):** This historical verdict reviewed an
> `IndustryId`/`IndustryTitle` comparison cohort. The active requirement now uses exact
> `GroupId`/`GroupTitle` equality. This verdict is not approval for the group-cohort correction and
> must not be used as production-rollout authorization for it. See `README.md`, `design.md`,
> `user-story.md`, `tasks.md`, and BUG-001.

## Verdict

**APPROVED**

Feature 125 satisfies the approved user story and design at the acceptance boundary. The
remaining items are operational release gates and documented limitations; they do not require
new implementation for acceptance.

## Review basis

Reviewed:

- `design.md`
- `user-story.md`
- `tasks.md`
- `implementation-plan.md`
- `slice-2-review.md` through `slice-6-review.md`

`slice-1-review.md` is not present in the repository. Slice 1 responsibilities were therefore
verified through the implementation plan, the Slice 3/4 persistence and provider evidence, and
the consolidated acceptance coverage. This missing artifact is a documentation gap, not an
observed implementation failure.

## 1. Acceptance-criteria coverage

All AC-01 through AC-44 are mapped in `tasks.md` and have corresponding implementation or test
evidence in the slice reviews.

| Criteria | Coverage conclusion | Evidence |
|---|---|---|
| AC-01–07 | Covered | Feature 119 canonical resolution, Feature 120 clarification/replay, four v1 capabilities, executor boundary, and mismatch outcomes verified in Slice 5. |
| AC-08–15 | Covered | Feature 114 P/S projection reuse, P/E and equilibrium contracts, validation/failure outcomes, provider policy reuse, immutable facts, and source provenance verified across Slice 3/4 evidence. |
| AC-16–25 | Covered | Decimal normalization, R7 interpolation, IQR/outliers, minimum benchmark size, classification, deterministic total ranking, partial metrics, 0/0 handling, and stable limits verified in Slice 2 and Slice 5. |
| AC-26–32 | Covered | Tehran calculation date, freshness/source barrier, lifecycle, atomic publication, versioning, correction, current selection, and watch references verified in Slice 3/4. |
| AC-33–37 | Covered | Configurable entry/exit streaks, neutral days, inconclusive pause, and concurrent/idempotent watch evaluation verified in Slice 4. |
| AC-38–40 | Covered | Published and diagnostic read contracts, persisted quality/provenance fields, historical snapshots, correction history, and audit references verified in Slice 5 and Slice 6. |
| AC-41–43 | Covered | Separate leases, bounded processing, operational activity/telemetry, options validation, runbook, and migration review verified in Slice 6. |
| AC-44 | Covered | Persian/English presentation contains persisted context and evidence without buy/sell recommendations; semantic governance coverage passed. |

The required deterministic fixtures are also represented by the slice evidence, including R7
2/3/4-value cases, zero-IQR and inclusive bounds, invalid/missing/stale facts, metric-specific
outliers, 0/0 ranking, tie ordering, correction, retry, concurrency, and partial-generation
handling.

## 2. Design compliance

The implementation follows the locked design decisions:

- NADPCO provider scope and stable industry/company identity are used; display names are not keys.
- P/E, P/S, and equilibrium use the specified gauge fields and decimal formulas.
- Feature 114 remains the P/S acquisition owner; `close` and circle `avg` are projected for Feature
  125, while `BoundaryAverage` remains visualization data.
- Calculation is off the AI request path and uses persisted, versioned, barrier-selected facts.
- R7/IQR-R7-1.5-v1, metric-specific outliers, global null ordering, positive-count ranking, and
  post-ranking Top-N behavior are deterministic.
- Publication, correction, watch-state, audit, freshness, and no-recommendation rules are
  persisted or enforced at their stated boundaries.

## 3. Architecture boundaries

The boundaries are acceptable:

- The domain engine owns normalization, benchmarks, classification, and ranking.
- Infrastructure owns provider facts, source barriers, persistence, publication, leases, and
  calculation provenance.
- The semantic adapter owns composition and canonical resolution integration; the read executor is
  read-only and accepts canonical IDs and bounded limits.
- The executor does not call providers, execute SQL supplied by the LLM, or recompute formulas,
  ranks, averages, or colors.
- Presentation renders persisted results and does not introduce investment advice.

## 4. Compatibility with Features 114, 118, 119, and 120

- **Feature 114:** P/S synchronization and visualization semantics remain owned by Feature 114;
  Feature 125 consumes one explicit provider-fact projection and does not create a duplicate P/S
  worker.
- **Feature 118:** Capabilities are registered through the existing semantic registry with the
  approved precedence and persisted-read route.
- **Feature 119:** Canonical company/industry resolution and typed outcomes remain authoritative;
  Feature 125 composes results and validates membership only.
- **Feature 120:** Pending slots, candidate canonical IDs, optimistic versions, replay idempotency,
  follow-up resolution, and task-switch cleanup use the existing clarification lifecycle.

The Slice 5 semantic and API verification passed, with no reported regression to these ownership
boundaries.

## 5. Migration readiness

Migration readiness is satisfactory for deployment review:

- The canonical migration is `20260812063122_Feature125Slice3Persistence`.
- Migration metadata, filenames, model snapshot, indexes, foreign keys, and reversible `Down`
  operations are aligned.
- Disposable PostgreSQL tests applied prerequisite migrations followed by the Feature 125 migration
  from zero and passed schema/index/foreign-key checks.
- `dotnet ef migrations has-pending-model-changes` reported no pending model changes for
  `FinancialIngestionDbContext`.
- No development or production database was changed during review.

Production application still requires the normal migration approval, backup, and change-window
procedure.

## 6. Deployment readiness

The feature is deployment-ready subject to the operational gates below. Evidence includes:

- API Release build: 0 warnings, 0 errors.
- Worker Release build: 0 warnings, 0 errors.
- Feature 125 unit/semantic/watch filter: 57 passed, 0 failed.
- Broader targeted unit/semantic/state filter: 78 passed, 0 failed.
- PostgreSQL Feature 125 integration filter: 10 passed, 0 failed.
- Operational runbook: `docs/feature-125-operations.md`.
- Startup options validation, bounded limits, redacted telemetry, activity evidence, and recovery
  procedures are documented and tested.

Before production enablement, operators must confirm provider health, migration execution, and
source population/readiness. The calculation trigger is now the existing worker schedule described
in Section 8.

## 7. Rollback readiness

Rollback and recovery are adequate:

- The migration has a reversible `Down` path and was exercised against disposable PostgreSQL.
- `Enabled` provides a feature/configuration gate for disabling processing and reads.
- Published calculation versions and source facts are immutable/auditable; corrected data creates a
  new version rather than destroying the prior result.
- Lower-readiness or failed attempts cannot replace the selected Published version.
- Same-barrier retries converge, advisory-lock serialization prevents duplicate same-day advances,
  and forward recovery can retry a non-current version without corrupting published history.

Any production rollback must follow the runbook and database change-control process; it must not
delete historical evidence as an ad hoc recovery step.

## 8. Daily trigger integration

Option B is selected. Feature 125 is wired into the existing
`NadpcoScheduledSyncWorker` → `NadpcoScheduledSyncCoordinator` flow. After the existing NADPCO
ingestion workflow succeeds, the coordinator invokes the application-level
`IIndustryRelativeValuationOrchestrationService`, which runs source-fact ingestion, calculation
input/barrier construction, snapshot publication, and the existing watch evaluation path.

The existing worker remains orchestration-only. No dedicated Feature 125 hosted service, scheduler,
deployment unit, or parallel lease mechanism was added. Existing worker retry, timeout,
cancellation, and lease behavior is reused. Feature 125's existing PostgreSQL advisory locks,
publication selection, calculation version allocation, immutable evidence, and watch idempotency
remain unchanged.

## 9. Trigger verification and operational readiness

Added verification covers:

- disabled Feature 125: the downstream service returns without resolving or invoking pipeline
  dependencies;
- successful existing scheduled ingestion: the coordinator invokes Feature 125 with a correlation
  ID;
- Feature 125 failure: existing worker retry is used and the selected Published calculation remains
  selected;
- duplicate existing-worker invocations: PostgreSQL integration proves same-barrier publication is
  idempotent and watch evaluation/transition is not duplicated;
- worker-triggered PostgreSQL flow: an existing worker coordinator run produces a Published
  snapshot and one idempotent watch evaluation from existing provider facts.

Operational documentation now describes the schedule, configuration gate, first enablement, source
fact usage, no synthetic history/streaks, failure recovery, and checks in
`docs/feature-125-operations.md`.

## 10. Known limitations and release gates

- A production `EXPLAIN ANALYZE` benchmark for a very large industry is pending. Operational limits
  must not be increased until the production-like query plan is reviewed.
- The broader semantic integration command reports four unrelated pre-existing failures in
  personalized-insight and scanner fixtures; they were not changed by Feature 125. The full unit
  history also records an unrelated flaky CyclicalWaves authentication test.
- The missing `slice-1-review.md` reduces review traceability, but later persistence, integration,
  and consolidated acceptance evidence covers the relevant Slice 1 contracts.

These limitations are explicitly known, bounded, and outside the accepted Feature 125 business
logic. The daily trigger gap is resolved through Option B; production enablement still requires the
standard migration/change-window approval, provider readiness, and the pending production
large-industry query-plan review.

## Final decision

**APPROVED** and fully production-rollout-ready for the selected Option B runtime flow, subject to
the documented production EXPLAIN ANALYZE review, migration/change-window approval, provider
readiness, and resolution or formal waiver of unrelated existing test failures.
