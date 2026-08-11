# Feature 125 — Implementation Task Breakdown

## Status

`TASKS_DRAFT`

This document breaks the approved Feature 125 design and user story into
implementation-sized tasks. It does not authorize implementation by itself;
the implementation and migration review gates remain in force.

## Implementation Strategy

Implement from the persistence and provider contracts upward, then add the
pure calculation/ranking engines, the leased daily pipeline, watch state, and
the persisted semantic read path. Keep provider acquisition off the AI path.
Reuse Feature 114’s P/S acquisition and existing CyclicalWaves resilience,
raw-payload, worker, lease, data-sync, and Tehran-date conventions. Keep schema
migration creation and verification in a separate phase after the model shape
has been reviewed.

Every vertical slice must add its tests with the implementation boundary. No
task may change the locked formulas, membership identity, R7/IQR algorithm,
ranking order, publication semantics, or watch thresholds.

## Dependency Graph

```text
T01/T02/T03/T04
        │
        ├── T05 ── T06 ── T07 ── T08 ── T09
        │              │
        │              └── T10 ── T11 ── T12
        │                              │
        ├── T13 ── T14 ── T15 ── T16 ── T17
        │                              │
        └──────────────────────────────┴── T18 ── T19 ── T20
                                                   │
                         T21 ── T22 ── T23 ── T24 ─┤
                                                   │
                         T25 ── T26 ── T27 ── T28 ─┤
                                                   │
                         T29 ── T30 ── T31 ── T32 ─┤
                                                   │
                         T33 ── T34 ── T35 ── T36 ─┤
                                                   │
                         T37 ── T38 ── T39 ── T40
```

The graph is logical rather than a requirement that all tasks be separate
commits. Tasks may be combined only when their scope and test boundary remain
explicit.

Refinement slices not shown in the compact graph are T09A (source-fact
orchestration), T10A (EF mappings), T12A (first-run population/backfill
policy), and T20A (version selection/retries). T25 depends on persisted-read
prerequisites rather than the final acceptance matrix, keeping the dependency
graph acyclic.

---

# Phase 0 — Repository Preparation / Contract Alignment

## Task 01 — Confirm architecture boundaries and integration seams

### Objective

Record the concrete application, domain, infrastructure, API, worker, and test
projects that own Feature 125 responsibilities.

### Design References

- `design.md` §2, §10, §11
- `user-story.md` AC-05–07, AC-41–43

### Scope

Inspect `FinancialIngestionDbContext`, existing Company/Industry catalog
normalizers, CyclicalWaves provider and ingestion classes, worker registration,
data-sync activity, Feature 118 registry, Feature 119 resolution, and Feature
120 task state. Produce an implementation boundary note in the task/PR
description; do not alter source files in this task.

### Dependencies

None.

### Implementation Notes

Use the repository’s existing Clean Architecture project ownership. Do not use
Feature 114’s `NoavaranEligibleCompanies` as Feature 125 membership.

### Tests Required

Architecture/project-reference validation and a review checklist confirming
that no AI-path provider dependency is introduced.

### Completion Criteria

All target extension points and their owning projects are named, and no
unapproved boundary or duplicate ingestion path is proposed.

## Task 02 — Align Feature 114 P/S reuse contract

### Objective

Confirm the existing P/S acquisition, payload, raw-payload, resilience, and
visualization contracts that Feature 125 must consume.

### Design References

- `design.md` §4.1, §13
- `user-story.md` AC-08, AC-12
- Feature 114 `provider-contract.md`

### Scope

Map the accepted circle payload and existing sync lifecycle to a single
Feature 125 provider-fact projection. Identify the exact place to publish
`close`/`avg` without changing `BoundaryAverage` or visualization rows.

### Dependencies

Task 01.

### Implementation Notes

Do not create a second P/S worker or reinterpret `BoundaryAverage`.

### Tests Required

Existing Feature 114 regression tests are identified for reuse; add a fixture
plan proving circle `avg` and `BoundaryAverage` are different facts.

### Completion Criteria

The reuse and projection seam is documented and accepted by the implementation
review with no duplicate ownership.

## Task 03 — Align semantic and clarification contracts

### Objective

Identify the exact Feature 118 capability registration, Feature 119 outcome,
and Feature 120 pending-task integration points.

### Design References

- `design.md` §10
- `user-story.md` AC-01–07
- Features 118, 119, and 120 user stories/tasks

### Scope

Map capability codes, slot types, precedence, persisted-read route, resolution
outcomes, clarification candidate IDs, optimistic versioning, and replay
behavior to existing abstractions.

### Dependencies

Task 01.

### Implementation Notes

Metric meanings remain owned by existing metric features. Feature 125 adds
semantic capabilities, not a new general metric parser.

### Tests Required

Identify existing registry, resolver, clarification, and follow-up replay test
fixtures that must remain green.

### Completion Criteria

All semantic integration points and regression test suites are named.

## Task 04 — Align worker, lease, timezone, and configuration conventions

### Objective

Identify reusable worker, distributed lease, activity, timeout, retry,
telemetry, Tehran timezone, and options-validation patterns.

### Design References

- `design.md` §5, §11
- `user-story.md` AC-26, AC-41–43

### Scope

Map source ingestion and calculation to separate lease names, the calculation
date lease key, bounded hosted-worker lifecycle, cancellation/deadline, and
persisted data-sync activity.

### Dependencies

Task 01.

