# Feature 125 — Persisted Snapshot Input Migration User Story

## Status

`USER_STORY_GROUP_COHORT_CORRECTION_READY_FOR_REVIEW`

This user story defines the migration from direct CyclicalWaves acquisition to Feature 127 persisted
snapshot consumption and the corrected Feature 125 eligibility, group-cohort membership,
SourceBarrier, and publication-readiness rules. It does not authorize code changes or database
migrations.

## Primary user story

As an operator of the industry-relative-valuation pipeline, I want Feature 125 to calculate only for
companies admitted by `NoavaranEligibleCompanies`, using the persisted CyclicalWaves metric snapshots
acquired by Feature 127, and compare a symbol only with eligible companies sharing its `GroupId`, so
that the benchmark represents the symbol's actual industry group rather than the broader
`IndustryId`, available metrics are not rejected by a full-catalog completeness gate, and there
remains one acquisition owner.

## Product outcome

The formulas, metric mappings, IQR algorithm, classification thresholds, and ranking comparator do
not change. Eligibility, calculation membership, benchmark/rank population, calculation identity,
SourceBarrier completeness, publication readiness, and presentation are keyed by `GroupId` and
`GroupTitle`. Existing industry-keyed historical rows remain historical and must not be silently
reinterpreted as group-keyed rows. Persistence, watch, semantic-resolution, and AI read contracts
must be revised where required to carry the group identity explicitly.

## Scope

### In scope

- Read persisted P/E, P/S, and equilibrium snapshots and their acquisition checks.
- Select successful freshness evidence deterministically.
- Map persisted payloads into the existing Feature 125 calculator input in memory.
- Replace the current source builder/input assembly path.
- Start the calculation universe only from `NoavaranEligibleCompanies`; use its `GroupId` as the
  comparison-cohort key and `GroupTitle` as the cohort display title.
- Use `Companies` for canonical company identity, `IndustryGroups` for canonical group validation,
  and `Industries` only for broader classification metadata. `IndustryId` must not group members.
- Include only eligible companies with at least one usable metric in calculation membership.
- Calculate metric benchmarks independently and remove the full-catalog SourceBarrier gate.
- Publish if and only if all three required benchmarks—P/E, P/S, and equilibrium—meet their existing
  minimum-clean-observation rule.
- Detach all direct CyclicalWaves and Feature 114 input dependencies from Feature 125.
- Preserve historical source-fact rows while stopping new acquisition writes to them.
- Prove output parity and the absence of active legacy provider calls.

### Out of scope and postponed

- normalization or calculation redesign;
- benchmark formula, IQR algorithm, minimum-clean-observation threshold, or ranking-comparator
  changes;
- classification changes;
- watch logic changes;
- changes to formulas, thresholds, or calculation meaning beyond the group-cohort correction;
- execution of schema migrations or production data backfills as part of this documentation-only
  amendment; the later implementation review must decide the required additive persistence change;
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

The existing normalization, metric formulas, R7/IQR benchmark algorithm, minimum-clean-observation
rule, ranking comparator, classification, watch logic, publication persistence, correction behavior,
and source-value semantics are acceptance baselines. Eligibility, group-cohort identity,
membership, publication readiness, persistence keys, and read/presentation identity are revised only
as explicitly stated below.

## Authoritative cohort correction

Feature 125 uses the following terms precisely:

- **industry group / comparison cohort:** the eligible rows sharing one non-null `GroupId`;
- **cohort title:** `GroupTitle` for that `GroupId`, validated against the provider-scoped
  `IndustryGroups` dimension when available;
- **industry metadata:** `IndustryId` and `IndustryTitle`, retained for display/audit context only.

No benchmark, rank, membership hash, SourceBarrier, publication pointer, watch state, semantic
same-cohort check, or AI result may combine companies solely because they share `IndustryId`.
`GroupId` must be stored and passed as a group identity; it must not be written into a field named
`IndustryId`, and `GroupTitle` must not be written into a field named `IndustryTitle` or
`IndustryTitleSnapshot`.

## Acceptance criteria

### AC-01 — Persisted-only snapshot reader

Given a canonical company and a required metric type (`PS`, `PE`, or `Equilibrium`), when Feature 125
assembles calculator input, then it reads the applicable Feature 127 persisted snapshot and
acquisition checks and does not call CyclicalWaves.

The applicable snapshot is the first row after exact company/provider/metric filtering and ordering
by `AcquisitionDateUtc DESC`, `CreatedAtUtc DESC`, and `Id DESC`. An unusable selected snapshot does
not cause fallback to an older snapshot.

There is no HTTP/provider fallback when a snapshot is absent, malformed, unusable, mismatched, or
stale. Existing Feature 125 missing/quality behavior applies.

