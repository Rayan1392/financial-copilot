# Feature 126 — CyclicalWaves Relative Valuation Ingestion

## Scope and implementation boundary

This breakdown is derived only from the approved `design.md`, `design-review.md`, `user-story.md`,
and `user-story-review.md`. It covers ingestion and the existing Feature 125 handoff only.

- Feature 126 owns scheduling, exact eligible-universe admission, CyclicalWaves acquisition,
  source-fact persistence, lease/fencing, bounded evidence, and handoff submission.
- Feature 125 remains the owner of freshness interpretation, calculation, publication, ranking,
  watch state, and downstream side effects, and consumes accepted source facts for those outcomes.
- Feature 114 is visualization-only: it owns visualization reads and validation of the visualization
  experience, but not CyclicalWaves acquisition, provider fetching, ingestion persistence, or source
  facts. Feature 126 is the sole ingestion persistence boundary; accepted P/S acquisition is persisted
  by Feature 126, consumed by Feature 125, and available to Feature 114 through visualization reads.
- NADPCO has no Feature 125 ownership after cutover.
- The AI request path remains read-only and makes no CyclicalWaves request.
- No manual Feature 126 trigger or new provider-specific endpoint is introduced.

## Slice 1 — Shared ingestion foundation

### T126-01 — Define shared CyclicalWaves acquisition contracts

- **Goal:** Establish one provider boundary for P/S, P/E, and equilibrium acquisition.
- **Description:** Define provider contracts/results for the shared scope-free P/S operation, P/E
  `/api/pe/circle-chart-data/{ISIN}`, and equilibrium `/api/equilibrium/gauge/{ISIN}`. Reuse the
  existing authenticated client, timeout, retry, throttling, bounded-response, telemetry, and
   token-cache policies. Preserve provider identity, endpoint, observation identity, watermark,
  payload hash, fetch time, identity evidence, and bounded audit payload in accepted results.
- **Related acceptance criteria:** AC-05, AC-06, AC-07, AC-08, AC-09, AC-10, AC-16.
- **Expected code areas:** `Infrastructure/Financial/Providers/CyclicalWaves/`, new Feature 126
  application contracts and adapters, and Feature 114 visualization read/validation seams.
- **Expected tests:** Unit contract/mapping tests; provider-contract tests for successful and
  rejected responses; logical P/S invocation versus physical HTTP-attempt counters; policy reuse
  tests proving no second logical P/S acquisition.
- **Dependencies:** Existing `CyclicalWavesDataProviderClient`; Feature 114 visualization read
  contracts only.
- **Completion criteria:** All three contracts have explicit accepted/rejected outcomes, exact field
  mappings, bounded evidence, and no scope enumeration or scheduling responsibility.

### T126-02 — Resolve the exact admitted universe

- **Goal:** Make `NoavaranEligibleCompanies` the sole Feature 126 admission source.
- **Description:** Materialize exactly `SELECT "SymbolIsin" FROM "NoavaranEligibleCompanies"` as the
  fixed run input. Allow canonical company/industry mapping only as post-admission enrichment. Treat
  blank, invalid, or unmapped admitted values as terminal input-quality/mapping outcomes; never
  replace them or apply an additional company, industry, market, provider-scope, or status filter.
- **Related acceptance criteria:** AC-03, AC-04, AC-21.
- **Expected code areas:** Existing `NoavaranCompanyScope`/view-query persistence area; new Feature
  126 universe resolver and admitted-item model.
- **Expected tests:** Unit and integration set-equality tests against the view result; architecture
  conformance test for the exact projection; tests proving enrichment cannot add/remove/skip rows and
  invalid rows receive terminal outcomes.
- **Dependencies:** T126-01; existing view and canonical mapping infrastructure.
- **Completion criteria:** The pipeline receives only the materialized exact view result and all
  later stages consume the complete fixed list.

### T126-03 — Integrate immutable source-fact persistence

