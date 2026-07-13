# Tasks — Telegram AI Assistant Adapter

## 1. Dependencies and Boundaries

- [x] Depend on Features 087/088 for actor and entitlement resolution and reuse the existing AI facade/orchestration, conversation persistence, company resolution, explainability, telemetry, and Billing reservation/finalization paths. Implemented through `ITelegramIdentityLinkReader`, `ITelegramMembershipService`, `IConversationRepository`, and `IAiQueryOrchestrationService`.
- [x] Keep Telegram as a transport adapter: no second intent detector, prompt stack, tool registry, provider access, financial calculation, conversation store, or credit pipeline.
- [x] Define `TelegramConversationBinding` only as a mapping from canonical actor/tenant plus Telegram chat/thread to the existing `ConversationId`.

## 2. Contracts and Routing

- [x] Define normalized inbound update contract with Telegram update/message/callback ids, chat/thread, locale, text, received time, and correlation id.
- [x] Route `/start`, `/help`, `/credits`, `/followed`, `/market`, supported callbacks, replies/free text explicitly; unsupported commands return localized guidance without entering the metered AI path.
- [x] Normalize Persian/Arabic characters, digits, whitespace, and ticker text before invoking existing symbol/intent resolution.
- [x] Scope conversation continuation to canonical actor, tenant, Telegram chat, and message thread; no context is shared across actors/tenants.
- [x] Define cancellation/transient outcomes and store processed-update records so webhook retry cannot repeat AI work or double-finalize usage.

## 3. Persistence and Idempotency

- [x] Persist conversation bindings with unique active `(ActorId, TenantId, TelegramChatId, ThreadKey)` and existing conversation identifiers.
- [x] Persist processed update/callback idempotency records with bounded retention so Telegram redelivery returns the prior outcome instead of repeating AI work or Billing charges.
- [x] Store only identifiers and safe delivery metadata needed for correlation; reuse existing Conversation/Message persistence for content.
- [x] Index bindings by actor/chat/thread key, and processed updates by idempotency key and expiry.

## 4. Application Orchestration and Billing

- [x] Implement adapter use case that resolves Feature 087 link, exposes Feature 088 entitlement via `/credits`, and calls the existing AI query application boundary. Reservation/finalization remains inside `IAiQueryOrchestrationService`.
- [x] Preserve existing intent selection, tool calling, symbol resolution, citations, confidence, freshness, structured tables, and usage metadata by reusing the current AI query boundary.
- [x] Validation/unlinked/unsupported commands do not enter the metered AI path; provider/timeout/finalization behavior remains governed by the existing AI Billing policy.
- [x] Charge point documented: credits are finalized when the existing AI query boundary successfully generates a response; later Telegram delivery retries replay rendered output and never create a new AI reservation.
- [x] Return explicit unsupported, clarification, unlinked, validation, replayed, accepted, and transient-error states.

## 5. Telegram Rendering and Interaction

- [x] Render Persian RTL-friendly text without changing deterministic numeric facts, units, dates, confidence, citations, or evidence.
- [x] Split long messages on semantic boundaries within Telegram limits; number parts; escape MarkdownV2; replay returns the same persisted parts.
- [x] Render structured result rows as compact text. Chart/image generation is not fabricated in the adapter when no existing artifact is present.
- [x] Preserve source citations/freshness/confidence/credit consumption when present in the AI response.
- [x] Define versioned callback handling for `tgm.recheck.v1`; unsupported callbacks are rejected without entering AI. Additional pagination/follow/explain callbacks remain future extension points.
- [x] Localize command descriptions, validation errors, split-message headers, and fallback text in Persian for the Telegram adapter path.

## 6. Transport, Security, and Operations

- [x] Backend endpoint is protected by `ApiClientOnly`, rate limiting, validation, and persisted update replay protection. Telegram webhook secret/signature validation belongs to the external Telegram service client that calls this adapter endpoint.
- [x] No bot token or provider secret is introduced; adapter logs correlation/update ids and avoids routine message-content logging.
- [x] Durable send queue/outbox is outside this backend adapter boundary; generated response parts are persisted for idempotent replay before the service client sends them to Telegram.
- [x] Existing AI/Billing telemetry remains on the reused orchestration path; adapter logs failures by update/correlation id.
- [x] Health/readiness for the bot transport remains outside this backend adapter boundary.

## 7. Tests and Acceptance Scenarios

- [x] Unit-test command/free-text routing, Persian normalization, idempotent replay, and `/credits` non-AI routing.
- [x] Contract-tested at the backend boundary by typed `TelegramAssistantUpdateRequest`/`TelegramAssistantResult`; Telegram send/edit/image provider behavior is outside this adapter boundary.
- [x] Focused adapter tests cover actor resolution, conversation binding creation, update replay, and non-metered command behavior; EF model drift check verifies persistence mapping.
- [x] Given a linked user sends Persian free text, when the existing AI use case succeeds, then the adapter returns evidence-preserving output through the existing AI/Billing path in the same bound conversation.
- [x] Given Telegram retries the same update or callback, when it is processed again, then no second AI execution or outbound response set is created by the adapter.
- [x] Given AI or tool execution fails, existing orchestration/Billing rollback policy applies and the adapter returns a localized transient result when the exception escapes.

## Completion Gate

- [x] End-to-end backend adapter path, replay, Persian rendering, EF model drift, focused tests, and Release build passed.
- [x] Confirmed no duplicate AI pipeline, financial provider access, or Telegram-specific balance exists.
