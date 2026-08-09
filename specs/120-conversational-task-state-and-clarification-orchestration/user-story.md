# Feature 120 — Conversational Task State and Clarification Orchestration

## Status

`[x]` Implemented and verified

## Story

As a Financial Copilot user,

I want the assistant to remember the active stock and request while we clarify or continue a question,

so that follow-ups such as `نمودارش رو هم بده` work without forcing me to repeat the full command.

## Business Context

Feature 019 provides conversation persistence and consent-aware memory, but prompt context is not a typed execution state. Deterministic preflight routes often inspect only the latest message. This prevents reliable slot carry-over and makes it difficult to distinguish a reply to a pending clarification from a new task.

This feature adds short-lived, typed task state and a dialogue policy over Features 117–119. It does not create long-term behavioral profiling.

## Goals

- Persist the active capability and resolved slots for safe follow-ups.
- Represent pending clarification/disambiguation explicitly.
- Fill omitted slots only from recent, compatible, unambiguous state.
- Detect task switches and avoid stale-context leakage.
- Ask one focused question at a time.
- Keep web and Telegram dialogue semantics equivalent.

## Scope

### In Scope

- Versioned `ConversationTaskState`.
- State lifecycle and expiration.
- Pending clarification/disambiguation state.
- Reference resolution for phrases such as “it,” “this stock,” and “its chart.”
- Slot carry-over policy and provenance.
- Task-switch and state-clear rules.
- Persistence, concurrency, idempotency, and replay behavior.
- Integration with orchestration, billing, feedback, and both channels.

### Out of Scope

- Durable personal preferences beyond Feature 019 consent policy.
- Guessing a symbol from unrelated old conversations.
- Autonomous multi-step financial actions.
- New financial capabilities.
- Free-form agent memory as an executable source of truth.

## Task State Contract

Conceptual state:

```csharp
public sealed record ConversationTaskState(
    Guid ConversationId,
    long Version,
    string? ActiveCapability,
    ResolvedSlot? ActiveEntity,
    ResolvedSlot? ActiveMetric,
    ResolvedSlot? ActivePeriod,
    ResolvedSlot? ActiveComparison,
    ResolvedSlot? ActivePresentation,
    PendingDialogueAction? PendingAction,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt);
```

Every carried slot records `ConversationState` as its provenance and identifies the originating message/state version.

## State Policy

1. State is conversation-scoped and actor/tenant-isolated.
2. Only validated canonical slots are stored.
3. A successful answer updates active task state with its executed capability and slots.
4. A clarification stores the expected slot and candidate/validation constraints.
5. The next message is first evaluated as a possible answer to the pending action, then as a possible new task.
6. A clear new capability/entity request replaces incompatible active state.
7. State expires after a configurable short duration and/or turn count.
8. General long-term memory cannot silently supply an execution-critical symbol without current-conversation confirmation.
9. Retried/replayed messages update state idempotently.
10. Concurrent messages use optimistic versioning or equivalent ordering protection.

## Clarification Policy

- Ask only for a required slot that cannot be safely resolved or defaulted.
- Ask one question per turn unless the user explicitly requests a form/menu.
- Include what was already understood when useful.
- For ambiguity, present concrete candidates from Feature 119.
- Do not ask for information already explicit in the current query.
- Do not ask a generic “what do you mean?” when a specific missing slot is known.
- Non-executable clarification must follow the explicit Billing policy established with Feature 117.

## Follow-Up Examples

### Presentation follow-up

```text
User: فروش ماهانه فولاد چقدر بوده؟
Assistant: [latest monthly sales answer]
User: نمودارش رو هم بده
```

Target frame uses active symbol `فولاد`, metric `MONTHLY_SALES`, capability `monthly_activity_trend`, and presentation `Chart`.

### Period refinement

```text
User: روند فروش کچاد را نشان بده
Assistant: [default period chart]
User: فقط برای یک سال اخیر
```

The second turn refines the period of the active trend task.

### Task switch

```text
User: روند فروش فولاد را نشان بده
User: P/E فملی چقدر است؟
```

The second message starts a symbol metric lookup for `فملی`; it must not inherit `فولاد` or trend presentation.

## Acceptance Criteria

1. A short follow-up can reuse a recent unambiguous symbol, metric, and capability.
2. Every carried slot exposes conversation-state provenance.
3. Pending clarification answers are resolved before normal routing when compatible.
4. A clear task switch replaces incompatible state.
5. Expired state is never used for execution.
6. Ambiguous or conflicting state triggers clarification rather than silent selection.
7. State is actor-, tenant-, and conversation-isolated.
8. Message retries/reloads do not duplicate task transitions or Billing operations.
9. Web and Telegram produce equivalent task transitions.
10. Consent-aware durable memory remains separate and unchanged.
11. State payloads contain canonical IDs/slots, not raw provider data or prompts.
12. Follow-up regression tests cover trend, lookup, comparison refinement, clarification, disambiguation, and task switch.

## Dependencies

- Feature `117`
- Feature `118`
- Feature `119`
- `019-conversation-memory`
- `031-backend-identity`
- `032-frontend-real-api-chat-cutover`
- `047` and `056` orchestration
- `089-telegram-ai-assistant-adapter`

## Priority

**High.** This turns one-shot routing into a reliable dialogue without broadening financial scope.
