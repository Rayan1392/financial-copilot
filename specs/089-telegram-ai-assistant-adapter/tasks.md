# Tasks — Telegram AI Assistant Adapter

## 1. Dependencies and Boundaries

- [ ] Depend on Features 087/088 for actor and entitlement resolution and reuse the existing AI facade/orchestration, conversation persistence, company resolution, explainability, telemetry, and Billing reservation/finalization paths.
- [ ] Keep Telegram as a transport adapter: no second intent detector, prompt stack, tool registry, provider access, financial calculation, conversation store, or credit pipeline.
- [ ] Define `TelegramConversationBinding` only as a mapping from canonical actor/tenant plus Telegram chat/thread to the existing `ConversationId`.

## 2. Contracts and Routing

- [ ] Define normalized inbound update contract with Telegram update/message/callback ids, linked actor, chat/thread, locale, text, reply context, received time, and correlation id.
- [ ] Route `/start`, `/help`, `/credits`, `/followed`, `/market`, supported callbacks, replies, and free text explicitly; unsupported commands return localized guidance without entering the metered AI path.
- [ ] Normalize Persian/Arabic characters, digits, whitespace, and ticker text before invoking existing symbol/intent resolution while preserving the original message for conversation display.
- [ ] Scope conversation continuation to canonical actor, tenant, Telegram private chat/thread, and explicit reply/callback context; never share context across users or group participants.
- [ ] Define cancellation/timeout outcomes and ensure client disconnect, webhook retry, or late provider response cannot double-finalize usage.

## 3. Persistence and Idempotency

- [ ] Persist conversation bindings with unique active `(ActorId, TenantId, TelegramChatId, ThreadId)` and validate referenced conversations belong to the actor.
- [ ] Persist processed update/callback idempotency records with bounded retention so Telegram redelivery returns the prior outcome instead of repeating AI work or Billing charges.
- [ ] Store only identifiers and safe delivery metadata needed for correlation; reuse existing Conversation/Message persistence for content.
- [ ] Index bindings by actor and chat, and processed updates by Telegram update id/expiry.

## 4. Application Orchestration and Billing

- [ ] Implement adapter use case that resolves Feature 087 link, verifies Feature 088/paid entitlement, reserves credit, calls the existing AI query application boundary, and commits or releases exactly once.
- [ ] Preserve existing intent selection, tool calling, symbol resolution, citations, confidence, freshness, structured tables, charts, and usage metadata.
- [ ] Release the reservation on validation failure, cancellation, timeout, provider failure, rendering failure before delivery eligibility, or unhandled exception according to existing Billing policy.
- [ ] Decide and document charge point for a successfully generated response whose Telegram delivery later fails; delivery retries must never create a new AI reservation.
- [ ] Return explicit unsupported, clarification, insufficient-credit, unlinked, membership-required, unavailable-data, provider-timeout, and transient-delivery states.

## 5. Telegram Rendering and Interaction

- [ ] Render Persian RTL-friendly text without changing deterministic numeric facts, units, dates, confidence, citations, or evidence.
- [ ] Split long messages on semantic boundaries within Telegram limits; number parts, avoid breaking Markdown/entities, and make retries idempotent per part.
- [ ] Render tables as compact text when legible and otherwise as an existing web deep link or generated chart/image artifact with accessible caption; do not fabricate a chart from absent data.
- [ ] Preserve source citations as Telegram-safe links and include freshness/confidence/credit consumption when present in the AI response.
- [ ] Define versioned callback actions for pagination, follow symbol, explain insight, retry, and open web; validate callback ownership and answer callback queries promptly.
- [ ] Localize command descriptions, validation errors, button labels, split-message headers, and fallback text in Persian and English.

## 6. Transport, Security, and Operations

- [ ] Validate Telegram webhook secret/signature mechanism, service authentication to backend, update replay protection, payload size, supported chat types, and rate limits.
- [ ] Keep bot token outside source control and redact message content, Telegram identifiers, auth headers, and callback payloads from routine logs.
- [ ] Process updates through a durable queue/outbox where required, with bounded concurrency per chat, cancellation, exponential retry, poison-message isolation, and no parallel reordering within a conversation.
- [ ] Emit traces from update receipt through identity, entitlement, Billing, AI provider/tool calls, rendering, and Telegram send; expose latency/failure metrics at each stage.
- [ ] Add health/readiness diagnostics for webhook/queue/Telegram transport and operational visibility for failed updates without exposing message content.

## 7. Tests and Acceptance Scenarios

- [ ] Unit-test command/free-text routing, Persian normalization, message splitting, Markdown escaping, callback ownership, and rendering of tables/citations/charts.
- [ ] Contract-test Telegram update, callback, send-message, edit-message, image, retry-after, blocked-bot, and malformed-provider responses.
- [ ] Integration-test actor/tenant/conversation isolation, entitlement denial, reservation commit/release, update replay, timeout/cancellation, and existing web behavior unchanged.
- [ ] Given a linked entitled user sends Persian free text, when the existing AI use case succeeds, then the bot returns evidence-preserving output and one Billing finalization in the same conversation.
- [ ] Given Telegram retries the same update or callback, when it is processed again, then no second AI execution, ledger entry, conversation message, or outbound response set is created.
- [ ] Given AI or tool execution fails/times out, when the request ends, then the reservation follows existing rollback policy and the user receives a localized retryable/non-retryable result.

## Completion Gate

- [ ] Keep tasks unchecked until end-to-end adapter, Billing, replay, Persian rendering, provider-failure, and regression tests pass.
- [ ] Confirm no duplicate AI pipeline, financial provider access, or Telegram-specific balance exists.
