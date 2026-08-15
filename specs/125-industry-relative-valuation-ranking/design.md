# Feature 125 Design — Persisted Snapshot Input Migration

## Status

`DESIGN_REVISION_READY_FOR_REVIEW`

This revision documents an acquisition-path migration only. It does not authorize implementation,
application-file changes, database migrations, or a redesign of Feature 125.

## 1. Architecture decision and migration scope

The sole goal is to replace Feature 125's current CyclicalWaves acquisition path with consumption of
the persisted snapshots and acquisition checks owned by Feature 127.

```text
Before:
Feature 125 calculation input -> Feature 126/direct CyclicalWaves acquisition -> provider

After:
Feature 127 persisted snapshots/checks -> snapshot reader -> in-memory source projection
    -> existing Feature 125 calculator
```

Feature 127 remains the single owner of CyclicalWaves transport, authentication, token caching,
retry and availability handling, response persistence, response hashing, change detection, and
acquisition diagnostics. Feature 125 becomes a persisted-data consumer and must have no provider
call fallback.

This is an architectural migration, not a database migration. No new persistence model or schema
change is required by this specification.

## 2. Locked Feature 125 behavior

Only the acquisition/input source changes. The following existing Feature 125 behavior must remain
unchanged:

- normalization;
- benchmark calculation, including the existing R7/IQR rules;
- ranking and tie-breaking;
- Green/Red and data-quality classification;
- watch entry, exit, streak, and idempotency logic;
- calculation publication, correction, and read behavior.

The existing calculations remain:

```text
PEPercent = CurrentPE / HistoricalAveragePE * 100
PSPercent = CurrentPS / HistoricalAveragePS * 100
EquilibriumPercent = CurrentMarketPrice / EquilibriumPrice * 100
```

The source-field meanings also remain unchanged:

| Metric | Current value | Reference value |
|---|---|---|
| P/E | P/E gauge `close` | P/E gauge `avg` |
| P/S | P/S gauge `close` | P/S gauge `avg` |
| Equilibrium | equilibrium gauge `close` | equilibrium gauge `balance` |

`PE_TTM` is not a replacement for the P/E gauge pair. Feature 114's `BoundaryAverage` is not the
historical P/S baseline. The migration must not substitute either value or alter the established
validation, freshness-expiry, benchmark, rank, classification, watch, or publication outcomes.

## 3. Persisted snapshot consumption

### 3.1 Snapshot reader boundary

A database-backed `CyclicalWavesMetricSnapshotReader` reads the persisted Feature 127 data needed by
the existing source builder. It reads only persisted rows and never calls CyclicalWaves.

For each canonical company and required metric type (`PS`, `PE`, and `Equilibrium`), the reader:

1. selects the applicable persisted `CyclicalWavesMetricSnapshot`;
2. finds the latest successful acquisition evidence linked to that exact snapshot and response;
3. evaluates freshness using that successful evidence and the existing Feature 125 freshness policy;
4. returns the snapshot plus provenance and diagnostic information to the mapper.

The snapshot's `RawResponseJson` remains the value source. The reader and mapper do not copy, mutate,
or add consumer-specific columns to Feature 127 persistence.

### 3.2 Deterministic successful-evidence selection

For a selected snapshot, the latest successful acquisition evidence is selected from acquisition
checks using all of these filters:

```text
CompanyId = requested company
ProviderName = requested provider
MetricType = requested metric type
SnapshotId = selected snapshot Id
ResponseHash = selected snapshot ResponseHash
Result = Changed OR NoChange
```

Matching rows are ordered exactly as follows:

1. `CompletedAtUtc DESC`
2. `CreatedAtUtc DESC`
3. `Id DESC`

The first row is the latest successful acquisition evidence. Its `CompletedAtUtc` is the successful
revalidation time used by the existing freshness policy. The secondary and tertiary keys make the
choice deterministic when completion timestamps are equal.

`NoChange` is successful evidence even though it does not create a duplicate snapshot. Therefore an
unchanged value can be freshly revalidated without changing the immutable value snapshot.

### 3.3 Later failure behavior

A failed acquisition check recorded after a successful snapshot does not immediately invalidate that
snapshot. The failure remains visible diagnostically through acquisition checks, while Feature 125
continues using the last valid snapshot and its latest successful acquisition evidence until the
existing freshness policy expires.

After freshness expires, Feature 125 uses its existing stale/unavailable handling. It must not call
the provider, invent a value, promote a failure to successful evidence, or extend freshness from a
failed check.

### 3.4 Snapshot-to-calculator mapping

The mapper parses the selected snapshot's `RawResponseJson` and creates the existing calculator input
shape in memory:

