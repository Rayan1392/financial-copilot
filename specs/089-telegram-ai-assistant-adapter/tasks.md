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

### 5.1 Canonical Result and Channel-Specific Presentation Amendment (2026-07-20)

- [x] Keep `AiQueryResponse` as the canonical channel-neutral answer. Telegram rendering consumes
  it after orchestration and must not issue a second prompt, invoke tools/providers, recalculate
  financial facts, or create a second Billing operation.
- [x] Extract a dedicated Telegram response-renderer contract from transport/update handling. Its
  input is the canonical result plus presentation context (locale and Telegram capabilities); its
  output is versioned `TelegramAssistantRenderedMessage` parts and actions.
- [x] Select deterministic layouts by canonical response shape/intent: clarification/error,
  one-symbol metric card, multi-symbol lookup list, scanner ranking, comprehensive analysis,
  chart/deep-link result, and safe generic fallback.
- [x] For one-symbol direct metric lookup, render the requested value once as a compact card and
  suppress the duplicate generic `TextAnswer` plus `SymbolLookupTable` presentation. This is
  display deduplication only; both canonical fields remain unchanged and auditable.
- [x] Render symbol/company, requested metric, canonical unit, daily change, observation/trading
  date, source/freshness, and missing/stale state from structured fields. Do not infer a unit or
  value that is absent from the canonical result.
- [x] Introduce governed Telegram label mapping for metric codes, evidence/source names, freshness
  states, and result headings. Do not expose `LATEST_PRICE`, `DAILY_CHANGE_PCT`, provider enum
  values, or raw property dumps as primary user-facing labels.
- [x] Render positive/negative/unchanged states consistently while preserving explicit signs and
  numeric values independently of emoji or other visual markers.
- [x] Render multi-symbol lookups as compact per-symbol rows/cards in canonical order. Render
  scanner results as a bounded ranked list with versioned previous/next callbacks or a web deep
  link for the full result; do not silently discard omitted rows.
- [x] Preserve comprehensive-analysis source text, figures, dates, author, and conclusions under
  the existing faithfulness policy. Telegram may only add headings, spacing, splitting, and
  navigation actions.
- [x] Render confidence, citations/freshness, and backend-produced Billing usage as concise
  secondary blocks/footers when present. Never calculate, round semantically, or mutate these
  values in the Telegram layer.
- [x] Apply one explicit Persian digit, thousands-separator, decimal, percent-sign, date, unit, and
  RTL policy after canonical values are selected. MarkdownV2 escaping and message splitting occur
  last and must not corrupt signs, separators, URLs, or callback data.
- [x] Version the Telegram rendering policy and persist the rendered message parts used for each
  processed update so idempotent replay remains byte-stable across retries and does not re-render
  under a newer policy unexpectedly.
- [x] Keep a safe generic fallback for unsupported canonical result shapes. It must preserve
  deterministic text/evidence and emit an observable renderer diagnostic without exposing raw JSON
  or internal enum/property names to the user.

### 5.2 Monthly Activity Trend Image and Caption Amendment (2026-07-20)

- [x] Detect canonical `MonthlyActivityTrendResult` before generic text rendering and build a
  Telegram-specific photo/caption presentation without modifying `AiQueryResponse.TextAnswer` or
  the structured trend payload.
- [x] Add a deterministic server-side PNG renderer using the canonical chart points only. Render
  previous/current fiscal-year bars, 12-month average line, Persian labels/title/unit/legend,
  missing periods as absent, and compact source/calculation metadata.
- [x] Keep image generation independent of Chromium, React, Recharts, and external chart services;
  package and load a deterministic Persian-capable font for identical local/hosted output.
- [x] Extend rendered-message contracts with an optional versioned photo attachment containing
  MIME type, file name, binary payload encoding, and SHA-256 content hash. Enforce bounded payload
  size and reject unsupported attachment types.
- [x] Build a concise deterministic caption from latest-month, YoY, average-comparison, source, and
  Billing usage metadata. Omit the Markdown table and split caption overflow into ordered text
  parts within Telegram limits.
- [x] Persist rendered attachment bytes/hash/version with the processed update so replay is
  byte-stable and does not invoke AI, trend calculation, chart rendering, or Billing again.
- [x] Extend the development Telegram transport to send photo attachments through multipart
  `sendPhoto`, including caption parse mode/actions, while retaining `sendMessage` for text parts.
- [x] If PNG generation, attachment validation/decoding, or `sendPhoto` fails, log safe diagnostics
  and send the deterministic text fallback without losing the answer or charging again.
- [x] Add focused tests for chart series, Persian labels, null-as-missing behavior, caption/table
  suppression, attachment hash/version, replay stability, multipart photo delivery contract, and
  text fallback.

### 5.3 Monthly Trend Chart Bidi and Bar Labels (2026-07-20)

- [x] Render numeric portions of Persian chart text as explicit LTR runs so dates, years,
  percentages, legend values, insight values, and Y-axis labels are not reversed.
- [x] Draw a compact, correctly ordered value label above every reported previous/current-year
  bar, rounded to the nearest whole number without decimals, while leaving missing periods unlabeled.
- [x] Version the changed chart artifact and add regression tests for representative dates,
  fiscal years, percentages, and decimal bar values.

## 6. Transport, Security, and Operations

- [x] Send Telegram `sendChatAction` with `action=typing` immediately after accepting a valid
  message or callback update and before backend AI processing. The action is transport UX only and
  does not alter the canonical AI result or Billing flow.
- [x] Apply the typing action to both free-text messages and callback queries, including link
  confirmation messages, while ignoring malformed updates that cannot identify a chat.
- [x] Treat typing-indicator delivery as best effort: log a warning for Telegram API failure and
  continue processing the update; cancellation still propagates normally.
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
- [x] Add renderer unit/snapshot tests proving the same canonical `AiQueryResponse` produces web
  and Telegram presentations with identical financial values, units, dates, evidence, confidence,
  and usage metadata despite different layouts.
- [x] Add a direct-price regression for `قیمت شگل چقدر است؟` proving Telegram emits one stock card,
  does not repeat the value in a generic table block, and does not expose `LATEST_PRICE`,
  `DAILY_CHANGE_PCT`, or `IntradayToday` as primary labels.
- [x] Add positive, negative, unchanged, stale, missing-unit, and missing-value card cases; the
  renderer must never infer unavailable facts.
- [x] Add multi-symbol and scanner tests for canonical ordering, bounded rows, omitted-row notice,
  pagination/deep-link actions, and Telegram message limits.
- [x] Add comprehensive-analysis tests proving message splitting and Markdown escaping do not
  paraphrase or alter source numbers, dates, author attribution, or conclusions.
- [x] Add Billing/idempotency tests proving presentation, renderer fallback, split delivery, and
  update replay never trigger another AI execution or usage charge.
- [x] Verify the worker sends the typing action before backend processing and that a failed typing
  action does not block response processing; `FinancialCopilot.Worker` Release build passed with
  zero warnings and zero errors.

## Completion Gate

- [x] End-to-end backend adapter path, replay, Persian rendering, EF model drift, focused tests, and Release build passed.
- [x] Confirmed no duplicate AI pipeline, financial provider access, or Telegram-specific balance exists.
- [x] Channel-specific presentation amendment is complete when canonical-result equivalence,
  Telegram layout profiles, governed labels, deterministic replay, and focused regression tests
  all pass without changing the web answer contract.
