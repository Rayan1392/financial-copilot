# Feature 126 — CyclicalWaves Relative Valuation Ingestion

Status: `IMPLEMENTATION_PLAN_READY_FOR_REVIEW`

This plan is derived only from the approved `design.md`, `design-review.md`, `user-story.md`,
`user-story-review.md`, `tasks.md`, and `tasks-review.md`. It is an execution plan; it authorizes
no production code, migration, or unrelated implementation change.

## Scope and development boundaries

Feature 126 owns:

- CyclicalWaves acquisition for P/S, P/E, and equilibrium;
- ingestion persistence;
- immutable provider-scoped source facts;
- the handoff producer and its fencing/snapshot evidence.

Feature 125 owns:

- calculation and source-barrier/freshness interpretation;
- publication and ranking/classification;
- watch evaluation and state;
- handoff consumer validation before downstream side effects.

Feature 114 owns visualization reads only. Its accepted P/S validation/persistence semantics are
reused by Feature 126, but Feature 114 does not own daily acquisition, ingestion persistence, the
Feature 126 pipeline, or the Feature 125 handoff.

NADPCO has no ownership after cutover. It must not trigger, wait for, or gate Feature 126 or the
Feature 125 ingestion handoff.

No migration is required. The approved design reuses existing source-fact, lease, Feature 125,
Feature 114, and eligibility-view persistence. No new table, column, index, run-history table, or
manual Feature 126 API is introduced.

No new scope, feature, or architecture may be introduced. Feature 125 formulas and downstream
behavior, Feature 114 visualization reads, and the AI read-only path remain unchanged.

## Execution order

1. Build the shared acquisition, admitted-universe, source-fact, lease, and fencing foundation.
2. Build the disabled daily worker and deterministic ingestion pipeline.
3. Prepare the handoff package and implement Feature 125 consumer validation.
4. Submit production handoff only after consumer validation is present and passing.
5. Define the pure ActivationGuard policy.
6. Wire runtime activation enforcement; no owner may perform provider, persistence, lease, or
   handoff work before the guard returns `Allowed`.
7. Detach NADPCO and isolate the competing Feature 114 P/S schedule.
8. Verify the no-owner window, then perform guarded forward cutover.
9. Verify drain and ordered rollback behavior.
10. Complete observability/recovery work and integration hardening before production handoff/cutover
    approval.

The ordering is mandatory: ActivationGuard policy precedes runtime enforcement; runtime enforcement
precedes Feature 126 activation; Feature 125 handoff validation precedes production handoff; and
cutover occurs only after ownership, fencing, recovery, and rollout safety checks exist.

## Implementation slices

### Slice 1 — Shared ingestion foundation

**Objective:** Establish the reusable CyclicalWaves contracts, exact admitted universe, immutable
source-fact persistence, renewable lease, and fencing primitives without enabling the daily worker.

**Included tasks:** T126-01, T126-02, T126-03, T126-04.

**Dependencies:** Existing CyclicalWaves client policies; `NoavaranEligibleCompanies` exact
`SymbolIsin` projection; existing source-fact and lease tables; existing canonical mappings only for
post-admission enrichment.

**Expected code areas:** Feature 126 application contracts/services; existing CyclicalWaves provider
client policies; eligibility projection/repository; `IndustryRelativeValuationSourceFacts` and
`IndustryRelativeValuationSourceLeases` persistence; lease acquisition/renewal/fencing seams.

**Required tests:** Unit contract and mapping tests; exact-universe/admission tests; provider identity
and operand validation tests; source-fact idempotency/correction tests; lease acquisition, renewal,
expiry, takeover, and stale-token tests; PostgreSQL persistence tests.

**Completion criteria:** The exact view result is the complete admitted universe; all three gauge
contracts map approved fields; accepted observations are immutable and provider-scoped; lease and
fencing operations are renewable and database-backed; no worker or provider schedule is enabled.

**Review checkpoint:** Confirm no independent eligibility query, no new schema object, no second P/S
validator, and no runtime path can bypass the lease/fencing foundation.

### Slice 2 — Daily ingestion pipeline and validated handoff

**Objective:** Implement the disabled daily pipeline from startup/cadence through isolated acquisition,
idempotent persistence, and a fenced handoff that Feature 125 validates before side effects.

**Included tasks:** T126-05, T126-06, T126-07, T126-08A, T125-126-01, T126-08B.

**Dependencies:** Slice 1; existing Feature 125 calculation/publication boundary; existing Feature 114
accepted P/S operation/visualization persistence; Tehran date helper; approved retry, timeout, and
bounded-concurrency policies.

