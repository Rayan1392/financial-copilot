# User Story — Telegram AI Assistant Adapter

## Status
`[x]` Implemented 2026-07-13

`[x]` Channel-specific presentation amendment implemented 2026-07-20.

`[x]` Monthly activity trend image/caption amendment implemented 2026-07-20.

Implemented as a backend Telegram assistant adapter boundary: an API-client-authenticated
Telegram service posts normalized updates to `POST /api/v1/telegram/assistant/updates`,
the adapter resolves the canonical actor from Feature 087, reuses the existing
`IAiQueryOrchestrationService` path for AI execution and Billing finalization, persists
actor/chat conversation bindings, stores processed-update idempotency records, and returns
Telegram-safe Persian rendered message parts to the caller. The bot-token send/queue layer
remains outside this backend adapter boundary.

## Feature
Expose the existing FinancialCopilot AI query experience through Telegram while preserving orchestration, explainability, citations, and credit accounting.

## Story

As a TahlilApp-AI user,

I want to ask financial questions and receive structured answers inside Telegram,

so that I can use the same trusted FinancialCopilot capabilities without opening the web application.

## Business Context

The bot must be a thin adapter over the current AI facade and preserve billing reservation/finalization, telemetry, citations, and conversation behavior.

## Canonical Result and Channel Presentation Boundary

The financial answer is produced once by the existing AI query application boundary. Its
structured `AiQueryResponse` is the canonical result for every delivery channel and remains the
source of truth for intent, financial facts, deterministic prose, tables, analyses, evidence,
freshness, confidence, and Billing usage metadata.

Web and Telegram may render that same canonical result differently because their presentation
capabilities differ. A Telegram renderer may select a compact layout, localize labels, reorder
display blocks, split messages, and attach Telegram actions, but it must not produce a second
answer, invoke a second LLM/prompt, rerun tools, alter facts, or independently calculate
confidence, freshness, units, citations, or credits.

```text
User query
    -> existing AI orchestration (once)
    -> canonical AiQueryResponse
        -> Web renderer: prose + native table/chart components
        -> Telegram renderer: cards + compact lists + message parts/actions
```

The invariant is **same answer and evidence, channel-appropriate representation**.

## Telegram Presentation Profile

- A one-symbol direct metric lookup is rendered as one compact stock card. The requested value
  must not be repeated in both prose and a generic table dump.
- A stock card uses localized user-facing labels and may show symbol/company, requested metric,
  explicit unit when present in the canonical result, daily change, observation/trading date,
  freshness/source, confidence, and usage metadata. It must not expose internal field names such
  as `LATEST_PRICE`, `DAILY_CHANGE_PCT`, or raw provider identifiers as primary labels.
- Positive, negative, unchanged, stale, and missing states may use consistent text or visual
  markers, but color/emoji never replaces the signed value or state text.
- Multi-symbol lookup results are rendered as compact per-symbol rows/cards without changing row
  order or values. Scanner results are rendered as a bounded ranked list with pagination or a
  deep link for additional rows.
- Comprehensive-analysis content preserves the existing faithfulness rules. Telegram may add
  headings and split message parts, but source statements, figures, dates, authors, and
  conclusions remain unchanged.
- Charts use an existing image artifact or deep link when available. The Telegram renderer never
  fabricates a chart from prose or silently drops the underlying structured facts.
- Confidence, citations/freshness, and Billing usage remain backend-owned metadata. Telegram
  places them in concise secondary blocks or footers when present; it never invents, recalculates,
  or changes them.
- Governed provider display labels are shared across channels. In particular, the internal source
  identifier `NoavaranCurrentApi` is presented to users as `نوآوران امین` on web and Telegram.
- Usage is rendered as a compact footer from the canonical Billing metadata, for example charged
  and remaining credits on one line. Rendering retries never charge again.
- Persian output is RTL-friendly and follows one digit/number-format policy. Values, signs,
  decimal precision, dates, and units must remain semantically identical to the canonical result.
- Telegram-specific limits, MarkdownV2 escaping, message splitting, and callback actions belong
  only to the Telegram presentation layer.
- If no specialized Telegram layout exists for a canonical result type, the renderer uses a safe
  generic fallback that preserves deterministic text and evidence without exposing raw object or
  enum serialization.

### Direct Price Example

For a canonical one-symbol price result, the intended Telegram representation is a compact card
similar to the following product copy (exact unit and metadata are taken from the result):

```text
شگل — گلتاش

آخرین قیمت: ۴٬۲۵۰ [واحد canonical]
تغییر روزانه: ۲٫۹۱٪+
تاریخ معامله: ۱۴۰۵/۰۴/۲۹
منبع/تازگی: [localized canonical evidence]

اعتبار: ۱ مصرف شد | ۹۹۸٬۵۶۹ باقی‌مانده
```

The web channel may continue to render the same result as prose plus an interactive table. The
different layout does not constitute a different financial answer.

## Monthly Activity Trend Image Profile

When the canonical `AiQueryResponse.MonthlyActivityTrendResult` is present, Telegram renders the
existing chart-ready values as a deterministic PNG and sends it as a photo. The core trend query,
financial calculations, persisted snapshots, and web response remain unchanged.

- The image uses the canonical previous-fiscal-year series, current-fiscal-year series, and
  trailing 12-month average. The Telegram layer performs layout only.
