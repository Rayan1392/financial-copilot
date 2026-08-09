# Feature 121 — Capability Guidance and Suggested Actions

## Status

`[x]` Implemented and verified across API, persistence, Web, and Telegram

## Story

As a Financial Copilot user,

I want the assistant to explain what it can answer and offer relevant next actions when my request is incomplete, unsupported, or has no data,

so that I can continue successfully without learning exact prompt syntax.

## Business Context

The frontend has assisted scanner metadata and fixed starter prompts, while the backend has no general, contextual guidance contract for non-success outcomes. Static examples can drift from active backend capabilities. Unknown/no-data responses commonly have no structured suggestions.

This feature exposes capability-backed guidance from Feature 118 and renders safe action suggestions in web and Telegram. It does not add new financial capabilities.

## Goals

- Generate guidance only from enabled executable capabilities.
- Prefer contextually relevant suggestions over a generic feature dump.
- Provide focused clarification actions, ambiguity choices, supported alternatives, and compact help menus.
- Keep web, Telegram, prompts, and starter examples aligned.
- Verify data availability before suggesting a specific answer when practical.

## Scope

### In Scope

- Versioned `SuggestedAction` and capability metadata contracts.
- Contextual suggestion policy using interpretation, outcome, resolved slots, and enabled capabilities.
- Clarification choices and disambiguation candidates.
- Unsupported/no-data/partial-result alternatives.
- Web action chips/buttons and accessible fallback text.
- Telegram compact buttons/pagination where supported.
- Registry-driven starter prompts/help content.
- Click/selection telemetry with no automatic execution.

### Out of Scope

- Enabling capabilities not present in Feature 118.
- Predictive investment advice or guaranteed forecasts.
- Showing every capability after every answer.
- Calling external providers merely to populate suggestions.
- Client-owned business routing.

## Suggested Action Contract

```csharp
public sealed record SuggestedAction(
    string Id,
    string Kind,
    string LocalizedLabel,
    string Message,
    string CapabilityCode,
    IReadOnlyDictionary<string, string> PresetSlots,
    string RelevanceReason,
    int RegistryVersion);
```

Initial kinds:

```text
FillSlot
ChooseEntity
Retry
RunRelatedCapability
ShowCapabilityHelp
RephraseExample
```

The client submits the action’s user-visible `Message` through the existing AI facade. It must not invoke hidden execution endpoints or trust client-supplied canonical IDs without backend revalidation.

## Suggestion Policy

Priority order:

1. answer the current pending clarification;
2. choose among ambiguity candidates;
3. retry a supported temporary failure;
4. use a closely related capability with already-resolved slots;
5. show 2–4 relevant capability examples;
6. show a compact general capability menu for very vague/unsupported requests.

Rules:

- Suggestions come only from enabled registry definitions.
- Suggestions preserve reply language.
- A no-data suggestion must not claim that alternative data exists unless availability is known; otherwise phrase it as a query the user can try.
- Do not suggest a capability blocked by entitlement/feature flag for the actor. A disabled capability may be described only when product policy explicitly permits an upgrade action.
- Do not include sensitive slot values or another actor’s recent symbols.
- Suggestions are bounded, ranked, deterministic for the same semantic inputs, and deduplicated.

## Frontend and Telegram Behavior

### Web

- Render suggestions below the assistant outcome as keyboard-accessible action chips/buttons.
- Preserve their order and labels on conversation reload.
- Clicking places/sends the visible message through the existing chat flow.
- Never execute financial actions solely from hidden metadata.

### Telegram

- Render a bounded inline keyboard when supported.
- Use replay-safe callback payloads referencing a server-validated action token or compact identifier.
- Fall back to numbered localized text when buttons are unavailable or expired.

## Starter Prompt Governance

- Starter prompts and help menus are generated from enabled capability examples.
- Build/test validation fails when a published starter prompt has no executable capability.
- Broad prompts such as market summary or portfolio analysis are removed/disabled until their capabilities are implemented.
- Example wording is product copy, not a route-specific exact-match requirement.

## Acceptance Criteria

1. Unsupported, clarification, disambiguation, no-data, partial, and temporary-failure outcomes may carry typed suggestions.
2. Suggestions reference only enabled, actor-available capabilities.
3. Suggestions are localized to the selected reply language.
4. A missing-symbol trend request offers a focused symbol request/example, not unrelated features.
5. Ambiguous entities are displayed as concrete choices from Feature 119.
6. Very vague requests receive a compact capability menu generated from the registry.
7. Static frontend examples cannot advertise a nonexistent route.
8. Web reload preserves the same suggestions without re-running interpretation.
9. Telegram callbacks are actor/conversation scoped and replay-safe.
10. Client input is revalidated by the backend before execution.
11. Suggestions never claim guaranteed financial outcomes or invent data availability.
12. Suggestion telemetry measures use without storing raw messages as metric dimensions.

## Dependencies

- Feature `117`
- Feature `118`
- Feature `119`
- Feature `120`
- `034-frontend-assisted-query-metadata`
- `048-frontend-ai-orchestration-v2-awareness`
- `089-telegram-ai-assistant-adapter`

## Priority

**High.** This is the user-visible guidance layer requested by the product goal.
