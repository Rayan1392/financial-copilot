# Feature 125 — Implementation Execution Plan

> **Superseded scope notice (2026-08-17):** Cohort membership in this historical plan is based on
> `IndustryId`. The active correction uses `GroupId`/`GroupTitle`; follow the amended `design.md`,
> `user-story.md`, and `tasks.md` before implementation.

## Status and execution boundary

This plan is for Stage 5 implementation execution after the approved design,
user story, task breakdown, and task-readiness review. It is an execution
sequence, not a change to the approved product rules.

The implementation must preserve the locked formulas, NADPCO identity rules,
R7/IQR algorithm, ranking comparator, publication semantics, watch thresholds,
provider-neutral fact boundary, and persisted-read-only AI boundary.

This document does not authorize production implementation during its creation.
In particular, no production code or migration is created as part of this
planning step. T12 and T39 remain later implementation/review gates; their
presence in the plan does not mean a migration is to be created now.

## Slice 1 — Data foundation and provider facts

### Goal

Establish repository seams, configuration conventions, provider-neutral source
facts, Feature 114 P/S reuse, P/E and equilibrium acquisition, immutable
observation persistence, source-ingestion orchestration, constraints, and the
approved first-run population policy.

### Tasks included

T01–T04, T05, T06, T07, T08, T09, T09A, T10, T10A, T11, T12, T12A.

T12 is planned as an isolated migration artifact/schema-verification task and
must not be started in this stage unless implementation authorization is given.
T12A must not invent historical facts or synthetic watch streaks.

### Expected code areas/projects

- `FinancialCopilot.Domain`: provider-neutral fact contracts, readiness and
  quality values, persistence-facing domain concepts, and immutable identity
  rules.
- `FinancialCopilot.Application`: fact acquisition/use-case contracts and
  source-ingestion orchestration interfaces.
- `FinancialCopilot.Infrastructure`: CyclicalWaves P/E and equilibrium client
  extensions, Feature 114 P/S projection, raw observation/fact repositories,
  EF models/configurations, indexes, constraints, leases, and activity storage.
- `FinancialCopilot.Worker`: source-ingestion workflow using the existing
  authentication, retry, timeout, rate-limit, and bounded-worker conventions.
- Existing Feature 114 synchronization code and its regression fixtures.
- Infrastructure/application integration-test projects and provider HTTP
  stubs. No AI endpoint changes are needed in this slice.

### Database impact

Define the versioned provider observation/source-fact and Feature 125
persistence model, provenance fields, source watermarks, payload hashes,
readiness, quality codes, and uniqueness/index rules. Existing Feature 114
visualization rows remain unchanged. T12 is the only planned schema artifact;
do not create or apply it while producing this plan.

### Tests required

Contract and parser tests for P/S `close`/`avg`, proving `BoundaryAverage` is
not used; P/E and equilibrium payload, decimal, identity, body-size, and
unknown-field tests; all distinct no-data/auth/rate-limit/timeout/network/5xx
and malformed-payload outcomes; immutability, idempotency, provenance,
watermark, persistence constraint, and source-lease tests; partial bootstrap
and retry tests; configuration/convention alignment tests.

### Dependencies

Start with T01–T04. T05 precedes all fact implementations. T06 depends on the
Feature 114 contract. T07 and T08 can proceed in parallel after T05. T09 and
T10/T10A/T11 require the fact contract and are coordinated before T09A.
T09A requires both provider clients, the P/S projection, persistence, and
worker conventions. T12 follows the model/constraint review. T12A follows
the schema decision and source orchestration.

### Completion criteria

Every accepted source observation has canonical identity, bounded validated
payload evidence, immutable versioning, freshness/provenance, and a stable
readiness/quality outcome. P/S is reused through one explicit Feature 114
projection. P/E and equilibrium use the existing CyclicalWaves pipeline. A
separate source lease and bounded orchestration persist outcomes without
calling providers from the AI path. The first-run population/no-backfill
decision is documented and testable.

### Risks

Duplicating the P/S worker; mapping `BoundaryAverage` instead of P/S `avg`;
confusing provider identifiers with display names; creating a second resilience
stack; leaking raw payloads/high-cardinality labels; rewriting old observations;
or treating incomplete bootstrap data as a valid historical watch sequence.

## Slice 2 — Calculation and ranking engine

### Goal

Build the deterministic pure calculation path from canonical NADPCO membership
through normalized relative metrics, benchmark publication, classification,
and stable total ranking.

