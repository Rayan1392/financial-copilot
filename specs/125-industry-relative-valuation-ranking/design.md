# Feature 125 Design — Persisted Snapshot Input Migration

## Status

`DESIGN_GROUP_COHORT_CORRECTION_READY_FOR_REVIEW`

This revision documents the persisted-input migration and the authoritative Feature 125 eligibility,
group-cohort membership, SourceBarrier, and publication-readiness rules. It does not authorize
implementation, application-file changes, or database migrations.

## 1. Architecture decision and migration scope

The input-path goal is to replace Feature 125's current CyclicalWaves acquisition path with
consumption of the persisted snapshots and acquisition checks owned by Feature 127. This revision
also corrects how the calculation universe, comparison cohort, and source completeness are derived.

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

The persisted-input migration alone did not require a schema change. The later group-cohort
correction does require a reviewed impact assessment for calculation/publication/watch/read identity.
Existing industry-keyed historical rows must remain historical; `GroupId` must not be hidden inside
an `IndustryId` field. This document changes requirements only and does not authorize a migration.

## 2. Locked Feature 125 behavior

Apart from the eligibility, group-cohort identity, calculation-membership, SourceBarrier, and publication-readiness
corrections in Section 4, the following Feature 125 behavior must remain unchanged:

- normalization;
- benchmark calculation, including the existing R7/IQR rules;
- ranking and tie-breaking;
- Green/Red and data-quality classification;
- watch entry, exit, streak, and idempotency decision logic, while its identity key moves from the
  broad industry to the explicit group cohort;
- publication/correction behavior and AI presentation semantics, while their identity contracts are
  revised to carry `GroupId` and `GroupTitle` explicitly.

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

For each canonically resolved eligible candidate and requested metric type (`PS`, `PE`, and
`Equilibrium`), the reader:

1. selects the applicable persisted `CyclicalWavesMetricSnapshot`;
2. finds the latest successful acquisition evidence linked to that exact snapshot and response;
3. evaluates freshness using that successful evidence and the existing Feature 125 freshness policy;
4. returns the snapshot plus provenance and diagnostic information to the mapper.

The snapshot's `RawResponseJson` remains the value source. The reader and mapper do not copy, mutate,
or add consumer-specific columns to Feature 127 persistence.

Feature 127 snapshot rows and their linked acquisition-check rows are immutable input facts from the
Feature 125 perspective. Feature 125 reads them with no tracking and must not update, replace, delete,
revalidate, backfill, or append to either Feature 127 data set.

The applicable snapshot is selected after filtering by exact `CompanyId`, provider
`CyclicalWaves`, and requested metric type. Matching snapshots are ordered by
`AcquisitionDateUtc DESC`, then `CreatedAtUtc DESC`, then `Id DESC`; the first row is selected. This
preserves the established snapshot-reader behavior and does not authorize a new acquisition or a
fallback to an older snapshot when the selected row is unusable.

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

Feature 125 may calculate its own percentages, industry benchmarks, classifications, and ranks from
usable persisted facts. It must not recalculate, infer, repair, or substitute the provider input
fields (`close`, `avg`, or `balance`), and it must not obtain a missing value from Feature 114,
Feature 126, another provider table, an HTTP request, a backfill, or a newly initiated acquisition.

The projection carries the provenance already required by the existing calculator input contract,
including canonical company identity, provider, metric/source kind, snapshot identity, response hash,
successful acquisition-check identity and timestamps, readiness, and quality reason.

## 4. Authoritative eligibility and calculation membership

### 4.1 Authority split and cohort key

`NoavaranEligibleCompanies` is the authoritative universe of companies eligible to participate in a
Feature 125 calculation cycle. Feature 125 starts from this set and must not independently expand the
calculation universe from the full `Companies` table.

The comparison cohort is the set of eligible rows with the same non-null `GroupId`.
`GroupTitle` is the displayed cohort title. `Companies` remains authoritative for canonical company
identity and symbol/display metadata; `IndustryGroups` validates the provider-scoped group key and
title; `Industries` supplies broader classification metadata only. `IndustryId` and `IndustryTitle`
must not group Feature 125 members or determine benchmarks and ranks.