- **Goal:** Persist accepted P/S, P/E, and equilibrium observations for Feature 125.
- **Description:** Project accepted results into the existing
  `IndustryRelativeValuationSourceFacts` boundary with `PSGauge`, `PEGauge`, and
  `EquilibriumGauge` source kinds. Persist independent fetched/persisted timestamps, endpoint,
  immutable observation identity, watermark, hash, readiness/quality code, identity evidence,
  provider scope, and bounded raw payload. Preserve prior valid facts on rejection or failure.
- **Related acceptance criteria:** AC-05, AC-06, AC-07, AC-08, AC-09, AC-11, AC-14, AC-20.
- **Expected code areas:** `Financial/Ingestion/Persistence/FinancialIngestionDbContext.cs`,
  existing source-fact entities/configurations/repositories, Feature 126 persistence adapter.
- **Expected tests:** Unit projection and identity tests; PostgreSQL integration tests for immutable
  insert/no-op/correction versioning, prior-fact preservation, metadata completeness, and compatible
  restart/replay behavior.
- **Dependencies:** T126-01; existing source-fact persistence model.
- **Completion criteria:** Unchanged observations are no-ops, corrections create immutable versions,
  invalid observations create no ready fact, and no migration is needed.

### T126-04 — Establish renewable lease and fencing foundation

- **Goal:** Protect all Feature 126 work and the Feature 125 handoff with one durable owner token.
- **Description:** Reuse `IndustryRelativeValuationSourceLeases` and existing columns to implement
  atomic daily acquisition, renewal, expiry/takeover, `Running|Handoff|Succeeded|Failed` state,
  unique owner/fencing token, Tehran date, and fenced protected actions. Include provider-work
  admission, fact writes, handoff, and terminal finalization in the fencing boundary.
- **Related acceptance criteria:** AC-12, AC-13, AC-14, AC-15, AC-20.
- **Expected code areas:** Existing lease entity/configuration/repository; new Feature 126 lease
  service, owner token, heartbeat, and protected-action abstractions.
- **Expected tests:** Unit state-transition tests; real PostgreSQL concurrency tests for contention,
  heartbeat, expiry/takeover, stale writes, stale finalization, and fencing rejection.
- **Dependencies:** T126-03; existing lease table and database transaction conventions.
- **Completion criteria:** One live owner is observable, losers perform zero protected actions, stale
  owners cannot write or hand off, and terminal markers remain retry-safe without schema changes.

## Slice 2 — Daily ingestion pipeline

### T126-05 — Implement the enabled daily worker boundary

- **Goal:** Provide the sole automatic Feature 126 invocation boundary.
- **Description:** Add a bounded hosted worker that resolves a scoped pipeline, performs an immediate
  current-Tehran-day evaluation when enabled, then observes `DailyCadenceMinutes`. Its enabled path
  must pass the runtime activation gate before provider calls, persistence, lease acquisition, or
  handoff; a non-`Allowed` result makes the path perform none of those actions. Disabled mode records
  its state and performs no provider work or handoff. A current-day successful marker is a no-op;
  failed, incomplete, expired, or abandoned attempts remain retryable.
- **Related acceptance criteria:** AC-01, AC-02, AC-16, AC-21.
- **Expected code areas:** `FinancialCopilot.Worker/Program.cs`, worker settings/registration, new
  Feature 126 worker and pipeline entry point, existing Tehran timezone helper.
- **Expected tests:** Worker unit/integration tests for disabled/enabled startup, cadence, Tehran day,
  successful-marker no-op, retry eligibility, and zero-call behavior.
- **Dependencies:** T126-04; the worker remains dormant and cannot be enabled until T126-09B is complete.
- **Completion criteria:** The worker contains no provider/persistence logic, exposes no manual API,
  and invokes exactly one scoped pipeline attempt boundary.

### T126-06 — Process the complete universe in deterministic pages

- **Goal:** Acquire all three metrics for every admitted symbol without narrowing the view result.
- **Description:** Materialize the admitted list, partition it deterministically for memory/provider
  load control, and process P/S, P/E, and equilibrium per admitted row with bounded concurrency and
  per-company timeout. Ensure each row reaches a terminal outcome for every metric before the run is
  classified.
