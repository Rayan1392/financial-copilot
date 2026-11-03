# Feature 125 — Persisted Snapshot Input Migration Tasks

## Status

`TASKS_GROUP_COHORT_CORRECTION_READY_FOR_REVIEW`

This task plan covers the migration from direct CyclicalWaves acquisition to Feature 127 persisted
snapshot consumption and the corrected Feature 125 eligibility, `GroupId`-based cohort membership,
SourceBarrier, and publication-readiness rules. It does not authorize implementation or database
migrations.

## Implementation constraints

- Preserve the existing Feature 125 formulas, normalization, R7/IQR algorithm,
  minimum-clean-observation rule, classification, ranking comparator, and watch decision logic.
- Treat `NoavaranEligibleCompanies` as the only Feature 125 eligibility universe.
- Use exact `GroupId` equality as the comparison-cohort boundary and `GroupTitle` as its display
  title. `IndustryId`/`IndustryTitle` are broader metadata only.
- Use `Companies` for canonical company identity and `IndustryGroups` for provider-scoped group
  validation; never use canonical tables to expand eligibility.
- Do not reinstate full-catalog or member-count-times-three SourceBarrier completeness.
- Supersede the earlier four-file allowlist. Do not begin implementation until an impact review
  identifies the minimum files required to carry explicit group identity through domain input,
  persistence/migration, publication selection, watch identity, semantic resolution, reads, and
  presentation.
- Do not modify the existing `IndustryRelativeValuationEngine`, normalization/formula code, R7/IQR
  implementation, ranking comparator, or classification logic. Publication/watch/read code may
  change only where required to replace the cohort key/title.
- Do not put `GroupId` into an `IndustryId` field or `GroupTitle` into an industry-title field.
- Preserve historical industry-keyed rows and distinguish them from new group-keyed calculations.
- Do not add a new semantic capability code; correct the cohort resolved by the existing capability.
- Do not delete legacy code or historical data as part of this phase.
- Do not write new acquisition data to `IndustryRelativeValuationSourceFacts`.
- Do not call CyclicalWaves or use Feature 114 P/S data from the Feature 125 input path.
- Do not introduce runtime identifiers that begin with or embed `Feature125`, `Feature126`, or
  `Feature127`.

Allowed naming examples are `IndustryRelativeValuationCalculator`,
`CyclicalWavesMetricSnapshotReader`, and `CyclicalWavesAcquisitionService`.

### Implementation authorization gate

This document records required work but does not authorize production changes. The implementation
review must produce an explicit file allowlist and decide the additive persistence/migration shape
for `GroupId`/`GroupTitle`. Feature 114 visualization and Feature 127 acquisition remain protected.

## Phase 1 dependency order

```text
T00 Group identity and persistence impact review
  -> T01 Snapshot reader
  -> T02 Mapping
    -> T03 Replace source builder and establish GroupId cohort membership
      -> T04 Apply metric-specific readiness and SourceBarrier semantics
        -> T05 Remove direct provider dependency
          -> T06 Parity and architecture tests
```

Tasks may be delivered together, but their acceptance boundaries must remain independently
verifiable.

# Phase 1 — Persisted Snapshot Input Migration

## Task 00 — Approve the group identity contract

### Objective

Define the minimum truthful contract for replacing `IndustryId`/`IndustryTitle` as Feature 125's
calculation cohort with `GroupId`/`GroupTitle` before any implementation begins.

### Scope

- Inventory every cohort-key seam: calculation input/result, membership hash, persisted calculation
  and member rows, uniqueness/current-selection indexes, correction lineage, watch state/evaluation,
  semantic same-cohort resolution, read request/model/repository, and Persian/English presentation.
- Specify explicit group-named fields and an additive migration if persistence requires them.
- Preserve and version historical industry-keyed rows; do not reinterpret or overwrite them.
- Confirm that the existing capability code remains stable while its resolved cohort becomes a
  group.
- Produce the reviewed production-file allowlist and rollback/backfill plan.

