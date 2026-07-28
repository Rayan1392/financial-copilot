# User Story — Smart Codal Announcement Alerts

## Status
`[x]` Implemented

## Feature
Notify users immediately when a relevant Codal announcement is published and optionally provide an evidence-bound AI summary.

## Story

As a TahlilApp-AI user,

I want to subscribe to Codal announcements for selected symbols,

so that I am informed quickly and can understand the material facts without repeatedly checking Codal.

## Business Context

Raw notification is deterministic and may be entitlement-based; AI summary is separately metered. Existing Codal/Noavaran ingestion remains authoritative.

## Dependencies

- Features 023
- 040
- 051
- 053
- 081-083
- 084-086
- 089
- and 097.

## In Scope

- Announcement subscription per followed symbol.
- Filters by announcement type and importance.
- Immediate raw announcement notification.
- Deterministic extraction of key facts, periods, and amounts.
- Optional credit-consuming AI summary and follow-up Q&A.
- Deduplication and source link.

## Out of Scope

- Polling Codal independently from existing ingestion.
- Invented sentiment or unsupported recommendation labels.
- Portfolio exposure inference.

## Acceptance Criteria

1. Announcement subscription per followed symbol.
2. Filters by announcement type and importance.
3. Immediate raw announcement notification.
4. Deterministic extraction of key facts, periods, and amounts.
5. Optional credit-consuming AI summary and follow-up Q&A.
6. Deduplication and source link.
7. All user-specific data is isolated by canonical actor and tenant context where applicable.
8. All responses and notifications expose source freshness and evidence when financial facts are shown.
9. Failure of this feature must not silently consume credits or create duplicate ledger entries.
10. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
POST /api/v1/codal-alerts/me/subscriptions
GET /api/v1/codal-alerts/me/subscriptions
PUT /api/v1/codal-alerts/me/subscriptions/{id}
DELETE /api/v1/codal-alerts/me/subscriptions/{id}
POST /api/v1/codal-alerts/me/insights/{insightEventId}/summary
```

## Data Model Proposal

```csharp
CodalAlertSubscription { Id; ActorId; ExternalCompanyId; AnnouncementTypesJson; MinimumImportance; AiSummaryEnabled; CreatedAtUtc; }
```

## Implementation Evidence

- Implemented on 2026-07-13 with actor-scoped subscription CRUD, followed-symbol ownership validation, `CodalAnnouncementMatched` insight events, idempotent raw notification intents, Billing-backed AI summaries, correlated summary-ready intents, and Telegram assistant summary callback handling.
- Validation passed: API Release build, `MarketInsight084Tests` unit slice, `MarketInsightEndpointTests` integration slice, and architecture tests.

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