- **Related acceptance criteria:** AC-03, AC-04, AC-05, AC-06, AC-07, AC-11.
- **Expected code areas:** New Feature 126 pipeline/orchestration, page/batch scheduler, per-company
  outcome model, and Feature 126 ingestion persistence adapter.
- **Expected tests:** Unit and integration tests with a universe larger than page size, deterministic
  ordering, bounded concurrency, exact three-metric terminal coverage, and one accepted P/S result
  persisted by Feature 126, consumed by Feature 125, and available to Feature 114 visualization reads.
- **Dependencies:** T126-01, T126-02, T126-03, T126-04.
- **Completion criteria:** Page size never truncates or filters the admitted universe; one logical P/S
  acquisition is persisted at the Feature 126 ingestion boundary and is available to both downstream
  consumers; complete and partial metric outcomes are retained.

### T126-07 — Add bounded retry, failure isolation, and idempotent replay

- **Goal:** Make provider/data-quality failures local and retries safe.
- **Description:** Apply existing retry/deadline policies for timeout, 429, network, authentication,
  and 5xx failures. Normalize terminal failure codes, reject malformed/oversized/identity-mismatch/
  non-finite/zero/negative operands, preserve prior valid facts, continue other metrics and symbols,
  and classify partial success only after all admitted metric outcomes are terminal. Preserve
  unchanged observation no-ops and corrected immutable versions on same-day retries.
- **Related acceptance criteria:** AC-09, AC-10, AC-11, AC-14.
- **Expected code areas:** Feature 126 outcome/error policy, provider adapters, source-fact write path,
  retry/deadline integration.
- **Expected tests:** Unit/provider-contract/integration tests for every approved failure class,
  partial metric success, sanitized evidence, retry bounds, unchanged replay, corrected observation,
  and prior-fact preservation.
- **Dependencies:** T126-01, T126-03, T126-06.
- **Completion criteria:** No per-symbol/per-metric failure aborts the remaining universe; cancellation
  and overall timeout are distinguished from per-company terminal outcomes.

### T126-08A — Prepare Feature125 handoff package

- **Goal:** Prepare an eligible complete/partial acquisition run for Feature 125 validation.
- **Description:** Create the deterministic ordered source snapshot/version manifest, explicit missing
  markers, digest, correlation/run identity, Tehran date, lease name, and current fencing token.
  Feature 126 prepares the handoff package only; it must not submit the production handoff or perform
  calculation, publication, watch, or downstream side effects. Cancellation, overall timeout, lease
  loss, stale token, or changed snapshot prevents package preparation.
- **Related acceptance criteria:** AC-11, AC-13, AC-15, AC-16, AC-20, AC-21.
- **Expected code areas:** Feature 126 handoff package/manifest contract; existing source-result tables.
- **Expected tests:** Unit and PostgreSQL concurrency/integration tests for complete and partial
  packages, manifest digest validation, correlation/run identity, current fencing-token inclusion,
  changed-snapshot rejection, no-submission guarantees, and NADPCO-independent preparation.
- **Dependencies:** T126-03, T126-04, T126-06, T126-07.
- **Completion criteria:** Feature 126 emits complete handoff evidence without submitting production
  handoff; the package is fenced, deterministic, and ready for Feature 125 validation.

### T125-126-01 — Feature125 consumer validation boundary

- **Goal:** Keep stale or invalid Feature 126 handoff packages from reaching Feature 125 side effects.
- **Description:** At the existing Feature 125 boundary, validate the fencing token and snapshot
  evidence before calculation/publication/watch processing and inside every existing side-effecting
  transaction. Reject stale tokens, changed snapshots, incomplete authorization, and invalid packages;
  protect existing calculation, publication, and watch side effects without adding Feature 126
  calculation or publication logic.
- **Related acceptance criteria:** AC-11, AC-13, AC-15, AC-16, AC-20, AC-21.
- **Expected code areas:** Existing Feature 125 orchestration, calculation/publication entry, watch
  side-effect boundaries, and transaction fencing validation.
