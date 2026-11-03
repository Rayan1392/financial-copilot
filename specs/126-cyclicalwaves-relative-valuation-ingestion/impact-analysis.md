# Feature 126 — CyclicalWaves Relative Valuation Ingestion: Impact Analysis

## Conclusion

The proposal is feasible without changing Feature 125’s calculation or semantic layers. The main
change is ownership: today the P/E/equilibrium source ingestion and Feature 125 orchestration are
invoked as a post-step of `NadpcoScheduledSyncCoordinator`, while P/S is fetched by a separate
Feature 114 worker. Feature 126 should introduce one Worker-owned CyclicalWaves pipeline that
fetches and persists P/S, P/E, and equilibrium facts, then invokes the existing Feature 125
calculation boundary.

The existing P/S ingestion should be refactored for reuse, not duplicated or deleted wholesale.
The pipeline must not fetch P/S by reading a snapshot produced by another scheduler, because that
would preserve an ordering dependency and would not guarantee a single daily run for all eligible
companies.

## 1. Current architecture impact

### Existing flow

The relevant production paths are currently split:

```text
NadpcoScheduledSyncWorker
    -> NadpcoScheduledSyncCoordinator
        -> NADPCO catalog/incremental synchronization
        -> IndustryRelativeValuationOrchestrationService
            -> IndustryRelativeValuationSourceIngestionService
                -> CyclicalWaves P/E + equilibrium
                -> reads the latest persisted Feature 114 P/S snapshot
            -> CalculationInputBuilder
            -> CalculationSnapshotWriter

CyclicalWavesPsVisualizationSyncWorker
    -> CyclicalWavesPsVisualizationSyncService
        -> CyclicalWaves P/S provider
        -> CompanyPs* visualization tables
```

The current design therefore has three material problems for the proposal:

1. Feature 125 source ingestion is coupled to the NADPCO scheduled workflow. A disabled, delayed,
   failed, or manually triggered NADPCO run affects relative-valuation freshness.
2. P/S is owned by a different worker and scope. The Feature 125 source service currently projects
   P/S from `CompanyPsGaugeSnapshots` after that other worker has run.
3. Operational configuration is split. `IndustryRelativeValuation.Enabled` controls the feature,
   `IndustryRelativeValuation:SourceIngestion:Enabled` controls source ingestion, and
   `CyclicalWavesPsSync:Enabled` controls the existing P/S worker. This makes “enabled” ambiguous.

Feature 126 should add a dedicated bounded hosted worker and coordinator. The worker should use one
configuration section with one enable/disable switch and a daily cadence. It should acquire a
dedicated distributed lease, use the canonical NADPCO company catalog as its eligible-company
universe, isolate failures per company, and run the three provider fetches before starting the
existing calculation/publication boundary.

The pipeline must remain off the AI request path. Feature 125 read services continue to consume
published snapshots only.

### Architectural boundary to preserve

Feature 125’s existing `IndustryRelativeValuationCalculationInputBuilder`,
`IndustryRelativeValuationCalculationSnapshotWriter`, domain engine, persisted read repository,
capability executors, entity resolution, and semantic registration should remain unchanged. The
refactor changes how fresh source facts arrive; it does not change formulas, benchmark rules,
publication rules, ranking, watch state, or semantic routes.

## 2. Classes and services affected

### Add or introduce

- `CyclicalWavesRelativeValuationWorker` — daily configuration-gated hosted worker.
- A small pipeline/coordinator service, for example
  `ICyclicalWavesRelativeValuationPipeline` and
  `CyclicalWavesRelativeValuationPipeline`, owning one run boundary, correlation id, deadline,
  retry policy, and orchestration order.
- A dedicated options type with one `Enabled` property, daily cadence, company/run limits,
  concurrency, timeout, and lease settings.

The worker should be registered in `FinancialCopilot.Worker/Program.cs` beside the other hosted
workers. It should not be registered as an API controller action and should have no manual trigger
endpoint.

### Refactor and reuse

- `IndustryRelativeValuationSourceIngestionService`: extract its provider-fact persistence and
  per-company processing into a reusable pipeline-facing component, or make it the implementation
  behind the new coordinator. It must no longer require `NadpcoScheduledSyncCoordinator` to invoke
  it.
- `CyclicalWavesPsVisualizationSyncService`: extract the accepted P/S provider fetch, validation,
  snapshot upsert, and Feature 125 fact projection behind a shared internal/application contract.
  The new pipeline should call that shared operation rather than issue a second P/S request.
