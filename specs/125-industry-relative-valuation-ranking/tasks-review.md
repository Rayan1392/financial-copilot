# Feature 125 — Stage 4 Implementation-Readiness Review

## Verdict

`APPROVED`

The task breakdown is implementation-ready after the task-only corrections
recorded below. `design.md` and `user-story.md` were not modified.

## Review scope

Reviewed:

- `tasks.md`
- `design.md`
- `user-story.md`

The review checked task size, implementation slices, dependency ordering,
migration isolation, first-run data requirements, AI/read-model coverage, and
whether agents can work from explicit task boundaries.

## Findings and disposition

### 1. Task size

The original persistence task combined model shape and EF mapping, and the
original publication task combined transaction atomicity, version selection,
current selection, correction, and retry behavior. Those were too broad for
independent implementation and failure diagnosis.

Resolved in `tasks.md`:

- `T10A` separates EF mappings and persistence invariants from model shape.
- `T20A` separates version/current-selection and retry behavior from the
  publication transaction.

The remaining broad tasks are review, integration, or end-to-end test gates;
they are intentionally later gates rather than mixed production slices.

### 2. Missing implementation slices

The provider client tasks existed, but no task owned the workflow that
enumerates companies, invokes P/E and equilibrium acquisition, consumes the
Feature 114 P/S projection, persists outcomes, and applies a separate source
lease.

Resolved by `T09A`, which explicitly owns source-ingestion orchestration and
its tests. The calculation worker remains owned by `T21`.

### 3. Dependency realism

The original graph contained a cycle: `T25` depended on `T38`, while the
acceptance/regression chain leading to `T38` depended on semantic integration.
That would prevent a normal implementation sequence.

Resolved by changing `T25` to depend on persisted-read prerequisites (`T03`,
`T17`, and `T20A`). The refinement slices are also reflected in task
dependencies. Configuration is now available before source/calculation worker
execution, and source orchestration is a prerequisite for the source barrier.

### 4. Migration isolation

Migration work is correctly isolated in `T12`; model and constraint work can
be reviewed first, and `T39` remains a separate deployment/rollback gate.
The task breakdown does not authorize implementation or migration creation by
itself. `T12A` depends on the isolated migration and does not combine schema
generation with provider, engine, worker, or API changes.

### 5. Initial population and backfill

The original breakdown did not state how the first usable data set is created,
how existing Feature 114 P/S observations may be reused, or whether watch
streaks may be fabricated from historical data.

Resolved by `T12A`, which requires an explicit bootstrap procedure, compatible
P/S reuse, acquisition of missing P/E/equilibrium facts, partial-bootstrap
retry behavior, and an explicit backfill/no-backfill decision. It prohibits
invented historical facts and synthetic watch streaks.

### 6. AI presentation and read models

`T28`–`T31` cover the persisted-read boundary, ranking read model, comparison
and summary contracts, and application/API tests. The task breakdown now also
requires `T30` to define user-facing presentation projection/serialization,
unavailable and outlier wording, evidence/status context, bounded limits, and
the no-buy/sell boundary. This is sufficient to keep presentation on the
persisted read path without allowing provider calls or LLM-supplied formulas.

### 7. Independent agent execution

Each implementation task has an objective, design/acceptance references,
dependencies, implementation notes, tests, and completion criteria. The
alignment tasks (`T01`–`T04`) establish repository seams before implementation;
the new refinement tasks make persistence, ingestion, bootstrap, and
publication boundaries explicit. The final integration tasks remain dependent
on the completed slices and are not prerequisites for starting independent
provider, engine, persistence, or read-model work.

## Remaining implementation constraints

- Preserve the locked formulas, membership identity, R7/IQR algorithm, ranking
  order, publication semantics, and watch thresholds.
- Keep provider acquisition off the AI request path.
- Do not create a second P/S worker or use `BoundaryAverage` as the P/S
  historical baseline.
- Do not create migrations as part of this review.

## Files changed by this review

- `tasks.md` — task-only readiness corrections.
- `tasks-review.md` — this review.

`design.md` and `user-story.md` were left unchanged.