- **Expected tests:** Feature 125 integration and PostgreSQL concurrency tests for valid complete and
  partial packages, stale-token rejection, changed-snapshot rejection, and zero calculation/publication/
  watch side effects after rejection; independent rollback to the pre-handoff behavior.
- **Dependencies:** T126-08A, T126-04.
- **Completion criteria:** Feature 125 alone controls calculation, publication, and watch behavior;
  every side-effecting path rejects stale or invalid packages before any existing side effect.

### T126-08B — Submit production handoff

- **Goal:** Submit only a Feature 125-validated handoff package to the existing production boundary.
- **Description:** Revalidate the prepared manifest, snapshot evidence, correlation/run identity, and
  current fencing token, then submit the production handoff and transition to `Handoff`. Feature 126
  performs ingestion handoff only; it performs no calculation, publication, watch, or downstream side
  effect. Cancellation, overall timeout, lease loss, stale token, or changed snapshot prevents
  submission.
- **Related acceptance criteria:** AC-11, AC-13, AC-15, AC-16, AC-20, AC-21.
- **Expected code areas:** Feature 126 production handoff adapter and existing source-result tables.
- **Expected tests:** Unit and PostgreSQL concurrency/integration tests for complete and partial
  submissions, Feature 125 validation gating, manifest/snapshot evidence validation, current
  fencing-token inclusion, changed-snapshot rejection, no-submission guarantees, and
  NADPCO-independent handoff.
- **Dependencies:** T126-08A AND T125-126-01.
- **Completion criteria:** Feature 126 can submit production handoff only after Feature 125 validation
  exists; only the live owner can submit it, and Feature 125 remains the sole downstream authority.

## Slice 3 — Ownership transition and handoff

### T126-09A — Define the pure ActivationGuard policy

- **Goal:** Prevent unsafe owner combinations before runtime work begins.
- **Description:** Evaluate only `CandidateConfigurationRevision`, `DeploymentIdentifier`, and
  `OwnerActivationStates`. Return `Allowed` or one deterministic closed-set rejection:
  `MissingConfigurationRevision`, `MissingDeploymentIdentifier`, or `ConflictingOwnerActivation`.
  Reject Feature 126 enabled with either legacy Feature 114 P/S owner or NADPCO Feature 125 trigger.
- **Related acceptance criteria:** AC-17, AC-18.
- **Expected code areas:** Configuration options/registration, new application-level
  `Feature126ActivationGuard` policy and result types.
- **Expected tests:** Configuration-policy unit and integration matrix tests proving exact allowed and
  rejected combinations and that operational evidence, drain state, and live leases are not inputs.
- **Dependencies:** Existing configuration/DI conventions; no runtime pipeline dependency.
- **Completion criteria:** The guard has the approved pure input/output contract and closed reasons;
  no mixed owner state can activate Feature 126.

### T126-09B — Wire runtime activation enforcement

- **Goal:** Make the pure activation decision mandatory on every owner activation path.
- **Description:** Wire the runtime `ActivationGuard` into the Feature 126 worker activation gate and
  both legacy owner activation gates. The worker must not perform provider calls, persistence, lease
  acquisition, or handoff unless the guard returns `Allowed`; legacy owners must not activate in a
  conflicting state. Register the gate in deployment/DI paths and verify the integration path, not
  only the policy result.
- **Related acceptance criteria:** AC-01, AC-02, AC-13, AC-17, AC-18, AC-20.
- **Expected code areas:** Worker and legacy-owner registration/activation paths, configuration/DI
  wiring, runtime activation gate, deployment verification seams.
- **Expected tests:** Runtime integration matrix for allowed/rejected revisions, worker zero-provider/
  zero-persistence/zero-lease/zero-handoff behavior, legacy-owner activation rejection, and startup
  registration coverage.
- **Dependencies:** T126-09A, T126-05, T126-04.
- **Completion criteria:** No enabled runtime path can bypass the pure guard; rejected activation has
  no provider, persistence, lease, or handoff effect.