Feature 127 snapshots and acquisition checks are immutable input facts for Feature 125. Feature 125
must not update, delete, replace, revalidate, backfill, or append to them. It may derive its own
percentages and benchmarks, but it may not recalculate, infer, repair, or substitute missing provider
input fields or initiate acquisition.

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

The current runtime already satisfies this detachment. AC-06 is verification-only for the bounded
implementation: it does not authorize edits to worker registration, dependency-injection
composition, orchestration, Feature 126/127 acquisition, or legacy components. If the precondition
fails, implementation stops for a separate boundary review.

### AC-07 — Authoritative eligibility and group cohort

Given a calculation cycle, when Feature 125 determines the companies it may evaluate, then it starts
only from `NoavaranEligibleCompanies`.

For a symbol comparison, Feature 125 takes the symbol's non-null `GroupId` from the eligible row and
forms the candidate cohort only from eligible rows with exactly the same `GroupId`. `GroupTitle` is
the cohort display title. `Companies` remains authoritative for canonical `CompanyId` and symbol
metadata; `IndustryGroups` validates the provider-scoped group identity/title; `Industries` supplies
broader classification metadata only. None of those canonical tables may add a company absent from
`NoavaranEligibleCompanies`.

For this phase, the eligible view row is joined to its canonical `Companies` row by the same `Id` and
provider. Its `GroupId` must match one provider-scoped `IndustryGroups.Id`. A missing company match,
null group, missing/non-unique group match, or conflicting `GroupTitle` is excluded diagnostically;
there is no fallback to `IndustryId`, title matching, or the full company catalog.

For every group, cohort size is the count of admitted calculation members for that cycle. It is
never the count of rows related by `IndustryId` or a count from the full `Companies` table. A group
with no admitted calculation member produces no Feature 125 calculation input or calculation row.

Eligible rows are deduplicated by resolved canonical `CompanyId`. An unresolved or ambiguous eligible
symbol is excluded with diagnostics and is never replaced by a company drawn from `Companies`.
Group cohort size is therefore the count of distinct admitted canonical company IDs sharing the
same `GroupId`.

For `شگل`, the expected candidate cohort is the 10 eligible rows with
`GroupId = 97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c` and
`GroupTitle = تولید محصولات آرایشی و بهداشتی`: `پاکشو`, `ساینا`, `شپارس`, `شپاکسا`, `شتولی`,
`شکف`, `شگل`, `شوینده`, `قرن`, and `کیمیاتک`.

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

### AC-10 — Controlled scope for the group correction

The earlier four-file production allowlist is superseded because it cannot implement a truthful
group-keyed calculation/read identity. A reviewed implementation plan must include every directly
affected group-key seam: input assembly, domain calculation identity, persistence/mapping/migration,
publication selection, watch keys, semantic same-cohort resolution, read repository/contracts, and
presentation. It must use explicit `GroupId`/`GroupTitle` names and preserve historical
industry-keyed records without reinterpretation.

No formula, IQR algorithm, minimum-clean-observation threshold, ranking comparator, classification
rule, source-value mapping, Feature 127 acquisition behavior, or Feature 114 visualization behavior
may change. This amendment authorizes documentation changes only; code, tests, migrations, and data
changes require a separate implementation review.

### AC-11 — Calculation membership

Given an eligible company that resolves to canonical identity and a non-null canonical group, when
persisted P/E, P/S, and equilibrium inputs are mapped and validated, then the company is a
calculation member of that `GroupId` cohort if at least one of those metrics is usable.

An eligible company with no usable valuation metric is not a calculation member. It does not increase
the calculation's group cohort size, produce an `InsufficientData` membership row, or make the group
incomplete or `Inconclusive`.

A metric is usable if and only if its deterministically selected Feature 127 snapshot and successful
evidence (`Changed` or `NoChange`) match canonical company, `CyclicalWaves` provider, metric,
`SnapshotId`, and `ResponseHash`; the successful evidence's `CompletedAtUtc` is not future-dated and
is within the existing freshness window; the required raw fields parse directly as decimals (`close`/`avg` for P/E and P/S,
`close`/`balance` for equilibrium); both operands are strictly positive; and normalization completes
without decimal overflow. Failure of any condition makes only that company/metric unusable. No
fallback, repair, coercion, substitution, or acquisition is allowed. Later IQR outlier exclusion does
not revoke calculation membership.

### AC-12 — Metric-specific benchmark membership

Given the admitted calculation members sharing one `GroupId`, when benchmarks are calculated, then each
metric uses its own usable observations independently:

- P/E uses members with usable P/E;
- P/S uses members with usable P/S;
- equilibrium uses members with usable equilibrium data.

A member is not required to have all three metrics. The existing minimum-clean-observation rule and
R7/IQR algorithm apply separately to each metric population.

### AC-13 — SourceBarrier is provenance, not full-catalog completeness