### Implementation Notes

Reuse existing policies; do not create a parallel authentication or resilience
stack.

### Tests Required

Identify lease contention, cancellation, retry, timezone, and options-validator
test patterns.

### Completion Criteria

The operational implementation seams and configuration conventions are fixed
before feature code begins.

---

# Phase 1 — Provider Fact Acquisition

## Task 05 — Implement the provider-neutral fact contract

### Objective

Define the application-facing `RelativeValuationSourceFact` contract and source
kind/readiness/quality enumerations.

### Design References

- `design.md` §1, §4
- `user-story.md` AC-13–15, AC-19

### Scope

Represent CompanyId, ProviderName, SourceKind, SourceObservationId,
CurrentValue, ReferenceValue, FetchedAtUtc, PersistedAtUtc, SourceWatermark,
PayloadHash, Readiness, QualityCode, and IdentityEvidence.

### Dependencies

Tasks 01–04.

### Implementation Notes

Source kinds are `PEGauge`, `PSGauge`, `EquilibriumGauge`, and `MarketPrice`.
Use decimal values and provider-neutral application contracts.

### Tests Required

Contract serialization/validation tests for all fields, source kinds, and
distinct missing/failure/quality states.

### Completion Criteria

The contract can carry every approved source fact and no implementation path
requires raw provider DTOs in calculation or read layers.

## Task 06 — Extend Feature 114 with the P/S fact projection

### Objective

Publish Feature 125’s P/S source fact from the existing Feature 114 accepted
gauge observation.

### Design References

- `design.md` §4.1
- `user-story.md` AC-08, AC-17

### Scope

Map circle `close` to CurrentPS and circle `avg` to HistoricalAveragePS while
carrying the accepted observation’s timestamps, hash, identity, and source ID.

### Dependencies

Tasks 02 and 05.

### Implementation Notes

Leave Feature 114 visualization rows and `BoundaryAverage` semantics unchanged.
Absent/invalid/zero/negative `avg` becomes `InvalidBaseline` for the relative
fact; visualization renderability remains independent.

### Tests Required

Regression fixtures with `avg != BoundaryAverage`, missing/invalid `avg`, and
proof that no second P/S worker or provider call is introduced.

### Completion Criteria

The accepted Feature 114 observation produces exactly one correct Feature 125
projection with inherited provenance.

## Task 07 — Add the P/E provider client contract

### Objective

Add the P/E CyclicalWaves request and normalization boundary.

### Design References

- `design.md` §4.2
- `user-story.md` AC-09, AC-11–12, AC-16

### Scope

Call `/api/pe/circle-chart-data/{isin}` through existing authentication,
retry, timeout, rate-limit, body-size, and telemetry policies. Accept required
fields `a,b,c,d,e,f,close,start,end,min,max,avg`; ignore additive fields; map
close/avg to CurrentPE/HistoricalAveragePE.

### Dependencies

Task 05.

### Implementation Notes

Retain bounded validated raw payload according to existing audit conventions;
never log it ordinarily. Verify canonical company/ISIN identity.

### Tests Required

Valid response, additive fields, malformed JSON, oversized body, decimal
overflow, non-finite/unusable values, identity mismatch, 404, 204, auth failure
after retry, 429, timeout, network failure, 5xx, raw-payload retention, and
ordinary-log redaction fixtures.

### Completion Criteria

Every accepted or rejected response yields the approved deterministic fact
readiness/quality result and no unusable P/E fact is persisted as usable.

## Task 08 — Add the equilibrium provider client contract

### Objective

Add the equilibrium gauge acquisition and normalization boundary.

### Design References

- `design.md` §4.3–§4.4
- `user-story.md` AC-10–12, AC-18

### Scope

Call `/api/equilibrium/gauge/{isin}` and accept the approved business fields;
map close to CurrentMarketPrice and balance to EquilibriumPrice; retain other
fields for audit/future visualization only.

### Dependencies

Task 05.

### Implementation Notes

Verify provider ticker/ISIN against the canonical link. Do not substitute an
existing quote source.

### Tests Required

Valid response, additive fields, malformed/oversized body, decimal overflow,
non-finite/unusable values, identity mismatch, 404, 204, auth-after-retry,
429, timeout, network failure, 5xx, retention, and log-redaction fixtures.

### Completion Criteria

Equilibrium source facts use only `close`/`balance` for calculation and every
failure is deterministic and unusable.

## Task 09 — Persist provider observations and source-fact versions

### Objective

Persist accepted and rejected provider observations with immutable provenance.

### Design References

- `design.md` §4, §8
- `user-story.md` AC-13–15, AC-31, AC-40

### Scope

Implement source-observation identity/hash handling, PayloadHash,
SourceWatermark, timestamps, identity evidence, readiness/quality state, and
new-version behavior when provider values change.

### Dependencies

Tasks 05–08.

### Implementation Notes

Accepted observations are append-only by source identity/hash. Provider failure
outcomes may be persisted as readiness evidence without becoming usable facts.

### Tests Required

Same observation no-op, changed payload creates a new version, prior evidence
unchanged, deterministic watermark, and exact calculation-input provenance.

### Completion Criteria

Calculation can select an immutable source version and prove which observation,
hash, and watermark was used.

## Task 09A — Implement Feature 125 source-ingestion orchestration

### Objective

Run the P/E and equilibrium acquisitions, and consume the Feature 114 P/S
projection, under the existing source-ingestion lifecycle.

