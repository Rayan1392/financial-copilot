# Feature 125 — Persisted Snapshot Input Migration User Story

## Status

`USER_STORY_REVISION_READY_FOR_REVIEW`

This user story defines only the migration from direct CyclicalWaves acquisition to Feature 127
persisted snapshot consumption. It does not authorize code changes or database migrations.

## Primary user story

As an operator of the industry-relative-valuation pipeline, I want Feature 125 to consume the
persisted CyclicalWaves metric snapshots acquired by Feature 127, so that there is one acquisition
owner while users receive the same Feature 125 calculations, rankings, classifications, watch
decisions, and published results as before.

## Product outcome

The user-visible Feature 125 behavior does not change. The same valid provider payload must produce
the same normalized P/E, P/S, and equilibrium inputs and the same downstream result. The only change
is how Feature 125 obtains those inputs.

## Scope

### In scope

- Read persisted P/E, P/S, and equilibrium snapshots and their acquisition checks.
- Select successful freshness evidence deterministically.
- Map persisted payloads into the existing Feature 125 calculator input in memory.
- Replace the current source builder/input assembly path.
- Detach all direct CyclicalWaves and Feature 114 input dependencies from Feature 125.
- Preserve historical source-fact rows while stopping new acquisition writes to them.
- Prove output parity and the absence of active legacy provider calls.

### Out of scope and postponed

- normalization or calculation redesign;
- benchmark or ranking changes;
- classification changes;
- watch logic changes;
- publication or read-model changes;
- semantic routing or clarification changes;
- new persistence models or database migrations;
- deletion, backfill, or migration of historical rows;
- changes to Feature 127 acquisition behavior;
- changes to Feature 114 visualization behavior.

## Locked behavior

The migration preserves the current formulas and field meanings:

| Metric | Persisted payload mapping | Existing formula |
|---|---|---|
| P/E | `close` and `avg` | `CurrentPE / HistoricalAveragePE * 100` |
| P/S | `close` and `avg` | `CurrentPS / HistoricalAveragePS * 100` |
| Equilibrium | `close` and `balance` | `CurrentMarketPrice / EquilibriumPrice * 100` |

All existing validation and data-quality behavior remains unchanged. `PE_TTM` is not used in place
of the P/E gauge values. Feature 114's `BoundaryAverage` is not used in place of the P/S gauge `avg`.

The existing normalization, benchmark calculation, ranking, classification, watch logic, and
publication behavior are acceptance baselines, not work items in this migration.

## Acceptance criteria

### AC-01 — Persisted-only snapshot reader

Given a canonical company and a required metric type (`PS`, `PE`, or `Equilibrium`), when Feature 125
assembles calculator input, then it reads the applicable Feature 127 persisted snapshot and
acquisition checks and does not call CyclicalWaves.

There is no HTTP/provider fallback when a snapshot is absent, malformed, unusable, mismatched, or
stale. Existing Feature 125 missing/quality behavior applies.

### AC-02 — Deterministic mapping

Given a valid persisted snapshot, when it is mapped, then:

- P/E maps `close` to `CurrentPE` and `avg` to `HistoricalAveragePE`;
- P/S maps `close` to `CurrentPS` and `avg` to `HistoricalAveragePS`;
- equilibrium maps `close` to `CurrentMarketPrice` and `balance` to
  `EquilibriumPrice`.

The mapped input retains the snapshot, response-hash, successful-check, timestamp, provider, metric,
and canonical-company provenance required by the existing calculator contract.

### AC-03 — Deterministic successful freshness evidence

Given a selected snapshot, when its latest successful acquisition evidence is resolved, then checks
are filtered by all of the following:

```text
CompanyId
ProviderName
MetricType
SnapshotId
ResponseHash
Result = Changed OR NoChange
```

The filtered rows are ordered exactly by:

1. `CompletedAtUtc DESC`;
2. `CreatedAtUtc DESC`;
3. `Id DESC`.

The first row is used, and its `CompletedAtUtc` is the successful revalidation time evaluated by the
existing Feature 125 freshness policy. A `NoChange` result is valid successful evidence.