### T126-10A — Detach NADPCO from Feature 125 ownership

- **Goal:** Remove NADPCO ownership of Feature 125 ingestion independently of other cutover work.
- **Description:** Remove the Feature 125 trigger ownership from `NadpcoScheduledSyncCoordinator` and
  prevent it from calling or waiting on Feature 126. Retain only legacy operations that make no
  provider request and cannot call the Feature 126 pipeline or handoff.
- **Related acceptance criteria:** AC-16, AC-18, AC-21.
- **Expected code areas:** `Financial/Ingestion/NadpcoApi/NadpcoScheduledSync.cs` and its registration.
- **Expected tests:** NADPCO disabled, failed, and never-run integration scenarios prove Feature 126
  remains independent and NADPCO cannot trigger Feature 125 behavior; rollback restores NADPCO's
  prior ownership without enabling Feature 126 concurrently.
- **Dependencies:** T126-09B, T126-08B, T125-126-01.
- **Completion criteria:** NADPCO has no Feature 125 trigger ownership and its rollback/test evidence
  is independent of Feature 114 schedule isolation.

### T126-10B — Isolate the legacy competing P/S schedule

- **Goal:** Ensure no legacy competing schedule can make the daily P/S provider call.
- **Description:** Disable and isolate any legacy daily P/S provider-fetch schedule associated with
  the former Feature 114 path while retaining Feature 114 visualization-only reads and validation.
  Feature 114 must not acquire data, fetch providers, persist ingestion facts, call the Feature 126
  pipeline, or submit its handoff.
- **Related acceptance criteria:** AC-16, AC-18, AC-21.
- **Expected code areas:** Legacy P/S worker/service/registration and Feature 114 visualization read
  seams.
- **Expected tests:** Integration tests prove zero legacy provider-fetch/protected requests after
  isolation, retained visualization reads remain available, and rollback restores only the legacy
  schedule with Feature 126 disabled and drained.
- **Dependencies:** T126-09B, T126-08B, T125-126-01.
- **Completion criteria:** Feature 114 remains visualization-only, no legacy schedule fetches P/S,
  and isolation/rollback evidence is independent of NADPCO detachment.

### T126-10C — Verify forward cutover

- **Goal:** Ensure Feature 126 becomes the only scheduled daily P/S and Feature 125 ingestion owner.
- **Description:** Verify the no-owner window after both ownership-isolation tasks, then enable
  Feature 126 only through the runtime guard. Prove exactly one scheduled daily P/S and Feature 125
  ingestion owner exists after forward cutover, with no manual/admin endpoint wired to Feature 126.
- **Related acceptance criteria:** AC-16, AC-18, AC-21.
- **Expected code areas:** Feature 126 activation configuration, rollout verification, existing
  Feature 114 and NADPCO boundaries.
- **Expected tests:** Independent forward-cutover verification of no-owner window, guard-enforced
  activation, exactly-one-owner state, no provider/protected calls from legacy paths, and no
  manual/admin endpoint wired to Feature 126.
- **Dependencies:** T126-09B, T126-10A, T126-10B, T126-08B, T125-126-01.
- **Completion criteria:** Exactly one scheduled P/S owner exists after cutover; NADPCO cannot trigger
  Feature 125 behavior; AI and retained admin operations remain outside Feature 126.

### T126-11 — Verify drain and safe rollback sequencing

- **Goal:** Make rollback preserve single ownership and prevent stale side effects.
- **Description:** Implement/verify the operational sequence: allowed all-disabled revision, Feature
  126 drain, proof of no live owner/request/handoff, then later restoration of selected legacy owners.
  Reject every mixed state in which Feature 126 and either legacy owner are enabled.
- **Related acceptance criteria:** AC-13, AC-17, AC-18, AC-20.
- **Expected code areas:** Activation configuration/runbook verification hooks, lease/worker drain
  status integration, deployment verification tests.