### Design References

- `design.md` §4, §5, §11
- `user-story.md` AC-08–15, AC-26, AC-41–43

### Scope

Add the source-ingestion workflow that enumerates the canonical NADPCO company
scope, invokes the P/E and equilibrium clients, consumes the P/S fact
projection, persists accepted and rejected observations through Task 09, and
records per-company outcome evidence. Use a source-ingestion lease separate
from the calculation lease and preserve per-company failure isolation.

### Dependencies

Tasks 04–09.

### Implementation Notes

This task owns source-fact orchestration and scheduling hookup; it does not own
the daily calculation worker. No provider acquisition is added to the AI path,
and no duplicate P/S worker is created.

### Tests Required

Company enumeration, P/S projection consumption, P/E/equilibrium dispatch,
partial-provider failure isolation, source lease contention, idempotent rerun,
cancellation/deadline, and persisted activity evidence.

### Completion Criteria

The source-ingestion workflow can populate latest facts without duplicate P/S
acquisition, cross-company failure propagation, or AI-path provider calls.

---

# Phase 2 — Source Facts / Provenance / Persistence

## Task 10 — Add Feature 125 persistence models and EF mappings

### Objective

Model the Feature 125 durable records in the Financial ingestion persistence
boundary. Keep this task limited to model shape and relationships; EF
configuration belongs to Task 10A.

### Design References

- `design.md` §4, §8, §9
- `user-story.md` AC-13–15, AC-30–40

### Scope

Add equivalent models for `RelativeValuationSourceFact`,
`IndustryRelativeValuationCalculation`, `IndustryRelativeValuationMetric`,
`CompanyIndustryRelativeValuation`, `IndustryWatchState`, and
`IndustryWatchTransition`, including selected-current publication marker,
membership/source-barrier hashes, algorithm/rank versions, and outbox evidence.

### Dependencies

Tasks 05 and 09.

### Implementation Notes

Use canonical CompanyId/IndustryId keys and preserve historical snapshots.
Exact class names may follow repository conventions without changing the
approved data contract.

### Tests Required

Model and relationship tests for source provenance, version fields, and
historical evidence preservation.

### Completion Criteria

All durable design records have an agreed model shape and no display title is
used as a key.

## Task 10A — Add Feature 125 EF mappings and persistence invariants

### Objective

Implement EF mappings and relationship-level persistence configuration for the
Task 10 models.

### Design References

- `design.md` §8
- `user-story.md` AC-13–15, AC-30–32, AC-37, AC-40

### Scope

Configure precision, nullability, foreign keys, historical relationships,
provenance fields, version fields, and selected-current/outbox/watch
relationships without changing the approved model shape.

### Dependencies

Task 10A.

### Tests Required

Mapping, precision, nullability, relationship, provenance, and historical
evidence tests using the repository's EF conventions.

### Completion Criteria

All Task 10 records are mapped consistently and the schema diff can be
reviewed independently from provider and calculation implementation.

## Task 11 — Define indexes, unique constraints, and current selection

### Objective

Make idempotency, lookup, rank, and selected-publication invariants enforceable
at the persistence layer.

### Design References

- `design.md` §8
- `user-story.md` AC-31–32, AC-37, AC-40

### Scope

Add unique identities `(CalculationDate, IndustryId, CalculationVersion)` and
`(CalculationId, CompanyId)`, source observation/hash uniqueness as approved,
watch evaluation identity, and an explicit unique selected-current marker.
Add indexes for IndustryId/CalculationDate/status/current selection and read
paths.

### Dependencies

Task 10.

### Implementation Notes

The selected current version is highest valid version for the date, then highest
calculation ID, under the explicit marker. Do not overwrite old evidence.

### Tests Required

Constraint violations, concurrent duplicate insert attempts, same-barrier no-op,
current-selection uniqueness, and historical query/index smoke tests.

### Completion Criteria

Database constraints enforce all approved identity and current-selection rules.

## Task 12 — Create the migration artifact and verify schema

### Objective

Create the isolated database migration required by Tasks 10A–11, if the existing
schema does not already provide the needed structures.

### Design References

- `design.md` §8
- `user-story.md` AC-31, AC-40

### Scope

Generate the migration only after model review; include tables, columns,
indexes, constraints, decimal precision, foreign keys, and any safe seed or
version metadata required by the approved design.

### Dependencies

Tasks 10A–11.

### Implementation Notes

Do not mix migration generation with provider, engine, worker, or API tasks.
Document whether migration is required; do not assume it is optional without
checking the actual model/schema diff.

### Tests Required

Clean-database apply, existing-database apply, model snapshot consistency,
constraint verification, representative insert/read, and rollback/recovery
considerations according to repository migration conventions.

### Completion Criteria

Migration review confirms schema matches the approved persistence contract and
does not destroy or rewrite existing Feature 114–120 data.

## Task 12A — Define and execute first-run population/backfill policy

### Objective

Make initial data availability and historical limits explicit before the first
published calculation is enabled.

### Design References

- `design.md` §3–§5, §8–§9, §11
- `user-story.md` AC-26–32, AC-36–40

### Scope

Document and implement the approved bootstrap procedure: reuse only
provenance-compatible existing Feature 114 P/S observations, acquire missing
P/E and equilibrium facts through Task 09A, establish the first canonical
membership/source barrier, and create the first daily snapshot. Explicitly
record whether historical calculation backfill is supported; if it is not,
record that watch streaks begin at the first valid published business date and
that no synthetic prior days are created. Define rerun/idempotency behavior
for a partially completed bootstrap.

