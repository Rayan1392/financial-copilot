# User Story — Telegram AI Assistant Adapter

## Status
`[ ]` Proposed

## Feature
Expose the existing FinancialCopilot AI query experience through Telegram while preserving orchestration, explainability, citations, and credit accounting.

## Story

As a TahlilApp-AI user,

I want to ask financial questions and receive structured answers inside Telegram,

so that I can use the same trusted FinancialCopilot capabilities without opening the web application.

## Business Context

The bot must be a thin adapter over the current AI facade and preserve billing reservation/finalization, telemetry, citations, and conversation behavior.

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
- Reuse of POST /api/ai/v1/query application boundary or equivalent internal use case.
- Persian RTL-friendly message rendering.
- Tables, charts-as-images or deep links, citations, freshness, confidence, and consumed-credit display.
- Conversation correlation per linked actor and Telegram chat.

## Out of Scope

- A separate Telegram LLM orchestration pipeline.
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