### Tasks included

T13–T17.

### Expected code areas/projects

- `FinancialCopilot.Domain`: membership snapshot, metric normalization and
  quality evaluation, benchmark/outlier value objects, classifications, and
  rank comparator.
- `FinancialCopilot.Application`: calculation-engine orchestration contracts
  and projections.
- `FinancialCopilot.Infrastructure`: canonical catalog queries and persistence
  adapters only where required by the engine boundary.
- Calculation unit-test project with exact decimal fixtures.

### Database impact

Consume catalog and source facts by canonical `CompanyId`/`IndustryId` and
prepare the versioned calculation, metric, member, and rank rows defined by
the design. No provider reads or AI-path calculations are introduced.

### Tests required

Membership inclusion/exclusion and left-join behavior; decimal normalization,
missing/invalid/non-positive/freshness outcomes; R7 quartiles for 2/3/4 and
larger samples; IQR=0, inclusive bounds, outliers, insufficient clean values;
classification equality/unknown cases; 0/0 handling; nullable tie ordering,
total ties, stable comparator, GlobalRank, and Top-N-after-ranking tests.

### Dependencies

Requires Slice 1’s contracts and persisted facts. T13 precedes T14–T17. T14
precedes benchmark and classification work. T15 and T16 are sequential at the
engine level, while T17 follows the completed classification contract.

### Completion criteria

For a fixed membership snapshot and source barrier, the engine produces the
same decimal outputs, benchmark evidence, quality reasons, classifications,
and complete-industry lexicographic ranks on every run. Missing members remain
visible, invalid values never enter benchmarks, and Top-N never changes rank
calculation.

### Risks

Using raw ratios across companies; using PE_TTM as the P/E gauge baseline;
rounding during calculation; incorrect R7 indexing; making one observation a
benchmark; pairwise null ordering; or allowing unclassifiable 0/0 members to
consume Top-N slots.

## Slice 3 — Daily calculation pipeline

### Goal

Turn source facts and the pure engine into a durable, Tehran-date, leased,
barriered, versioned, atomically published daily snapshot with safe retries and
correction history.

### Tasks included

T18–T21.

### Expected code areas/projects

- `FinancialCopilot.Application`: calculation request, source barrier,
  readiness lifecycle, publication, and version-selection use cases.
- `FinancialCopilot.Domain`: calculation statuses and publication invariants.
- `FinancialCopilot.Infrastructure`: transactional repositories, current
  selection, hashes, idempotency, and lease/activity persistence.
- `FinancialCopilot.Worker`: scheduled daily calculation worker and cadence.
- Worker/integration-test projects using persisted fixtures and transaction
  boundaries.

### Database impact

Persist calculation versions, membership/source-barrier hashes, per-industry
metric rows, member/rank rows, status/timestamps, selected published version,
and audit evidence. Publication must atomically include the calculation output
and its watch evaluation/outbox handoff where defined by the approved design.

### Tests required

Tehran timezone and business-date tests; barrier completeness/freshness and
source-version selection; Pending/Ready/Published/Inconclusive/Failed
lifecycle; atomic rollback and no partial publication; same-barrier no-op;
corrected-data new-version selection; lower-readiness protection; lease
contention; cancellation/deadline; rerun and activity evidence tests.

### Dependencies

Requires Slice 2’s engine and Slice 1’s persistence/source facts. T18 precedes
T19, T20, and T21. T20 and T20A are one publication design and must be
implemented/reviewed together before T21. T21 is the final slice task because
it depends on all calculation orchestration behavior.

### Completion criteria

One configured business date has at most one active publisher, captures a
complete source barrier, produces a durable version, and exposes only a fully
validated Published selection to normal readers. Recalculation preserves old
evidence, selects only the complete corrected version, and cannot advance
watch state twice.

### Risks

Reading uncommitted provider rows; publishing partial generations; using local
date instead of Tehran date; allowing stale facts into a barrier; replacing a
published version with a weaker retry; or letting same-date recalculation
advance downstream state more than once.

## Slice 4 — Watch state machine

### Goal

Evaluate valid published daily snapshots and persist the long-term
NotWatching/EntryPending/Watching/ExitPending state machine with paused
inconclusive days and auditable transitions.

### Tasks included

T22–T24.

### Expected code areas/projects

- `FinancialCopilot.Domain`: predicates, counters, durable states, and
  transition invariants.