### Dependencies

Tasks 09A, 12, 13, and 32.

### Implementation Notes

This task must not invent historical facts or watch streaks. Any supported
historical replay must use the same immutable source, membership, calculation,
and publication contracts as daily processing.

### Tests Required

Existing-P/S reuse, missing-source acquisition, clean first-run population,
partial-bootstrap retry, no-synthetic-watch-history, and optional approved
historical-replay tests.

### Completion Criteria

Operations and implementation agents have an executable first-run procedure,
an explicit backfill/no-backfill decision, and deterministic recovery behavior.

---

# Phase 3 — Industry Calculation Engine

## Task 13 — Resolve canonical NADPCO membership snapshots

### Objective

Build the membership resolver used by every calculation date.

### Design References

- `design.md` §2–§3
- `user-story.md` AC-01–04, AC-24, AC-26, AC-29

### Scope

Join Company.IndustryId to provider-scoped Industry.Id; include active/current
NADPCO members; retain no-fact members; exclude missing-classification and
inactive companies from new eligible snapshots; preserve historical membership
and support industry movement on future dates.

### Dependencies

Tasks 01, 10, and 12.

### Implementation Notes

Membership is not the intersection of metric rows and does not use Feature 114
Noavaran scope.

### Tests Required

Provider-scoped identity, display-title collision, inactive, unclassified,
moved-company, no-fact member, and historical-membership fixtures.

### Completion Criteria

The resolver produces a deterministic membership hash and complete canonical
member set for a calculation date.

## Task 14 — Implement decimal normalization and quality evaluation

### Objective

Implement the three locked formulas and per-company metric readiness/reason
classification.

### Design References

- `design.md` §1, §4, §7
- `user-story.md` AC-16–19, AC-22, AC-24

### Scope

Normalize P/E, P/S, and equilibrium using the exact source mappings; reject
missing, non-finite, overflowed, zero, negative, stale, unavailable, and
identity-invalid inputs with distinct reasons.

### Dependencies

Tasks 09 and 13.

### Implementation Notes

Use decimal arithmetic throughout; never use PE_TTM, BoundaryAverage, raw peer
values, or an alternate market-price source.

### Tests Required

Exact formula mapping, decimal precision, missing, zero/negative,
non-finite/overflow, stale/unavailable, and identity mismatch tests.

### Completion Criteria

Every company/metric has a deterministic normalized value or a persisted
quality/readiness reason and no invalid input reaches benchmark calculation.

## Task 15 — Implement R7 quartiles and IQR outlier detection

### Objective

Implement the locked metric-independent benchmark preparation algorithm.

### Design References

- `design.md` §6
- `user-story.md` AC-20–22

### Scope

Sort clean decimal values, calculate R7 Q1/Q3, IQR and inclusive 1.5 bounds,
mark only outside values as outliers, handle zero IQR, and persist algorithm
identifier `IQR-R7-1.5-v1`.

### Dependencies

Task 14.

### Implementation Notes

Do not round during calculation. Outliers are metric-specific and remain in
member results.

### Tests Required

Explicit 2-, 3-, and 4-value R7 samples; zero IQR; exact lower/upper boundary;
outside-bound outlier; all missing; all non-positive; one clean value; and
mixed-quality samples.

### Completion Criteria

Quartiles, bounds, clean/outlier counts, and reasons match the approved
algorithm for every required fixture.

## Task 16 — Implement benchmark publication and classification

### Objective

Publish clean arithmetic means only when valid and classify each company metric.

### Design References

- `design.md` §6–§7
- `user-story.md` AC-21–22, AC-38–39

### Scope

Require at least two clean observations; persist benchmark readiness/reason,
clean average, bounds, counts, Green/Red/Unclassifiable classification, and
outlier/missing/invalid reasons.

### Dependencies

Task 15.

### Implementation Notes

Missing is never Red. Zero/negative is Red for the company but excluded from
the benchmark. A benchmark is independent for each metric.

### Tests Required

Minimum-population, equality Green, no-benchmark Unclassifiable, metric-specific
outlier, missing-vs-invalid, and all-invalid tests.

### Completion Criteria

Metric rows and member metric fields are complete, deterministic, and aligned
with the persisted read contract.

---

# Phase 4 — Ranking Engine

## Task 17 — Implement deterministic rank comparator and Top-N projection

### Objective

Persist a stable global order and apply limits only after complete ranking.

### Design References

- `design.md` §7
- `user-story.md` AC-23–25

### Scope

Implement PositiveMetricCount DESC, PEPercent ASC null-last, PSPercent ASC
null-last, EquilibriumPercent ASC null-last, ValidMetricCount DESC, CompanyId
ASC; handle 0/0, rank eligibility, GlobalRank, total ranked count, Top-N, and
pagination.

### Dependencies

Task 16.

### Implementation Notes

Null ordering is global and transitive. Do not pairwise-skip missing values.

### Tests Required

0/0, 0/3, 1/2, nullable metrics, complete ties, CompanyId tie-break, full
rank-before-limit, default/max limits, rejection above max, and repeated stable
pagination tests.

### Completion Criteria

The same persisted calculation always returns the same ranks and page members.

---

# Phase 5 — Daily Calculation Pipeline