- Previous year and current year use distinguishable bar colors; the 12-month average uses a
  reference line. Null/unreported months remain empty and are never rendered as zero.
- Mixed Persian/number labels use explicit bidi-safe text runs so years, dates, percentages,
  axis ticks, and decimal values preserve their logical digit order. Every reported bar shows its
  value rounded to the nearest whole number above the bar; missing bars have no value label.
- The image includes a Persian title, company symbol/name, unit, Persian fiscal-month labels,
  series legend, and compact source/calculation metadata. Persian font assets are embedded or
  deterministically available in the renderer. User-facing calculation dates use Jalali
  `yyyy/MM/dd` formatting while the canonical UTC timestamp remains unchanged.
- The default output is a readable landscape PNG suitable for Telegram preview, targeting a
  16:9 layout around 1280x720 without depending on browser/Chromium screenshot execution.
- The photo caption contains the latest month, same-month YoY comparison, comparison with the
  12-month average, concise source/freshness metadata, and backend-produced usage values when
  present. It omits the Markdown chart table.
- Caption construction respects Telegram's caption limit after entity parsing. Overflow details
  are sent as subsequent text parts without repeating or changing the financial answer.
- `TelegramAssistantRenderedMessage` supports an optional versioned media attachment. The
  attachment carries image content/type/file name and content hash needed for deterministic replay.
- Processed-update replay reuses the persisted rendered attachment and caption and never reruns the
  AI query, financial calculation, or Billing operation.
- PNG generation and `sendPhoto` delivery are best effort. If generation, decoding, or Telegram
  photo upload fails, the transport sends the concise deterministic text summary and logs the
  failure; it does not silently drop the answer.
- The Telegram transport uses multipart `sendPhoto` for binary image content and applies the
  caption's parse mode and inline actions to the photo request. Text-only messages continue to use
  `sendMessage`.

## Dependencies

- Features 009
- 010
- 013
- 018
- 019
- 047
- 056
- 087
- and 088.

## In Scope

- Telegram command, text-message, callback-query, and reply routing.
- Immediate Telegram typing activity while an inbound question or callback is being processed.
- Reuse of POST /api/ai/v1/query application boundary or equivalent internal use case.
- Persian RTL-friendly message rendering.
- Channel-specific rendering from the canonical `AiQueryResponse`, without channel-specific AI
  reasoning or financial answer generation.
- Tables, charts-as-images or deep links, citations, freshness, confidence, and consumed-credit display.
- Conversation correlation per linked actor and Telegram chat.

## Out of Scope

- A separate Telegram LLM orchestration pipeline.
- A Telegram-specific financial response contract that competes with or changes the canonical AI
  query result.
- Direct database/provider access from bot handlers.
- Investment advice or buy/sell wording.

## Acceptance Criteria

1. Telegram command, text-message, callback-query, and reply routing.
2. Reuse of POST /api/ai/v1/query application boundary or equivalent internal use case.
3. Persian RTL-friendly message rendering.
4. Tables, charts-as-images or deep links, citations, freshness, confidence, and consumed-credit display.
5. Conversation correlation per linked actor and Telegram chat.
6. All user-specific data is isolated by canonical actor and tenant context where applicable.
7. All responses and notifications expose source freshness and evidence when financial facts are shown.
8. Failure of this feature must not silently consume credits or create duplicate ledger entries.
9. The capability is protected by explicit plan/entitlement checks and auditable authorization.
10. Web and Telegram consume the same canonical AI result; only their renderers differ.
11. Rendering never changes financial values, units, dates, row order, evidence, confidence, or
    Billing usage metadata.
12. A single-symbol direct lookup is rendered once as a compact Telegram card and does not repeat
    the same metric in deterministic prose plus a generic table dump.
13. Internal metric/provider identifiers are mapped to governed user-facing labels; raw identifiers
    remain available only where required for traceability or diagnostics.
14. Renderer selection is deterministic from the canonical response shape/intent and never invokes
    AI, tools, providers, or Billing.
15. Idempotent replay returns the same versioned Telegram message parts and creates no new AI or
    Billing execution.
16. For a valid text message or callback query, the Telegram transport sends the `typing` chat
    action before calling the backend so the user knows that a response is being prepared.
17. A failure to send the typing action is logged and does not prevent the backend request or
    final response from being delivered.
18. A canonical monthly activity trend response produces one Telegram photo with a deterministic
    PNG chart and a concise caption instead of a Markdown table dump.
19. The image values and caption figures exactly match the canonical trend payload; Telegram does
    not calculate financial values from raw data or prose.
20. Null and future current-year months are visually absent, not zero, and previous-year missing
    periods remain absent.
21. Photo caption overflow is delivered as ordered text message parts within Telegram limits.
22. Media attachment content is versioned, hashed, persisted with the processed update, and reused
    byte-for-byte on idempotent replay.
23. A chart-rendering or `sendPhoto` failure falls back to deterministic text without another AI
    execution or usage charge.
24. Web rendering and API consumers continue to receive the unchanged canonical monthly trend
    result and may render an interactive chart/table independently.

## API / Integration Proposal

```text
Telegram webhook/update handler -> Application adapter -> existing AI query use case
Commands: /start /help /credits /followed /market
```

## Data Model Proposal

```csharp
TelegramConversationBinding { ActorId; TelegramChatId; ConversationId; LastMessageAtUtc; }
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
