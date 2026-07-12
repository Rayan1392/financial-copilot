# User Story — Telegram Alert History and Explainability

## Status
`[ ]` Proposed

## Feature
Let users review previous alerts, understand why each alert fired, and inspect its evidence and subsequent market reaction.

## Story

As a TahlilApp-AI user,

I want to review past alerts and ask why an alert was generated,

so that I can trust the system, verify evidence, and learn from repeated market patterns.

## Business Context

Historical explanations must use immutable event/rule evidence and detector versions. Post-event reaction is descriptive analytics, not performance marketing.

## Dependencies

- Features 084
- 086
- 091-093
- 097.

## In Scope

- Alert history by symbol, type, and date.
- Why-this-alert explanation from persisted rule/evidence.
- Source, period, freshness, confidence, and detector version.
- Optional post-event price-reaction analytics with explicit horizon.
- Reply-based follow-up questions tied to alert context.

## Out of Scope

- Backtested performance claims without methodology.
- Rewriting historical evidence.
- Personal investment advice.

## Acceptance Criteria

1. Alert history by symbol, type, and date.
2. Why-this-alert explanation from persisted rule/evidence.
3. Source, period, freshness, confidence, and detector version.
4. Optional post-event price-reaction analytics with explicit horizon.
5. Reply-based follow-up questions tied to alert context.
6. All user-specific data is isolated by canonical actor and tenant context where applicable.
7. All responses and notifications expose source freshness and evidence when financial facts are shown.
8. Failure of this feature must not silently consume credits or create duplicate ledger entries.
9. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
GET /api/v1/alerts/me/history
GET /api/v1/alerts/me/{id}
POST /api/ai/v1/query with alertId context
```

## Data Model Proposal

```csharp
UserAlertRecord { Id; ActorId; NotificationIntentId; InsightEventId?; AlertRuleId?; DeliveredAtUtc; EvidenceSnapshotJson; DetectorVersion; ReactionAnalyticsJson?; }
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