**Expected code areas:** `CyclicalWavesRelativeValuationWorker`; pipeline orchestration; deterministic
page processing; P/S shared operation; P/E/equilibrium adapters; per-company/per-metric outcomes;
source-fact write boundary; handoff manifest/package and Feature 125 validation in all side-effecting
transactions.

**Required tests:** Unit tests for worker gating, mappings, page completeness, timeout/retry,
failure isolation, idempotent replay, and manifest digest; provider contract tests for approved
responses/failures; PostgreSQL integration tests for fenced writes and complete/partial packages;
Feature 125 tests proving stale-token, changed-snapshot, and invalid-package rejection causes zero
calculation/publication/watch side effects; one logical P/S acquisition and one Feature 126
persistence boundary.

**Completion criteria:** Disabled startup makes no provider requests or handoff; enabled execution
processes every admitted symbol to terminal per-metric outcome; accepted facts are persisted once;
partial failures do not abort the universe; T126-08A prepares but does not submit; Feature 125
consumer validation is active before T126-08B can submit; only the live fenced owner can hand off.

**Review checkpoint:** Validate the Feature 125 handoff contract and rejection behavior before
approving any production handoff. Confirm Feature 126 performs no calculation, publication, watch,
or AI-path work.

### Slice 3 — Ownership transition, activation, cutover, and rollback

**Objective:** Make single ownership enforceable and complete a guarded forward cutover only after
legacy owners are detached or isolated.

**Included tasks:** T126-09A, T126-09B, T126-10A, T126-10B, T126-10C, T126-11.

**Dependencies:** Slice 2, especially T125-126-01 and T126-08B; existing deployment/configuration/DI
conventions; lease drain and live-owner evidence.

**Expected code areas:** Pure `Feature126ActivationGuard` policy/result types; worker and legacy-owner
activation gates; NADPCO coordinator/registration; legacy Feature 114 P/S worker/registration;
deployment verification and drain/rollback seams.

**Required tests:** ActivationGuard policy matrix; runtime activation matrix proving rejected states
make zero provider/persistence/lease/handoff effects; NADPCO-disabled/failed/never-run independence;
legacy P/S zero-fetch tests with visualization reads retained; forward-cutover exactly-one-owner and
no-owner-window tests; rollback drain, stale-owner, and forbidden mixed-state tests.

**Completion criteria:** Pure policy exists before runtime use; all activation paths enforce it; mixed
Feature 126/legacy owner states are rejected; NADPCO has no post-cutover ownership; Feature 114 is
visualization-read-only; cutover proves exactly one scheduled daily P/S and Feature 125 ingestion
owner; rollback restores legacy ownership only after Feature 126 is disabled and drained.

**Review checkpoint:** Require independent evidence for NADPCO detachment, legacy P/S isolation,
Feature 125 handoff validation, no-owner window, guarded activation, and ordered rollback before
enabling Feature 126.

### Slice 4 — Observability and recovery

**Objective:** Make lifecycle summaries, canonical serialization, recovery, terminal markers, and
same-day retry deterministic and auditable using only existing lease columns.

**Included tasks:** T126-12A, T126-12B, T126-13.

**Dependencies:** Slice 1 fencing; Slice 2 pipeline/handoff; Slice 3 activation/drain behavior.

**Expected code areas:** Operational summary DTOs and lifecycle aggregation; failure/endpoint maps;
canonical UTF-8 serializer; lease recovery and terminal-marker transitions; pipeline recovery seams.

**Required tests:** Unit lifecycle/count invariant tests; observability serialization byte-contract
tests for ordering, escaping, and forbidden values; PostgreSQL recovery/concurrency tests for crash,
expiry, takeover, cancellation, timeout, lease loss, partial success, retry, and replay.

**Completion criteria:** Every terminal outcome has one deterministic bounded summary; summaries are
canonical bytes; `Succeeded`/`PartialSuccess` prevent same-day automatic reruns; crashed `Running`
leases recover only after expiry; no success is inferred from facts/logs; stale owners cannot persist,
handoff, or record terminal state.

**Review checkpoint:** Verify observability serialization independently of pipeline correctness and
verify recovery cannot create stale downstream side effects or synthesize older missed days.

### Slice 5 — Integration hardening and final acceptance

**Objective:** Prove database atomicity, provider behavior, ownership boundaries, rollout safety, and
all approved acceptance criteria without expanding scope.

**Included tasks:** T126-14, T126-15, T126-16.

**Dependencies:** Slices 1–4; T125-126-01; all activation, ownership, recovery, and handoff gates.

**Expected code areas:** `tests/FinancialCopilot.IntegrationTests/`; PostgreSQL fixtures and
transaction helpers; provider contract fixtures; architecture/conformance and rollout verification
suites; Feature 114 visualization-read and NADPCO boundary seams.