- `CyclicalWavesDataProviderClient`: reuse its existing authentication, timeout, retry, throttling,
  response bounds, and P/E/equilibrium endpoint implementations. Do not create a second HTTP or
  token stack.
- `IndustryRelativeValuationOrchestrationService`: preserve its calculation and snapshot-writing
  responsibilities. Its scheduling dependency should be removed or reduced so the new pipeline can
  invoke it directly after source acquisition.
- `NadpcoScheduledSyncCoordinator`: remove the Feature 125 invocation from the NADPCO workflow
  once the independent worker is active. NADPCO synchronization remains responsible for the catalog
  that supplies the next run’s eligible universe.

### Keep unchanged

- `IndustryRelativeValuationCalculationInputBuilder`.
- `IndustryRelativeValuationCalculationSnapshotWriter`.
- `IndustryRelativeValuationEngine` and Feature 125 domain models.
- Feature 125 semantic resolver, capability executors, read repository, and API contracts.
- The normalized catalog and industry identity rules.

### Manual API surface

No manual sync API should be added. The existing P/S visualization admin controller and existing
NADPCO scheduled-sync admin endpoints are separate legacy surfaces; they must not become entry
points for the new relative-valuation pipeline. If the product later requires operational replay,
that should be a separately approved administrative capability, not part of Feature 126.

## 3. Tables and persistence affected

### Existing tables reused by the pipeline

The current model already contains the required Feature 125 persistence boundary:

- `IndustryRelativeValuationSourceFacts` — immutable/provider-scoped P/S, P/E, and equilibrium
  source facts, including current/reference values, timestamps, endpoint, watermark, readiness,
  quality, identity evidence, payload hash, and bounded raw payload.
- `IndustryRelativeValuationSourceLeases` — distributed source-ingestion lease.
- `IndustryRelativeValuationCalculations` — versioned calculation and publication status.
- `IndustryRelativeValuationMetrics` — per-industry metric benchmarks and readiness.
- `CompanyIndustryRelativeValuations` — per-company normalized values, classifications, reasons,
  and rank.
- `IndustryWatchStates`, `IndustryWatchTransitions`, and `IndustryWatchEvaluations` — watch state
  and durable evaluation history.
- `IndustryRelativeValuationOutbox` — durable downstream publication/event handoff.

The source pipeline should continue to persist facts keyed by provider, source kind, and immutable
source observation identity. A changed provider observation must create a new fact version; it must
not overwrite a fact already used by a calculation.

### P/S tables reused, not repurposed

Feature 114’s existing tables remain authoritative for its visualization contract:

- `CompanyPsGaugeSnapshots`.
- `CompanyPsHistoryPoints`.
- `CompanyPsSeriesSyncStates`.
- `CompanyPsVisualizationLeases`.

The new pipeline may write the accepted daily P/S snapshot through the shared Feature 114 service
and then publish one `PSGauge` fact projection into `IndustryRelativeValuationSourceFacts`. It must
keep the distinction between circle `avg` and visualization `BoundaryAverage`; only circle `close`
and circle `avg` are valid for Feature 125’s P/S relative calculation.

`Companies` and `Industries` are read as the canonical NADPCO catalog and industry universe. No
schema change is required for the proposed scheduling refactor.

### Run history

The current source lease and Feature 125 calculation rows provide correctness and publication
state, but there is no requirement in this proposal for a new run-history table. The first slice
should use structured logs and existing activity conventions. Adding a dedicated pipeline-run table
would be a separate persistence decision and would require a migration; it should not be smuggled
into this refactor.

## 4. Should existing P/S ingestion be replaced?

No wholesale replacement. The correct change is to replace its scheduling ownership for the daily
relative-valuation use case while preserving its accepted provider contract and visualization
tables.

Recommended target:

1. Extract a reusable P/S company-sync operation from
   `CyclicalWavesPsVisualizationSyncService`.
2. Have the new relative-valuation pipeline invoke that operation once per eligible company.
3. Have the operation persist the normal Feature 114 snapshot and emit the Feature 125 `PSGauge`
   projection from the same accepted payload/timestamps/hash.
4. Prevent the old P/S worker from issuing a duplicate daily provider request for the same company.
   It may remain for history-only/visualization responsibilities if those cannot be folded into the
   new run, but it must use a distinct, explicit purpose and not race the daily relative pipeline.