### Acceptance criteria

- `user-story.md` authoritative cohort correction and AC-07/AC-10.

### Completion criteria

Architecture and migration reviewers approve an explicit group identity design. Until then, Tasks
01–06 remain unauthorized for implementation.

## Task 01 — Add the persisted snapshot reader

### Objective

Provide a database-backed read boundary for the persisted CyclicalWaves metric snapshots and
acquisition checks owned by Feature 127.

### Scope

- Read the applicable persisted snapshot for each canonically resolved eligible candidate and
  requested metric type:
  `PS`, `PE`, and `Equilibrium`.
- Filter snapshots by exact company, `CyclicalWaves` provider, and metric, then select the first by
  `AcquisitionDateUtc DESC`, `CreatedAtUtc DESC`, and `Id DESC`; do not fall back to an older snapshot
  when the selected row is unusable.
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
- Treat snapshots and acquisition checks as immutable input facts: use read-only/no-tracking access
  and do not update, delete, replace, revalidate, backfill, or append to them.

### Dependencies

None.

### Acceptance criteria

- `user-story.md` AC-01, AC-03, AC-04, and AC-08.

### Tests required

- Exact filter matching for every identity and result field.
- Snapshot-selection tie cases for `AcquisitionDateUtc`, `CreatedAtUtc`, and `Id`, plus no fallback
  from an unusable selected snapshot.
- Tie cases for each of the three ordering keys.
- `Changed` and `NoChange` evidence selection.
- Later failure while the last success is fresh and after it becomes stale.
- A provider-client spy proving the reader does not make an HTTP/provider call.
- Change-tracking and persistence assertions proving no Feature 127 snapshot or check is mutated and
  no acquisition/check row is created.

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
- Define a usable metric exactly as a deterministically selected snapshot/evidence pair that matches
  canonical company, `CyclicalWaves` provider, metric, `SnapshotId`, and `ResponseHash`; has successful
  `Changed`/`NoChange` evidence whose `CompletedAtUtc` is not future-dated and is within the existing
  freshness window;
  contains directly parseable decimal `close`/`avg` fields for P/E or P/S, or `close`/`balance` fields
  for equilibrium; has both operands strictly greater than zero; and normalizes without decimal
  overflow.
- Mark the metric unusable when any condition fails; do not repair, coerce, infer, substitute, or
  acquire an input value. IQR outlier status occurs later and does not change input usability.
- Use an in-memory projection/mapper; do not create a new persistence model or acquisition copy.

### Dependencies

Task 01.

### Acceptance criteria

- `user-story.md` AC-02, AC-08, AC-10, and AC-16.

### Tests required

- Valid P/E, P/S, and equilibrium payload fixtures.
- P/S `avg` versus Feature 114 `BoundaryAverage` regression fixture.
- P/E gauge values versus `PE_TTM` regression fixture.
- Existing missing, malformed, invalid, identity-mismatch, and stale quality fixtures.
- Future-dated evidence, non-positive operands, decimal-overflow, and every remaining usable-metric
  condition.
- Assertions proving the mapper never repairs or substitutes provider values.
- Provenance field mapping assertions.

### Completion criteria

Equivalent persisted payloads produce calculator inputs equivalent to the legacy source path, with no
formula or quality-rule changes.

## Task 03 — Replace the source builder and establish group-cohort membership

### Objective

Make the Feature 125 calculation pipeline start from the authoritative eligible universe, resolve
canonical company and group identity, and admit only companies with usable persisted valuation data
into the exact same-`GroupId` cohort.

### Scope

- Start only from `NoavaranEligibleCompanies`.
- Resolve each eligible row through the existing identity-preserving join to `Companies` by the same
  `Id` and provider. Take `GroupId`/`GroupTitle` from the eligible projection and validate the group
  through provider-scoped `IndustryGroups`; do not enumerate the full `Companies` table to add
  candidates.
- Group candidates exclusively by non-null `GroupId`. Retain `IndustryId`/`IndustryTitle` as
  metadata and never use them to merge different groups.