## Task 18 — Build calculation request, Tehran date, and source barrier

### Objective

Capture the immutable input set for one calculation date.

### Design References

- `design.md` §5, §8
- `user-story.md` AC-15, AC-26, AC-29

### Scope

Use the repository Tehran timezone/business-date helper, resolve all canonical
members, select latest freshness-approved source versions for every member and
source kind, and persist barrier hash, membership hash, observations, and
watermarks.

### Dependencies

Tasks 09A, 11, 12A, 13, 17, and 32.

### Implementation Notes

Freshness is evaluated against PersistedAtUtc with configured defaults; the
calculation never reads uncommitted provider rows.

### Tests Required

Tehran/Windows fallback date, stale threshold, barrier completeness, source
version selection, membership hash, unchanged-values-next-day, and uncommitted
row isolation tests.

### Completion Criteria

Every calculation has a reproducible barrier and date before engine execution.

## Task 19 — Implement readiness lifecycle and calculation orchestration

### Objective

Drive Pending, Ready, Published, Inconclusive, and Failed status transitions.

### Design References

- `design.md` §5
- `user-story.md` AC-27–28

### Scope

Persist Pending during assembly, Ready after required rows/barriers exist,
Inconclusive when a required benchmark cannot evaluate, Failed when a
consistent version cannot be produced, and Published only for a complete
validated snapshot.

### Dependencies

Tasks 16 and 18.

### Implementation Notes

Pending/Ready/Failed are not normal AI-visible; Inconclusive is diagnostic/history
only and never a watch day. Company-level missing metrics may remain in an
otherwise valid Published snapshot.

### Tests Required

Independent status transition/visibility tests for all five statuses,
benchmark-insufficient Inconclusive, provider-failure isolation, and mixed-
generation rejection.

### Completion Criteria

Status and visibility behavior is durable, monotonic, and independently tested.

## Task 20 — Implement atomic publication, version selection, and retries

### Objective

Publish a complete calculation and its dependent records atomically and
idempotently.

### Design References

- `design.md` §5, §8
- `user-story.md` AC-30–32, AC-37

### Scope

Atomically write calculation, metric, member/rank, and watch evaluation/outbox
evidence. Leave version selection, current-marker rules, and retry policy to
Task 20A so the transaction boundary can be implemented and tested separately.

### Dependencies

Tasks 11, 17–19.

### Implementation Notes

Provider ingestion commits separately. A failed transaction leaves no current
partial version. Prior Published rows remain auditable.

### Tests Required

Transaction rollback, concurrent publish, and complete-dependent-record
atomicity tests.

### Completion Criteria

A candidate publication transaction is atomic and never selects a partial
version.

## Task 20A — Implement calculation version selection and safe retries

### Objective

Apply deterministic current-version, correction, and retry rules around the
atomic publication transaction.

### Design References

- `design.md` §8–§9
- `user-story.md` AC-31–32, AC-37

### Scope

Implement same-barrier no-op, corrected version creation, lower-readiness
protection, selected-current marker selection, retry/concurrency handling, and
selected-calculation references used by watch evaluation.

### Dependencies

Tasks 11, 19, and 20.

### Tests Required

Same-barrier retry, corrected source, lower-readiness replacement, selected-
current uniqueness, concurrent version selection, and same-date watch
reference tests.

### Completion Criteria

Only a complete valid version can become current; prior published evidence is
preserved and retries cannot create a second watch day.

## Task 21 — Add scheduled daily calculation worker

### Objective

Run the calculation pipeline once per configured business date under the
approved operational contract.

### Design References

- `design.md` §5, §11
- `user-story.md` AC-26, AC-29, AC-41–43

### Scope

Add bounded hosted-worker scheduling, calculation lease key
`industry-relative-valuation:{CalculationDate}`, separate lease ownership,
cancellation/deadline, cadence, correlation ID, persisted activity/status,
and per-company failure isolation.

### Dependencies

Tasks 04, 09A, 20A, and 32.

### Implementation Notes

Reuse existing Worker/DataSync patterns and configured provider policies. Only
one worker may publish a date.

### Tests Required

Lease contention, duplicate worker, cancellation/deadline, cadence validation,
activity counts/status/failure codes, and one-company-failure isolation.

### Completion Criteria

The worker can safely run, stop, retry, and report one date without duplicate
publication or cross-company failure propagation.

---

# Phase 6 — Long-Term Watch State Machine

## Task 22 — Implement watch evaluation predicates and configurable counters

### Objective

Evaluate valid Published snapshots and update pending counters using configured
thresholds.

### Design References

- `design.md` §9
- `user-story.md` AC-33–36, AC-43

### Scope

Implement all-three-benchmarks-valid gating, strict entry/exit predicates,
thresholds 1..30, defaults 3, exact-100 neutral handling, neutral reset, and
Inconclusive pause.

### Dependencies

Task 20A.

### Implementation Notes

Inconclusive is an evaluation outcome, not a durable state. Entry and exit
counters are mutually exclusive.

### Tests Required

Threshold 1, 2, 3, and greater than 3 for entry and exit; exact 100; neutral
reset; Inconclusive pause/continue; insufficient benchmark invalid-day tests.

### Completion Criteria

Predicates and counter changes match the configured values for every valid and
inconclusive case.

## Task 23 — Implement watch state transitions and evidence

### Objective

Persist NotWatching, EntryPending, Watching, and ExitPending transitions with
complete evidence.