```text
PS:          CurrentPS = close; HistoricalAveragePS = avg
PE:          CurrentPE = close; HistoricalAveragePE = avg
Equilibrium: CurrentMarketPrice = close; EquilibriumPrice = balance
```

Existing behavior for absent snapshots, malformed JSON, missing properties, unusable decimals,
non-positive operands, identity mismatch, and stale evidence is preserved. None of these conditions
may trigger acquisition.

The projection carries the provenance already required by the existing calculator input contract,
including canonical company identity, provider, metric/source kind, snapshot identity, response hash,
successful acquisition-check identity and timestamps, readiness, and quality reason.

## 4. `IndustryRelativeValuationSourceFacts` transition

`IndustryRelativeValuationSourceFacts` is no longer an acquisition table for Feature 125.

For the first implementation:

- existing rows remain unchanged for historical compatibility;
- the new processing path stops writing new acquisition data to the table;
- compatibility with the existing calculator input may be preserved through an in-memory
  projection/mapper from Feature 127 snapshots;
- no deletion, backfill, row migration, schema migration, or historical-data rewrite is performed.

The table must not be repurposed as a second persisted copy of Feature 127 snapshots or checks. Any
future removal or data migration requires a separate reviewed change.

## 5. Explicit legacy path retirement

The migration detaches the following legacy paths from the active Feature 125 runtime flow:

- `CyclicalWavesRelativeValuationWorker`;
- `IFeature126RelativeValuationPipeline`;
- `ICyclicalWavesRelativeValuationProviderClient`;
- direct provider calls from `IndustryRelativeValuationSourceIngestionService`;
- the Feature 114 P/S data dependency used as Feature 125 input.

Detachment means these paths are no longer scheduled, resolved, invoked, or used to build new
Feature 125 calculation inputs. No deletion is required. Historical tables and existing historical
rows remain unchanged.

Removing Feature 125's dependency on Feature 114 does not redesign or migrate Feature 114 itself.
Feature 125 reads the Feature 127 P/S snapshot directly through the new reader and mapper.

## 6. Runtime naming rule

All new or renamed runtime identifiers must be domain- or provider-oriented. This applies to classes,
interfaces, records, methods, services, workers, options, dependency-injection registrations, and
other production symbols.

The following feature-number patterns are forbidden in runtime identifiers:

```text
Feature125*
Feature126*
Feature127*
```

This prohibition covers any runtime identifier that begins with or embeds those feature-number
labels. Feature numbers may appear in specification prose, traceability notes, and migration history,
but not in production identifiers.

Allowed naming examples include:

- `IndustryRelativeValuationCalculator`;
- `CyclicalWavesMetricSnapshotReader`;
- `CyclicalWavesAcquisitionService`.

Legacy feature-numbered names listed in the retirement section are referenced only to identify the
paths being detached; they are not naming precedents.

## 7. Required implementation boundary

### In scope for the first migration phase

- a persisted snapshot/check reader;
- deterministic mapping into the existing calculator input;
- replacement of the current source builder/input assembly path;
- removal of Feature 125's direct provider and Feature 114 input dependencies;
- parity and architecture tests for the cutover.

### Postponed and out of scope

- calculation or normalization redesign;
- benchmark or ranking changes;
- classification changes;
- watch behavior changes;
- publication/read-model changes;
- semantic routing or clarification changes;
- new persistence models, acquisition tables, or schema migrations;
- deletion or migration of historical tables or rows;
- changes to Feature 127 acquisition behavior;
- changes to Feature 114 visualization behavior.

## 8. Verification strategy

The migration is complete only when tests demonstrate:

- P/E, P/S, and equilibrium inputs produced from persisted snapshots match the inputs produced by
  the retired path for equivalent payloads;
- downstream normalization, benchmarks, ranks, classifications, watch decisions, and publication
  outputs remain unchanged for the same inputs;
- successful-evidence ordering follows `CompletedAtUtc DESC`, `CreatedAtUtc DESC`, `Id DESC` after
  applying every required identity/result filter;
- a later failure is diagnostic only until the last successful evidence becomes stale;
- `NoChange` refreshes successful evidence without requiring a new snapshot;
- the new path writes no acquisition rows to `IndustryRelativeValuationSourceFacts`;
- no active Feature 125 path resolves or calls a CyclicalWaves provider client;
- no Feature 114 P/S row is used as Feature 125 input;
- no new runtime identifier uses a forbidden feature-number pattern.

## 9. Non-goals

This design does not implement code, modify application files, delete legacy code, change historical
data, create a database migration, redesign Feature 125, or expand the public/semantic feature surface.
