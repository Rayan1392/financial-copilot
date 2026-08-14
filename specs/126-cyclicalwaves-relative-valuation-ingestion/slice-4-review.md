# Feature 126 — Slice 4 Strict Review

## Verdict

**APPROVED**

## Review scope

Reviewed only:

- `Feature126Observability.cs`
- `IndustryRelativeValuationSourceContracts.cs`
- `IndustryRelativeValuationLeaseStore.cs`
- `RelativeValuationPipeline.cs`
- Slice 4 tests and Slice 2–4 regression tests

The review was performed against `design.md`, `user-story.md`, `tasks.md`,
`implementation-plan.md`, and the Slice 1–3 review documents. No production
code, migration, or approved baseline document was modified.

## Implementation-blocking findings

### 1. Slice 4 observability is not connected to the production run path

`Feature126OperationalSummary`, its factory, and the canonical serializer are
standalone application types. `RelativeValuationPipeline.RunAsync` still
returns only `Feature126IngestionRunResult`, logs a completion line, and throws
on failure; it never creates, emits, or persists a deterministic operational
summary.

As a result, the following required outcomes are not observably represented by
the actual worker run:

- disabled and activation-rejected runs;
- lease contention and recovered takeover;
- failed, cancelled, timeout, or lease-lost runs;
- partial success versus complete success;
- handoff failure;
- same-day successful no-op.

The lease row records only `Running|Handoff|Succeeded|Failed`; there is no
terminal summary or operational sink carrying the `RunState`, metric counts,
failure-code counts, endpoint counts, handoff status, and bounded counts. The
Slice 4 tests construct summaries directly and therefore do not prove runtime
observability. This fails the Slice 4 completion criterion that every terminal
outcome have one deterministic bounded summary and that success not be inferred
from facts or logs.

### 2. Failure-code mapping loses pipeline failures and is not complete

`Feature126Maps.Failures` projects input onto the fixed
`Feature126FailureCodes.Ordered` list, silently discarding every other key.
However, the pipeline emits failure/status codes that are absent from that list,
including `InvalidNonPositiveInput`, `PersistenceRejected`,
`MissingAdmissionIdentity`, and `LeaseContended`; activation rejection reasons
are also returned without a corresponding operational failure mapping.

Those failures consequently cannot appear in `FailureCodeCounts`. A failed
metric or run can be reported without its cause, violating the requirement that
metric outcomes and failure mappings be distinguishable and deterministic. The
tests cover ordering of the approved keys only; they do not assert that every
runtime failure code maps to one approved bucket.

### 3. Canonical serialization does not enforce collection ordering at its boundary

`Feature126CanonicalJsonSerializer.Map` iterates the supplied
`IReadOnlyDictionary` directly. Deterministic ordering is therefore an
incidental property of dictionaries produced by `Feature126Maps`, not a
guarantee of the serializer for its public `Feature126OperationalSummary`
input. A caller can construct the public record with the same key/value pairs
inserted in a different order and receive different bytes.

The required byte contract is a serializer guarantee, so collection keys must
be emitted from explicit canonical key lists (or sorted using an explicit
ordinal rule) inside serialization, with tests covering differently ordered
input dictionaries. The current tests serialize the same already-normalized
summary twice and do not test this contract.

## Recovery and lease assessment

The database lease implementation does fence updates by lease name, date,
owner token, and non-expired row state. Expired `Running` rows can be taken
over, and `HasSucceededAsync` makes a persisted same-day `Succeeded` marker
retry-safe. The pipeline also transitions through `Handoff` and checks ownership
before `Succeeded`.

Those primitives do not remedy Finding 1: recovery, takeover, lease loss,
partial completion, and no-op behavior remain invisible as Slice 4 summaries.
There are also no PostgreSQL Slice 4 crash/expiry/takeover/cancellation/
terminal-marker tests in the reviewed Slice 4 test set; the required recovery
evidence remains incomplete.

## Ownership regression check

No new ownership regression was found in the reviewed paths:

- Feature 126 remains the scheduled acquisition, source-fact, lease/fencing,
  and Feature 125 handoff owner.
- Feature 125 remains the calculation/publication/watch owner.
- Feature 114 remains the visualization owner.
- The prior Slice 3 ownership fixes remain present, including removal of the
  legacy Feature 114 scheduled provider-fetch registration and NADPCO's lack of
  a Feature 125 trigger call.

## Test evidence

- Slice 4 observability plus Slice 2–4 focused regressions: **26 passed, 0
  failed**.
- Feature 126 unit regression set: **30 passed, 0 failed**.
- Feature 125/Feature 126 PostgreSQL handoff and Slice 1 integration subset:
  **15 passed, 0 failed**.
- Full solution: architecture **10 passed**; unit **1,494 passed / 1 failed**;
  integration **282 passed / 146 failed**.

The full-suite failures are unrelated existing failures. The unit failure is
`CyclicalWavesAuthHandlerTests.Response401_TriggersReloginAndRetry`; the
integration failures are the existing authentication/API/data-backed endpoint
failures (including the unrelated financial-statement schema assertion). No
failed test is in the Slice 4 or Feature 126 regression classes.

The focused tests are insufficient for approval because they validate summary
helpers in isolation and do not exercise production summary emission, runtime
failure-code mapping, serializer behavior with differently ordered input
collections, or the required PostgreSQL recovery/concurrency matrix.

## Slice 4 remediation verification

The reviewed blockers were remediated without starting a new slice:

- `RelativeValuationPipeline` now creates and publishes one operational summary
  for disabled, activation-rejected, same-day no-op, lease-contended,
  recovered, successful, partial, cancelled, timeout, lease-lost, and handoff-
  failed outcomes. The summary is attached to `Feature126IngestionRunResult`
  and canonical bytes are retained by the bounded runtime summary registry.
- The worker invokes the pipeline in disabled mode as well, so disabled and
  no-op outcomes use the same observable execution boundary.
- Runtime failure codes now map to fixed canonical buckets, including
  `InvalidNonPositiveInput`, `PersistenceRejected`,
  `MissingAdmissionIdentity`, `LeaseContended`, and activation rejection
  reasons. Unknown codes map to `UnexpectedFailure` rather than disappearing.
- Serializer map emission now sorts keys using ordinal ordering. The unit suite
  proves insertion-order-independent byte equality and complete failure-code
  accounting.
- PostgreSQL tests cover expired takeover, stale renewal/write rejection,
  terminal transition rejection, recovered ownership, lease-loss/recovered
  summary visibility, and same-day successful no-op summary publication.

## Remediation test evidence

- Slice 4 observability tests: **7 passed, 0 failed**.
- Slice 2–4 focused regression tests: **28 passed, 0 failed**.
- PostgreSQL Slice 1/4 recovery tests: **6 passed, 0 failed**.
- Solution build: **passed, 0 errors**.

No migration was added or modified. Feature 126, Feature 125, and Feature 114
ownership boundaries remain unchanged.
