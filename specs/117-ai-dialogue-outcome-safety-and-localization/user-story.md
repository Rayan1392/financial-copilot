# Feature 117 — AI Dialogue Outcome Safety and Localization

## Status

`[x]` Implemented and verified

## Story

As a Financial Copilot user,

I want every unanswered, incomplete, unsupported, or failed AI request to receive a clear response in my language,

so that I understand what happened and what I can do next instead of receiving vague, ungrounded, or accidental English text.

## Business Context

The active Microsoft Agent Framework V2 workflow can return raw agent prose when no structured tool result is produced. Several deterministic routes also collapse missing input, unresolved symbols, absent rows, and failures into `null` or plain text. The public response has clarification fields, but branches do not use them consistently.

This feature introduces a safe outcome boundary before the broader conversational semantic layer. It does not attempt full intent understanding. It makes all non-success states explicit, localized, persisted, observable, and backward-compatible.

Reference: `docs/ai-query-semantic-dialogue-layer-review.md`.

## Goals

- Separate business intent from dialogue/execution outcome.
- Prevent ungrounded `Unknown` prose from becoming an authoritative answer.
- Guarantee application-owned Persian messages for Persian clarification, ambiguity, no-data, unsupported, and failure states.
- Preserve existing successful structured responses and faithfulness rules.
- Make clarification fields consistent across V1, V2, web, Telegram, and conversation reload.
- Establish typed reason codes for later semantic routing and telemetry.

## Scope

### In Scope

- Additive `DialogueOutcome` and `OutcomeReasonCode` contracts.
- A deterministic response composer for non-success outcomes.
- Reply-language detection/selection and a final language guard for application-generated states.
- Mapping existing branches into explicit outcomes.
- Consistent clarification propagation.
- Persistence and API mapping of the new fields.
- Web and Telegram rendering of the same semantic outcome.
- Billing/telemetry integration without duplicate accounting.

### Out of Scope

- Full capability registry or LLM-based semantic frame extraction.
- Multi-turn slot filling.
- New financial data or query capabilities.
- Automatic alias learning.
- Replacing successful evidence-grounded narratives or ComprehensiveAnalysis faithfulness behavior.

## Outcome Contract

The product must distinguish at least:

```csharp
public enum DialogueOutcome
{
    Answered,
    PartialAnswer,
    ClarificationNeeded,
    DisambiguationNeeded,
    NoData,
    Unsupported,
    TemporarilyUnavailable,
    Failed
}
```

Required initial reason codes:

```text
none
capability_not_recognized
required_input_missing
entity_ambiguous
entity_not_found
supported_but_no_rows
data_stale_or_ineligible
partial_evidence
provider_or_tool_timeout
provider_or_tool_failure
response_validation_failed
language_guard_applied
```

Reason codes are machine-readable, stable, and never contain provider secrets, SQL, stack traces, or user-sensitive text.

## Response Rules

1. `Intent` describes the business capability; `Outcome` describes what happened.
2. `Unknown` must not be used as a substitute for `Unsupported`, `ClarificationNeeded`, or `Failed`.
3. A branch asking the user for information must set:
   - `Outcome = ClarificationNeeded` or `DisambiguationNeeded`;
   - `ClarificationRequired = true`;
   - non-empty `ClarificationMessage`;
   - a suitable reason code.
4. A supported query with no eligible stored rows must return `NoData`, not `Unsupported`.
5. A timeout/exception must return `TemporarilyUnavailable` or `Failed`, not `NoData`.
6. Existing successful responses return `Answered` unless their result explicitly declares partial evidence.
7. Raw model prose without a validated structured result must not claim financial facts or invent supported capabilities.

## Localization Policy

- Determine `ReplyLanguage` from the current user message, with conversation preference only as a documented tie-breaker for mixed/empty input.
- Persian includes Persian/Arabic-script dominant input and approved mixed financial notation such as `P/E فولاد`.
- All application-owned non-success templates must exist in Persian and English.
- Persian templates must preserve RTL-compatible punctuation and financial Latin tokens.
- Model output must not override the selected reply language for non-success states.
- If response validation detects a language mismatch, replace the response with the deterministic localized template and record `language_guard_applied`.

## Unknown and Unsupported Policy

When no enabled capability can be validated:

- return `Outcome = Unsupported` and `capability_not_recognized`;
- use a concise localized explanation;
- do not generate market commentary, a guessed answer, or a generic English apology;
- expose an empty/additive suggestion collection for Feature 121 to populate later;
- preserve the original message for audit without including it in unsafe telemetry dimensions.

Until Feature 121 is implemented, a bounded static fallback may mention only capabilities verified in the active backend. It must be centralized and tested, not duplicated in prompts and clients.

## Billing Policy

- Existing Billing remains the accounting source of truth.
- Outcome mapping must not create a second reservation or finalization.
- The implementation must define whether interpretation-only unsupported/clarification outcomes consume an AI operation credit and apply that policy consistently in V1/V2 and both channels.
- Tool/provider failures must follow the existing failed-operation/refund policy.
- A conversation reload never creates new usage.

## Security and Observability

- Persist outcome, reason code, reply language, intent, correlation ID, and whether the language guard was applied.
- Do not persist raw exception detail in assistant payloads.
- Missing-answer feedback is fire-and-forget and must not change the response.
- Telemetry dimensions must be bounded; raw user text belongs only in the existing protected conversation/feedback stores.

## Acceptance Criteria

1. Every AI facade response has a typed `DialogueOutcome`.
2. Existing clients remain compatible with the additive contract.
3. Persian unsupported requests always receive deterministic Persian text.
4. An agent response with no validated tool/result cannot return invented financial facts.
5. Missing-symbol branches consistently set the public clarification fields.
6. Ambiguous entity, unknown entity, no rows, stale data, and technical failure are distinguishable.
7. Successful scanner, lookup, trend, statement, disclosure, gauge, and comprehensive-analysis results remain unchanged except for additive outcome metadata.
8. ComprehensiveAnalysis source wording and numeric faithfulness remain intact.
9. Web live response, conversation reload, and Telegram represent the same outcome and reason.
10. A provider exception is never shown as “data not found.”
11. No internal exception, prompt, schema, or provider detail reaches the user.
12. Billing tests prove no duplicate charge and enforce the documented non-execution policy.

## Dependencies

- `009-explainable-results`
- `010` and `013` Billing integration/domain
- `017-ai-evaluation-and-regression`
- `018-ai-observability-and-telemetry`
- `019-conversation-memory`
- `028-missing-answer-feedback`
- `032-frontend-real-api-chat-cutover`
- `047` and `056` Microsoft Agent Framework orchestration
- `089-telegram-ai-assistant-adapter`

## Priority

**Critical.** This is the minimum safe fallback behavior and should be delivered before semantic routing expansion.