### Design References

- `design.md` §8–§9
- `user-story.md` AC-33–37, AC-40

### Scope

Persist current/prior streaks, previous/next state, transition date/reason,
calculation ID, algorithm version, and transition identity.

### Dependencies

Task 22.

### Implementation Notes

A neutral day returns to the applicable stable state and clears both counters.

### Tests Required

Entry/exit first-pending and threshold transitions, mutual exclusion,
transition evidence, neutral return, and audit-history tests.

### Completion Criteria

Every state change and unchanged paused evaluation is durable and explainable.

## Task 24 — Enforce same-date watch evaluation idempotency

### Objective

Prevent retries, concurrency, and corrected same-date versions from advancing
watch streaks twice.

### Design References

- `design.md` §8–§9
- `user-story.md` AC-32, AC-37

### Scope

Use `(IndustryId, CalculationId, EvaluationKind)` evaluation identity and
selected-calculation references for outbox/transition processing.

### Dependencies

Tasks 20A and 23.

### Implementation Notes

Same-date CalculationVersion increase is not automatically a second watch day.

### Tests Required

Repeated, concurrent, same-barrier, and corrected-same-date evaluation tests;
assert one streak increment and one transition at most.

### Completion Criteria

Watch processing is replay-idempotent under all approved retry scenarios.

---

# Phase 7 — Semantic Layer Integration

## Task 25 — Register Feature 118 capabilities and precedence

### Objective

Register the four v1 capabilities with approved slots, aliases, precedence,
and persisted-read route.

### Design References

- `design.md` §10
- `user-story.md` AC-05–07

### Scope

Register symbol-vs-industry, industry ranking, industry summary, and pair
comparison capabilities; wire optional Industry/ResultLimit/Presentation slots
where approved; preserve plain P/S and Feature 115 ownership.

### Dependencies

Tasks 03, 17, and 20A.

### Implementation Notes

Do not expose provider or calculation services to the semantic AI path.

### Tests Required

Registry/version/slot/route tests, precedence tests, alias tests, and
plain-P/S/explicit-gauge regression tests.

### Completion Criteria

All four capabilities resolve deterministically to the persisted read route.

## Task 26 — Integrate Feature 119 entity resolution

### Objective

Resolve canonical company, symbol, industry, pair, mismatch, ambiguity, and
missing/not-found outcomes for Feature 125 intents.

### Design References

- `design.md` §3, §10
- `user-story.md` AC-01–04, AC-07

### Scope

Use provider-scoped industry identity, canonical IDs, symbol-derived industry,
explicit membership validation, and `Resolved`, `Ambiguous`, `NotFound`,
`Missing`, `InvalidIndustryMembership`, and `DifferentIndustries` outcomes.

### Dependencies

Tasks 03, 13, and 25.

### Implementation Notes

Different-industry pairs never fall through to raw comparison.

### Tests Required

Canonical resolution, ambiguous title/provider scope, missing/not-found,
wrong-industry actual membership, same-industry pair, and cross-industry pair.

### Completion Criteria

Every supported intent receives an explicit resolver outcome without invented
symbols or industries.

## Task 27 — Integrate Feature 120 clarification and replay

### Objective

Persist and resume one-turn clarification for unresolved Feature 125 requests.

### Design References

- `design.md` §10
- `user-story.md` AC-07

### Scope

Store pending slot, candidate canonical IDs, optimistic version, original
intent, follow-up resolution, replay idempotency, and task-switch reset.

### Dependencies

Task 26.

### Implementation Notes

Wrong explicit industry asks clarification with actual membership; different
industries ask which symbol’s own industry to analyze.

### Tests Required

One-turn resume, stale optimistic version, duplicate follow-up, candidate
selection, task switch, and replay-idempotency tests.

### Completion Criteria

Clarification resumes the original supported intent exactly once and never
reuses stale pending state.

## Task 28 — Enforce persisted-read executor boundary

### Objective

Implement the semantic executor input contract and reject unauthorized LLM
calculation inputs.

### Design References

- `design.md` §10
- `user-story.md` AC-06, AC-38–39

### Scope

Accept canonical IDs, selected calculation identity, and bounded limit; load
only Published rows for normal financial reads; expose diagnostic/history route
separately.

### Dependencies

Tasks 20A, 25, and 26.

### Implementation Notes

No provider calls, formulas, SQL, ranks, averages, or colors may come from the
LLM or be calculated during the read.

### Tests Required

Read-only query tests, unready-status rejection, provider-call spy, formula/SQL/
rank/average/color rejection, and bounded-limit tests.

### Completion Criteria

The AI route is a persisted snapshot read and cannot perform live financial
calculation.

---

# Phase 8 — Read Models / API Contracts

## Task 29 — Implement industry ranking read model

### Objective

Return the persisted ranked industry list with the approved evidence fields.

### Design References

- `design.md` §10
- `user-story.md` AC-23–25, AC-38–39

### Scope

Expose industry identity, selected calculation/date/version, freshness/status,
benchmark evidence, member rows, counts, GlobalRank, TotalRankedMembers,
quality/outlier reasons, AlgorithmVersion, RankVersion, and bounded pagination.

### Dependencies

Tasks 17, 20A, and 28.

### Implementation Notes

Do not calculate or fetch facts in the read model.

### Tests Required

Published read fields, unclassifiable benchmark, 0/0, Top-N, pagination,
status visibility, and audit evidence tests.