5. Remove the old P/S worker only after visualization requirements and history cadence are covered
   by the new shared service and regression evidence proves no Feature 114 behavior changed.

Reading an old P/S snapshot from the new source service is insufficient: it introduces freshness
and ordering coupling to the old worker and can leave P/S stale while P/E and equilibrium are new.
Creating a second P/S HTTP client or second snapshot model is also incorrect because it duplicates
provider calls and risks divergent validation semantics.

## 5. Migration impact

### Expected impact: no new migration for the refactor

The repository already contains the source-fact/lease and Feature 125 calculation, member, watch,
outbox, and P/S visualization tables. A worker, coordinator, option binding, service extraction,
and removal of the NADPCO call site do not alter the database model.

Therefore Feature 126 should create no migration and should not edit the EF model snapshot or any
existing migration.

### Migration guardrails

- Do not add a second fact table for P/S, P/E, or equilibrium.
- Do not add a second lease table unless an independently justified lease key cannot use the existing
  `IndustryRelativeValuationSourceLeases` boundary.
- Do not change Feature 125 unique keys, publication-selection indexes, or source-fact columns.
- Do not add a run-history table in the implementation slices covered by this analysis.
- If implementation discovers a missing operational field, stop and raise a separate migration
  decision; do not modify the schema opportunistically.

## 6. Recommended implementation slices

### Slice 1 — Boundary and configuration

- Define the single pipeline options section and one `Enabled` switch.
- Decide whether `IndustryRelativeValuation.Enabled` becomes read/calculation-only or is folded
  into the new pipeline option; do not leave two independently required enable switches.
- Document cadence, lease, timeout, retry, concurrency, eligible-company limit, and failure policy.
- Add unit tests for option validation and disabled behavior.

### Slice 2 — Shared P/S acquisition and projection

- Extract the P/S fetch/validation/upsert operation from Feature 114.
- Preserve all existing P/S visualization fields and semantics.
- Emit exactly one `PSGauge` source fact from the accepted circle payload.
- Prove circle `avg` is used as the historical P/S baseline and `BoundaryAverage` is not.
- Prove the new operation does not create duplicate provider calls when the old worker and new
  pipeline overlap.

### Slice 3 — Independent P/E/equilibrium source run

- Reuse the existing CyclicalWaves client contracts and resilience policies.
- Fetch P/S, P/E, and equilibrium for every eligible canonical NADPCO company, with per-company
  failure isolation and bounded concurrency.
- Persist `PEGauge`, `PSGauge`, and `EquilibriumGauge` facts idempotently with source provenance.
- Keep the equilibrium gauge `close` as the Feature 125 market-price input; do not substitute a
  different quote source.

### Slice 4 — Worker, lease, and decoupling

- Add the daily hosted worker and dedicated pipeline coordinator.
- Use a distributed lease and a run correlation id; make same-day retries safe and idempotent.
- Invoke the existing Feature 125 orchestration after source acquisition.
- Remove the Feature 125 invocation from `NadpcoScheduledSyncCoordinator` after the new worker is
  proven active, avoiding double calculation/publication.
- Do not add a manual endpoint.

### Slice 5 — P/S worker transition

- Decide whether the existing P/S worker becomes history-only, delegates to the shared operation,
  or is retired.
- Ensure there is one clear owner for the daily P/S fetch and one clear owner for P/S visualization
  history.
- Retain Feature 114 API/read behavior and add regression coverage for its existing output.

### Slice 6 — End-to-end verification and rollout

- Test disabled/enabled scheduling, lease contention, cancellation, timeout, retry, partial company
  failures, provider 404/204, malformed/oversized payload, identity mismatch, 429, 5xx, and auth
  failure outcomes.
- Verify one daily run produces all three source kinds where available and that a provider failure
  does not delete canonical industry membership.
- Verify Feature 125 calculation rows, publication selection, rankings, watch state, and semantic
  reads are unchanged for the same source facts.
- Verify the NADPCO scheduled workflow no longer triggers Feature 125 and that no duplicate P/S
  request is made.
- Verify no migration files or production changes are included in this analysis-only task.

## Non-goals

- Changing Feature 125 formulas, benchmark statistics, ranking, watch state, or semantic routing.
- Changing canonical NADPCO company/industry membership rules.
- Adding a manual sync API.
- Adding migrations.
- Creating a second P/S ingestion model or second CyclicalWaves authentication stack.