- Do not introduce a symbol resolver, matching precedence, linkage table, fallback identity path,
  or title-based group fallback. Exclude a missing/non-unique company, null group,
  missing/non-unique group, or conflicting group title diagnostically.
- Deduplicate eligible rows by resolved canonical `CompanyId`; exclude unresolved or ambiguous
  eligible symbols with diagnostics and never replace them from the canonical catalog.
- Replace the current acquisition-backed source builder/input assembly with the snapshot reader and
  in-memory mapper for each eligible, canonically resolved company.
- Admit a company to calculation membership when at least one mapped P/E, P/S, or equilibrium metric
  is usable.
- Exclude an eligible company with zero usable valuation metrics from calculation membership and
  group cohort size, and do not create an `InsufficientData` result row for it.
- Define group cohort size and every equivalent member-count value as the distinct admitted
  canonical `CompanyId` count after this admission step; never query or count a full
  `IndustryId`/`Companies` roster for that value.
- Produce no calculation input or calculation row for a group with zero admitted members.
- Retain usable metrics for partially covered members without requiring all three metrics.
- Preserve the existing calculator contract; normalization and all downstream processing remain
  unchanged.
- Stop writing new acquisition data to `IndustryRelativeValuationSourceFacts`.
- Leave all existing `IndustryRelativeValuationSourceFacts` rows unchanged for historical
  compatibility.
- Do not delete, backfill, migrate, or repurpose the table.
- Hand off explicit `GroupId`/`GroupTitle` to the reviewed persistence contract from Task 00; never
  alias them into industry-named fields.

### Dependencies

Tasks 01 and 02.

### Acceptance criteria

- `user-story.md` AC-05, AC-07, AC-10, and AC-11.

### Tests required

- Input assembly integration tests using persisted snapshot/check fixtures.
- An eligible-universe intersection test proving canonical company/group data cannot expand the
  Feature 125 universe.
- A same-industry/different-group fixture proving exact `GroupId` isolation.
- The `شگل` fixture proving the candidate cohort contains only `پاکشو`, `ساینا`, `شپارس`, `شپاکسا`,
  `شتولی`, `شکف`, `شگل`, `شوینده`, `قرن`, and `کیمیاتک` before usable-metric admission.
- Fixtures for one, two, three, and zero usable metrics, proving that zero-metric companies are
  excluded while partial members are retained.
- An assertion that excluded zero-metric companies produce no calculation/result row and do not
  increase group cohort size.
- An assertion that canonical `Companies` and same-`IndustryId` counts cannot affect group cohort
  size, including extra eligible companies with another `GroupId`.
- A zero-admitted-member group fixture proving no calculation is emitted.
- An assertion that the new path writes no `IndustryRelativeValuationSourceFacts` acquisition row.
- Regression coverage proving existing historical rows remain readable where current compatibility
  behavior requires them.
- A schema/model diff proving any additive group identity fields match the Task 00 design and do not
  reinterpret or destroy historical industry-keyed rows.

### Completion criteria

The calculator receives only admitted eligible-company inputs from the same `GroupId` and persisted
snapshots; canonical tables do not expand the universe; zero-metric companies produce no membership
rows; no new source-fact acquisition rows are written; and historical data remains untouched.

## Task 04 — Apply metric-specific benchmarks and publication readiness

### Objective

Remove the full-catalog SourceBarrier gate and determine benchmark readiness independently from the
usable observations available for each metric.

### Scope

- Build the P/E benchmark population from calculation members with usable P/E only.
- Build the P/S benchmark population from calculation members with usable P/S only.
- Build the equilibrium benchmark population from calculation members with usable equilibrium data
  only.
- Apply the existing minimum-clean-observation rule and R7/IQR algorithm independently to each
  metric population.
- Keep SourceBarrier as provenance for snapshots that actually participate in the calculation.
- Set selected source count to `SourceBarrier.Selections.Count`; any equivalent
  `SelectedSourceCount` field records that value.
