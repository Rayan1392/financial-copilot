# Feature 125 — Persisted Snapshot Input Migration Tasks

## Status

`TASKS_REVISION_READY_FOR_REVIEW`

This task plan covers only the migration from direct CyclicalWaves acquisition to Feature 127
persisted snapshot consumption. It does not authorize implementation or database migrations.

## Implementation constraints

- Preserve the existing Feature 125 normalization, benchmark, ranking, classification, watch, and
  publication behavior.
- Do not add persistence models, tables, columns, migrations, semantic routes, or new calculation
  behavior.
- Do not delete legacy code or historical data as part of this phase.
- Do not write new acquisition data to `IndustryRelativeValuationSourceFacts`.
- Do not call CyclicalWaves or use Feature 114 P/S data from the Feature 125 input path.
- Do not introduce runtime identifiers that begin with or embed `Feature125`, `Feature126`, or
  `Feature127`.

Allowed naming examples are `IndustryRelativeValuationCalculator`,
`CyclicalWavesMetricSnapshotReader`, and `CyclicalWavesAcquisitionService`.

## Phase 1 dependency order

```text
T01 Snapshot reader
  -> T02 Mapping
    -> T03 Replace source builder
      -> T04 Remove direct provider dependency
        -> T05 Parity and architecture tests
```

Tasks may be delivered together, but their acceptance boundaries must remain independently
verifiable.

# Phase 1 — Persisted Snapshot Input Migration

## Task 01 — Add the persisted snapshot reader

### Objective

Provide a database-backed read boundary for the persisted CyclicalWaves metric snapshots and
acquisition checks owned by Feature 127.

### Scope

- Read the applicable persisted snapshot for each canonical company and required metric type:
  `PS`, `PE`, and `Equilibrium`.
- Resolve the latest successful acquisition evidence using all required filters:
  `CompanyId`, `ProviderName`, `MetricType`, `SnapshotId`, `ResponseHash`, and
  `Result = Changed OR NoChange`.
- Apply the exact evidence ordering:
  `CompletedAtUtc DESC`, then `CreatedAtUtc DESC`, then `Id DESC`.
- Return snapshot and successful-check provenance needed by the existing input contract.
- Use the successful check's `CompletedAtUtc` with the existing freshness policy.
- Keep later failures available as diagnostics without allowing them to refresh freshness or
  immediately invalidate a still-fresh successful snapshot.
- Read persisted data only; provide no provider-call fallback.

### Dependencies

None.

### Acceptance criteria

- `user-story.md` AC-01, AC-03, AC-04, and AC-08.

### Tests required

- Exact filter matching for every identity and result field.
- Tie cases for each of the three ordering keys.
- `Changed` and `NoChange` evidence selection.
- Later failure while the last success is fresh and after it becomes stale.
- A provider-client spy proving the reader does not make an HTTP/provider call.

### Completion criteria

The reader returns deterministic persisted snapshot/evidence results and preserves the established
freshness and stale/unavailable behavior.

## Task 02 — Map persisted snapshots into existing calculator input

### Objective

Project persisted snapshot payloads into the calculator input shape currently consumed by Feature
125 without changing calculator semantics.

### Scope

- Map P/E `close`/`avg` to `CurrentPE`/`HistoricalAveragePE`.
- Map P/S `close`/`avg` to `CurrentPS`/`HistoricalAveragePS`.
- Map equilibrium `close`/`balance` to `CurrentMarketPrice`/`EquilibriumPrice`.
- Carry canonical identity, provider, metric/source kind, snapshot ID, response hash,
  successful-check identity/timestamps, readiness, and quality reason.
- Preserve existing outcomes for absent snapshots, malformed JSON, missing fields, unusable decimal
  values, non-positive operands, identity mismatch, and stale evidence.
- Use an in-memory projection/mapper; do not create a new persistence model or acquisition copy.

### Dependencies

Task 01.

### Acceptance criteria

- `user-story.md` AC-02, AC-07, AC-08, and AC-10.

### Tests required

- Valid P/E, P/S, and equilibrium payload fixtures.
- P/S `avg` versus Feature 114 `BoundaryAverage` regression fixture.
- P/E gauge values versus `PE_TTM` regression fixture.
- Existing missing, malformed, invalid, identity-mismatch, and stale quality fixtures.
- Provenance field mapping assertions.

### Completion criteria

Equivalent persisted payloads produce calculator inputs equivalent to the legacy source path, with no
formula or quality-rule changes.

## Task 03 — Replace the Feature 125 source builder

### Objective

Make the existing Feature 125 calculation pipeline obtain its input from the persisted snapshot
reader and mapper.

### Scope

- Replace the current acquisition-backed source builder/input assembly with the snapshot reader and
  in-memory mapper.
- Preserve the existing calculator contract where practical so normalization and all downstream
  processing remain unchanged.