### AC-04 — Failure after success

Given a valid snapshot with successful acquisition evidence, when a later acquisition attempt fails,
then the failure is exposed diagnostically through acquisition checks but does not immediately
invalidate the snapshot. Feature 125 continues to use the last valid snapshot until the existing
freshness policy expires.

A failed check cannot refresh or extend freshness. Once the successful evidence is stale, existing
Feature 125 stale/unavailable behavior applies without a provider call or invented value.

### AC-05 — Source-fact transition

Given existing `IndustryRelativeValuationSourceFacts` rows, when the new processing path runs, then:

- existing rows remain unchanged for historical compatibility;
- no new acquisition data is written to the table;
- existing calculator input compatibility may be supplied by an in-memory projection/mapper;
- no table deletion, row migration, backfill, or schema migration occurs.

The table is not treated as an acquisition table or as a second copy of Feature 127 persistence.

### AC-06 — Legacy path retirement

When the new source path is active, the following are detached from Feature 125 scheduling,
dependency resolution, invocation, and input construction:

- `CyclicalWavesRelativeValuationWorker`;
- `IFeature126RelativeValuationPipeline`;
- `ICyclicalWavesRelativeValuationProviderClient`;
- direct provider calls from `IndustryRelativeValuationSourceIngestionService`;
- the Feature 114 P/S data dependency for Feature 125 input.

No deletion is required, and historical tables and rows remain unchanged. Feature 114 itself is not
redesigned by this migration.

### AC-07 — Calculation and publication parity

Given equivalent provider payloads and equivalent canonical membership, when the old and new input
paths are evaluated in parity tests, then the new path produces the same calculator inputs and the
same downstream normalization, benchmarks, ranks, classifications, watch decisions, and publication
outputs.

No locked formula, algorithm, threshold, ordering, state transition, or publication rule may change
to make parity pass.

### AC-08 — Acquisition ownership

Given any Feature 125 runtime execution, then Feature 127 remains the only owner of CyclicalWaves
transport, authentication, token caching, retries, availability handling, response persistence,
hashing, change detection, acquisition checks, and acquisition scheduling.

Feature 125 reads persisted evidence without mutating it or adding consumer-specific fields to the
Feature 127 tables.

### AC-09 — Runtime naming

Given a new or renamed runtime identifier introduced by this migration, then it does not begin with
or embed any of these feature-number patterns:

```text
Feature125*
Feature126*
Feature127*
```

The rule applies to all production identifiers, including classes, interfaces, records, methods,
services, workers, options, and dependency-injection registrations. Domain/provider names such as
`IndustryRelativeValuationCalculator`, `CyclicalWavesMetricSnapshotReader`, and
`CyclicalWavesAcquisitionService` are allowed.

The legacy names in AC-06 are mentioned only as detachment targets.

### AC-10 — No scope expansion

Given implementation of this migration, then no new persistence model, semantic route, calculation
algorithm, ranking behavior, classification rule, watch behavior, publication behavior, or Feature
114 visualization behavior is introduced or changed.

## Required parity and architecture test coverage

The implementation plan must include tests for:

- valid P/E, P/S, and equilibrium snapshot mappings;
- `avg` versus Feature 114 `BoundaryAverage` and P/E gauge values versus `PE_TTM`;
- absent snapshots, malformed JSON, missing fields, unusable decimals, identity mismatch, and stale
  successful evidence using existing quality outcomes;
- successful-check identity filters and the exact three-key ordering;
- `Changed` and `NoChange` successful evidence;
- a later failure before and after freshness expiry;
- no new writes to `IndustryRelativeValuationSourceFacts`;
- no active legacy worker, pipeline, provider client, direct provider call, or Feature 114 input path;
- identical calculator and downstream outputs for equivalent old-path and snapshot-path fixtures;
- rejection of feature-numbered runtime identifiers introduced by the migration.

## Definition of done

The specifications are implementation-ready when every task maps to these acceptance criteria, the
implementation scope contains only the five migration slices, and all postponed redesign work is
absent from the active task plan.