- **Expected tests:** Rollout verification and PostgreSQL concurrency tests for forward activation,
  forbidden mixed states, drain while processing, stale owner rejection, and ordered rollback.
- **Dependencies:** T126-04, T126-09B, T126-10A, T126-10B, T126-10C, T125-126-01.
- **Completion criteria:** Rollback cannot restore a legacy owner until Feature 126 is disabled and
  drained, with no live request/handoff/owner remaining.

## Slice 4 — Observability and recovery

### T126-12A — Construct the operational summary

- **Goal:** Construct the approved operational summary state before serialization.
- **Description:** Construct the fixed summary shape and lifecycle state matrix, terminal-only counts,
  `FailureCodeCounts`, `EndpointCounts`, null/zero/NotApplicable rules, timestamps, Tehran date,
  enums, and bounded values. Exclude credentials, tokens, raw payloads, symbols/ISIN lists, exception
  collections, unknown enums, and excess keys. Leave byte-level serialization to T126-12B.
- **Related acceptance criteria:** AC-19.
- **Expected code areas:** New Feature 126 operational summary DTOs, lifecycle aggregation, failure-code/
  endpoint maps, and summary construction boundary.
- **Expected tests:** Section J lifecycle matrix, exact map leaves, lifecycle equalities, terminal-only
  counting, forbidden values, and recovered-then-lost ownership construction tests.
- **Dependencies:** T126-04, T126-07, T126-08B.
- **Completion criteria:** Every scheduled/terminal state constructs exactly one deterministic summary
  with correct lifecycle and counter invariants.

### T126-12B — Serialize the canonical operational summary

- **Goal:** Produce the approved byte-for-byte summary representation.
- **Description:** Serialize T126-12A output as compact, BOM-free UTF-8 with fixed property ordering,
  exact escaping, no slash/non-ASCII escaping, five short control escapes, and uppercase `\\u00XX`
  for remaining controls. Emit only the approved keys and values.
- **Related acceptance criteria:** AC-19.
- **Expected code areas:** Canonical serializer and summary emission boundary.
- **Expected tests:** UTF-8, ordering, escaping, forbidden-value, and byte-for-byte contract tests,
  including identical summaries across recovery paths and the T126-12A seam.
- **Dependencies:** T126-12A.
- **Completion criteria:** Identical summary objects always produce identical approved bytes.

### T126-13 — Implement recovery and terminal-state handling

- **Goal:** Make restart, crash, timeout, cancellation, takeover, and handoff recovery deterministic.
- **Description:** Persist only the approved durable lease envelope and terminal marker in existing
  columns. Leave crashed `Running` attempts for expiry; permit a new token to recover the current day;
  make `Succeeded` and `PartialSuccess` current-day markers no-ops; record failed terminal states only
  while fencing is live; never synthesize older missed days.
- **Related acceptance criteria:** AC-02, AC-10, AC-11, AC-13, AC-14, AC-19, AC-20.
- **Expected code areas:** Feature 126 pipeline lifecycle, lease recovery service, terminal marker
  transitions, observability state builder.
- **Expected tests:** Recovery integration tests for restart, crash before/after handoff, expired lease
  takeover, same-day retry, cancellation, overall timeout, lease loss, partial success, and unchanged
  replay while preserving prior facts.
- **Dependencies:** T126-04, T126-07, T126-08B, T126-12A, T126-12B.
- **Completion criteria:** Recovery never infers success from facts/logs, never hands off after forbidden
  dispositions, and retains auditable immutable facts and deterministic terminal evidence.

## Slice 5 — Integration hardening

### T126-14 — Prove PostgreSQL persistence and fencing concurrency

- **Goal:** Validate database-level atomicity and compatibility on real PostgreSQL.
- **Description:** Exercise real concurrent transactions for lease acquisition/renewal/takeover,
  fenced source-fact writes, `Running`→`Handoff`, Feature 125 validation in all side-effecting
  transactions, terminal marker writes, immutable observation replay/correction, restart, and
  rollback compatibility.