- Retain `RequiredSelectionCount` or equivalent `ExpectedSourceCount` only for contract compatibility
  and set it to the same selected/admitted count; never derive it from full canonical industry
  membership, other groups in the same industry, or `member count × three`.
- Make `IsComplete` describe only successful deterministic materialization of selected provenance,
  not catalog or per-member metric coverage; do not iterate missing company/metric combinations when
  determining it.
- Pass only `IsComplete = true` inputs to the existing publication writer. Treat provenance
  materialization failure as an input-assembly operational failure that emits no calculation row,
  never as an `Inconclusive` coverage result.
- Do not use SourceBarrier count equality as an all-or-nothing gate for otherwise calculable
  benchmarks.
- Treat P/E, P/S, and equilibrium as the three required benchmarks. Supply the existing publication
  writer with an input that is `Published` if and only if all three can be produced under the existing
  minimum rule, even when eligible non-members have no data or members have partial metric coverage.
- Ensure every benchmark, rank, membership hash, SourceBarrier, publication pointer, and watch
  identity is scoped to one `GroupId`.
- Use `Inconclusive` only when a required benchmark cannot be produced, including insufficient clean
  observations for that metric.
- Preserve formulas, minimum thresholds, IQR mechanics, classification, ranking comparator, and
  watch decision logic. Modify persistence/watch/read identity only as approved by Task 00.

### Dependencies

Task 03.

### Acceptance criteria

- `user-story.md` AC-12, AC-13, AC-14, AC-15, and AC-16.

### Tests required

- Independent metric populations, including a 3/3/2 P/E, P/S, and equilibrium coverage fixture.
- The A-through-E example from AC-15, proving D and E neither create result rows nor block
  publication.
- A SourceBarrier regression proving full canonical membership and `member count × three` are not
  completeness gates.
- A regression proving another `GroupId` under the same `IndustryId` contributes no source,
  benchmark, rank, or readiness state.
- Assertions that `Selections.Count`, `RequiredSelectionCount`/`ExpectedSourceCount`, and `IsComplete`
  have only the provenance semantics defined above.
- A partial-member fixture proving a missing metric does not discard that company's other usable
  observations.
- `Published` cases where each required benchmark meets the existing minimum independently.
- `Inconclusive` cases where a required metric population falls below the existing minimum.
- Regression coverage for unchanged formulas, R7/IQR, classification, ranking comparator, watch,
  persistence, correction, and AI read behavior.
- An architecture/diff assertion that the existing calculation engine, ranking comparator, watch
  evaluation, publication writer, and AI read files are unchanged.

### Completion criteria

Each benchmark uses its own clean metric observations, SourceBarrier is provenance rather than a
full-catalog gate, and publication status depends on required benchmark producibility.

## Task 05 — Verify the direct provider and legacy acquisition path remain detached

### Objective

Verify the active Feature 125 runtime has exactly one input path: persisted Feature 127 snapshots.
This is an architecture-verification task and makes no production-code change.

### Scope

Verify the following remain detached from Feature 125 scheduling, dependency resolution, invocation,
and input construction:

- `CyclicalWavesRelativeValuationWorker`;
- `IFeature126RelativeValuationPipeline`;
- `ICyclicalWavesRelativeValuationProviderClient`;
- direct provider calls from `IndustryRelativeValuationSourceIngestionService`;
- the Feature 114 P/S data dependency used for Feature 125 input.

No deletion is required. Historical tables and rows remain unchanged. Do not redesign Feature 114 or
change Feature 127 acquisition behavior.

The current worker registration and dependency-injection composition are preconditions. Do not edit
them or any legacy/acquisition/orchestration component under this task. If any precondition assertion
fails, stop and request a separate implementation-boundary review.

Review every new or renamed runtime symbol and reject identifiers beginning with or embedding
`Feature125`, `Feature126`, or `Feature127`. The legacy names above remain only as detachment targets.

### Dependencies

Task 04.

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