- Stop writing new acquisition data to `IndustryRelativeValuationSourceFacts`.
- Leave all existing `IndustryRelativeValuationSourceFacts` rows unchanged for historical
  compatibility.
- Do not delete, backfill, migrate, or repurpose the table.
- Do not add a new persistence model or schema migration.

### Dependencies

Tasks 01 and 02.

### Acceptance criteria

- `user-story.md` AC-05, AC-07, and AC-10.

### Tests required

- Input assembly integration tests using persisted snapshot/check fixtures.
- An assertion that the new path writes no `IndustryRelativeValuationSourceFacts` acquisition row.
- Regression coverage proving existing historical rows remain readable where current compatibility
  behavior requires them.
- A schema/model diff showing that no new persistence model or migration is introduced.

### Completion criteria

The calculator receives in-memory projected snapshot inputs, no new source-fact acquisition rows are
written, and historical data remains untouched.

## Task 04 — Detach the direct provider and legacy acquisition path

### Objective

Ensure the active Feature 125 runtime has exactly one input path: persisted Feature 127 snapshots.

### Scope

Detach the following from Feature 125 scheduling, dependency resolution, invocation, and input
construction:

- `CyclicalWavesRelativeValuationWorker`;
- `IFeature126RelativeValuationPipeline`;
- `ICyclicalWavesRelativeValuationProviderClient`;
- direct provider calls from `IndustryRelativeValuationSourceIngestionService`;
- the Feature 114 P/S data dependency used for Feature 125 input.

No deletion is required. Historical tables and rows remain unchanged. Do not redesign Feature 114 or
change Feature 127 acquisition behavior.

Review every new or renamed runtime symbol and reject identifiers beginning with or embedding
`Feature125`, `Feature126`, or `Feature127`. The legacy names above remain only as detachment targets.

### Dependencies

Task 03.

### Acceptance criteria

- `user-story.md` AC-06, AC-08, AC-09, and AC-10.

### Tests required

- Dependency-injection and worker-registration tests proving the legacy path is inactive.
- Call-path/provider-client spies proving no direct CyclicalWaves call is reachable from Feature 125.
- A regression assertion proving Feature 114 P/S data is not used as Feature 125 input.
- An architecture/naming test covering all runtime identifiers introduced or renamed by the
  migration.

### Completion criteria

No active Feature 125 execution resolves, schedules, or invokes a legacy acquisition component,
direct provider call, or Feature 114 input dependency.

## Task 05 — Prove cutover parity

### Objective

Demonstrate that changing the acquisition path does not change Feature 125 behavior or output.

### Scope

- Run equivalent legacy-path and persisted-snapshot-path fixtures for valid P/E, P/S, and equilibrium
  payloads.
- Compare calculator inputs and downstream results.
- Cover successful freshness evidence, `NoChange`, later failures, freshness expiry, invalid/missing
  payloads, and identity mismatch.
- Verify architecture constraints: no provider call, no Feature 114 input, no new source-fact write,
  no schema change, and no forbidden runtime identifier.
- Treat any normalization, benchmark, rank, classification, watch, or publication difference as a
  migration regression, not as an opportunity to revise the algorithm.

### Dependencies

Tasks 01–04.

### Acceptance criteria

- All criteria in `user-story.md`, with primary focus on AC-07 and AC-10.

### Tests required

- Golden/master or equivalent deterministic parity fixtures comparing calculator inputs.
- Existing normalization and validation regression suite.
- Existing benchmark and R7/IQR regression suite.
- Existing ranking and classification regression suite.
- Existing watch state/idempotency regression suite.
- Existing publication/correction/read regression suite.
- Architecture tests for retired dependencies, writes, schema, and naming.

### Completion criteria

All parity and architecture tests pass without changing established Feature 125 calculations or
downstream behavior.

## Postponed work — not part of Phase 1

No task is created in this specification for:

- calculation or normalization redesign;
- benchmark or ranking changes;
- classification changes;
- watch changes;
- publication or read-model changes;
- semantic routing or clarification changes;
- new persistence models or migrations;
- deletion/migration of `IndustryRelativeValuationSourceFacts` or any historical table;
- Feature 114 visualization migration;
- Feature 127 acquisition changes.

Any of these requires a separate reviewed specification and task plan.

## Traceability

| Migration requirement | Tasks |
|---|---|
| Persisted snapshot/check reader | T01 |
| Exact freshness evidence filters and ordering | T01, T05 |
| Later failure behavior | T01, T05 |
| P/E, P/S, equilibrium mapping | T02, T05 |
| Replace source builder | T03, T05 |
| `IndustryRelativeValuationSourceFacts` transition | T03, T05 |
| Retire legacy paths and Feature 114 input dependency | T04, T05 |
| Preserve calculations and downstream behavior | T02, T03, T05 |
| Runtime naming prohibition | T04, T05 |
| No new persistence or semantic scope | T03–T05 |
