# User Story — Notification Orchestration and Noise Control

## Status
`[x]` Implemented — 2026-07-15

## Feature
Deliver timely Telegram notifications without duplicates, spam, or repeated low-value alerts.

## Story

As a TahlilApp-AI user,

I want to control how and when alerts are delivered,

so that I receive useful notifications without spam, duplicates, or interruptions during quiet hours.

## Business Context

All alert-producing features publish notification intents into a durable outbox. Delivery workers own Telegram transport and retry behavior.

The implementation now provides domain-owned lifecycle and preference policy, durable actor-isolated preferences and intents, digest batching, leased multipart delivery with retry/dead-letter handling, REST and Telegram settings, terminal outcome handoff to Feature 099, Billing capability seeding, retention/redaction, operational metrics, and DataAdmin recovery operations.

## Dependencies

- Features 018
- 087
- and every alert-producing feature 090-096.

## In Scope

- Notification outbox and delivery audit.
- Deduplication keys and idempotent delivery.
- Per-event cooldown and suppression.
- Daily caps, quiet hours, severity threshold, and digest mode.
- Retry, dead-letter, and delivery status.
- User controls to mute event type or symbol.

## Out of Scope

- Detection logic.
- AI analysis generation.
- Provider ingestion.
- At-most-once delivery claims without persistence.

## Acceptance Criteria

1. Notification outbox and delivery audit.
2. Deduplication keys and idempotent delivery.
3. Per-event cooldown and suppression.
4. Daily caps, quiet hours, severity threshold, and digest mode.
5. Retry, dead-letter, and delivery status.
6. User controls to mute event type or symbol.
7. All user-specific data is isolated by canonical actor and tenant context where applicable.
8. All responses and notifications expose source freshness and evidence when financial facts are shown.
9. Failure of this feature must not silently consume credits or create duplicate ledger entries.
10. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
GET /api/v1/notifications/me/preferences
PUT /api/v1/notifications/me/preferences
GET /api/v1/notifications/me/history
```

## Data Model Proposal

```csharp
NotificationIntent { Id; ActorId; Channel; EventType; EntityKey; DeduplicationKey; Severity; PayloadJson; NotBeforeUtc; ExpiresAtUtc?; Status; AttemptCount; LastError?; }
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
