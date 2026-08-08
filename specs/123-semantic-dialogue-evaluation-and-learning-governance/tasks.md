# Feature 123 Tasks — Semantic Dialogue Evaluation and Learning Governance

## [x] Task 1 — Define Semantic Event and Reason Taxonomy

Version bounded event names, fields, correlation, retention, and privacy rules.

Acceptance:

- Every outcome/capability/clarification transition is representable.
- Raw user text is excluded from metrics dimensions.

## [x] Task 2 — Complete Missing-Answer Coverage

Emit fire-and-forget feedback for all V1/V2 semantic outcomes, including deterministic routes.

Acceptance:

- Feedback failure never changes latency-critical user behavior beyond the existing bounded side effect.
- Duplicate/replayed messages do not create misleading duplicate events.

## [x] Task 3 — Define Versioned Evaluation Dataset

Create case contracts and seed suites for capabilities, slots, entities, outcomes, language, dialogue, channels, Billing, and security.

Acceptance:

- Cases identify required and forbidden executor calls.
- Financial values use deterministic fixtures.

## [x] Task 4 — Implement Offline Regression Runner

Run semantic interpretation and orchestration with fake/provider-neutral model and data adapters.

Acceptance:

- Results are reproducible in CI.
- Failures show expected vs actual semantic fields without leaking secrets.

## [x] Task 5 — Add Production Aggregate Dashboards

Report success/failure and dialogue metrics by capability, registry version, channel, and bounded reason.

Acceptance:

- High-risk capability regressions are visible independently.
- Alerts detect language, wrong-route, and failure-rate regressions.

## [x] Task 6 — Build Reviewed Phrase Candidate Workflow

Aggregate eligible evidence and classify candidate capability/presentation/period/comparison phrases.

Acceptance:

- Support/distinct-actor thresholds and redaction are enforced.
- Entity aliases are excluded and routed to identity governance.

## [x] Task 7 — Add Collision, Approval, and Regression Gates

Require cross-registry collision analysis, human approval, and passing tests before activation.

Acceptance:

- Every promotion records approver, rationale, evidence summary, version, and rollback state.
- A phrase cannot map incompatibly to multiple active capabilities.

## [x] Task 8 — Add Canary and Rollback Controls

Activate registry versions by capability/cohort and monitor quality deltas.

Acceptance:

- Rollback is immediate and does not require database migration rollback.
- Historical responses retain their original registry version.

## [x] Task 9 — Define and Enforce Completion Evidence

Require linked CI results, dashboard snapshots/queries, canary duration, and regression thresholds before marking Features 117–123 complete.

Acceptance:

- Checklist status cannot be based solely on code presence.
- Stale spec status is detected during documentation verification.

## Completion Gate

Keep the feature unchecked until evaluation is reproducible, production outcomes are measurable, candidate learning is human-governed, and rollout/rollback gates have demonstrated evidence.