For this phase, canonical resolution is the existing identity-preserving join from the
`NoavaranEligibleCompanies` view row to `Companies` by the same `Id` and provider, plus an exact
provider-scoped `GroupId` validation through `IndustryGroups`. The view already supplies `GroupId`
and `GroupTitle`. A missing/non-unique company, null group, missing/non-unique group, or conflicting
group title is excluded diagnostically. There is no fallback to `IndustryId`, display-title matching,
or the full company catalog.

### 4.2 Cycle membership

For each calculation cycle, input assembly follows this order:

1. read symbols from `NoavaranEligibleCompanies`;
2. map each eligible symbol to canonical `CompanyId` plus non-null `GroupId`/`GroupTitle`; validate
   the group through `IndustryGroups` and retain `IndustryId`/`IndustryTitle` only as metadata;
3. read persisted `PS`, `PE`, and `Equilibrium` snapshots from
   `CyclicalWavesMetricSnapshots` through `CyclicalWavesMetricSnapshotReader`;
4. map and validate the available metric inputs using the explicit usable-metric definition in
   Section 4.3;
5. admit a company to the Feature 125 calculation when at least one valuation metric is usable;
6. group only admitted calculation members with the same canonical `GroupId`.

An eligible company with no usable P/E, P/S, or equilibrium metric is not a calculation member. It
does not contribute an `InsufficientData` membership row, increase the calculation's group cohort
size, or make the group incomplete or `Inconclusive`.

Eligible rows are deduplicated by resolved canonical `CompanyId`. An unresolved or ambiguous eligible
symbol is excluded with diagnostics and must not be replaced by another company. For each group,
`GroupSize` and every equivalent member-count value are exactly the number of distinct admitted
canonical `CompanyId` values sharing that `GroupId` in the cycle. No `IndustryId` roster or full
`Companies` navigation may supply that size. If a group has no admitted calculation member, Feature
125 creates no calculation input or calculation row for that group.

An admitted company may have only a subset of the three metrics. Missing metrics remain absent for
that company and do not remove its usable metrics from their benchmark populations.

### 4.3 Explicit usable-metric definition

A metric is usable for calculation membership if and only if one deterministically selected Feature
127 snapshot/evidence pair satisfies every condition below at `CalculatedAtUtc`:

1. the company is already admitted by `NoavaranEligibleCompanies` and resolves to the same canonical
   `CompanyId` used by the snapshot;
2. provider is exactly `CyclicalWaves`, and metric type is exactly `PE`, `PS`, or `Equilibrium`;
3. the snapshot is linked to successful acquisition evidence for the same company, provider, metric,
   `SnapshotId`, and `ResponseHash`, with result `Changed` or `NoChange`;
4. the selected successful evidence's `CompletedAtUtc` is not in the future and is inside the
   existing Feature 125 freshness window;
5. `RawResponseJson` is a JSON object and both required fields parse directly as `decimal` values:
   `close` plus `avg` for P/E and P/S, or `close` plus `balance` for equilibrium;
6. both parsed operands are strictly greater than zero; and
7. the existing percentage normalization completes without decimal overflow.

If any condition fails, that company/metric is unusable and contributes neither a selected source nor
a benchmark observation. No fallback, repair, coercion, or alternate data source is permitted.
IQR outlier status is evaluated later and does not make the input metric unusable for calculation
membership; it only applies the existing benchmark-exclusion behavior.

### 4.4 Metric-specific benchmark membership

Benchmark membership is evaluated independently for each metric:

- the P/E benchmark uses admitted companies with usable P/E observations;
- the P/S benchmark uses admitted companies with usable P/S observations;
- the equilibrium benchmark uses admitted companies with usable equilibrium observations.

No rule requires every calculation member to have all three metrics. The existing minimum-clean-
observation rule and existing R7/IQR algorithm are applied separately to each metric population.

### 4.5 SourceBarrier semantics

The SourceBarrier records provenance/evidence for the snapshots that actually participate in the
calculation. It is not an all-or-nothing completeness gate against either:

- full canonical `Companies` industry membership; or
- companies in other `GroupId` cohorts that share the same `IndustryId`; or
- `member count × three metric types`.

Consequently, selected source count means `SourceBarrier.Selections.Count`. Any existing equivalent
field named `SelectedSourceCount` records that same value. `RequiredSelectionCount` (or an equivalent
field named `ExpectedSourceCount`) is retained only for contract compatibility and is set to that same
selected/admitted count; it is not a pre-selection coverage target. No count may be calculated from
the full canonical industry roster, another group, or require three sources per member.