### Completion Criteria

Ranking responses satisfy AC-38/39 from persisted rows only.

## Task 30 — Implement symbol comparison and summary read models

### Objective

Expose symbol-vs-industry, same-industry pair, and industry summary contracts.

### Design References

- `design.md` §10
- `user-story.md` AC-03–07, AC-38–40

### Scope

Return canonical symbol/company, industry, normalized metrics, benchmarks,
classification, counts, ranks, freshness, provenance, and summary evidence.
Define the user-facing presentation projection and serialization for all four
capabilities, including explicit unavailable/outlier wording, source/status
context, bounded result limits, and the no-buy/sell-recommendation boundary.

### Dependencies

Tasks 26, 28, and 29.

### Implementation Notes

Same-industry pairs compare persisted relative results; different industries
return the typed clarification outcome and no comparison.

### Tests Required

Symbol lookup, same-industry pair, cross-industry rejection, summary, missing
metric, outlier explanation fields, diagnostic/history reads, exact response
serialization, and no-recommendation presentation tests.

### Completion Criteria

All four capability outputs map to stable persisted read contracts.

## Task 31 — Add API/application contract tests

### Objective

Verify the public application/API response shapes and normal versus diagnostic
visibility without introducing live calculations.

### Design References

- `design.md` §10
- `user-story.md` AC-06, AC-38–39

### Scope

Add endpoint/use-case integration coverage for ranking, symbol comparison,
summary, pair outcomes, bounded limits, statuses, evidence, and clarification.

### Dependencies

Tasks 27–30.

### Implementation Notes

Use repository integration-test factories and persisted fixtures; do not call
CyclicalWaves from AI request tests.

### Tests Required

Full API/application contract suite with provider-call assertions and normal /
diagnostic visibility assertions.

### Completion Criteria

API/application contracts are deterministic and complete for all supported
capabilities and typed outcomes.

---

# Phase 9 — Operational Concerns

## Task 32 — Add options and startup validation

### Objective

Implement all Feature 125 configuration keys, defaults, ranges, and validation.

### Design References

- `design.md` §11
- `user-story.md` AC-25, AC-33–34, AC-43

### Scope

Add Enabled, cadence 1440..10080, freshness 1..168/default 26, IQR 1.5..5/
default 1.5, default limit 1..100/default 3, max limit 1..1000/default 100,
entry/exit 1..30/default 3, and default<=maximum validation.

### Dependencies

Task 04.

### Implementation Notes

Persist algorithm/rank versions so configuration changes do not rewrite history.

### Tests Required

Boundary, below/above range, default, disabled, and default-limit-greater-than-
maximum startup tests.

### Completion Criteria

Invalid configuration fails startup deterministically and valid configuration
is available to worker/calculation/watch/read components.

## Task 33 — Add telemetry, logging, and activity evidence

### Objective

Expose operational state without leaking raw provider payloads or high-cardinality
identifiers.

### Design References

- `design.md` §4, §5, §11
- `user-story.md` AC-11–12, AC-41–43

### Scope

Persist correlation/activity records, status/count/failure evidence, source
barrier hashes, and bounded/hashed telemetry labels. Integrate existing retry,
timeout, and rate-limit instrumentation.

### Dependencies

Tasks 09, 19, 21, and 32.

### Implementation Notes

Raw payloads are audit-only and never ordinary logs.

### Tests Required

Log redaction, bounded-label, activity persistence, failure-code, and retry /
timeout telemetry tests.

### Completion Criteria

Operators can observe required health/readiness evidence through existing
conventions without raw sensitive/high-cardinality labels.

## Task 34 — Add deployment and operational runbook evidence

### Objective

Document runtime ordering, worker enablement, lease names, status inspection,
failure recovery, and audit/history verification.

### Design References

- `design.md` §5, §8, §11
- `user-story.md` AC-29–32, AC-41–43

### Scope

Prepare implementation-review documentation for provider ingestion before daily
calculation, migration ordering, worker enablement, retry/recovery, and status
diagnostics.

### Dependencies

Tasks 12, 21, and 33.

### Implementation Notes

This task documents operations; it does not enable production execution.

### Tests Required

Review checklist and dry-run evidence for ordering, lease contention, rollback,
and recovery procedures.

### Completion Criteria

Operational reviewers can verify safe deployment and recovery without changing
the approved design.

---

# Phase 10 — Testing Strategy

## Task 35 — Consolidate calculation unit tests

### Objective

Create the first-class unit suite for normalization, quality, R7/IQR,
classification, ranking, and comparators.

### Design References

- `design.md` §6–§7, §12
- `user-story.md` AC-16–25

### Scope

Cover pure calculations and all required edge fixtures independently of EF,
HTTP, worker, or AI dependencies.

### Dependencies

Tasks 14–17.

### Implementation Notes

Use exact decimal expected values and assert algorithm/rank version identifiers.

### Tests Required

All 2/3/4 R7, IQR=0, inclusive bounds, insufficient clean, missing/invalid,
outlier, equality, 0/0, nullable tie, total tie, and stable comparator cases.

### Completion Criteria

Pure engine behavior is fully covered and deterministic.

## Task 36 — Consolidate provider and persistence integration tests

### Objective

Verify provider contracts, source facts, EF persistence, barriers, and atomic
publication together.

### Design References

- `design.md` §4–§5, §8, §12
- `user-story.md` AC-08–15, AC-26–32

