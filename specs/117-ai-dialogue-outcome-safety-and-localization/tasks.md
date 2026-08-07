# Feature 117 Tasks — AI Dialogue Outcome Safety and Localization

## [x] Task 1 — Audit Current Non-Success Branches

Inventory V1/V2 branches and result types that currently return `Unknown`, `Clarification`, `null`, empty results, or exception prose.

Acceptance:

- The audit covers every `DetectedIntent` and every deterministic V2 preflight route.
- Each branch is mapped to a target outcome and reason code.
- Existing successful/faithful response behavior is recorded as a regression baseline.

## [x] Task 2 — Add Versioned Outcome Contracts

Add `DialogueOutcome`, bounded reason codes, `ReplyLanguage`, and additive API/persistence fields.

Acceptance:

- Existing clients can deserialize responses without changes.
- Unknown enum values have a safe client fallback.
- Persisted message contract versioning and payload limits are preserved.

## [x] Task 3 — Implement Reply-Language Selection

Implement deterministic Persian/English selection for system-owned responses.

Acceptance:

- Persian, English, and mixed financial queries have tests.
- Character normalization does not mutate the persisted original user text.
- Language selection does not require a network/provider call.

## [x] Task 4 — Implement Localized Outcome Composer

Create centralized templates for clarification, disambiguation, no-data, unsupported, temporary failure, and permanent failure.

Acceptance:

- Templates contain structured facts only.
- Persian wording is RTL-safe and English wording is grammatically complete.
- No template claims a capability or data source not supplied by its input.

## [x] Task 5 — Guard the Raw Unknown Path

Prevent unvalidated agent prose from becoming the final answer when no structured capability result exists.

Acceptance:

- Unknown prose cannot introduce financial numbers or unsupported capability claims.
- The deterministic unsupported response is used on validation failure.
- A reason code records why replacement occurred.

## [x] Task 6 — Align Clarification Propagation

Correct all branches that ask a question through `TextAnswer` without setting clarification fields.

Acceptance:

- Missing symbol and missing required route parameters set all clarification fields.
- `ClarificationRequired` is false for no-data and technical-failure outcomes.
- Persisted/reloaded responses retain the same state.

## [x] Task 7 — Map No-Data and Failure Reasons

Introduce typed application results/adapters where necessary to avoid undifferentiated `null` at the orchestration boundary.

Acceptance:

- Entity not found, no rows, stale/ineligible data, timeout, and exception map differently.
- No raw exception text crosses the Application/API boundary.

## [x] Task 8 — Integrate Billing, Feedback, and Telemetry

Apply the existing accounting policy once and emit bounded outcome telemetry for every route.

Acceptance:

- No duplicate reservation/finalization occurs.
- Feedback failures cannot alter the response.
- Raw user text is not used as a high-cardinality metric label.

## [x] Task 9 — Update Web and Telegram Rendering

Render localized outcome messages consistently while preserving structured successful blocks.

Acceptance:

- Live and reloaded web messages match.
- Telegram meaning matches web even when layout differs.
- No client invents a fallback message independently.

## [~] Task 10 — Add Regression and Contract Tests

Cover all outcomes, reason codes, languages, channels, persistence, security, and successful intent regressions.

Acceptance:

- Tests include model wrong-language and invented-prose fixtures.
- ComprehensiveAnalysis faithfulness and successful result snapshots remain unchanged.
- The full AI facade test suite passes before rollout.

## Completion Gate

Keep the feature unchecked until every non-success route has a typed outcome, Persian language safety is deterministic, raw unknown prose is guarded, and V1/V2/channel/billing regressions pass.