**Required tests:** Real PostgreSQL concurrent lease/fencing/source-fact/handoff transactions;
provider contract matrix for 204/404, malformed/oversized payloads, identity mismatch, invalid
operands, auth, 429, timeout, network, and 5xx; pipeline integration and partial-success tests;
rollout verification for disabled/failed/never-run NADPCO, no manual trigger, AI read-only behavior,
exactly-one-owner cutover, and ordered rollback; final AC-01–AC-21 acceptance matrix; observability
serialization regression suite.

**Completion criteria:** All approved provider and database guarantees have executable evidence;
fencing losers produce zero protected effects; Feature 125 formulas/publication/watch, Feature 114
reads, and AI behavior remain unchanged; staged rollout and rollback pass; every AC-01–AC-21 is
verified; no migration or unapproved scope is present.

**Review checkpoint:** Final architecture, ownership, safety, rollout, and acceptance review. Only
after this checkpoint may the approved staged deployment proceed.

## Testing strategy by slice

| Slice | Unit tests | Provider contract tests | PostgreSQL integration tests | Concurrency/fencing tests | Rollout verification tests | Observability serialization tests |
|---|---|---|---|---|---|---|
| 1 | Contracts, mappings, eligibility, fact identity | Payload identity/quality acceptance | Fact/lease persistence | Lease acquire/renew/expiry/takeover | — | — |
| 2 | Worker, paging, retry, isolation, manifest | Full approved provider response matrix | Fenced writes and handoff package/consumer boundary | Stale token and changed snapshot rejection | Disabled worker/no side effects | Summary inputs only where emitted |
| 3 | Pure guard policy and activation decisions | — | Owner state and handoff compatibility | Mixed-owner rejection, drain, stale owner | No-owner window, forward cutover, rollback | — |
| 4 | Lifecycle aggregation and recovery decisions | — | Crash/recovery/terminal markers | Takeover, cancellation, lease loss, replay | Drain/restart recovery checks | Canonical byte-for-byte contract |
| 5 | Boundary/conformance regressions | End-to-end provider/pipeline matrix | Full real-transaction suite | Full fencing and side-effect exclusion | Staged rollout, NADPCO independence, rollback | Final regression and AC evidence |

## Deployment strategy

### Staged rollout

1. Deploy the implementation with Feature 126 disabled. Verify registration, configuration revision,
   deployment identifier, and zero provider/persistence/lease/handoff activity from the disabled
   worker.
2. Validate the pure ActivationGuard policy against the candidate revision and owner activation
   states. Do not treat logs, drain state, or live leases as policy inputs.
3. In a controlled transition, detach NADPCO Feature 125 trigger ownership and disable/isolate the
   legacy Feature 114 P/S provider schedule. Verify retained Feature 114 visualization reads.
4. Prove the no-owner window and absence of live legacy requests/owners/handoffs.
5. Enable Feature 126 only through runtime ActivationGuard enforcement. Run bounded verification,
   including exact admitted-universe processing, one P/S acquisition, immutable facts, and the
   validated Feature 125 handoff.
6. Confirm exactly one scheduled daily P/S and Feature 125 ingestion owner, with no manual/admin
   endpoint wired to Feature 126, then expand according to the approved staged deployment process.

### ActivationGuard validation

`Allowed` requires a candidate configuration revision, deployment identifier, and non-conflicting
owner activation state. Feature 126 enabled alongside either the legacy Feature 114 P/S owner or
the NADPCO Feature 125 trigger is rejected deterministically. A rejected activation performs no
provider request, persistence, lease acquisition, or handoff.

### Forward cutover

Forward cutover is permitted only after Feature 125 handoff consumer validation, fencing, recovery,
observability, NADPCO detachment, legacy P/S isolation, and rollout verification are complete. The
cutover state has Feature 126 as the sole scheduled acquisition/ingestion owner; Feature 125 remains
the sole downstream calculation/publication/watch authority.

### Rollback path

1. Apply an allowed all-disabled revision.
2. Stop new Feature 126 work and drain it.
3. Prove no live Feature 126 owner, provider request, persistence operation, or handoff remains.
4. Restore only the selected legacy owner(s), never a mixed state with Feature 126 enabled.
5. Verify NADPCO/legacy behavior independently and preserve Feature 114 visualization reads.

Rollback must not infer success from facts or logs and must reject stale tokens and in-flight stale
handoffs. No destructive data operation or migration rollback is involved.

## Explicit exclusions

- No migration required.
- No production code, schema change, manual run API, endpoint, new calculation, publication, watch,
  visualization-write ownership, or AI request-path acquisition is planned here.
- No new architecture, scope, or feature beyond the approved task breakdown is introduced.