### Scope

Use HTTP stubs and database fixtures for all provider outcomes, provenance,
freshness, statuses, constraints, transactions, retries, corrections, and
current selection.

### Dependencies

Tasks 09–12, 09A, 10A, 12A, 18–20, and 20A.

### Tests Required

All provider fixtures, migration-backed persistence, stale/partial generations,
concurrent retry, rollback, same-barrier no-op, corrected version, and
lower-readiness protection.

### Completion Criteria

Provider-to-published-snapshot behavior is verified without live provider calls.

## Task 37 — Consolidate watch and semantic regression tests

### Objective

Verify watch state, semantic routing, clarification, and existing feature
ownership together.

### Design References

- `design.md` §9–§10, §12
- `user-story.md` AC-01–07, AC-33–37, AC-44

### Scope

Test watch thresholds/configuration, pause/idempotency, capabilities,
resolution/clarification, persisted-read-only behavior, plain P/S, Feature 115,
and no-buy/sell language boundary.

### Dependencies

Tasks 24–32 and 35–36.

### Tests Required

Threshold 1/2/3/>3, exact 100, Inconclusive, duplicates, provider-call spy,
Feature 118 registry, Feature 119 outcomes, Feature 120 replay/task switch,
Feature 114 P/S, Feature 115 gauge, and recommendation regression tests.

### Completion Criteria

Feature 125 integrates without regression in Features 114, 115, 118, 119, or
120.

## Task 38 — Run complete acceptance and traceability matrix

### Objective

Prove every AC and design section is covered before implementation review.

### Design References

- `design.md` §1–§13
- `user-story.md` AC-01–44 and Required Deterministic Fixture Coverage

### Scope

Maintain a matrix from AC to task, test, and design section; identify any
uncovered criterion; run the complete unit/integration/regression suite.

### Dependencies

Tasks 35–37.

### Implementation Notes

No uncovered criterion may be waived by inference or by relying on an LLM.

### Tests Required

Full test suite and generated/manual coverage report.

### Completion Criteria

Every AC maps to at least one task and concrete test, every design section maps
to tasks, and all required fixtures pass.

---

# Phase 11 — Migration / Deployment Review

## Task 39 — Migration review and rollback assessment

### Objective

Review the isolated migration artifact and deployment/rollback safety.

### Design References

- `design.md` §8, §11
- `user-story.md` AC-31, AC-40, AC-43

### Scope

Review migration ordering after existing provider/catalog tables, indexes and
constraints, clean/existing database behavior, rollback or forward-recovery
plan, and preservation of historical evidence.

### Dependencies

Tasks 12, 34, and 38.

### Implementation Notes

If no migration is required, record the verified model/schema reason and retain
the same validation evidence.

### Tests Required

Apply/rollback or forward-recovery rehearsal, schema diff, constraint smoke,
and deployment-order verification.

### Completion Criteria

Migration/deployment review is approved independently from feature code review.

## Task 40 — Implementation readiness and handoff gate

### Objective

Confirm the task breakdown, implementation evidence, and later release gates
are ready without marking implementation started.

### Design References

- `design.md` §12–§13
- `user-story.md` Definition of Ready for Task Breakdown

### Scope

Collect task/AC/design matrix, test evidence, migration decision, operational
runbook, open-risk list, and explicit approval for implementation/release
gates.

### Dependencies

Tasks 38–39.

### Implementation Notes

This is a review gate only; it does not implement code, apply migrations, or
update the global implementation ledger.

### Tests Required

Final checklist confirming no provider call on AI path, no raw comparison, no
buy/sell recommendation, no duplicate P/S worker, and all gates recorded.

### Completion Criteria

Implementation review has a complete, traceable handoff and no unapproved
business-rule change.

## Acceptance-Criteria Coverage

| Acceptance criteria | Tasks |
|---|---|
| AC-01–07 | T03, T13, T25–T28, T30–T31, T37 |
| AC-08–15 | T02, T05–T12, T36 |
| AC-16–25 | T14–T17, T35–T36 |
| AC-26–32 | T18–T21, T36 |
| AC-33–37 | T22–T24, T37 |
| AC-38–40 | T10–T12, T20, T28–T31, T36 |
| AC-41–43 | T04, T21, T32–T34, T39 |
| AC-44 | T37–T38 |
| Required fixture coverage | T06–T08, T15, T17, T21–T24, T26–T27, T35–T38 |

## Design-Section Coverage

| Design section | Tasks |
|---|---|
| §1 Scope and invariants | T05, T14, T35 |
| §2 Repository boundaries | T01, T13, T18 |
| §3 Industry universe | T01, T13, T26 |
| §4 Provider-neutral facts | T02, T05–T09, T12, T36 |
| §5 Date/readiness/publication | T04, T18–T21, T29, T36 |
| §6 Benchmark algorithm | T15–T16, T35 |
| §7 Classification/ranking | T14–T17, T29, T35 |
| §8 Snapshot/correction | T09–T12, T20, T23–T24, T36, T39 |
| §9 Watch state | T22–T24, T37 |
| §10 Semantic/read contracts | T03, T25–T31, T37 |
| §11 Operations/configuration | T04, T21, T32–T34, T39 |
| §12 Tests/fixtures | T06–T08, T15, T17, T21–T24, T35–T38 |
| §13 Non-goals | T01–T04, T25, T28, T37–T40 |
