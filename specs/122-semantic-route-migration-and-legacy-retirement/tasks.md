# Feature 122 Tasks — Semantic Route Migration and Legacy Retirement

## [ ] Task 1 — Define Dispatcher and Typed Execution Result

Create the semantic capability executor interface, dispatcher, execution context, and typed result contract.

Acceptance:

- Contracts are provider/framework-neutral.
- Only enabled registered capabilities can execute.
- Exceptions are translated without exposing internals.

## [ ] Task 2 — Add Per-Capability Flags and Shadow Comparison

Support legacy, shadow, canary, semantic-primary, and rollback states.

Acceptance:

- Shadow mode does not execute data use cases twice or charge twice.
- Route agreement telemetry is bounded and correlated.

## [ ] Task 3 — Migrate Monthly Activity Trend

Use query frame, canonical entity resolution, typed outcome, task state, and existing trend use case.

Acceptance:

- Canonical and paraphrased requests return identical structured facts.
- Missing/ambiguous/not-found/no-data/failure states are distinct.
- No local first-token symbol extraction remains in the active semantic path.

## [ ] Task 4 — Migrate Direct Symbol Metric Lookup

Reuse governed metric semantics and lookup services through the dispatcher.

Acceptance:

- Lookup/scanner/trend/analysis/gauge precedence remains correct.
- Metric follow-up consumes typed task state.
- Existing confidence/explainability data is preserved.

## [ ] Task 5 — Migrate Remaining Deterministic Routes

Add adapters for product mix, statements, disclosures, sales quality, P/S gauge, and verified specialized routes.

Acceptance:

- Each adapter declares slots, output, failure mapping, and data requirements in the registry.
- Business calculations/repositories are reused, not duplicated.

## [ ] Task 6 — Migrate Agent Tool Routes

Put scanner, comprehensive analysis, and delegated lookup behind validated capabilities.

Acceptance:

- Tool selection cannot bypass registry/slot validation.
- ComprehensiveAnalysis faithfulness and date-window behavior do not regress.
- Unsupported/no-tool prose is handled by Feature 117.

## [ ] Task 7 — Align Workflow Steps and Billing Boundary

Apply interpretation/dialogue before execution and typed diagnosis afterward in V2 and V1 rollback.

Acceptance:

- One reservation/finalization path exists per operation.
- Clarification and unsupported policies are tested explicitly.

## [ ] Task 8 — Preserve API, Persistence, and Channel Contracts

Map existing structured payloads unchanged while adding semantic metadata.

Acceptance:

- Live/reload parity holds.
- Existing frontend/Telegram renderers do not require financial recomputation.

## [ ] Task 9 — Add Golden Routing and Payload Equivalence Suite

Cover all active capabilities, paraphrases, conflicts, outcomes, languages, and multi-turn cases.

Acceptance:

- Tests assert both selected capability and byte/semantic equivalence of structured financial facts.
- Negative-route assertions prove competing executors were not called.

## [ ] Task 10 — Canary, Observe, and Retire Legacy Rules

Roll out Slice A/B, then remaining capabilities, and remove duplicated phrase/token/prompt logic after gates pass.

Acceptance:

- Rollback is configuration-driven and does not require migration rollback.
- Architecture tests prevent new local raw-text symbol extraction and unregistered routes.
- Registry-generated prompt/metadata replaces retired duplicates.

## Completion Gate

Keep the feature unchecked until every enabled route is migrated or explicitly deferred, V1/V2 and channel parity pass, Billing is single-path, and retired legacy logic cannot be reintroduced accidentally.