`SourceBarrier.IsComplete` means only that the deterministic provenance set for the already selected
usable facts was materialized successfully. It must not represent catalog coverage or per-member
metric coverage. The barrier builder must not iterate missing company/metric combinations when
determining completeness, and count equality must not reject otherwise calculable benchmarks.

Every calculation input passed to the existing publication writer has `IsComplete = true` by
construction. If the selected provenance set cannot be materialized deterministically, input assembly
fails/skips that input and reports an operational error; it must not write an `Inconclusive`
calculation based on company/metric coverage.

### 4.6 Publication readiness

P/E, P/S, and equilibrium are all required group benchmarks. A successfully assembled group
calculation is `Published` if and only if each of those three benchmarks can be produced from its own
available clean observations. Missing data for a non-participating eligible company, or missing
metrics for a partially covered calculation member, does not independently make the group
`Inconclusive`.

`Inconclusive` is used when a required benchmark itself cannot be produced, including when fewer
than the existing required minimum number of clean observations remain for that metric. Publication
readiness is therefore derived entirely from the existing benchmark outputs. The existing
publication decision rule remains unchanged. The group identity must, however, flow explicitly
through calculation persistence, current-selection uniqueness, correction lineage, watch identity,
read contracts, and presentation. Historical industry-keyed rows must not be reinterpreted.

### 4.7 Membership example

If `NoavaranEligibleCompanies` contains A, B, C, D, and E in Group X, and only A, B, and C have
usable P/E, P/S, and equilibrium data, then the calculation membership and group size are three.
D and E are not calculation members, do not produce `InsufficientData` rows, and do not make the
SourceBarrier incomplete. If all required benchmarks satisfy the existing minimum-clean-observation
rule, the calculation status is `Published`.

For the concrete `شگل` case, Group X is
`97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c` / `تولید محصولات آرایشی و بهداشتی`, whose 10 eligible
symbols are `پاکشو`, `ساینا`, `شپارس`, `شپاکسا`, `شتولی`, `شکف`, `شگل`, `شوینده`, `قرن`, and
`کیمیاتک`. Other `محصولات شیمیایی` symbols with a different `GroupId` are outside this cohort.

## 5. `IndustryRelativeValuationSourceFacts` transition

`IndustryRelativeValuationSourceFacts` is no longer an acquisition table for Feature 125.

For the first implementation:

- existing rows remain unchanged for historical compatibility;
- the new processing path stops writing new acquisition data to the table;
- compatibility with the existing calculator input is preserved through an in-memory
  projection/mapper from Feature 127 snapshots;
- no deletion, backfill, row migration, schema migration, or historical-data rewrite is performed.

The table must not be repurposed as a second persisted copy of Feature 127 snapshots or checks. Any
future removal or data migration requires a separate reviewed change.

## 6. Explicit legacy path retirement

The migration detaches the following legacy paths from the active Feature 125 runtime flow:

- `CyclicalWavesRelativeValuationWorker`;
- `IFeature126RelativeValuationPipeline`;
- `ICyclicalWavesRelativeValuationProviderClient`;
- direct provider calls from `IndustryRelativeValuationSourceIngestionService`;
- the Feature 114 P/S data dependency used as Feature 125 input.

Detachment means these paths are no longer scheduled, resolved, invoked, or used to build new
Feature 125 calculation inputs. No deletion is required. Historical tables and existing historical
rows remain unchanged.

For the bounded implementation reviewed here, this detachment is a verified runtime precondition,
not an additional production-code work item. The current worker registration and dependency-
injection composition already satisfy it. If that precondition is found false during implementation,
work stops for a separate boundary review; this specification does not authorize changes to worker
registration, dependency-injection composition, orchestration, Feature 126/127 acquisition, or the
legacy components themselves.

Removing Feature 125's dependency on Feature 114 does not redesign or migrate Feature 114 itself.
Feature 125 reads the Feature 127 P/S snapshot directly through the new reader and mapper.

## 7. Runtime naming rule

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

## 8. Required implementation boundary

### In scope for the first migration phase