Given selected snapshots for admitted calculation members, when SourceBarrier evidence is recorded,
then it describes the snapshots that participated. It is not checked against full canonical
`Companies`/`IndustryId` membership, other groups in the same industry, or `member count × three
metric types`.

Selected source count is `SourceBarrier.Selections.Count`; any equivalent `SelectedSourceCount` field
records that value. `RequiredSelectionCount` or equivalent `ExpectedSourceCount` is retained only for
contract compatibility and is set to the same selected/admitted value, not a pre-selection coverage
target. `IsComplete` describes only successful deterministic materialization of selected provenance.
The builder must not enumerate missing company/metric combinations to determine completeness. None
of these fields may reinstate full-catalog or all-three-metrics completeness or reject otherwise
calculable benchmarks.

Every input passed to the existing publication writer has `IsComplete = true` by construction. A
failure to materialize deterministic provenance is an input-assembly operational failure and emits no
calculation row; it is not an `Inconclusive` coverage result.

### AC-14 — Publication readiness

Given a group cohort with excluded eligible companies or partially covered calculation members, the
calculation is `Published` if and only if each of the three required benchmarks—P/E, P/S, and
equilibrium—has enough clean observations under the existing minimum rule.

The group calculation is `Inconclusive` only when a required benchmark cannot be produced, including when its
metric-specific population has fewer than the existing minimum number of clean observations. Missing
data for non-members or missing metrics on partially covered members is not independently a reason
for `Inconclusive`.

This readiness is supplied to the existing calculation snapshot/publication writer through corrected
membership, benchmark populations, and provenance-only SourceBarrier semantics. The publication
writer itself is not changed.

### AC-15 — Authoritative membership example

Given eligible companies A, B, C, D, and E with the same canonical `GroupId`, and usable P/E, P/S,
and equilibrium data only for A, B, and C, then the calculation membership and group cohort size are three.
D and E create no result rows and do not make SourceBarrier incomplete. If every required benchmark
meets the existing minimum-clean-observation rule, the calculation status is `Published`.

An otherwise eligible company F with the same `IndustryId` but a different `GroupId` is never a
candidate, never contributes a benchmark observation, and never affects rank, SourceBarrier, or
publication status for A through E.

### AC-16 — Calculation parity for admitted inputs

Given equivalent persisted payloads for the same admitted metric observations, when the legacy and
new calculation paths are compared, then they produce the same normalized values, benchmark values,
ranks, classifications, watch decisions, persistence fields, and AI read output within the same
`GroupId` cohort, except where the old `IndustryId` cohort or full-catalog completeness gate conflicts
with AC-07 and AC-11 through AC-15.

No locked formula, algorithm, threshold, comparator, or state transition may change to make parity
pass.

## Required parity and architecture test coverage

The implementation plan must include tests for:

- valid P/E, P/S, and equilibrium snapshot mappings;
- `avg` versus Feature 114 `BoundaryAverage` and P/E gauge values versus `PE_TTM`;
- absent snapshots, malformed JSON, missing fields, unusable decimals, identity mismatch, and stale
  successful evidence using existing quality outcomes;
- every condition in the explicit usable-metric definition, including future evidence, non-positive
  operands, and decimal overflow;
- read-only verification that Feature 125 neither mutates Feature 127 facts nor invokes acquisition,
  repair, backfill, or alternate input substitution;
- successful-check identity filters and the exact three-key ordering;
- `Changed` and `NoChange` successful evidence;
- a later failure before and after freshness expiry;
- no new writes to `IndustryRelativeValuationSourceFacts`;
- no active legacy worker, pipeline, provider client, direct provider call, or Feature 114 input path;
- eligible-universe intersection tests proving canonical tables cannot expand membership;
- group-cohort tests proving same `GroupId` rows are included and same `IndustryId`/different
  `GroupId` rows are excluded;
- cohort-size assertions proving it equals admitted calculation membership and never an
  `IndustryId` or full-catalog `Companies` count;
- exclusion of eligible companies with zero usable metrics and absence of result rows for them;
- partial-coverage fixtures proving independent P/E, P/S, and equilibrium benchmark populations;
- SourceBarrier fixtures proving no full-catalog or member-count-times-three gate remains;
- `Published` and `Inconclusive` cases driven only by required metric benchmark availability;
- identical downstream outputs for equivalent admitted metric observations, subject to the corrected
  eligibility/readiness rules;
- rejection of feature-numbered runtime identifiers introduced by the migration.
- persistence/read tests proving `GroupId` and `GroupTitle` are explicit and historical
  industry-keyed rows are not reinterpreted;
- architecture/diff review proving formulas, ranking comparator, watch decision logic, and source
  mappings remain unchanged while their identity keys move to the group cohort.

## Definition of done

The specifications are implementation-ready when every task maps to these acceptance criteria, the
group identity/persistence impact is explicitly reviewed, and all unrelated formula or provider
redesign work is absent from the active task plan.