- `FinancialCopilot.Application`: watch evaluation and transition services.
- `FinancialCopilot.Infrastructure`: watch state, evaluation, transition, and
  event-idempotency persistence.
- Calculation/worker integration tests for published and inconclusive days.

### Database impact

Add/use `IndustryWatchState` and append-only `IndustryWatchTransition` plus
same-date evaluation identity/upsert storage, all linked to the selected
calculation id and algorithm version.

### Tests required

All-three-average requirement; at least-two-clean-observation requirement;
entry/exit below/above 100; exact 100; configurable thresholds 1/2/3/>3;
valid neutral-day reset; inconclusive pause; mutually exclusive counters;
same-date duplicate suppression; transition evidence and correction behavior.

### Dependencies

Requires Slice 3’s selected Published calculation identity and publication
transaction. T22 defines predicates before T23 transitions; T24 completes
same-date idempotency and must be verified against recalculation behavior.

### Completion criteria

Only valid Published snapshots can advance a streak. Inconclusive snapshots
record diagnostics without changing state/counters. Duplicate evaluation cannot
increment twice, and every transition is linked to the selected calculation
with prior/new counters and an explicit reason.

### Risks

Treating Inconclusive as a durable state; resetting paused streaks; treating
100 as either predicate; advancing entry and exit together; or coupling state
advancement to an unselected same-date calculation.

## Slice 5 — Semantic AI integration and read models

### Goal

Expose Feature 125 through the existing semantic registry, entity-resolution
and clarification authorities, and stable persisted read contracts—without
provider calls or LLM-supplied calculations on the AI path.

### Tasks included

T25–T31.

### Expected code areas/projects

- `FinancialCopilot.Domain`/`FinancialCopilot.Application`: capability
  definitions, precedence, typed resolver outcomes, clarification slots,
  executor inputs, and response/read-model contracts.
- Existing Feature 118 registry, Feature 119 entity-resolution integration,
  and Feature 120 clarification/replay integration.
- `FinancialCopilot.Infrastructure`: read repositories over selected Published
  rows and clarification persistence adapters.
- `FinancialCopilot.API`: application/endpoint serialization and bounded-limit
  contracts.
- Semantic, application, and API contract-test projects.

### Database impact

Read only from selected Published calculation/read-model rows for normal
financial responses. Persist or reuse Feature 120 pending clarification state
with optimistic versioning and replay idempotency. No provider-fact writes or
live calculation tables are touched by an AI request.

### Tests required

Capability registration and precedence; canonical symbol/industry/pair
resolution; ambiguous/not-found/missing/mismatch/different-industry outcomes;
one-turn clarification and task-switch reset; persisted-read-only/provider
call-spy tests; formula/SQL/rank/average/color rejection; bounded limits;
ranking, symbol, pair, summary, unavailable/outlier, diagnostic, freshness,
evidence, and exact serialization contracts; no-buy/sell language boundary.

### Dependencies

Requires Slice 2 persisted engine contracts and Slice 3 selected snapshots;
T25 requires the persisted-read prerequisites, not the final acceptance suite.
T26 follows capability definitions and canonical catalog access. T27 follows
T26. T28 requires published data and resolver inputs. T29 follows T28 and
ranking persistence. T30 follows T26/T28/T29. T31 follows T27–T30.

### Completion criteria

All four approved capabilities resolve deterministically and return stable
persisted evidence. Normal reads expose only Published data, bounded results,
quality/outlier context, and no provider calls or recalculation. Clarification
and cross-industry outcomes are typed and replay-safe.

### Risks

Routing plain P/S or Feature 115 into the new capability; resolving by display
title alone; comparing different industries; allowing the model to supply
formulas or SQL; exposing unready rows as financial results; or accidentally
calling CyclicalWaves from the AI request path.

## Slice 6 — Operations, testing, deployment

### Goal

Complete configuration validation, observability, operational documentation,
test consolidation, acceptance traceability, migration review, and the final
implementation-readiness handoff.

### Tasks included

T32–T40.

### Expected code areas/projects

- Existing configuration/startup validation in `FinancialCopilot.API`,
  `FinancialCopilot.Worker`, and shared application/infrastructure options.
- Existing data-sync activity, logging, telemetry, and provider-health
  conventions.
- Deployment/runbook documentation under the repository’s existing docs/spec
  conventions.
- Unit, integration, semantic regression, API contract, migration-review, and
  traceability test projects.

### Database impact