- a persisted snapshot/check reader;
- deterministic mapping into the existing calculator input;
- replacement of the current source builder/input assembly path using the authoritative eligible
  universe, exact `GroupId` cohorts, and metric-specific calculation membership;
- correction of SourceBarrier construction so it cannot apply the full-catalog or
  member-count-times-metric-count gate;
- preparation of existing publication-writer inputs based on whether all three required metric
  benchmarks can be produced;
- explicit propagation of `GroupId`/`GroupTitle` through calculation identity, persistence,
  publication selection, watch identity, semantic same-cohort validation, read contracts, and
  presentation;
- removal of Feature 125's direct provider and Feature 114 input dependencies;
- parity and architecture tests for the cutover.

### Postponed and out of scope

- calculation or normalization redesign;
- benchmark formula, IQR algorithm, minimum-clean-observation threshold, or ranking changes;
- classification changes;
- changes to watch decision rules beyond replacing the cohort identity key;
- changes to publication/correction semantics beyond replacing the cohort identity key;
- new semantic capability codes; the existing comparison capability remains but resolves a group;
- new acquisition tables or acquisition-schema changes;
- deletion or migration of historical tables or rows;
- changes to Feature 127 acquisition behavior;
- changes to Feature 114 visualization behavior.

The earlier four-file production-code allowlist is explicitly superseded. It is insufficient because
the current domain, persistence, publication, watch, semantic, and read models identify a calculation
by industry. Before implementation, an impact review must enumerate the minimum affected files and
approve an additive group identity contract/migration. The implementation must not repurpose
industry-named fields to hold group values.

The calculator's formulas, R7/IQR implementation, classification logic, and ranking comparator stay
unchanged. Feature 127 acquisition and Feature 114 visualization also stay unchanged. Changes are
limited to replacing the cohort identity and membership boundary wherever the Feature 125 pipeline
currently assumes `IndustryId`/`IndustryTitle`.

## 9. Verification strategy

The migration is complete only when tests demonstrate:

- P/E, P/S, and equilibrium inputs produced from persisted snapshots match the inputs produced by
  the retired path for equivalent payloads;
- `NoavaranEligibleCompanies` is the only source of Feature 125 eligibility, while `Companies` and
  `IndustryGroups` validate identity metadata without expanding that set;
- a symbol comparison includes only eligible rows with the same `GroupId`, and explicitly excludes
  eligible rows with the same `IndustryId` but a different `GroupId`;
- eligible companies with no usable valuation metric are excluded from calculation membership and
  do not produce `InsufficientData` rows;
- each benchmark uses only its metric-specific clean observations and retains the existing minimum
  and R7/IQR rules;
- the explicit seven-condition usable-metric definition is applied before calculation membership;
- Feature 127 snapshots/checks are read-only immutable inputs and no missing provider field is
  recalculated, repaired, acquired, or substituted;
- SourceBarrier provenance contains the selected participating snapshots and is not validated against
  full canonical membership, other groups, or `member count × three`;
- a group publishes when its required benchmarks are producible, despite excluded companies or
  partial per-company metric coverage;
- group identity is explicit in calculation persistence, current-selection uniqueness, watch state,
  semantic resolution, read models, and presentation;
- historical industry-keyed calculations remain distinguishable and are never treated as group rows;
- the existing formulas, ranking comparator, watch decision logic, and source mappings are unchanged;
- downstream normalization, ranks, classifications, watch decisions, persistence, and AI reads remain
  unchanged for the same admitted metric inputs;
- successful-evidence ordering follows `CompletedAtUtc DESC`, `CreatedAtUtc DESC`, `Id DESC` after
  applying every required identity/result filter;
- a later failure is diagnostic only until the last successful evidence becomes stale;
- `NoChange` refreshes successful evidence without requiring a new snapshot;
- the new path writes no acquisition rows to `IndustryRelativeValuationSourceFacts`;
- no active Feature 125 path resolves or calls a CyclicalWaves provider client;
- no Feature 114 P/S row is used as Feature 125 input;
- no new runtime identifier uses a forbidden feature-number pattern.

## 10. Non-goals

This design does not implement code, modify application files, delete legacy code, change historical
data, execute a database migration, alter the locked formulas/IQR/classification/ranking/watch
decision logic, or add a new public semantic capability. A later reviewed implementation may require
an additive migration and contract changes solely to represent `GroupId`/`GroupTitle` truthfully.
