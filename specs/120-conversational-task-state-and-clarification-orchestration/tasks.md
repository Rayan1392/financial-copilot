# Feature 120 Tasks — Conversational Task State and Clarification Orchestration

## [x] Task 1 — Define Task-State and Pending-Action Contracts

Create versioned task state, slot provenance, pending clarification/disambiguation, and transition models.

Acceptance:

- Contracts contain only validated semantic facts.
- State version and expiration are explicit.
- Existing conversation payload compatibility is documented.

## [x] Task 2 — Define Lifecycle and Compatibility Policy

Specify update, reuse, replace, clear, expiry, and capability-slot compatibility rules.

Acceptance:

- Task switches cannot leak old symbols/metrics.
- Default timeout/turn limits are configurable and validated.

## [x] Task 3 — Implement Conversation-Scoped State Persistence

Persist task state with actor/tenant isolation and optimistic concurrency.

Acceptance:

- Concurrent/replayed messages are idempotent.
- Conversation deletion/retention policy includes task state.
- No cross-conversation state read is possible.

## [x] Task 4 — Add Pending Clarification Resolution

Interpret the next message against the expected slot/candidates before normal routing.

Acceptance:

- A symbol-only reply can complete a missing-symbol trend request.
- A candidate choice resolves disambiguation deterministically.
- An obvious new task cancels/replaces the pending action.

## [x] Task 5 — Add Safe Slot Carry-Over

Fill omitted compatible slots from recent active state.

Acceptance:

- Carried slots are marked as conversation-derived.
- Conflicting explicit input always wins after validation.
- Expired or low-confidence state is ignored.

## [x] Task 6 — Integrate Dialogue Gate into V1 and V2

Place task-state resolution before executable route selection and align rollback behavior.

Acceptance:

- Deterministic preflight routes receive the completed frame, not only raw latest text.
- V1/V2 produce equivalent state transitions.

## [x] Task 7 — Align Billing and Feedback

Apply the explicit clarification charging policy and record clarification lifecycle events.

Acceptance:

- No duplicate reservation occurs across clarification turns.
- `clarification_requested`, `clarification_resolved`, and `clarification_abandoned` are observable.

## [x] Task 8 — Support Web and Telegram Conversation Semantics

Ensure both clients preserve conversation identifiers and render pending actions consistently.

Acceptance:

- Channel formatting differences do not change state behavior.
- Reload does not re-execute or mutate task state.

## [x] Task 9 — Add Multi-Turn and Concurrency Tests

Cover pronouns, chart follow-up, period refinement, symbol correction, task switch, expiration, retry, concurrent sends, and actor isolation.

Acceptance:

- Golden scenario `فروش ماهانه فولاد` → `نمودارش رو هم بده` executes the trend for `فولاد`.
- No test permits stale-context financial execution.

## Completion Gate

Keep the feature unchecked until typed state is isolated, versioned, expiry-safe, idempotent, and all required multi-turn routing and Billing regressions pass.