Validate the already-approved schema/model and operational evidence. T39 may
review an isolated migration artifact and rollback/forward-recovery behavior,
but this planning task creates none and must not silently broaden schema scope.

### Tests required

Configuration boundary/default/disabled/startup tests; redaction and bounded
telemetry labels; activity/failure/retry evidence; lease/recovery dry runs;
calculation unit consolidation; provider/persistence integration suite;
watch/semantic regression suite; complete AC/design traceability matrix;
migration schema/rollback or forward-recovery rehearsal; final handoff checks.

### Dependencies

T32 can begin after T04 and should be available before enabling workers. T33
requires source/calculation activity. T34 requires operational behavior and
schema decisions. T35 depends on T14–T17; T36 on provider/persistence and
pipeline work; T37 on watch/semantic work plus prior tests; T38 follows T35–T37;
T39 follows T12/T34/T38; T40 follows T38–T39.

### Completion criteria

Valid configuration fails fast when invalid; operators can inspect health,
readiness, barriers, counts, and failure codes without raw payload leakage;
all required tests and fixtures pass; every acceptance criterion and design
section maps to a task and concrete evidence; migration/deployment review and
implementation handoff are independently approved.

### Risks

Enabling workers before data/schema readiness; leaking provider payloads;
high-cardinality telemetry; declaring coverage by inference; weakening
regression tests for Features 114/115/118/119/120; or treating migration review
as permission to create/apply a migration during this stage.

## Parallel implementation opportunities

The following work is safe to parallelize when its stated prerequisites are
complete and write ownership is kept separate:

- T01–T04 can be reviewed in parallel after the repository read-through,
  subject to a single agreed contract baseline.
- T07 and T08 can be implemented in parallel after T05.
- Provider contract fixtures (T06–T08) can proceed alongside persistence model
  design (T09–T11), provided the provider-neutral contract is frozen.
- T14’s quality rules and T15’s pure R7/IQR implementation can be developed
  in parallel after T13, then integrated through T16.
- Read-model contract design (T29–T30) can be prepared in parallel after the
  executor boundary (T28), with T30 still requiring resolver outcomes.
- T32’s options validation and T35’s pure calculation test consolidation can
  proceed in parallel with late integration work once their prerequisites are
  met.

Parallel work must not share an unresolved model, change Feature 114’s
visualization semantics, or bypass the source-barrier/publication owner.

## Sequential implementation requirements

The critical path is:

`T01–T04 → T05 → T06–T11 → T09A → T12/T12A → T13 → T14 → T15 → T16 → T17 → T18 → T19 → T20/T20A → T21 → T22 → T23/T24 → T25–T28 → T29/T30 → T31 → T32–T40`.

Important ordering constraints are:

- Freeze canonical identity, Feature 114 reuse, semantic, worker, timezone,
  and configuration seams before production implementation.
- Define the provider-neutral fact contract before any provider adapter or
  persistence implementation.
- Complete source facts and canonical membership before the calculation engine.
- Complete the pure engine before the daily source barrier and publication
  workflow.
- Publish calculation versions before evaluating or reading watch state.
- Complete capability/resolution/executor boundaries before final read models
  and API contracts.
- Run consolidated tests and traceability before migration/deployment review
  and final handoff.

## Recommended first implementation slice

Start with **Slice 1 — Data foundation and provider facts**, beginning with
T01–T04 as a short contract-alignment pass, then T05 and the provider/
persistence work. This is the recommended first slice because every later
slice depends on canonical identity, provider-neutral facts, Feature 114 P/S
reuse, source provenance, and the existing worker/lease conventions. Keep T12
explicitly gated: planning and review may identify its required schema, but no
migration should be created or applied as part of this request.

## Recommended execution order summary

1. Slice 1: align seams, establish facts/persistence, and define the approved
   first-run population policy.
2. Slice 2: implement and prove the pure calculation and ranking engine.
3. Slice 3: add source barriers, daily orchestration, atomic publication, and
   safe correction/retry behavior.
4. Slice 4: attach the durable watch state machine to selected Published
   calculations.
5. Slice 5: integrate semantic capabilities, resolution/clarification, and
   persisted read models/API contracts.
6. Slice 6: finish operations, consolidated tests, traceability, deployment
   review, and implementation handoff.

At every slice boundary, run the slice-specific tests and verify that no
provider call, raw cross-company comparison, LLM-supplied formula, or
unapproved schema change has entered the implementation.
