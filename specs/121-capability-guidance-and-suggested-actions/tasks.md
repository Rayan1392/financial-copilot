# Feature 121 Tasks — Capability Guidance and Suggested Actions

## [ ] Task 1 — Define Versioned Guidance Contracts

Add suggested actions, capability help summaries, preset slot metadata, and persistence/API mappings.

Acceptance:

- Contracts are additive and bounded.
- Existing clients ignore them safely.
- Hidden canonical values are not trusted from clients.

## [ ] Task 2 — Implement Contextual Suggestion Policy

Rank actions from outcome, reason, frame, pending state, enabled capabilities, and actor availability.

Acceptance:

- Clarification/disambiguation actions outrank generic help.
- Output is deterministic, deduplicated, localized, and capped.

## [ ] Task 3 — Add Availability and Entitlement Guards

Filter disabled/unavailable capabilities and avoid false data-availability claims.

Acceptance:

- Suggestions never bypass backend entitlements.
- No synchronous external provider call is introduced.

## [ ] Task 4 — Generate Starter Prompts and Help Metadata

Replace independent static examples with registry-derived projections.

Acceptance:

- Every published example maps to one enabled capability.
- Disabling a capability removes its starter examples.

## [ ] Task 5 — Implement Web Suggested Actions

Render accessible action chips with persisted/reloaded parity.

Acceptance:

- Keyboard, screen-reader, RTL, mobile, light, and dark behavior pass.
- Clicking submits the visible message through `POST /api/ai/v1/query`.
- Duplicate sends are prevented using existing chat behavior.

## [ ] Task 6 — Implement Telegram Suggested Actions

Add bounded inline buttons or numbered fallback text with secure callback handling.

Acceptance:

- Actions are actor/conversation scoped and expire safely.
- Callback replay cannot duplicate Billing/execution.

## [ ] Task 7 — Persist Exact Guidance Snapshot

Store the actions returned with the assistant message.

Acceptance:

- Reload does not silently regenerate against a newer registry.
- Registry version is retained.
- Payload size limits are enforced.

## [ ] Task 8 — Add Guidance Analytics

Record presented, selected, expired, and successfully resolved actions using bounded IDs/codes.

Acceptance:

- Analytics correlate with outcome and capability.
- Raw text and sensitive slots are excluded from metric dimensions.

## [ ] Task 9 — Add Outcome and Channel Tests

Cover contextual suggestions for every non-success outcome and vague help, in Persian/English, web/Telegram, live/reload.

Acceptance:

- No test receives a disabled/nonexistent capability.
- No-data wording remains honest about unverified alternatives.

## Completion Gate

Keep the feature unchecked until guidance is registry-backed, actor-safe, localized, persisted, channel-consistent, and all advertised examples are executable.
