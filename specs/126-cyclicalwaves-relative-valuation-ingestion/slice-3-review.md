# Feature 126 — Slice 3 Strict Review

## Verdict

**APPROVED**

## Review scope

Reviewed only the Slice 3 implementation against `design.md`, `user-story.md`,
`tasks.md`, `implementation-plan.md`, `slice-1-review.md`, and `slice-2-review.md`.
No production code, migration, or approved baseline document was modified.

## Previously identified blockers — resolved

### 1. Feature 126 → Feature 125 handoff is defined but not implemented end-to-end

The new application contracts define a deterministic package, run identity,
fencing token, snapshot digest, and a validation helper. However, the runtime
path never creates or submits that package:

- `Feature126RelativeValuationPipeline.RunAsync` persists source facts and
  transitions the lease directly to `Succeeded`; it never transitions to
  `Handoff`, creates `Feature126HandoffPackage`, or calls a Feature 125
  consumer.
- `Feature126HandoffPackage` and `Feature125HandoffValidationBoundary` are
  referenced only by the Slice 3 unit tests; there is no production call site.
- `IndustryRelativeValuationOrchestrationService` still builds Feature 125
  calculation inputs directly and writes snapshots without validating a
  Feature 126 package, fencing token, or snapshot evidence.

Therefore Feature 126 does not trigger the existing Feature 125 calculation /
publication boundary at all, and the runtime cannot guarantee that a rejected
handoff produces zero calculation, publication, or watch effects. The passing
unit tests prove only the standalone helper behavior, not the production
boundary required by T125-126-01/T126-08B.

### 2. The legacy Feature 114 P/S scheduled owner remains active and bypasses ActivationGuard

`FinancialCopilot.Worker.Program` still registers
`CyclicalWavesPsVisualizationSyncWorker`. That worker gates only on
`CyclicalWavesPsSyncOptions.Enabled`, then invokes the visualization sync
service. It does not evaluate `Feature126ActivationGuard` and has no runtime
zero-fetch isolation when Feature 126 is enabled.

Consequently a configuration can enable Feature 126 and the legacy P/S
provider-fetch schedule simultaneously. This violates the required single
daily P/S owner and permits duplicate provider requests / ingestion writes.
The implementation has not established the approved no-owner window or
rollback-safe owner transition; retained visualization reads must be separated
from the legacy provider-fetch schedule before Feature 126 can be activated.

## ActivationGuard assessment

The pure policy matches the approved closed rejection set and deterministic
inputs: blank revision, blank deployment identifier, and Feature 126 enabled
with either legacy owner. The Feature 126 worker and pipeline evaluate it before
lease acquisition and provider/persistence work. The guard itself is not the
blocking defect; the legacy P/S activation path is the missing enforcement
boundary noted above.

## NADPCO ownership assessment

The coordinator no longer invokes the Feature 125 orchestration service, so the
intended NADPCO detachment is present and NADPCO does not call Feature 126.
Existing `NadpcoScheduledSyncCoordinatorTests` still assert the old ownership
contract and now fail; those assertions are stale and must be updated to verify
zero Feature 125 trigger calls. This does not compensate for the missing
Feature 126 handoff described in Finding 1.

## Scope assessment

No final production rollout switch, Feature 125 formula/publication change,
Feature 114 read-path change, or migration was introduced in the reviewed
changes. The remaining scope issue is ownership isolation: the old Feature 114
provider schedule is still registered and callable.

## Test evidence

- Slice 3 focused tests plus Slice 2 regression tests: **21 passed, 0 failed**.
- Feature 125 PostgreSQL handoff integration tests: **11 passed, 0 failed**.
- Full solution run: architecture **10 passed**; unit **1,489 passed / 1
  failed**; integration **282 passed / 146 failed**.
- Remaining failures are unrelated authentication/provider and broad API
  integration failures, including the CyclicalWaves auth-handler test.

## Remediation verification

The production path now creates the deterministic package from persisted source
fact evidence, transitions the fenced lease to `Handoff`, and submits through
Feature 125 validation. Feature 125 reloads current snapshot evidence and
rejects stale-token, changed-snapshot, and incomplete packages before opening
calculation/publication/watch side effects.

The Feature 114 scheduled provider-fetch worker registration was removed;
visualization reads and retained visualization service/admin functionality remain
available. NADPCO tests now assert zero Feature 125 trigger calls.

Slice 3/2 focused tests: **21 passed, 0 failed**. Feature 125 PostgreSQL
handoff integration tests: **11 passed, 0 failed**. Full suite retains one
unrelated CyclicalWaves authentication unit failure and 146 unrelated
integration failures. No Slice 4 observability/recovery work was started, and
no migration was added.