- **Related acceptance criteria:** AC-12, AC-13, AC-14, AC-15, AC-20.
- **Expected code areas:** `tests/FinancialCopilot.IntegrationTests/`, existing PostgreSQL fixture,
  persistence/transaction test helpers, Feature 125 downstream integration seam.
- **Expected tests:** PostgreSQL concurrency and recovery suites with real competing transactions;
  assert losers/stale owners produce zero provider/fact/handoff/downstream effects.
- **Dependencies:** T126-03, T126-04, T126-08B, T125-126-01, T126-11, T126-13.
- **Completion criteria:** Database-level tests prove the fencing and compatibility guarantees without
  adding schema objects.

### T126-15 — Complete provider-contract and pipeline integration coverage

- **Goal:** Harden all P/S, P/E, and equilibrium transport and validation behavior.
- **Description:** Cover the approved provider response matrix: 204, 404, malformed JSON, oversized
  response, identity mismatch, non-finite values, zero/negative operands, authentication failure,
  429, timeout, network failure, and 5xx. Verify logical versus physical attempt counts, exact
  mappings, bounded retries, isolation, one logical CyclicalWaves P/S acquisition, one Feature 126
  ingestion persistence boundary, and multiple downstream consumers reading the accepted persisted
  result.
- **Related acceptance criteria:** AC-05, AC-06, AC-07, AC-08, AC-09, AC-10, AC-11.
- **Expected code areas:** `tests/FinancialCopilot.IntegrationTests/`, provider test doubles/contract
  fixtures, Feature 126 pipeline tests, and Feature 114 visualization read tests.
- **Expected tests:** Provider-contract, unit, and end-to-end ingestion tests for each approved case
  and partial-success combination.
- **Dependencies:** T126-01, T126-03, T126-06, T126-07, T126-09B.
- **Completion criteria:** Every approved provider failure and acceptance path has deterministic test
  proof and no test requires a second logical P/S acquisition.

### T126-16 — Verify boundaries, rollout, and full acceptance matrix

- **Goal:** Demonstrate that the implementation satisfies all approved acceptance criteria without
  expanding scope.
- **Description:** Run architecture-conformance, boundary, rollout, NADPCO-independence, AI
  read-only, no-manual-trigger, and full AC-01–AC-21 verification. Confirm Feature 125 formulas,
  publication/watch behavior, Feature 114 visualization reads, and AI behavior remain unchanged.
- **Related acceptance criteria:** AC-01 through AC-21.
- **Expected code areas:** Existing architecture/conformance tests, worker/DI registration, Feature
  114 and NADPCO integration seams, rollout verification suite.
- **Expected tests:** Full acceptance suite; configuration-policy matrix; staged cutover/rollback;
  disabled/failed/never-run NADPCO scenarios; no-provider-call AI/read-path tests; deterministic
  observability contract tests; final PostgreSQL and provider suites.
- **Dependencies:** T126-05 through T126-15, T125-126-01, T126-09A, T126-09B, T126-10A,
  T126-10B, T126-10C, T126-12A, T126-12B.
- **Completion criteria:** Each AC-01–AC-21 has passing executable evidence, ownership boundaries
  are enforced, rollout/rollback is safe, and no unapproved product scope is present.

## Migration boundary

- **Tasks requiring a migration:** None.
- **Tasks requiring no migration:** T126-01 through T126-16 and T125-126-01. Feature 126 owns
  CyclicalWaves acquisition, ingestion persistence, and source facts; Feature 125 consumes those
  source facts for calculation/publication/watch; Feature 114 has visualization reads only, with no
  acquisition or ingestion persistence ownership. The approved design explicitly reuses
  `IndustryRelativeValuationSourceFacts`, `IndustryRelativeValuationSourceLeases`, existing Feature 125
  downstream tables, and the existing `NoavaranEligibleCompanies` view. Lease state, fencing, and the durable terminal marker use the
  existing lease columns; no new table, column, index, run-history table, or destructive persistence
  change is authorized.

Status:
TASK_BREAKDOWN_READY_FOR_REVIEW