## Task 06 — Prove cutover and corrected-membership behavior

### Objective

Demonstrate persisted-input parity for admitted observations and prove the corrected eligibility,
membership, SourceBarrier, and publication-readiness behavior.

### Scope

- Run equivalent legacy-path and persisted-snapshot-path fixtures for valid P/E, P/S, and equilibrium
  payloads.
- Compare calculator inputs and downstream results.
- Cover successful freshness evidence, `NoChange`, later failures, freshness expiry, invalid/missing
  payloads, and identity mismatch.
- Verify architecture constraints: no provider call, no Feature 114 input, no new source-fact write,
  no unreviewed schema change, and no forbidden runtime identifier.
- Verify the authoritative eligible-universe intersection, zero-metric exclusion, partial metric
  coverage, exact `GroupId` isolation, metric-specific benchmark populations, and benchmark-driven
  publication status.
- Treat any formula, IQR, minimum-threshold, rank-comparator, classification, or watch-decision
  difference as a regression. Reviewed differences required to replace industry identity with group
  identity, plus removal of the superseded full-catalog gate, are expected.
- Verify by file/diff boundary that production changes match the Task 00 allowlist and do not change
  unrelated providers, formulas, or features.

### Dependencies

Tasks 01–05.

### Acceptance criteria

- All criteria in `user-story.md`, with primary focus on AC-07 and AC-11 through AC-16.

### Tests required

- Golden/master or equivalent deterministic parity fixtures comparing calculator inputs.
- Existing normalization and validation regression suite.
- Existing benchmark and R7/IQR regression suite.
- Existing ranking and classification regression suite.
- Existing watch state/idempotency regression suite.
- Existing publication/correction/read regression suite.
- Authoritative eligibility and canonical-resolution integration fixtures.
- Metric-specific membership, SourceBarrier, and publication-readiness fixtures from Task 04.
- Architecture tests for retired dependencies, writes, schema, and naming.

### Completion criteria

All parity, corrected group-membership, publication-readiness, persistence/read identity, and
architecture tests pass without changing the locked Feature 125 formulas.

## Postponed work — not part of Phase 1

No task is created in this specification for:

- calculation-formula or normalization redesign beyond the specified membership correction;
- benchmark formula, IQR, minimum-clean-observation, or ranking-comparator changes;
- classification changes;
- watch decision-rule changes beyond replacing the cohort key;
- publication/correction or AI response-semantic changes beyond replacing the cohort key/title;
- new semantic capability codes or unrelated clarification behavior;
- unrelated persistence models or migrations beyond the additive group identity approved in T00;
- deletion/migration of `IndustryRelativeValuationSourceFacts` or any historical table;
- Feature 114 visualization migration;
- Feature 127 acquisition changes.

Any of these requires a separate reviewed specification and task plan.

## Traceability

| Migration requirement | Tasks |
|---|---|
| Explicit group identity/persistence impact and allowlist | T00, T06 |
| Persisted snapshot/check reader | T01 |
| Exact freshness evidence filters and ordering | T01, T06 |
| Later failure behavior | T01, T06 |
| P/E, P/S, equilibrium mapping | T02, T06 |
| Authoritative eligible universe and canonical company/group resolution | T03, T06 |
| Same-`GroupId` cohort and same-industry/different-group exclusion | T03, T04, T06 |
| Zero-metric exclusion and partial-member admission | T03, T06 |
| Metric-specific benchmark membership | T04, T06 |
| SourceBarrier provenance without full-catalog completeness | T04, T06 |
| Benchmark-driven `Published`/`Inconclusive` status | T04, T06 |
| Replace source builder | T03, T06 |
| `IndustryRelativeValuationSourceFacts` transition | T03, T06 |
| Retire legacy paths and Feature 114 input dependency | T05, T06 |
| Preserve locked formulas and decision logic while correcting identity contracts | T00, T02–T04, T06 |
| Runtime naming prohibition | T05, T06 |
| No unrelated persistence or semantic scope | T00, T03–T06 |
